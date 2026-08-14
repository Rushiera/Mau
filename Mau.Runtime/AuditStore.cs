using System;
using System.IO;
using System.Text;

namespace Mau.Runtime
{
    /// <summary>
    /// 审计存储——第四根支柱（查询/时序留痕）基座：环形缓冲全量 + 固有寻路落盘。
    /// 寻路格式：Data/audit/{sessionId}/{yyyyMMdd}.md——会话=目录，日期=文件，每文件带会话头。
    /// 写入侧 = 框架全部职责：只保证事件产生 + 落盘，不做动态查询（读取三形态归 AuditQuery，未来）。
    /// </summary>
    public sealed class AuditStore
    {
        // [段1] 状态字段
        /// <summary>事件锁——seq/缓冲/落盘统一互斥</summary>
        private readonly object _gate = new object();
        /// <summary>环形缓冲——全量事件（满覆盖最旧）</summary>
        private readonly AuditEvent[] _ring;
        /// <summary>环形缓冲容量</summary>
        private readonly int _capacity;
        /// <summary>时间源——测试注入固定时间（默认 DateTime.Now）</summary>
        private readonly Func<DateTime> _now;
        /// <summary>全局事件序号——lock 内单调分配</summary>
        private long _seq;
        /// <summary>环形缓冲写指针</summary>
        private long _writeIndex;
        /// <summary>累计事件总数——统计数据源</summary>
        private long _total;
        /// <summary>已配置标记——ConfigureAudit 幂等</summary>
        private bool _configured;
        /// <summary>审计根目录——宿主注入（ConfigureAudit）</summary>
        private string _root = "";
        /// <summary>会话 ID——yyyyMMdd_HHmmss 形态</summary>
        private string _sessionId = "";
        /// <summary>会话目录路径——root/sessionId</summary>
        private string _sessionDir = "";
        /// <summary>运行模式——会话头字段（run/test 等）</summary>
        private string _mode = "";
        /// <summary>加载实体数——会话头字段</summary>
        private int _loadCount;
        /// <summary>会话保留天数——默认 7，启动/跨天清理</summary>
        private int _retainDays = 7;
        /// <summary>落盘总开关——trace 类由事件 Persistable 逐条控制</summary>
        private bool _persistEnabled = true;
        /// <summary>当日文件写入器——null = 未配置/未落盘</summary>
        private StreamWriter? _writer;
        /// <summary>当前日期——跨天滚动基准（yyyyMMdd）</summary>
        private string _currentDate = "";
        /// <summary>上次 flush 时间——1s 批量阈值</summary>
        private DateTime _lastFlush;
        /// <summary>上次清理日期——按日清理基准</summary>
        private DateTime _lastCleanDate;
        /// <summary>已关闭标记——Shutdown 幂等</summary>
        private bool _disposed;
        /// <summary>
        /// 全局当前帧号——FlowRunner.Tick 驱动（TickFrame 更新）
        /// </summary>
        private long _currentFrame;

        /// <summary>
        /// flow.tick 事件开关——默认关（价值最低量最大）
        /// </summary>
        private bool _enableTickEvents;

        /// <summary>
        /// 程序级默认实例——RuntimeLog 静态埋点入口（ConfigureAudit 时设置）
        /// </summary>
        public static AuditStore? Default
        {
            get;
            set;
        }

        /// <summary>
        /// 重置程序级默认实例——关闭并置空（D26 统一 Reset 契约；宿主切换/测试隔离调用）
        /// </summary>
        public static void Reset()
        {
            AuditStore? current = Default;
            Default = null;
            if (current != null)
            {
                current.Shutdown();
            }
        }
        /// <summary>
        /// 构造审计存储——环形缓冲容量（默认 10000，满则覆盖最旧）
        /// </summary>
        /// <param name="capacity">环形缓冲容量</param>
        /// <param name="now">时间源——测试注入固定时间（默认 DateTime.Now）</param>
        public AuditStore(int capacity = 10000, Func<DateTime>? now = null)
        {
            _capacity = capacity;
            _ring = new AuditEvent[capacity];
            if (now == null)
            {
                _now = () => DateTime.Now;
            }
            else
            {
                _now = now;
            }
        }

        /// <summary>
        /// 已配置事件总数——环形缓冲总写入量（统计数据源之一）
        /// </summary>
        public long Total
        {
            get
            {
                lock (_gate)
                {
                    return _total;
                }
            }
        }
        /// <summary>
        /// 环形缓冲容量——查询数据源边界（AuditQuery 输出标注用；落盘文件为 MD 留痕不参与查询）
        /// </summary>
        public int RingCapacity
        {
            get
            {
                lock (_gate)
                {
                    return _capacity;
                }
            }
        }
        /// <summary>
        /// 注入审计根目录——创建会话目录 + 写会话头 + 启动过期清理。宿主启动时调用一次（幂等）。
        /// </summary>
        /// <param name="root">审计根目录（如 Data/audit）</param>
        /// <param name="mode">运行模式（run/test 等）——会话头字段</param>
        /// <param name="loadCount">加载实体数——会话头字段</param>
        /// <param name="startFrame">起始帧——会话头字段（宿主从 FlowRunner 取）</param>
        /// <param name="retainDays">会话保留天数（默认 7）</param>
        /// <param name="persistEnabled">落盘总开关（默认 true——trace 类由事件 Persistable 逐条控制）</param>
        public void ConfigureAudit(string root, string mode, int loadCount = 0, long startFrame = 0, int retainDays = 7, bool persistEnabled = true)
        {
            lock (_gate)
            {
                // [段1] 幂等检查
                if (_configured)
                {
                    return;
                }
                if (root == null || root.Length == 0)
                {
                    throw new ArgumentException("AuditStore root is empty.", "root");
                }
                // [段2] 会话初始化
                _root = root;
                _mode = mode;
                _loadCount = loadCount;
                _retainDays = retainDays;
                _persistEnabled = persistEnabled;
                _sessionId = _now().ToString("yyyyMMdd_HHmmss");
                _sessionDir = Path.Combine(root, _sessionId);
                Directory.CreateDirectory(_sessionDir);
                _currentDate = _now().ToString("yyyyMMdd");
                _lastFlush = _now();
                _lastCleanDate = _now().Date;
                // [段3] 首个会话文件 + 会话头
                OpenWriterForDate(_currentDate, startFrame);
                _configured = true;
                Default = this;
                // [段4] 启动清理——过期会话目录
                CleanOldSessionsLocked();
                // [段5] 会话启动事件——app.start（生命周期类别）
                Record("AuditStore", "app.start", startFrame, new AuditProp[] {
                    new AuditProp("version", VersionInfo.GetEntryVersion()),
                    new AuditProp("mode", _mode),
                    new AuditProp("loadCount", _loadCount.ToString()),
                    new AuditProp("sessionId", _sessionId)
                });
            }
        }

        /// <summary>
        /// 记录审计事件——seq 分配 + 环形缓冲写入 + 按需落盘（1s 定时 flush + 跨天滚动）。
        /// 未配置时仅内存（环形缓冲仍工作）；配置后按 Persistable 落盘。
        /// </summary>
        /// <param name="source">来源（CommandBus/OA/FlowRunner/ConfigStore/...）</param>
        /// <param name="category">类别（cmd.set/oa.post/trace.*/cfg.load/log.*/...）</param>
        /// <param name="frame">全局帧号</param>
        /// <param name="props">属性键值数组（可空——无属性事件）</param>
        /// <param name="persistable">是否落盘（默认 true）</param>
        public void Record(string source, string category, long frame, AuditProp[]? props, bool persistable = true)
        {
            lock (_gate)
            {
                // [段1] 分配序号 + 构造事件
                if (_disposed)
                {
                    return;
                }
                _seq = _seq + 1;
                long actualFrame = _currentFrame;
                if (frame >= 0)
                {
                    actualFrame = frame;
                }
                AuditProp[] effectiveProps;
                if (props == null)
                {
                    effectiveProps = Array.Empty<AuditProp>();
                }
                else
                {
                    effectiveProps = props;
                }
                AuditEvent ev = new AuditEvent(_seq, actualFrame, _now().ToString("HH:mm:ss"), source, category, effectiveProps, persistable);
                // [段2] 环形缓冲写入（满则覆盖最旧）
                _ring[(int)(_writeIndex % _capacity)] = ev;
                _writeIndex = _writeIndex + 1;
                _total = _total + 1;
                // [段3] 落盘路径——配置 + 开关 + 可落盘三条件
                if (_configured && _persistEnabled && persistable)
                {
                    // [段4] 跨天滚动检查
                    RollDateIfNeededLocked(ev.Frame);
                    if (_writer != null)
                    {
                        _writer.Write(FormatEvent(ev));
                        // [段5] 定时 flush（1s 批量）
                        DateTime now = _now();
                        if ((now - _lastFlush).TotalSeconds >= 1.0)
                        {
                            _writer.Flush();
                            _lastFlush = now;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 环形缓冲只读快照——最近事件（最多 capacity 条，写时拷贝）。测试/统计/未来查询的数据源。
        /// </summary>
        /// <returns>事件数组——时间序（旧→新）</returns>
        public AuditEvent[] Snapshot()
        {
            lock (_gate)
            {
                long count = Math.Min(_total, _capacity);
                AuditEvent[] result = new AuditEvent[count];
                long start = _writeIndex - count;
                for (long i = 0; i < count; i = i + 1)
                {
                    result[i] = _ring[(int)((start + i) % _capacity)];
                }
                return result;
            }
        }

        /// <summary>
        /// 强制落盘——当前未 flush 的事件写入磁盘
        /// </summary>
        public void Flush()
        {
            lock (_gate)
            {
                if (_writer != null)
                {
                    _writer.Flush();
                }
                _lastFlush = _now();
            }
        }

        /// <summary>
        /// 关闭——flush + 释放写入器（幂等）。宿主退出时调用。
        /// </summary>
        public void Shutdown()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }
                // 会话停止事件——app.stop（生命周期类别，落盘在关闭前完成）
                Record("AuditStore", "app.stop", -1, new AuditProp[] {
                    new AuditProp("total", _total.ToString()),
                    new AuditProp("sessionId", _sessionId)
                });
                _disposed = true;
                if (_writer != null)
                {
                    _writer.Flush();
                    _writer.Dispose();
                    _writer = null;
                }
            }
        }

        /// <summary>
        /// 打开当日会话文件——关旧开新 + 写文件头（会话头，跨天文件同样携带）
        /// </summary>
        /// <param name="date">日期 yyyyMMdd</param>
        /// <param name="startFrame">会话头起始帧</param>
        private void OpenWriterForDate(string date, long startFrame)
        {
            // [段1] 关闭旧写入器
            if (_writer != null)
            {
                _writer.Flush();
                _writer.Dispose();
                _writer = null;
            }
            // [段2] 打开当日文件——FileShare.ReadWrite（审计文件允许并发读：观测者/AuditQuery 读正在写的文件）
            string filePath = Path.Combine(_sessionDir, date + ".md");
            FileStream fs = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            StreamWriter w = new StreamWriter(fs, new UTF8Encoding(false));
            // [段3] 文件头——会话头
            w.Write(BuildSessionHeader(startFrame));
            _writer = w;
        }        /// <summary>
        /// 构建会话头文本——版本/模式/加载数/起始帧/起始时间/sessionId（每文件一个）
        /// </summary>
        /// <param name="startFrame">起始帧</param>
        /// <returns>会话头 MD 文本</returns>
        private string BuildSessionHeader(long startFrame)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("# Audit Session ").Append(_sessionId).AppendLine();
            sb.Append("# 版本: ").Append(VersionInfo.GetEntryVersion()).AppendLine();
            sb.Append("# 模式: ").Append(_mode).AppendLine();
            sb.Append("# 加载数: ").Append(_loadCount).AppendLine();
            sb.Append("# 起始帧: ").Append(startFrame).AppendLine();
            sb.Append("# 起始时间: ").Append(_now().ToString("yyyy-MM-dd HH:mm:ss")).AppendLine();
            sb.AppendLine();
            return sb.ToString();
        }
        /// <summary>
        /// 跨天滚动检查——日期变化则关旧开新（新文件带会话头）+ 按日清理
        /// </summary>
        /// <param name="frame">当前事件帧号——跨天文件会话头起始帧</param>
        private void RollDateIfNeededLocked(long frame)
        {
            string date = _now().ToString("yyyyMMdd");
            if (date == _currentDate)
            {
                return;
            }
            _currentDate = date;
            OpenWriterForDate(date, frame);
            // 顺带按日清理
            if (_now().Date != _lastCleanDate)
            {
                _lastCleanDate = _now().Date;
                CleanOldSessionsLocked();
            }
        }

        /// <summary>
        /// 清理过期会话目录——目录名 yyyyMMdd_HHmmss 前缀日期早于阈值则删除（占用忽略）
        /// </summary>
        private void CleanOldSessionsLocked()
        {
            if (_root.Length == 0)
            {
                return;
            }
            DateTime threshold = _now().Date.AddDays(-_retainDays);
            string[] dirs = Directory.GetDirectories(_root);
            for (int i = 0; i < dirs.Length; i = i + 1)
            {
                string name = Path.GetFileName(dirs[i]);
                if (name.Length >= 8)
                {
                    if (DateTime.TryParseExact(name.Substring(0, 8), "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out DateTime dirDate))
                    {
                        if (dirDate < threshold)
                        {
                            try
                            {
                                Directory.Delete(dirs[i], true);
                            }
                            catch (IOException)
                            {
                                // 目录被占用——忽略，下轮再清
                            }
                            catch (UnauthorizedAccessException)
                            {
                                // 权限不足——忽略，下轮再清
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 事件 MD 格式化——标题行 + 属性行 + 空行
        /// </summary>
        /// <param name="ev">审计事件</param>
        /// <returns>MD 文本（含结尾换行）</returns>
        internal static string FormatEvent(AuditEvent ev)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("## E").Append(ev.Seq.ToString("D7"))
              .Append(" | F").Append(ev.Frame.ToString("D6"))
              .Append(" | ").Append(ev.Time)
              .Append(" | ").Append(ev.Source)
              .Append(" | ").Append(ev.Category).AppendLine();
            AuditProp[] props = ev.Props;
            for (int i = 0; i < props.Length; i = i + 1)
            {
                sb.Append("- ").Append(props[i].Key).Append(": ").Append(props[i].Value).AppendLine();
            }
            sb.AppendLine();
            return sb.ToString();
        }
        /// <summary>
        /// 全局当前帧号——FlowRunner.Tick 驱动（TickFrame 更新），埋点坐标统一来源
        /// </summary>
        public long CurrentFrame
        {
            get
            {
                lock (_gate)
                {
                    return _currentFrame;
                }
            }
        }

        /// <summary>
        /// flow.tick 事件开关——默认关（价值最低量最大；需要时开，建议每 60 帧）
        /// </summary>
        public bool EnableTickEvents
{
    get
    {
        lock (_gate)
        {
            return _enableTickEvents;
        }
    }

    set
    {
        lock (_gate)
        {
            _enableTickEvents = value;
        }
    }
}/// <summary>
        /// <summary>
        /// 推进全局帧号——FlowRunner.Tick 每帧调用（全局帧号坐标的唯一来源）
        /// </summary>
        /// <param name="frame">宿主帧号</param>
        public void TickFrame(long frame)
        {
            lock (_gate)
            {
                _currentFrame = frame;
            }
        }

        /// <summary>
        /// 载荷摘要——{值类型}:{长度}:{前16字符}（设计 §五 按量级封魔）。密钥永不进事件——埋点侧只传 configured/missing 标记。
        /// </summary>
        /// <param name="value">原始值（null → "null"）</param>
        /// <returns>摘要文本</returns>
        public static string Summarize(string? value)
        {
            if (value == null)
            {
                return "null";
            }
            int len = value.Length;
            int take = len;
            if (take > 16)
            {
                take = 16;
            }
            string head = value.Substring(0, take);
            return "str:" + len + ":\"" + head + "\"";
        }
    }
}

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Mau.Runtime;
using CatHome4.Contracts;

namespace CH4
{
    /// <summary>
    /// 会话相位——P9.1 四相环（动作段 StartLlm/LlmDone/StartToolBatch/ToolBatchDone/Done 封装进相位 Enter/Exit——design-llm-streaming §6.2）。
    /// </summary>
    internal enum ChatPhase
    {
        /// <summary>空闲——无活跃轮次（可接收用户消息）</summary>
        Idle,
        /// <summary>LLM 后台流式等待——主线程轮询完成槽</summary>
        LlmRunning,
        /// <summary>工具批执行——Dog 逐帧轮询</summary>
        ToolBatchRunning,
        /// <summary>终态动作段——前文落盘 + 状态复位（一帧完成回 Idle）</summary>
        Done
    }

    /// <summary>
    /// 宿主会话实体——上下文/前文/LLM 槽/工具批/相位环（P9.1 会话对象化——design-llm-streaming §六规格）。
    /// 线程模型：相位与上下文仅主线程推进；LLM 槽后台 Task.Run 写、主线程轮询（volatile Busy 置位/复位）。
    /// SSE 推送/日志/工具直执经宿主服务引用回调——ChatSession 不持有宿主静态面。
    /// </summary>
    internal sealed partial class ChatSession
    {
        /// <summary>LLM 超时帧上限——同原 WaitForLlm MaxFramesPerRun 语义（30000 帧 ≈ 25 分钟空转上限）</summary>
        private const long LlmTimeoutFrames = 30000;

        /// <summary>工具批等待帧上限——最长工具 2400 帧（120 秒，A48 统一口径）+ 600 余量</summary>
        private const long ToolBatchWaitFrames = 2400 + 600;

        /// <summary>工具单 Dog owner ID——宿主 Dog 域（同 ToolOwnerId——OA 未开存活校验，多 Dog 未来可扩展独立 ID）</summary>
        private const long ToolOwnerId = 1;

        // [段1] 标识与持久化
        /// <summary>会话唯一 ID——构造注入 = 猫 key（唯一标识；会话重建不换键）</summary>
        private string _id;

        /// <summary>显示名——P9.1 为 "majordomo"；P9.3 用户输入</summary>
        private readonly string _displayName;

        /// <summary>消息历史——会话上下文容器（第一条 system；Mau.Runtime 实体复用）</summary>
        private readonly ChatContext _context;

        /// <summary>前文落盘——P9.1 保持 Data/sessions/majordomo.json（sessions/&lt;id&gt;.json 化是 P9.4）</summary>
        private readonly SessionStore _store;
        // [段2] LLM 槽 6 字段——后台 Task.Run 写、主线程轮询读
        /// <summary>LLM 后台运行中——volatile 置位（后台 finally 复位）</summary>
        private volatile bool _llmBusy;

        /// <summary>LLM 后台结果——完整回复文本</summary>
        private string _llmResultText;

        /// <summary>LLM 后台结果——完整思考文本（工具轮次回传铁律）</summary>
        private string _llmReasoning;

        /// <summary>LLM 后台结果——tool_calls JSON 数组（translate 聚合后整体）</summary>
        private string _llmToolCallsJson;

        /// <summary>LLM 后台错误标志</summary>
        private bool _llmError;

        /// <summary>LLM 后台错误文本（ERR| 前缀——失败可见性）</summary>
        private string _llmErrorText;

        /// <summary>本轮是否发生过重试——首个 Text/Reasoning 到达时回填 retry 视图 resolved（S2 §8.4）</summary>
        private bool _sawRetry;

        /// <summary>retry 气泡视图序号——多次重试复用同一气泡（replaceSeq 替换不堆叠）</summary>
        private long _retrySeq;

        /// <summary>retry 视图块索引——同一重试序列原位更新落盘块（-1=无块；轮终复位）</summary>
        private int _retryBlockIndex = -1;

        /// <summary>retry 气泡原文快照——最近一次重试的尝试序号（A86：resolved 保留报错信息）</summary>
        private string _retryAttempt = "";

        /// <summary>retry 气泡原文快照——最近一次重试的次数上限（A86）</summary>
        private string _retryMax = "";

        /// <summary>retry 气泡原文快照——最近一次重试的原因摘要（含中文注释；A86）</summary>
        private string _retryReason = "";

        /// <summary>空回复续传计数——无回复/纯空格时同上下文重发（无上限——LLM 兜底；整轮清零）</summary>
        private int _emptyReplyRetry;

        /// <summary>STREAM_CLOSED 续传标志——SSE 流未以 [DONE] 结束时置位（PumpLlm 检查；续传后复位）</summary>
        private bool _streamClosedRetry;

        // [段2b] P6 中止——暂停标志与取消令牌（LLM 流物理取消面）
        /// <summary>中止请求标志——HTTP 线程置位（volatile 跨线程可见），主线程 Pump 消费执行收尾</summary>
        private volatile bool _pauseRequested;

        /// <summary>本轮 LLM 流取消令牌——LaunchLlm 创建；Pause 时 Cancel（下一轮重建——取消不跨轮）</summary>
        private System.Threading.CancellationTokenSource _pauseCts;

        /// <summary>Token 用量整轮累计——prompt（含 cache hit）</summary>
        private long _usagePrompt;

        /// <summary>Token 用量整轮累计——completion</summary>
        private long _usageCompletion;

        /// <summary>Token 用量整轮累计——cache hit（CH2 双格式：prompt_cache_hit_tokens / cached_tokens）</summary>
        private long _usageCacheHit;

        /// <summary>Token 用量会话累计——prompt（本次请求增量随轮同期堆入；仅新会话复位，跨轮保留）</summary>
        private long _sessionPrompt;

        /// <summary>Token 用量会话累计——completion（跨轮持续累加；仅新会话复位）</summary>
        private long _sessionCompletion;

        /// <summary>Token 用量会话累计——cache hit（跨轮持续累加；仅新会话复位）</summary>
        private long _sessionCacheHit;

        /// <summary>最近一轮真实 usage 统计——CloseRound 落盘（info 自查/前端显示数据源；零估算）</summary>
        private SessionStats _lastStats;

        /// <summary>单次前文长度——最近一次请求的 prompt（覆盖式；非累计——前文长度数据源）</summary>
        private long _contextTokens;

        /// <summary>本轮开始时间戳——Stopwatch.GetTimestamp（roundsum 总耗时）</summary>
        private long _roundStartTick;

        /// <summary>本轮工具调用次数——工具 Dog 登记处累加（roundsum toolCount）</summary>
        private int _toolCallCount;

        /// <summary>本轮工具主动 done 标记——sleep 等待登记 / restart 收尾登记置位（roundsum done=tool；QQ 转发面据此续约来源）</summary>
        private bool _toolDone;

        /// <summary>当前运行态——PhaseIdle/Wait/Link/Think/Tool/Run/Reply（-1=无活跃）</summary>
        private int _phaseKind = -1;

        /// <summary>当前态开始时间戳——Stopwatch.GetTimestamp</summary>
        private long _phaseStartTick;

        /// <summary>七态累计毫秒——idle/wait/link/think/tool/run/reply（态切换结算；idle 恒 0——空闲不计时）</summary>
        private long[] _phaseAccumMs = new long[PhaseCount];

        /// <summary>态字段锁——后台消费线程切态、主线程结算与读面（design-ch4-llm §2.1 线程安全）</summary>
        private readonly object _phaseLock = new object();

        /// <summary>本轮 API 请求次数——每次 LaunchLlm 累加（含重试重发与空回复续传；= link 段数）</summary>
        private int _requestCount;

        /// <summary>运行态常量——空闲（本地·空转：轮未开始或收尾中；不计时——只作态名）</summary>
        private const int PhaseIdle = 0;

        /// <summary>运行态常量——等待（本地·退避：重试退避期间；长度由本地决定）</summary>
        private const int PhaseWait = 1;

        /// <summary>运行态常量——链路（远端·等待：请求发出到首个语义增量帧；长度由远端决定）</summary>
        private const int PhaseLink = 2;

        /// <summary>运行态常量——思考（远端·流：reasoning_content 增量）</summary>
        private const int PhaseThink = 3;

        /// <summary>运行态常量——工具（远端·流：LLM 输出 tool_calls 决策的流式过程）</summary>
        private const int PhaseTool = 4;

        /// <summary>运行态常量——执行（本地·程序过程：工具批发单到下一请求发出）</summary>
        private const int PhaseRun = 5;

        /// <summary>运行态常量——回复（远端·流：content 增量）</summary>
        private const int PhaseReply = 6;

        /// <summary>运行态态数——七态（索引与常量一一对应）</summary>
        private const int PhaseCount = 7;

        /// <summary>运行态名表——索引对应（JSON 产出与观测面文本化）</summary>
        private static readonly string[] PhaseNames = { "idle", "wait", "link", "think", "tool", "run", "reply" };

        // [段3] 工具批
        /// <summary>工具批执行中——reload 拒绝检查面（任一会话 TRUE 即拒绝）</summary>
        private bool _toolBatchActive;
        /// <summary>工具单列表——普通工单（OA 认领；host-* 延迟直执登记）</summary>
        private readonly List<ToolOrderDog> _dogs;
        /// <summary>host-* 延迟直执清单——批次末尾宿主直执（顺序保证：同批 mau-proj 等先完成产物落地）</summary>
        private readonly List<ToolOrderDog> _hostDogs;
        /// <summary>timeback back 后置执行单——批内剥离出的 back（A106 批内次序：批内其余工具完成后执行；null=本批无 back）</summary>
        private ToolOrderDog _timebackBackDog;

        // [段4] 相位环
        /// <summary>当前相位</summary>
        private ChatPhase _phase;

        /// <summary>当前工具轮次（0 起——无限续轮，直到 LLM 给出最终回复；日志轮数显示）</summary>
        private long _round;

        /// <summary>当前相位已运行帧数——超时计时面</summary>
        private long _phaseFrames;

        /// <summary>待处理用户消息队列——忙时排队（原同步阻塞天然排队语义保持）</summary>
        /// <summary>
        /// 待处理消息实体——内容 + 来源（user=人发送/system=系统自动）。
        /// </summary>
        private sealed class PendingMessage
        {
            /// <summary>消息内容</summary>
            public string Content;

            /// <summary>来源——user/system</summary>
            public string Source;
        }

        /// <summary>待处理用户消息队列——忙时排队（原同步阻塞天然排队语义保持）</summary>
        private readonly Queue<PendingMessage> _pending;

        // [段5] 宿主服务引用（构造注入——宿主级共享面）
        /// <summary>LLM 运行时——ChatStream 调度（宿主 Bootstrap 注入；M3 apiConfigId 切换 SwapLlmRuntime 替换）</summary>
        private ILlmRuntime _llmRuntime;

        /// <summary>OA 工单平台——Dog Post/轮询</summary>
        private readonly OA _oa;

        /// <summary>工具定义表——后台流式携带（宿主 BuildToolSpecs 产物；M3 改 toolNames 新会话 SetToolSpecs 更新）</summary>
        private ToolSpec[] _tools;

        /// <summary>
        /// 当前授权工具名提供者——catKey → 工具名数组（Admin 域实时解析当前 cat.cfg 名单；宿主启动期组合根注入）。
        /// 授权面与注入面解耦（design-ch4-tools §三·十一）：注入面 _tools 是会话级提示快照，授权面实时查询。
        /// 未接线（测试 / 裸构造）→ 回落 _tools 快照（旧语义）+ 一次告警——不静默放宽、不静默拒绝。
        /// </summary>
        internal static Func<string, string[]> AuthorizedToolNamesProvider;

        /// <summary>授权面提供者缺失告警标志——只报一次（防每次判定刷屏）</summary>
        private bool _authorizedProviderWarned;

        /// <summary>宿主工具直执回调——host-* 延迟直执（批次末尾执行；OA 工具一律走 OA 认领，无直执）</summary>
        private readonly Func<string, string, string> _executeTool;

        /// <summary>猫 key——工具执行按猫裁剪（M4e 白名单；默认猫=majordomo；多猫=会话 ID）</summary>
        private string _catKey = "";

        /// <summary>缓存隔离键取值非法告警标志——只报一次（防每请求刷屏）</summary>
        private bool _cacheIsolationWarned;

        /// <summary>
        /// 设置猫 key——构造后由创建方赋值（默认猫 majordomo / 多猫会话 ID）。
        /// </summary>
        /// <param name="catKey">猫 key</param>
        public void SetCatKey(string catKey)
        {
            _catKey = catKey;
        }

        /// <summary>
        /// 缓存隔离键——LLM 请求身份（请求体 user_id + x-opencode-session 头）。
        /// 取值按全局配置 llm.cache_isolation：cat（默认——键 = 猫 key，会话重建不换键）/ off（不携带 user_id）。
        /// 非法值回落 cat 并告警一次（读面兜底 + 失败可见）。
        /// </summary>
        /// <returns>隔离键（空=不隔离——Provider 侧回落默认会话头）</returns>
        private string ResolveCacheIsolationKey()
        {
            string mode = "cat";
            ConfigStore cfg = null;
            if (DataBox.TryResolve<ConfigStore>(out cfg) && cfg != null)
            {
                string value = cfg.Get("llm.cache_isolation", "");
                if (value.Length > 0)
                {
                    mode = value.Trim().ToLowerInvariant();
                }
            }
            if (mode == "off")
            {
                return "";
            }
            if (mode != "cat" && !_cacheIsolationWarned)
            {
                _cacheIsolationWarned = true;
                LogStore.Add("LLM", 2, "llm.cache_isolation 取值非法（" + mode + "）——回落 cat；合法值 cat/off", "CONFIG");
            }
            return _catKey;
        }

        /// <summary>
        /// 工具执行包装——设置猫上下文后调宿主直执（M4e：按猫解析；执行后恢复）。仅 host-* 延迟直执使用。
        /// </summary>
        /// <param name="name">工具名</param>
        /// <param name="args">参数 JSON</param>
        /// <returns>执行结果</returns>
        private string RunTool(string name, string args)
        {
            string prev = ToolCatContext.CurrentCatKey;
            ToolCatContext.SetCat(_catKey);
            try
            {
                return _executeTool(name, args);
            }
            finally
            {
                ToolCatContext.SetCat(prev);
            }
        }

        /// <summary>HTTP 外观层——SSE 转发面（Bootstrap 段6 宿主 HTTP 启动后 Attach 赋值——构造时宿主 HTTP 未启动）</summary>
        private IHostPush _httpHost;

        /// <summary>环境信息提供器——info 内置工具数据源（入口壳注入；空=工具返回不可用）</summary>
        private Func<string> _envInfoProvider;

        /// <summary>本轮结束通知回调——入口壳注入（Q：托盘 BalloonTip——displayName + reply 摘要/Token 统计）</summary>
        private Action<string, string> _roundNotify;

        /// <summary>
        /// 建立会话实体——宿主级服务经构造注入；会话生命周期数据自持。
        /// </summary>
        /// <param name="id">会话 ID（时间戳注入）</param>
        /// <param name="displayName">显示名</param>
        /// <param name="context">消息历史容器</param>
        /// <param name="store">前文落盘</param>
        /// <param name="llmRuntime">LLM 运行时</param>
        /// <param name="oa">OA 工单平台</param>
        /// <param name="tools">工具定义表</param>
        /// <param name="executeTool">工具直执回调</param>
        /// <param name="viewStore">视图存储——F4 视图持久化</param>
        public ChatSession(string id, string displayName, ChatContext context, SessionStore store, ILlmRuntime llmRuntime, OA oa, ToolSpec[] tools, Func<string, string, string> executeTool, SessionViewStore viewStore)
        {
            if (id == null)
            {
                id = "";
            }
            if (displayName == null)
            {
                displayName = "";
            }
            _id = id;
            _displayName = displayName;
            _context = context;
            _store = store;
            _viewStore = viewStore;
            _llmRuntime = llmRuntime;
            _oa = oa;
            _tools = FilterPrivilegedSpecs(tools);
            _executeTool = executeTool;
            _dogs = new List<ToolOrderDog>();
            _hostDogs = new List<ToolOrderDog>();
            _pending = new Queue<PendingMessage>();
            _phase = ChatPhase.Idle;
            _round = 0;
            _phaseFrames = 0;
            _llmResultText = "";
            _llmReasoning = "";
            _llmToolCallsJson = "";
            _llmErrorText = "";
            _httpHost = null;
        }

        /// <summary>真实前文末条消息——append 后取最近一条生成视图块（F4 视图钩子）</summary>
        private LlmMessage LastMessage()
        {
            LlmMessage[] all = _context.GetMessages();
            return all[all.Length - 1];
        }

        /// <summary>当前视图时间戳——Unix 毫秒（视图排序键——真实时序权威，跨重启稳定；替代宿主帧号）</summary>
        private static long ViewTimestamp()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        /// <summary>重建视图层——从真实前文完全重置（启动恢复后调用；真实前文绝对可用）</summary>
        public void RebuildView()
        {
            _viewStore.Rebuild(_context.GetMessages());
            // 注入报告——启动恢复时从 view.json 读回（非真实前文派生；Rebuild 不重建）
            _viewStore.LoadInjectReport();
        }
        /// <summary>
        /// 旧会话留档——session.new 清空前导出（A87：user 消息 / 正式回复 / 注入报告 / 每轮结算 → sessions_old 下 MD 文件）。
        /// </summary>
        /// <returns>落盘文件绝对路径（空串=未生成——失败已由视图层记 ERR 日志）</returns>
        public string ArchiveLegacyView()
        {
            string path = _viewStore.ArchiveLegacy(_id, _displayName);
            if (path.Length > 0)
            {
                LogStore.Add("CatHome4", 1, "旧会话留档: " + path, "CHAT");
            }
            return path;
        }

        /// <summary>清空视图层——session.new 清前文时同步（视图随生命周期清理）</summary>
        public void ClearView()
        {
            _viewStore.Clear();
        }

        /// <summary>
        /// 消息落盘——上下文追加后立即 append（A47 增量落盘：每消息完成即落盘，崩溃只影响最后一行）。
        /// </summary>
        /// <param name="msg">追加的消息（可空=未入上下文）</param>
        private void AppendMessage(LlmMessage? msg)
        {
            if (msg == null || _store == null)
            {
                return;
            }
            _store.Append(msg.Value);
        }

        /// <summary>
        /// 会话重置显式事件——session.new 清前文后推送（前端收到后清空气泡再拉 history——消除竞态；问题一修复）。
        /// </summary>
        public void PushSessionReset()
        {
            if (_httpHost != null)
            {
                string resetJson = "{\"type\":\"session_reset\"}";
                _httpHost.PushView("control", resetJson, -1, 0);
            }
        }

        /// <summary>
        /// 设置已加载统计——启动恢复时从会话文件读出（TryLoad 带 stats 重载；旧文件 null=零值）。
        /// </summary>
        /// <param name="stats">持久化统计（可空）</param>
        public void SetLoadedStats(SessionStats? stats)
        {
            if (stats != null)
            {
                _lastStats = stats.Value;
            }
        }

        /// <summary>
        /// 重置统计——session.new 清前文后调用（新会话零统计起算；会话级 token 累计同归零）。
        /// </summary>
        public void ResetStats()
        {
            _lastStats = new SessionStats();
            // 会话级 token 累计——新会话唯一归零点（轮级由 StartRound 逐轮清零；回滚不清——同会话延续）
            _sessionPrompt = 0;
            _sessionCompletion = 0;
            _sessionCacheHit = 0;
        }

        /// <summary>
        /// 最近一轮真实 usage 统计——info 自查/前端显示数据源（零估算；CloseRound 更新）。
        /// </summary>
        public SessionStats LastStats
        {
            get
            {
                return _lastStats;
            }
        }

        /// <summary>
        /// 前文条数——送入 LLM 的消息数（含注入块）。
        /// 消费面：图片包裹编号前缀（组装时刻取真值）+ 前端「前文 n 条」文案口径（HTTP 线程读取，只读计数）。
        /// </summary>
        internal int ContextCount
        {
            get
            {
                return _context.GetMessageCount();
            }
        }

        /// <summary>
        /// 最后一次前文变动时刻——Unix 毫秒（0=从未变动）；展示层自行格式化「距今」。
        /// 数据源 = ChatContext.LastChangeAt（含工具结果写入；恢复导入不刷新）。消费面：info 自查 / 快照 sessions 段。
        /// </summary>
        public long LastContextChangeAt
        {
            get
            {
                return _context.LastChangeAt;
            }
        }

        /// <summary>
        /// 实时前文长度——最近一次请求的 prompt（请求级；每次 usage 帧覆盖）。
        /// 对照 LastStats.LastContextTokens（轮末落盘的最近一轮值）；info / cat.info 消费 ContextTokensKnown（实时优先 + 轮末回落）。
        /// </summary>
        public long ContextTokens
        {
            get
            {
                return _contextTokens;
            }
        }
        /// <summary>
        /// 已知最新前文长度——请求级实时值优先，未发起过请求（或宿主重启后未请求）回落最近一轮轮末落盘值。
        /// 消费面：info tokens 段 / cat.info 每猫 context 字段——「看别猫」场景需要 idle 猫也有可读值（两者皆真实 usage 值，零估算）。
        /// </summary>
        public long ContextTokensKnown
        {
            get
            {
                if (_contextTokens > 0)
                {
                    return _contextTokens;
                }
                return _lastStats.LastContextTokens;
            }
        }

        /// <summary>
        /// 设置注入报告——session.new 后调用（持久化进视图：内存设置 + view.json 落盘；独立字段 Rebuild 不清）。
        /// </summary>
        /// <param name="json">注入报告 JSON（BuildInjectReportJson 产物）</param>
        public void SetInjectReport(string json)
        {
            _viewStore.SetInjectReport(json);
            _viewStore.Save();
        }

        /// <summary>内存视图块——按生成序（history 数据源；F4 视图持久化）</summary>
        public ViewBlock[] GetViewBlocks()
        {
            return _viewStore.GetBlocks();
        }

        /// <summary>会话唯一 ID——构造注入 = 猫 key（唯一标识；无独立"会话身份"层）</summary>
        public string Id
        {
            get
            {
                return _id;
            }
        }

        /// <summary>会话显示名（P9.3 前端可见）</summary>
        public string DisplayName
        {
            get
            {
                return _displayName;
            }
        }

        /// <summary>消息历史——会话操纵面复用（视图/清空/计数）</summary>
        public ChatContext Context
        {
            get
            {
                return _context;
            }
        }

        /// <summary>视图存储——视图块读取面（Admin 装配 QQBot 转发/历史数据源；internal——IVT 域消费）</summary>
        internal SessionViewStore ViewStore
        {
            get
            {
                return _viewStore;
            }
        }

        /// <summary>前文落盘——会话操纵面复用（session.new 重注入落盘）</summary>
        public SessionStore Store
        {
            get
            {
                return _store;
            }
        }

        /// <summary>工具批执行中——reload 拒绝检查（任一会话 TRUE 即拒绝）</summary>
        public bool ToolBatchActive
        {
            get
            {
                return _toolBatchActive;
            }
        }

        /// <summary>
        /// 更新工具注入面——session.new 时从最新 cat.cfg 重裁剪后调用（会话级生效；授权面实时查询见 IsToolAllowed——design-ch4-tools §三·十一）。
        /// </summary>
        /// <param name="specs">新注入面数组（FilterToolSpecs 产物）</param>
        public void SetToolSpecs(ToolSpec[] specs)
        {
            _tools = FilterPrivilegedSpecs(specs);
        }
        /// <summary>
        /// 会话注入面工具名——_tools 快照派生（info tools_drift 左值：与本猫当前授权集比对；design-ch4-tools §三·十一）。
        /// </summary>
        public string[] DeclaredToolNames
        {
            get
            {
                if (_tools == null)
                {
                    return new string[0];
                }
                string[] names = new string[_tools.Length];
                for (int i = 0; i < _tools.Length; i = i + 1)
                {
                    names[i] = _tools[i].Name;
                }
                return names;
            }
        }
        /// <summary>
        /// 默认会话判定——特权面（majordomo-*）可见与可调的授权基准。
        /// 判据：猫 key = majordomo（创建方 SetCatKey 后成立）；构造期 catKey 未赋值，以显示名兜底（默认猫 displayName = majordomo）。
        /// 设计依据：design-ch4-host-restart §二——特权面是单点授权，判据不得取运行时会话 ID（时间戳）。
        /// </summary>
        private bool IsDefaultSession
        {
            get
            {
                if (_catKey == "majordomo")
                {
                    return true;
                }
                return _catKey.Length == 0 && _displayName == "majordomo";
            }
        }
        /// <summary>特权面可见性过滤——非默认会话剔除全部特权工具（判据 = 注册面组级标记，见 IsPrivilegedTool；默认会话判定见 IsDefaultSession）。设计依据：声明面（cat.cfg toolNames）的全量保底语义是「防外部损坏」，不构成授权通道（design-ch4-host-restart §二）——故特权工具在会话内单点剔除，与 IsToolAllowed 拦截构成双面。</summary>
        /// <param name="specs">原始工具面</param>
        /// <returns>过滤后的工具面（默认会话原样返回）</returns>
        private ToolSpec[] FilterPrivilegedSpecs(ToolSpec[] specs)
        {
            if (specs == null || IsDefaultSession)
            {
                return specs;
            }
            List<ToolSpec> kept = new List<ToolSpec>();
            for (int i = 0; i < specs.Length; i = i + 1)
            {
                if (!IsPrivilegedTool(specs[i].Name))
                {
                    kept.Add(specs[i]);
                }
            }
            return kept.ToArray();
        }
        /// <summary>特权工具判定——读注册面组级标记（ToolRegistry 单一真相源：工具定义 JSON 根级 privileged；未注册 = 非特权）。</summary>
        /// <param name="name">工具名</param>
        /// <returns>true=特权（仅主干会话可见可调）</returns>
        private static bool IsPrivilegedTool(string name)
        {
            ToolRegistryEntry entry = ToolRegistry.Find(name);
            if (entry == null)
            {
                return false;
            }
            return entry.Privileged;
        }

        /// <summary>
        /// 换 LLM 运行时——apiConfigId 切换立即生效（M3：API 配置切换即时；主线程 Idle 时调用）。
        /// </summary>
        /// <param name="runtime">新运行时实例（按新 API 配置构造）</param>
        public void SwapLlmRuntime(ILlmRuntime runtime)
        {
            _llmRuntime = runtime;
        }

        /// <summary>会话空闲——AllIdle 判定面</summary>
        public bool IsIdle
        {
            get
            {
                return _phase == ChatPhase.Idle;
            }
        }

        /// <summary>当前相位（快照观测面）</summary>
        public ChatPhase Phase
        {
            get
            {
                return _phase;
            }
        }

        /// <summary>当前工具轮次（快照观测面——状态卡预览用）</summary>
        public long Round
        {
            get
            {
                return _round;
            }
        }

        /// <summary>消息历史条数（快照观测面——状态卡预览用）</summary>
        public int MsgCount
        {
            get
            {
                return _context.GetMessages().Length;
            }
        }

        /// <summary>待处理消息数（快照观测面——忙时排队可见）</summary>
        public int PendingCount
        {
            get
            {
                return _pending.Count;
            }
        }

        /// <summary>Note 计划激活中——任务未全完成（快照观测面——状态卡 Note 徽标）</summary>
        public bool NoteActive
        {
            get
            {
                return _noteTasks != null && _noteCurrent < _noteTasks.Length;
            }
        }

        /// <summary>
        /// 附加 HTTP 外观层——Bootstrap 段6 宿主 HTTP 启动后调用（SSE 转发面就位）。
        /// </summary>
        /// <param name="host">HTTP 外观层实例</param>
        public void AttachHost(IHostPush host)
        {
            _httpHost = host;
        }

        /// <summary>
        /// 注入环境信息提供器——info 内置工具数据源（拆分后由入口壳注入；原直接调用 Program.BuildEnvInfo）
        /// </summary>
        /// <param name="provider">环境信息构建委托</param>
        public void AttachEnvInfo(Func<string> provider)
        {
            _envInfoProvider = provider;
        }

        /// <summary>
        /// 注入本轮结束通知回调——CloseRound 正常收尾时调用（Q：系统通知 Tip——displayName + 末轮回复摘要；入口壳接托盘 BalloonTip）。
        /// </summary>
        /// <param name="notify">通知回调（标题 + 正文）</param>
        public void AttachRoundNotify(Action<string, string> notify)
        {
            _roundNotify = notify;
        }

        /// <summary>
        /// 中止当前轮次——取消 LLM 流（后台 Task 取消）/ 放弃未完成工具批；已完成内容保留 + 前文格式修复（不裁剪——裁剪唯一通道是回滚）。
        /// 线程安全：仅置位 + Cancel（volatile 标志跨线程可见）；相位推进在主线程 Pump 消费。
        /// </summary>
        public void Pause()
        {
            if (_phase == ChatPhase.Idle)
            {
                // 无进行中对话——前端按钮 idle 态禁用；防御性返回
                return;
            }
            _pauseRequested = true;
            if (_pauseCts != null)
            {
                _pauseCts.Cancel();
            }
            LogStore.Add("CatHome4", 1, "收到中止指令——正在停止本轮（已完成内容保留）", "CHAT");
        }

        /// <summary>中止收尾——主线程 Pump 消费（相位串行）。已完成工具结果保留（不丢信息）/未完成放弃+格式修复/前文落盘/复位 Idle + paused 事件；实现委托 FinalizeInterrupted（中断收尾公共实现）。</summary>
        private void PauseFinalize()
        {
            _pauseRequested = false;
            FinalizeInterrupted("{\"type\":\"paused\",\"text\":\"已停止本轮（前文保留 + 格式修复）\"}", "本轮已中止——前文保留 + 格式修复");
        }
        /// <summary>
        /// 中断收尾公共实现——已完成工具结果保留 + 上下文格式修复 + 截断落盘 + 序号复位 + 状态复位 Idle + control 事件。
        /// 调用面：PauseFinalize（用户中止）——宿主重启不再走强制中断（A72：本轮常规结束，见 RestartRequest）。
        /// </summary>
        /// <param name="ctrlJson">前端 control 事件 JSON（type/text）</param>
        /// <param name="logPrefix">日志前缀（实现追加落盘条数）</param>
        private void FinalizeInterrupted(string ctrlJson, string logPrefix)
        {
            // [段0] 已完成工具结果保留——未完成放弃（ReplaceMessages 对未配对声明补占位）+ 先行卡补终态（完成/已中止）
            for (int i = 0; i < _dogs.Count; i = i + 1)
            {
                ToolOrderDog dog = _dogs[i];
                bool done = dog.IsClosed && dog.Result != null && dog.Result.Length > 0;
                if (done)
                {
                    AppendMessage(_context.AddToolResult(dog.ToolCallId, dog.Name, dog.Result));
                    NoteTimebackEvent();
                }
                PushToolCardFinal(dog, i + 1, _dogs.Count, done);
            }
            _dogs.Clear();
            _hostDogs.Clear();
            _toolBatchActive = false;
            // [段1] 上下文格式修复——S3 ReplaceMessages 原地（幂等；孤儿 tool_calls 补占位/孤立结果丢弃）
            _context.ReplaceMessages(_context.GetMessages());
            // [段2] 前文落盘——落盘保真
            LlmMessage[] toSave = _context.GetMessages();
            _lastStats.EntryCount = toSave.Length;
            _store.AppendMeta(_lastStats);
            // [段2b] 运行态——中断结算（失败/中止轮同出统计：L2 摘要留档——design-ch4-llm §2.1 终止语义）
            PhaseSettle();
            LogStore.Add("LLM", 2, "本轮运行态统计（中断）: " + BuildRunStateSummary(), "LLM");
            // [段3] 视图序号复位——流式容器由前端 seal（已显示内容保留）；思考段经唯一出口收口
            SealReasonStream();
            _textStreamSeq = 0;
            ResetRetryView();
            // [段4] 状态复位——Idle（不推 chatdone/roundsum/Note 拉起——中断非正常完成语义）
            _round = 0;
            _phase = ChatPhase.Idle;
            _phaseFrames = 0;
            _llmBusy = false;
            _llmError = false;
            _llmErrorText = "";
            _llmResultText = "";
            _llmReasoning = "";
            _llmToolCallsJson = "";
            _emptyReplyRetry = 0;
            _streamClosedRetry = false;
            SetChatState("idle");
            // [段5] 前端通知——control 事件（seal + 按钮复位）
            if (_httpHost != null)
            {
                _httpHost.PushView("control", ctrlJson, -1, 0);
            }
        }

        /// <summary>
        /// 宿主重启请求登记（A72——design-ch4-delay §7.2）：登记重启请求 + 置停机态，**不中断本轮**。
        /// 本轮照常续 LLM 轮并正常 CloseRound 完整结算（roundsum / chatdone / 前文落盘齐全）；
        /// 停机态拒收新输入（在途排队消息丢弃并出声）；全局 Idle 后由主循环闸门执行接力（design-ch4-host-restart §三 T2/T3）。
        /// </summary>
        /// <param name="requestJson">重启请求 JSON（target/push，由 majordomo 工具组积木落盒）</param>
        private void RestartRequest(string requestJson)
        {
            DataBox.Set<string>("global", "host_restart_request", requestJson);
            DataBox.Set<string>("global", "host_restart_state", "requested");
            LogStore.Add("CatHome4", 1, "宿主重启请求已登记——本轮照常收尾（停机态拒收新输入），等待全局空闲", "RESTART");
        }

        /// <summary>
        /// 宿主重启停机态判定——DataBox 全局盒（键与 Program.Restart 同源）。
        /// </summary>
        /// <returns>true=停机态（拒收新输入）</returns>
        private static bool IsHostRestarting()
        {
            string state;
            if (DataBox.TryGet<string>("global", "host_restart_state", out state))
            {
                return state == "requested";
            }
            return false;
        }

        /// <summary>
        /// 停机态丢弃在途排队消息（A72）——宿主即将重启，不再开新轮；丢弃并出声（不静默）。
        /// </summary>
        private void DropPendingIfRestarting()
        {
            if (_pending.Count == 0)
            {
                return;
            }
            if (!IsHostRestarting())
            {
                return;
            }
            int dropped = _pending.Count;
            _pending.Clear();
            LogStore.Add("CatHome4", 2, "宿主重启停机态——丢弃在途排队消息 " + dropped.ToString() + " 条", "RESTART");
        }

        /// <summary>
        /// sleep 作废——主干被「非 sleep 输入」启动时，本猫未到点 sleep 全部销毁（等待语义：人回来了就不再需要叫醒）。
        /// 告知以 systemauto 名义汇总一条注入（在触发消息之前落前文 + 视图 + SSE）。
        /// 触发点：Pump Idle 分支启动轮之前（唯一入口——含 QQ 消息 / Note 拉起 / timer / delay / restart 回执）。
        /// </summary>
        /// <param name="triggerSource">触发本轮的输入来源（sleep=自身到点，不销毁）</param>
        private void ConsumeSleepOnWake(string triggerSource)
        {
            if (triggerSource == "sleep")
            {
                return;
            }
            DelayEntry[] killed = DelayQueue.CancelBySource(_catKey, "sleep");
            if (killed.Length == 0)
            {
                return;
            }
            StringBuilder sb = new StringBuilder();
            sb.Append("（系统自动 · sleep 作废）等待被提前启动打断——以下定时唤醒已销毁（共 ");
            sb.Append(killed.Length.ToString());
            sb.Append(" 条）：");
            for (int i = 0; i < killed.Length; i = i + 1)
            {
                sb.Append("\n  #");
                sb.Append(killed[i].Id.ToString());
                sb.Append(" 原定 ");
                sb.Append(DelayQueue.FormatTime(killed[i].DueAt));
                sb.Append(" 到期");
            }
            string notice = sb.ToString();
            AppendMessage(_context.AddUserMessage(notice));
            _viewStore.OnUserMessage(LastMessage(), ViewTimestamp(), _context.GetMessageCount() - 1);
            if (_httpHost != null)
            {
                string userJson = "{\"content\":" + JsonUtil.Serialize(notice) + ",\"source\":\"systemauto\"}";
                _httpHost.PushView("user", userJson, -1, 0);
            }
            LogStore.Add("CatHome4", 1, "sleep 作废（等待被提前启动打断）: cat=" + _catKey + " | 销毁 " + killed.Length.ToString() + " 条 | 触发来源 " + triggerSource, "DELAY");
        }

        /// <summary>
        /// 用户消息入队——Idle 时 Pump 立即启动轮次；忙时排队等待（原同步阻塞天然排队语义保持）。
        /// 主线程泵消费调用（ThreadGuard：不推进相位——启动在 Pump）。
        /// </summary>
        /// <param name="content">用户消息内容</param>
        /// <param name="source">来源——user（人发送，默认）/system（系统自动）</param>
        public void PostUserMessage(string content, string source = "user")
        {
            if (content == null || content.Length == 0)
            {
                return;
            }
            if (source == null || source.Length == 0)
            {
                source = "user";
            }
            // [段1] 宿主重启停机态——拒收一切新需求（design-ch4-host-restart §三 T2：停机后等的是在途轮次，不是等人）
            string restartState;
            if (DataBox.TryGet<string>("global", "host_restart_state", out restartState) && restartState == "requested")
            {
                LogStore.Add("CatHome4", 2, "宿主重启中——本条输入未受理（来源 " + source + "）", "RESTART");
                return;
            }
            PendingMessage msg = new PendingMessage();
            msg.Content = content;
            msg.Source = source;
            _pending.Enqueue(msg);
        }

        /// <summary>
        /// 一帧推进——主线程每帧调用（PumpSessions 轮转）。相位分发：Idle 检查排队 / Llm 轮询完成槽 / 工具批轮询 Dog / 终态落盘复位。
        /// </summary>
        public void Pump()
        {
            // P6 中止——暂停请求消费（任何相位统一收尾：LLM 流取消/工具批放弃/已完成保留）
            if (_pauseRequested)
            {
                PauseFinalize();
                return;
            }
            if (_phase == ChatPhase.Idle)
            {
                // A72——停机态丢弃在途排队消息（宿主即将重启，不再开新轮）
                DropPendingIfRestarting();
                if (_pending.Count > 0)
                {
                    PendingMessage next = _pending.Dequeue();
                    // sleep 作废——主干被「非 sleep 输入」启动即销毁本猫未到点 sleep（等待语义；告知先于触发消息）
                    ConsumeSleepOnWake(next.Source);
                    StartRound(next.Content, next.Source);
                }
                return;
            }
            if (_phase == ChatPhase.LlmRunning)
            {
                PumpLlm();
                return;
            }
            if (_phase == ChatPhase.ToolBatchRunning)
            {
                PumpToolBatch();
                return;
            }
            if (_phase == ChatPhase.Done)
            {
                CloseRound();
            }
        }

        /// <summary>
        /// 启动新轮次——追加用户消息 + chat_state=working + StartLlm 动作段（构造消息序列 → 后台流式消费）。
        /// </summary>
        /// <param name="content">用户消息</param>
        /// <param name="source">来源——user/system</param>
        private void StartRound(string content, string source)
        {
            // E3 Token 统计——整轮清零（工具续轮 LaunchLlm 不清——跨轮累加语义）
            _usagePrompt = 0;
            _usageCompletion = 0;
            _usageCacheHit = 0;
            // roundsum 统计——单次前文/工具计数/七态计时清零 + 请求计数清零 + 轮次起表 + 进入 link 相位
            _contextTokens = 0;
            _toolCallCount = 0;
            _requestCount = 0;
            // 空回复续传计数——整轮清零（同 CH2 [段2.3] 每轮独立语义）
            _emptyReplyRetry = 0;
            _streamClosedRetry = false;
            _toolDone = false;
            _roundStartTick = System.Diagnostics.Stopwatch.GetTimestamp();
            lock (_phaseLock)
            {
                _phaseAccumMs = new long[PhaseCount];
                _phaseKind = -1;
                _phaseStartTick = 0;
            }
            PhaseEnter(PhaseLink);
            AppendMessage(_context.AddUserMessage(content));
            _viewStore.OnUserMessage(LastMessage(), ViewTimestamp(), _context.GetMessageCount() - 1);
            // 单向数据流改造——所有进内核的消息统一出口：SSE user 事件（前端只画不判）
            if (_httpHost != null)
            {
                string userJson = "{\"content\":" + JsonUtil.Serialize(content) + ",\"source\":\"" + source + "\"}";
                _httpHost.PushView("user", userJson, -1, 0);
            }
            SetChatState("working");
            LaunchLlm();
        }

        /// <summary>
        /// StartLlm 动作段——消息序列固化 + 槽清零 + 后台 Task.Run 消费（主线程零阻塞；LlmRunning 相位帧计数归零）。
        /// </summary>
        private void LaunchLlm()
        {
            // P6 中止——每轮新取消令牌（上一轮取消不跨轮；Pause 时 Cancel 后台流）
            _pauseCts = new System.Threading.CancellationTokenSource();
            LlmMessage[] messages = _context.GetMessages();
            _llmBusy = true;
            _llmResultText = "";
            _llmReasoning = "";
            _reasonAccum.Clear();
            _llmToolCallsJson = "";
            _llmError = false;
            _llmErrorText = "";
            System.Threading.Tasks.Task.Run(delegate
            {
                _ = ConsumeLlmStream(messages, _pauseCts.Token);
            });
            _phase = ChatPhase.LlmRunning;
            _phaseFrames = 0;
            // 运行态——请求发出即链路等待（首轮/续轮/重试重发同路径；长度由远端决定——design-ch4-llm §2.1）
            _requestCount = _requestCount + 1;
            PhaseEnter(PhaseLink);
        }

        /// <summary>
        /// 空回复续传——流成功结束但无回复文本（STREAM_CLOSED 或 content 空）时同上下文重发（CH2 [段2.3] 移植；无上限——LLM 兜底）。
        /// 复用 S2 retry 视图机制：置位 _sawRetry → 续传流首个 Text/Reasoning 到达时前端自动回填 resolved。
        /// </summary>
        /// <param name="reason">续传原因——日志 + retry 视图展示</param>
        private void RetryEmptyReply(string reason)
        {
            _emptyReplyRetry = _emptyReplyRetry + 1;
            _streamClosedRetry = false;
            LogStore.Add("LLM", 2, "空回复续传 第" + _emptyReplyRetry.ToString() + "/∞ 次（" + reason + "）——同上下文重发", "LLM");
            // A94——原文快照齐备：续传路径同样写入三元组（否则 resolved / failed 终态丢失报错原文；原因加中文注释与 Retrying 分支同源）
            _retryAttempt = _emptyReplyRetry.ToString();
            _retryMax = "∞";
            _retryReason = ErrorNote.Apply(reason);
            _sawRetry = true;
            PushRetryView("retrying");
            // 同上下文重发——LaunchLlm 内部清槽 + 帧计数归零（上下文未污染：空文本未入 Ctx/无错误文本入 Ctx）
            LaunchLlm();
        }
        /// <summary>
        /// 构造 retry 视图载荷（A94 统一出口）——报错原文三元组（尝试序号 / 次数上限 / 原因摘要）在所有状态下齐备，
        /// 终态（resolved / failed）只追加状态标注，不覆盖原文。
        /// </summary>
        /// <param name="state">状态（retrying / resolved / failed）</param>
        /// <returns>retry 视图载荷 JSON</returns>
        private string BuildRetryViewJson(string state)
        {
            return "{\"state\":" + JsonUtil.Serialize(state) + ",\"attempt\":\"" + _retryAttempt + "\",\"max\":\"" + _retryMax + "\",\"text\":" + JsonUtil.Serialize(_retryReason) + "}";
        }
        /// <summary>
        /// retry 视图推送统一出口（A94）——构造载荷 + 原位落盘 + 推事件：
        /// retrying 新建气泡（replaceSeq=-1，返回序号存 _retrySeq）；resolved / failed 替换既有气泡（replaceSeq=_retrySeq）。
        /// </summary>
        /// <param name="state">状态（retrying / resolved / failed）</param>
        private void PushRetryView(string state)
        {
            string json = BuildRetryViewJson(state);
            _retryBlockIndex = _viewStore.UpsertRetry(json, ViewTimestamp(), _retryBlockIndex);
            if (_httpHost == null)
            {
                return;
            }
            if (state == "retrying")
            {
                _retrySeq = _httpHost.PushView("retry", json, -1, _retrySeq);
                return;
            }
            _httpHost.PushView("retry", json, _retrySeq, 0);
        }
        /// <summary>
        /// 重试视图态清零（A94 单一出口）——气泡序号 / 落盘块索引 / 原文三元组 / 待回填标志；
        /// 轮终（正常 / 中断 / 错误中止）统一调用，防跨轮残留污染（旧原因被复用 / 陈旧序号替换错气泡）。
        /// </summary>
        private void ResetRetryView()
        {
            _retrySeq = 0;
            _retryBlockIndex = -1;
            _retryAttempt = "";
            _retryMax = "";
            _retryReason = "";
            _sawRetry = false;
        }

        /// <summary>
        /// 后台消费 LLM 流——写会话槽 + SSE 转发 + 结算行。Task.Run 执行——异常全兜底（错误可见性）。
        /// </summary>
        /// <param name="messages">消息序列</param>
        private async System.Threading.Tasks.Task ConsumeLlmStream(LlmMessage[] messages, System.Threading.CancellationToken ct)
        {
            try
            {
                // [段1] 槽预置 + 后台流式消费
                StringBuilder text = new StringBuilder();
                string toolCalls = "";
                await foreach (LlmStreamEvent ev in _llmRuntime.ChatStream(messages, _tools, ResolveCacheIsolationKey(), ct))
                {
                    if (ev.Kind == LlmStreamKind.Text)
                    {
                        // S2 §8.4——重试成功回填：本轮发生过重试且首个 Text 到达 → retry 气泡更新为 resolved（只回填一次）
                        if (_sawRetry)
                        {
                            _sawRetry = false;
                            PushRetryView("resolved");
                        }
                        text.Append(ev.Text);
                        // 运行态——Text 增量到达即回复态（远端·流；长度由远端决定）
                        PhaseEnter(PhaseReply);
                        // P6 外观层转发——LLM 增量实时推送 SSE（协议 §4.2 llm 事件）
                        if (_httpHost != null)
                        {
                            string streamTextJson = "{\"kind\":\"text\",\"text\":" + JsonUtil.Serialize(ev.Text) + "}";
                            _textStreamSeq = _httpHost.PushView("stream", streamTextJson, -1, _textStreamSeq);
                        }
                    }
                    else if (ev.Kind == LlmStreamKind.Reasoning)
                    {
                        // S2 §8.4——重试成功回填（Reasoning 也是恢复信号）
                        if (_sawRetry)
                        {
                            _sawRetry = false;
                            PushRetryView("resolved");
                        }
                        _reasonAccum.Append(ev.Text);
                        // 运行态——Reasoning 增量到达即思考态（远端·流；长度由远端决定）
                        PhaseEnter(PhaseThink);
                        if (_httpHost != null)
                        {
                            string streamReasonJson = "{\"kind\":\"reasoning\",\"text\":" + JsonUtil.Serialize(ev.Text) + "}";
                            _reasonStreamSeq = _httpHost.PushView("stream", streamReasonJson, -1, _reasonStreamSeq);
                        }
                    }
                    else if (ev.Kind == LlmStreamKind.ToolCalls)
                    {
                        // S2 §8.4——重试成功回填（ToolCalls 也是恢复信号——工具轮场景无文本）
                        if (_sawRetry)
                        {
                            _sawRetry = false;
                            PushRetryView("resolved");
                        }
                        toolCalls = ev.Text;
                        // 运行态——ToolCalls（流末聚合）不切态：tool 态已由 ToolCallsStart（首个增量帧）进入；
                        // run 态在发单时进入（PumpLlm 工具分支）——design-ch4-llm §2.1
                        // 视图——"进行中"工具卡在 StartToolBatch 段统一推送（LLM 输出工具即出卡；完成/中断原位替换）
                    }
                    else if (ev.Kind == LlmStreamKind.ToolCallsStart)
                    {
                        // 运行态——工具决策流开始（远端·流：LLM 输出 tool_calls 的流式过程；长度由远端决定）
                        PhaseEnter(PhaseTool);
                    }
                    else if (ev.Kind == LlmStreamKind.RetryResume)
                    {
                        // 运行态——退避结束、重发开始：wait → link（长度重回远端决定）
                        PhaseEnter(PhaseLink);
                    }
                    else if (ev.Kind == LlmStreamKind.Retrying)
                    {
                        // P6 中止——暂停中忽略重试事件（取消被误判为传输错误的防御：不推 retry 气泡——根治在 Runtime catch OCE 冒泡）
                        if (_pauseRequested)
                        {
                            continue;
                        }
                        // 运行态——重试退避开始：link → wait（长度由本地决定——退避时长）
                        PhaseEnter(PhaseWait);
                        // S2 §8.4——重试可见性：独立视图条目（retry renderType）——不入会话槽/上下文/日志（Runtime 已记 L2）
                        _sawRetry = true;
                        // RETRY|N/3|原因摘要 —— 提取尝试序号与原因（原因摘要自身含 | —— 取第二个分隔符之后全段）
                        string retryText = ev.Text;
                        string attempt = "";
                        string max = "";
                        string reason = retryText;
                        int sep1 = retryText.IndexOf('|');
                        int sep2 = sep1 < 0 ? -1 : retryText.IndexOf('|', sep1 + 1);
                        if (sep2 > 0)
                        {
                            string seg = retryText.Substring(sep1 + 1, sep2 - sep1 - 1);
                            string[] am = seg.Split('/');
                            if (am.Length >= 2)
                            {
                                attempt = am[0];
                                max = am[1];
                            }
                            else
                            {
                                attempt = seg;
                            }
                            reason = retryText.Substring(sep2 + 1);
                        }
                        // A69 视图层报错中文注释——重试原因摘要追加中文注释
                        reason = ErrorNote.Apply(reason);
                        // A86——原文快照：resolved 时保留报错信息（前端原文 + 追加「已恢复」，不覆盖）
                        _retryAttempt = attempt;
                        _retryMax = max;
                        _retryReason = reason;
                        PushRetryView("retrying");
                    }
                    else if (ev.Kind == LlmStreamKind.Usage)
                    {
                        // E3 Token 统计——解析 usage JSON 累计整轮（工具多轮累加）+ 单次前文覆盖 + 转发 SSE（前端覆盖式显示整轮累计值）
                        // 会话级累计——本次请求增量随轮同期堆入（跨轮保留；仅新会话复位）
                        long reqPrompt = _usagePrompt;
                        long reqCompletion = _usageCompletion;
                        long reqCacheHit = _usageCacheHit;
                        ParseUsage(ev.Text, ref _usagePrompt, ref _usageCompletion, ref _usageCacheHit, ref _contextTokens);
                        _sessionPrompt = _sessionPrompt + (_usagePrompt - reqPrompt);
                        _sessionCompletion = _sessionCompletion + (_usageCompletion - reqCompletion);
                        _sessionCacheHit = _sessionCacheHit + (_usageCacheHit - reqCacheHit);
                        if (_httpHost != null)
                        {
                            string usageJson = "{\"prompt\":" + _usagePrompt.ToString()
                                + ",\"completion\":" + _usageCompletion.ToString()
                                + ",\"cacheHit\":" + _usageCacheHit.ToString()
                                + ",\"context\":" + _contextTokens.ToString()
                                + ",\"sessionPrompt\":" + _sessionPrompt.ToString()
                                + ",\"sessionCompletion\":" + _sessionCompletion.ToString()
                                + ",\"sessionCacheHit\":" + _sessionCacheHit.ToString() + "}";
                            string usageCtrl = "{\"type\":\"usage\",\"data\":" + usageJson + "}";
                            _httpHost.PushView("control", usageCtrl, -1, 0);
                        }
                    }
                    else if (ev.Kind == LlmStreamKind.Done)
                    {
                        if (_httpHost != null)
                        {
                            // F4 视图——done 由整块 replace 表达（流式结束不单独推事件）
                        }
                    }
                    else if (ev.Kind == LlmStreamKind.Error)
                    {
                        // P6 中止——暂停中忽略错误事件（取消引发的 TaskCanceled 转 Error 不推前端/不置错误）
                        if (_pauseRequested)
                        {
                            continue;
                        }
                        _llmError = true;
                        _llmErrorText = ev.Text;
                        // 空回复续传——STREAM_CLOSED（SSE 流未以 [DONE] 结束）：置位续传标志（PumpLlm 走续传），不推前端 error（无上限——LLM 兜底）
                        if (ev.Text.StartsWith("ERR|STREAM_CLOSED", StringComparison.Ordinal))
                        {
                            _streamClosedRetry = true;
                        }
                        // 非续传错误——不在此推视图/落盘：错误可见性统一出口 = AbortRoundError
                        // （主线程下一帧收尾落盘 + 推送——单块单事件；在此双推会造成重复错误气泡）
                    }
                }
                // [段2] 结果槽落位
                _llmResultText = text.ToString();
                _llmReasoning = _reasonAccum.ToString();
                _llmToolCallsJson = toolCalls;
                // L1-META 结算行（D5 分级——观测全链：LLM 流完成一行为准，SSE log 事件实时可见）
                string llmSummary = "模型已回复（" + text.Length.ToString() + " 字符）";
                if (toolCalls.Length > 0)
                {
                    llmSummary = llmSummary + "，本轮调用了工具";
                }
                else
                {
                    llmSummary = llmSummary + "，未调用工具";
                }
                // E3 Token 统计——结算行带本轮 usage（轮级：本轮全部请求累加；SSE 推送仍走 usage 事件——前端覆盖式显示整轮累计值）
                llmSummary = llmSummary + "。本轮 Token：输入 " + _usagePrompt.ToString() + "（含缓存 " + _usageCacheHit.ToString() + "）· 输出 " + _usageCompletion.ToString();
                LogStore.Add("LLM", 0, llmSummary, "LLM");
            }
            catch (System.OperationCanceledException ex)
            {
                // P6 中止——用户暂停取消流：不置错误（PumpLlm 检查 _pauseRequested 走收尾）
                // 超时兜底——HttpClient.Timeout（60s）抛 TaskCanceledException（OCE 子类）也走此分支：非暂停请求的取消 = 超时/物理中断 → 置位续传（不静默吞并）
                if (!_pauseRequested)
                {
                    _llmError = true;
                    _streamClosedRetry = true;
                    _llmErrorText = "ERR|STREAM_CLOSED|SSE 流请求取消（超时/物理中断）: " + ex.GetType().Name;
                }
            }
            catch (Exception ex)
            {
                // P6 中止——暂停中异常忽略（取消竞态窗口——取消标志未及读取时异常已抛）
                if (!_pauseRequested)
                {
                    _llmError = true;
                    // 空回复续传——网络物理中断（连接重置/代理断连/IO 抖动）与优雅断流（EOF 无 [DONE]）同语义：置位续传（TranslateSse 迭代器内异常无法事件化——消费层兜底）
                    if (ex is System.Net.Http.HttpRequestException || ex is System.IO.IOException || ex is System.Net.Sockets.SocketException)
                    {
                        _streamClosedRetry = true;
                        _llmErrorText = "ERR|STREAM_CLOSED|SSE 流物理中断: " + ex.GetType().Name + "|" + ex.Message;
                    }
                    else
                    {
                        _llmErrorText = "ERR|" + ex.GetType().Name + "|" + ex.Message;
                    }
                }
            }
            finally
            {
                _llmBusy = false;
            }
        }
        /// <summary>
        /// 思考段终结——离开 think 态的唯一收口（莎 2026-09-22 定）：流式思考块转整块（replaceSeq 命中流式容器）+ 序号复位。
        /// 幂等——无在途思考流式（序号 0 或内容空）时静默返回。调用面：PhaseEnter 离开 think 态 + 中止/暂停收尾。
        /// </summary>
        private void SealReasonStream()
        {
            long seq = _reasonStreamSeq;
            _reasonStreamSeq = 0;
            string content = _reasonAccum.ToString();
            if (seq == 0 || content.Length == 0)
            {
                return;
            }
            if (_httpHost != null)
            {
                string reasonJson = "{\"content\":" + JsonUtil.Serialize(content) + "}";
                _httpHost.PushView("reason", reasonJson, seq, 0);
            }
        }

        /// <summary>运行态切换——结算旧态累计毫秒 + 进入新态（七态：idle/wait/link/think/tool/run/reply；锁内）；同态连续计时（重复事件不重置起表）；idle 不计时——只作态名。</summary>
        /// <param name="kind">目标态（PhaseIdle/PhaseWait/PhaseLink/PhaseThink/PhaseTool/PhaseRun/PhaseReply）</param>
        private void PhaseEnter(int kind)
        {
            bool leaveThink = false;
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            lock (_phaseLock)
            {
                // 同态——连续计时：不结算、不重置起表（重复事件不吞时长；2026-09-16 实测修正）
                if (_phaseKind == kind)
                {
                    return;
                }
                // 思考段终结判定——离开 think 态即收口（唯一出口：全部态转移都经本方法；莎 2026-09-22 定）
                if (_phaseKind == PhaseThink && kind != PhaseThink)
                {
                    leaveThink = true;
                }
                if (_phaseKind >= 0 && _phaseStartTick > 0)
                {
                    long ms = (now - _phaseStartTick) * 1000 / System.Diagnostics.Stopwatch.Frequency;
                    if (ms < 0) { ms = 0; }
                    _phaseAccumMs[_phaseKind] = _phaseAccumMs[_phaseKind] + ms;
                }
                _phaseKind = kind;
                // 空闲态不计时——idle 只作态名（轮未开始 / 收尾中）；计时效用为零，且实时增量会让快照 sessions 段每帧脏变化
                if (kind == PhaseIdle)
                {
                    _phaseStartTick = 0;
                }
                else
                {
                    _phaseStartTick = now;
                }
            }
            // 锁外推事件——思考段整块（幂等：无在途思考流式时静默返回）
            if (leaveThink)
            {
                SealReasonStream();
            }
        }

        /// <summary>
        /// 结算当前运行态——roundsum 生成前 / 终止收尾（错误中止 / 暂停 / 宿主重启中断 / Note 拉起）调用；累计落七态 + 态标记归零 + 快照留档。
        /// </summary>
        private void PhaseSettle()
        {
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            lock (_phaseLock)
            {
                if (_phaseKind >= 0 && _phaseStartTick > 0)
                {
                    long ms = (now - _phaseStartTick) * 1000 / System.Diagnostics.Stopwatch.Frequency;
                    if (ms < 0) { ms = 0; }
                    _phaseAccumMs[_phaseKind] = _phaseAccumMs[_phaseKind] + ms;
                    _phaseKind = -1;
                    _phaseStartTick = 0;
                }
            }
        }
        /// <summary>
        /// 七态累计快照——锁内复制（含当前活跃态实时增量）。约定：调用方必须已持有 _phaseLock。
        /// </summary>
        /// <returns>七态毫秒数组（索引同 PhaseNames）</returns>
        private long[] SnapshotPhaseAccumLocked()
        {
            long[] copy = new long[PhaseCount];
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            for (int i = 0; i < PhaseCount; i = i + 1)
            {
                copy[i] = _phaseAccumMs[i];
            }
            if (_phaseKind >= 0 && _phaseStartTick > 0)
            {
                long ms = (now - _phaseStartTick) * 1000 / System.Diagnostics.Stopwatch.Frequency;
                if (ms < 0) { ms = 0; }
                copy[_phaseKind] = copy[_phaseKind] + ms;
            }
            return copy;
        }
        /// <summary>运行态读取面——当前态名 + 七态累计毫秒表（idle 恒 0——空闲不计时）+ 本轮请求次数（锁内快照；观测面与前端拉取口子数据源）。</summary>
        /// <param name="name">输出：当前态名（idle/wait/link/think/tool/run/reply）</param>
        /// <param name="requests">输出：本轮 API 请求次数（= link 段数）</param>
        /// <returns>七态累计毫秒表（态名 → 毫秒）</returns>
        internal Dictionary<string, long> GetRunState(out string name, out int requests)
        {
            lock (_phaseLock)
            {
                long[] ms = SnapshotPhaseAccumLocked();
                Dictionary<string, long> table = new Dictionary<string, long>();
                for (int i = 0; i < PhaseCount; i = i + 1)
                {
                    table[PhaseNames[i]] = ms[i];
                }
                if (_phaseKind >= 0 && _phaseKind < PhaseCount)
                {
                    name = PhaseNames[_phaseKind];
                }
                else
                {
                    name = "idle";
                }
                requests = _requestCount;
                return table;
            }
        }
        /// <summary>
        /// 设置会话运行态盒——按会话键（chat_state:&lt;catKey&gt;）+ 旧全局键兼容（观测面与前端拉取口子）。
        /// </summary>
        /// <param name="state">粗粒度态（idle/working/tools——细粒度七态走 PhaseEnter）</param>
        private void SetChatState(string state)
        {
            DataBox.Set<string>("global", "chat_state", state);
            if (_catKey != null && _catKey.Length > 0)
            {
                DataBox.Set<string>("global", "chat_state:" + _catKey, state);
            }
        }
        /// <summary>运行态摘要行——当前态 + 六态毫秒（idle 不计时故不入行）+ 本轮请求次数（失败轮统计留档——日志观测面；正常轮由 roundsum 承载）。</summary>
        /// <returns>摘要文本（state=… link=…ms … requests=N）</returns>
        private string BuildRunStateSummary()
        {
            string name;
            int requests;
            Dictionary<string, long> ms = GetRunState(out name, out requests);
            return "state=" + name
                + " wait=" + ms["wait"].ToString() + "ms"
                + " link=" + ms["link"].ToString() + "ms"
                + " think=" + ms["think"].ToString() + "ms"
                + " tool=" + ms["tool"].ToString() + "ms"
                + " run=" + ms["run"].ToString() + "ms"
                + " reply=" + ms["reply"].ToString() + "ms"
                + " requests=" + requests.ToString();
        }
        /// <summary>
        /// 本猫运行态块 JSON——对话端口增量推送数据源（状态条；变化才推、不变不推——前端零轮询）。
        /// </summary>
        /// <returns>运行态 JSON（sessionId / runState / runMs 七键 / requests）</returns>
        public string BuildRunStateJson()
        {
            string name;
            int requests;
            Dictionary<string, long> ms = GetRunState(out name, out requests);
            return "{\"sessionId\":" + JsonUtil.Serialize(Id)
                + ",\"runState\":\"" + name + "\""
                + ",\"runMs\":{\"idle\":" + ms["idle"].ToString()
                + ",\"wait\":" + ms["wait"].ToString()
                + ",\"link\":" + ms["link"].ToString()
                + ",\"think\":" + ms["think"].ToString()
                + ",\"tool\":" + ms["tool"].ToString()
                + ",\"run\":" + ms["run"].ToString()
                + ",\"reply\":" + ms["reply"].ToString() + "}"
                + ",\"requests\":" + requests.ToString() + "}";
        }

        /// <summary>构建 roundsum 载荷——本轮 Token 消耗 + 工具次数 + 请求次数 + 总耗时 + 六态用时（idle 不计时故不入载荷；CloseRound 推送/落盘数据源）+ done（本轮结束语义：stream=流式自然收尾 / tool=工具主动 done）。</summary>
        /// <returns>roundsum 视图载荷 JSON（{"type":"roundsum","data":{...}}）</returns>
        private string BuildRoundSumJson()
        {
            long miss = _usagePrompt - _usageCacheHit;
            if (miss < 0) { miss = 0; }
            long elapsedMs = 0;
            if (_roundStartTick > 0)
            {
                elapsedMs = (System.Diagnostics.Stopwatch.GetTimestamp() - _roundStartTick) * 1000 / System.Diagnostics.Stopwatch.Frequency;
                if (elapsedMs < 0) { elapsedMs = 0; }
            }
            string stateName;
            int requests;
            Dictionary<string, long> ms = GetRunState(out stateName, out requests);
            // roundsum 只承载时长与计数——当前态名由此处不留档（观测面走 GetRunState）
            _ = stateName;
            // done——工具主动 done（sleep 等待 / restart 收尾登记）与流式自然 done 的分野（QQ 转发面据此决定来源是否续约）
            string doneKind = "stream";
            if (_toolDone)
            {
                doneKind = "tool";
            }
            return "{\"type\":\"roundsum\",\"data\":{\"prompt\":" + _usagePrompt.ToString()
                + ",\"completion\":" + _usageCompletion.ToString()
                + ",\"cacheHit\":" + _usageCacheHit.ToString()
                + ",\"miss\":" + miss.ToString()
                + ",\"done\":\"" + doneKind + "\""
                + ",\"toolCount\":" + _toolCallCount.ToString()
                + ",\"requests\":" + requests.ToString()
                + ",\"elapsedMs\":" + elapsedMs.ToString()
                + ",\"phases\":{\"wait\":" + ms["wait"].ToString()
                + ",\"link\":" + ms["link"].ToString()
                + ",\"think\":" + ms["think"].ToString()
                + ",\"tool\":" + ms["tool"].ToString()
                + ",\"run\":" + ms["run"].ToString()
                + ",\"reply\":" + ms["reply"].ToString() + "}}}";
        }

        /// <summary>
        /// 解析 usage JSON——prompt/completion/cacheHit 累加到整轮计数（CH2 语义：多工具轮累加）；singlePrompt 覆盖式为单次请求 prompt（前文长度）。
        /// </summary>
        /// <param name="usageJson">usage JSON 字符串（{"prompt":N,"completion":N,"cacheHit":N}）</param>
        /// <param name="prompt">prompt 累计引用</param>
        /// <param name="completion">completion 累计引用</param>
        /// <param name="cacheHit">cacheHit 累计引用</param>
        /// <param name="singlePrompt">单次 prompt 覆盖引用——最近一次请求的前文长度（非累计）</param>
        private static void ParseUsage(string usageJson, ref long prompt, ref long completion, ref long cacheHit, ref long singlePrompt)
        {
            if (usageJson == null || usageJson.Length == 0)
            {
                return;
            }
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(usageJson))
                {
                    JsonElement root = doc.RootElement;
                    if (root.TryGetProperty("prompt", out JsonElement p) && p.ValueKind == JsonValueKind.Number)
                    {
                        prompt = prompt + p.GetInt64();
                        singlePrompt = p.GetInt64();
                    }
                    if (root.TryGetProperty("completion", out JsonElement c) && c.ValueKind == JsonValueKind.Number)
                    {
                        completion = completion + c.GetInt64();
                    }
                    if (root.TryGetProperty("cacheHit", out JsonElement h) && h.ValueKind == JsonValueKind.Number)
                    {
                        cacheHit = cacheHit + h.GetInt64();
                    }
                }
            }
            catch (Exception ex)
            {
                // 解析失败静默——观测面不受单帧畸形影响
                LogStore.Add("ChatSession", 2, "usage 帧解析失败，累计跳过: " + ex.Message, "SYS");
            }
        }

        /// <summary>
        /// LlmRunning 相位推进——轮询完成槽；完成 → LlmDone 动作段三分支（错误/纯文本/工具）；帧超限 → 超时错误路径（同 WaitForLlm 语义）。
        /// </summary>
        private void PumpLlm()
        {
            _phaseFrames = _phaseFrames + 1;
            if (_llmBusy)
            {
                if (_phaseFrames < LlmTimeoutFrames)
                {
                    return;
                }
                // 超时兜底——同 WaitForLlm 超时语义（后台 Task 完成后槽再写为等价现状竞态——终态已出）
                _llmBusy = false;
                _llmError = true;
                _llmErrorText = "ERR|LLM_TIMEOUT|LLM 调用超时（帧上限 " + LlmTimeoutFrames.ToString() + "）";
            }
            // LlmDone 动作段——S2 修复：错误文本不入上下文（就地修复——断点干净，不污染 ChatContext）
            // 错误可见性：前端 error 控制事件（ConsumeLlmStream 已推）+ LogStore L3；上下文保持断点（用户消息 + 已完成 turn 保留）
            if (_llmError)
            {
                // 空回复续传——STREAM_CLOSED → 同上下文重发（无上限——LLM 兜底；StreamClosedRetry 标志由 ConsumeLlmStream 置位）
                if (_streamClosedRetry)
                {
                    RetryEmptyReply("SSE 流中断（未以 [DONE] 结束）");
                    return;
                }
                // API 错误重试耗尽（Runtime 有限重试 3 次已过）——中止本轮：落盘断点 + 错误气泡（不走 CloseRound——不 roundsum/chatdone/Note 拉起）
                AbortRoundError();
                return;
            }
            // timeback 事件计数——think（本轮产出思考内容）计 1；assistant 产出在下文两分支各计 1
            if (_llmReasoning.Length > 0)
            {
                NoteTimebackEvent();
            }
            if (_llmToolCallsJson.Length == 0)
            {
                // 空回复续传——流正常结束但无回复文本（有思考无回复或完全空）→ 同上下文重发（CH2 [段2.3] 移植；无上限——LLM 兜底；Trim 判空）
                if (string.IsNullOrWhiteSpace(_llmResultText))
                {
                    RetryEmptyReply("模型未生成回复文本");
                    return;
                }
                // 纯文本回复——本轮完成
                AppendMessage(_context.AddAssistantMessage(_llmResultText));
                NoteTimebackEvent();
                _viewStore.OnAssistantText(LastMessage(), ViewTimestamp(), _context.GetMessageCount() - 1);
                if (_httpHost != null)
                {
                    string textJson = "{\"content\":" + JsonUtil.Serialize(_llmResultText) + ",\"msgIndex\":" + (_context.GetMessageCount() - 1).ToString() + "}";
                    _httpHost.PushView("text", textJson, _textStreamSeq, 0);
                }
                _textStreamSeq = 0;
                _reasonStreamSeq = 0;
                // 单向数据流改造——忙时插话：本轮结束有排队消息 → 插入 Ctx + user 事件 + 直接开新轮（跳过 Done/CloseRound）
                if (_pending.Count > 0)
                {
                    PendingMessage next = _pending.Dequeue();
                    AppendMessage(_context.AddUserMessage(next.Content));
                    _viewStore.OnUserMessage(LastMessage(), ViewTimestamp(), _context.GetMessageCount() - 1);
                    if (_httpHost != null)
                    {
                        string userJson = "{\"content\":" + JsonUtil.Serialize(next.Content) + ",\"source\":\"" + next.Source + "\"}";
                        _httpHost.PushView("user", userJson, -1, 0);
                    }
                    _round = 0;
                    LaunchLlm();
                    return;
                }
                // 运行态——进入收尾（idle 只作态名不计时；轮末落盘与统计已由 PhaseSettle 结算——design-ch4-llm §2.1）
                PhaseEnter(PhaseIdle);
                _phase = ChatPhase.Done;
                return;
            }
            // StartToolBatch 动作段——assistant tool_calls 入上下文 + chat_state=tools + 发单
            AppendMessage(_context.AddAssistantToolCalls(_llmToolCallsJson, _llmReasoning));
            NoteTimebackEvent();
            _viewStore.OnAssistantToolCalls(LastMessage(), ViewTimestamp(), _context.GetMessageCount() - 1);
            // 思考段整块——由 SealReasonStream 在离开 think 态时统一推送（工具决策流首帧即收口；唯一出口，莎 2026-09-22 定）
            // 工具轮 seal——视图层补 gap text 块（全量外观真源：前端历史/QQBot 转发消费）+ SSE 推送（实时）；空文本不推
            if (_llmResultText.Length > 0)
            {
                _viewStore.AppendGapText(_llmResultText, ViewTimestamp());
                if (_httpHost != null)
                {
                    string sealTextJson = "{\"content\":" + JsonUtil.Serialize(_llmResultText) + ",\"msgIndex\":-1}";
                    _httpHost.PushView("text", sealTextJson, _textStreamSeq, 0);
                }
            }
            _reasonStreamSeq = 0;
            _textStreamSeq = 0;
            // 工具卡先行推送——LLM 输出工具（tool_calls 聚合完成）即出"进行中"卡；完成 / 中断时以同序号原位替换
            List<ToolCallInfo> toolCalls = ParseToolCalls(_llmToolCallsJson);
            Dictionary<string, long> cardSeqs = PushToolCardPending(toolCalls);
            // 运行态——发单即执行态（本地·程序过程：工具批到下一请求发出；长度由本地决定）
            PhaseEnter(PhaseRun);
            SetChatState("tools");
            EnterToolBatch(_llmToolCallsJson, cardSeqs);
        }
        /// <summary>
        /// 工具调用条目——tool_calls JSON 解析产物（先行推卡消费；字段与 OpenAI wire 对齐）。
        /// </summary>
        private sealed class ToolCallInfo
        {
            /// <summary>tool_call_id——结果配对键</summary>
            public string Id;

            /// <summary>工具名</summary>
            public string Name;

            /// <summary>参数 JSON（未注入 catId——发单前注入）</summary>
            public string Arguments;

            /// <summary>并发序号（1-based——tool_calls 数组顺序）</summary>
            public int Index;

            /// <summary>并发总数（同批 tool_calls 数组长度）</summary>
            public int Total;
        }

        /// <summary>
        /// 解析 tool_calls JSON（OpenAI wire：{id, function:{name, arguments}}）——逐条条目（携带 Index/Total）。
        /// 解析失败 / 非数组 → 空列表（容错——与发单侧同语义：空批立即收敛）。
        /// </summary>
        /// <param name="toolCallsJson">tool_calls JSON 数组</param>
        /// <returns>条目列表</returns>
        private List<ToolCallInfo> ParseToolCalls(string toolCallsJson)
        {
            List<ToolCallInfo> list = new List<ToolCallInfo>();
            if (toolCallsJson == null || toolCallsJson.Length == 0)
            {
                return list;
            }
            JsonDocument doc = null;
            try
            {
                doc = JsonDocument.Parse(toolCallsJson);
            }
            catch (Exception ex)
            {
                LogStore.Add("CatHome4", 3, "tool_calls 解析失败: " + ex.Message, "TOOL");
                return list;
            }
            using (doc)
            {
                JsonElement root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Array)
                {
                    return list;
                }
                int total = root.GetArrayLength();
                for (int i = 0; i < total; i = i + 1)
                {
                    JsonElement call = root[i];
                    ToolCallInfo info = new ToolCallInfo();
                    info.Id = GetStringProp(call, "id");
                    // OpenAI wire：name/arguments 在 function 嵌套对象内
                    JsonElement funcEl;
                    if (call.TryGetProperty("function", out funcEl))
                    {
                        info.Name = GetStringProp(funcEl, "name");
                        info.Arguments = GetStringProp(funcEl, "arguments");
                    }
                    else
                    {
                        info.Name = "";
                        info.Arguments = "";
                    }
                    info.Index = i + 1;
                    info.Total = total;
                    list.Add(info);
                }
            }
            return list;
        }

        /// <summary>
        /// 工具卡先行推送——LLM 输出工具（tool_calls 聚合完成）即推"进行中"卡（无 result 字段 → 前端 ⏳ 处理中）；
        /// 工具完成 / 中断时以同序号 replaceSeq 原位替换（PumpToolBatch 段3 / PushToolCardFinal）。
        /// 声明面外工具不推卡（拦截是即时的——只在完成时出 ERR 卡）。
        /// </summary>
        /// <param name="calls">工具调用条目（ParseToolCalls 产物）</param>
        /// <returns>tool_call_id → 先行卡视图序号（空=无推送通道 / 无可推工具）</returns>
        private Dictionary<string, long> PushToolCardPending(List<ToolCallInfo> calls)
        {
            Dictionary<string, long> seqs = new Dictionary<string, long>(StringComparer.Ordinal);
            if (_httpHost == null)
            {
                return seqs;
            }
            for (int i = 0; i < calls.Count; i = i + 1)
            {
                ToolCallInfo call = calls[i];
                if (!IsToolAllowed(call.Name))
                {
                    continue;
                }
                string json = "{\"name\":" + JsonUtil.Serialize(call.Name)
                    + ",\"arguments\":" + JsonUtil.Serialize(InjectCatId(call.Arguments))
                    + ",\"toolIndex\":" + call.Index.ToString()
                    + ",\"toolTotal\":" + call.Total.ToString() + "}";
                long seq = _httpHost.PushView("toolcard", json, -1, 0);
                seqs[call.Id] = seq;
            }
            return seqs;
        }

        /// <summary>
        /// 工具卡终态补推——中断收尾（中止 / 宿主重启）时先行卡补终态：已完成 → 完整卡；未完成 → 已中止卡。
        /// 无先行卡（声明面拦截 / 已由段3 终结）不推——防重复卡。
        /// </summary>
        /// <param name="dog">工具单</param>
        /// <param name="index">并发序号（1-based）</param>
        /// <param name="total">并发总数</param>
        /// <param name="done">true=已完成（结果保留）；false=未完成（本轮中止）</param>
        private void PushToolCardFinal(ToolOrderDog dog, int index, int total, bool done)
        {
            if (_httpHost == null || dog.CardSeq < 0)
            {
                return;
            }
            string result;
            if (done)
            {
                result = dog.Result;
            }
            else
            {
                result = "（本轮已中止——工具未执行完成）";
            }
            // A69 视图层报错中文注释——真实前文保持原文
            result = ErrorNote.Apply(result);
            string json = "{\"name\":" + JsonUtil.Serialize(dog.Name)
                + ",\"arguments\":" + JsonUtil.Serialize(dog.ArgsJson)
                + ",\"result\":" + JsonUtil.Serialize(result)
                + ",\"toolIndex\":" + index.ToString()
                + ",\"toolTotal\":" + total.ToString() + "}";
            _httpHost.PushView("toolcard", json, dog.CardSeq, 0);
            dog.CardSeq = -1;
        }

        /// <summary>
        /// StartToolBatch 动作段——解析 tool_calls → OA 发单（host-* 延迟直执登记 / 普通工单 Post / Post 失败诚实 ERR）→ ToolBatchRunning。解析失败 = 空批（allDone 立即成立——等价原 try-catch 跳过语义：续轮保持）。
        /// </summary>
        /// <param name="toolCallsJson">tool_calls JSON 数组</param>
        /// <param name="cardSeqs">tool_call_id → 先行"进行中"卡视图序号（PushToolCardPending 产物；缺省 -1=无先行卡）</param>
        private void EnterToolBatch(string toolCallsJson, Dictionary<string, long> cardSeqs)
        {
            _toolBatchActive = true;
            _dogs.Clear();
            _hostDogs.Clear();
            _timebackBackDog = null;
            List<ToolCallInfo> calls = ParseToolCalls(toolCallsJson);
            // [P0] 剥离——批内 timeback 至多 start × 1 + back × 1（A106 批内次序 · design-ch4-timeback §2.4）：
            // 各取首条剥离，重复调用（同类第 2 条起）判参数面拒绝（不静默丢弃）
            int startIndex = -1;
            int backIndex = -1;
            bool[] duplicated = new bool[calls.Count];
            for (int i = 0; i < calls.Count; i = i + 1)
            {
                if (calls[i].Name != "timeback")
                {
                    continue;
                }
                string action = ExtractTimebackAction(calls[i].Arguments);
                if (action == "start")
                {
                    if (startIndex < 0)
                    {
                        startIndex = i;
                    }
                    else
                    {
                        duplicated[i] = true;
                    }
                    continue;
                }
                if (action == "back")
                {
                    if (backIndex < 0)
                    {
                        backIndex = i;
                    }
                    else
                    {
                        duplicated[i] = true;
                    }
                }
            }
            // [P1] 前置——start 先于同批其余工具执行（前文末条仍为本批 assistant 声明 → 锚点可信）；
            // 失败不阻断（作用域未开时其余工具按真实态判定）
            ToolOrderDog startDog = null;
            if (startIndex >= 0)
            {
                ToolCallInfo startCall = calls[startIndex];
                ToolOrderDog preDog = new ToolOrderDog(startCall.Id, startCall.Name, InjectCatId(startCall.Arguments));
                long startCardSeq;
                preDog.CardSeq = cardSeqs.TryGetValue(startCall.Id, out startCardSeq) ? startCardSeq : -1;
                if (!IsToolAllowed(startCall.Name))
                {
                    preDog.Result = "ERR|TOOL_FORBIDDEN|工具不在当前授权面: " + startCall.Name;
                    LogStore.Add("CatHome4", 2, "工具 " + startCall.Name + " 被拒绝：不在本会话声明面", "TOOL");
                }
                else
                {
                    preDog.Result = ExecuteBuiltin(startCall.Name, preDog.ArgsJson);
                    _toolCallCount = _toolCallCount + 1;
                }
                preDog.IsClosed = true;
                startDog = preDog;
            }
            // [P2] 主体——按原数组序分派（_dogs 入列序 = 声明序：结果回填序与声明序一致）；
            // 判定读 P1 之后的作用域终局态（本批含 start 则整批按作用域活跃判定——防数组前位工具逃逸）
            for (int i = 0; i < calls.Count; i = i + 1)
            {
                ToolCallInfo call = calls[i];
                // 批内重复 timeback——参数面拒绝（批内至多 start × 1 + back × 1）
                if (duplicated[i])
                {
                    ToolOrderDog dupDog = new ToolOrderDog(call.Id, call.Name, call.Arguments);
                    dupDog.Result = "ERR|TIMEBACK_ARGS|批内不允许多条 timeback 调用（至多 start × 1 + back × 1）——第 " + (i + 1).ToString() + " 条被拒";
                    dupDog.IsClosed = true;
                    _dogs.Add(dupDog);
                    LogStore.Add("CatHome4", 2, "timeback 批内重复调用被拒（第 " + (i + 1).ToString() + " 条）", "TIMEBACK");
                    continue;
                }
                // 剥离条目——start 已在前置段执行（授权面判定已在 P1 完成）
                if (i == startIndex)
                {
                    _dogs.Add(startDog);
                    continue;
                }
                // M2c 拦截——声明面外工具直接拒绝（ERR 回执不进 OA；host-* 同拦；拦截即时生效）
                if (!IsToolAllowed(call.Name))
                {
                    ToolOrderDog forbiddenDog = new ToolOrderDog(call.Id, call.Name, call.Arguments);
                    forbiddenDog.Result = "ERR|TOOL_FORBIDDEN|工具不在当前授权面: " + call.Name;
                    forbiddenDog.IsClosed = true;
                    _dogs.Add(forbiddenDog);
                    LogStore.Add("CatHome4", 2, "工具 " + call.Name + " 被拒绝：不在本会话声明面", "TOOL");
                    continue;
                }
                // timeback 本体修正黑名单——作用域存活期禁止对 CH4 自身做修正（莎 2026-09-28 定；统一在此点拦 host-* / 内置 / OA 三分支）
                if (_timebackScope != null && IsTimebackBodyLocked(call.Name))
                {
                    ToolOrderDog lockedDog = new ToolOrderDog(call.Id, call.Name, call.Arguments);
                    lockedDog.Result = "ERR|TIMEBACK_LOCKED|timeback 作用域内禁止对 CH4 自身做修正: " + call.Name + "——先 back 回收";
                    lockedDog.IsClosed = true;
                    _dogs.Add(lockedDog);
                    LogStore.Add("CatHome4", 2, "工具 " + call.Name + " 被拒绝：timeback 作用域内禁止本体修正", "TIMEBACK");
                    continue;
                }
                // image-inject 作用域门禁——仅 timeback 活跃时可用（design-ch4-chat-images §8.4-3：域外拒绝，不静默降级；
                // 方向与上一条黑名单相反——这里拦的是「域外调用」）
                if (call.Name == "image-inject" && _timebackScope == null)
                {
                    ToolOrderDog scopeDog = new ToolOrderDog(call.Id, call.Name, call.Arguments);
                    scopeDog.Result = "ERR|TIMEBACK_REQUIRED|图片插入仅在 timeback 作用域内可用（图片不必常驻主干）——先 start 开锚";
                    scopeDog.IsClosed = true;
                    _dogs.Add(scopeDog);
                    LogStore.Add("CatHome4", 2, "工具 " + call.Name + " 被拒绝：timeback 作用域外不可用", "TIMEBACK");
                    continue;
                }
                // per-cat 路由——载荷注入猫 key（会话标识 ≡ 猫 key；积木按 catId 解析猫级文件系统与配置面）
                string arguments = InjectCatId(call.Arguments);
                // roundsum 工具计数——合法工具调用 +1（被拒工具不计）
                _toolCallCount = _toolCallCount + 1;
                // 剥离条目——back 后置执行（PumpToolBatch 段2b-2：其余工具全部完成之后、结果回填之前）
                if (i == backIndex)
                {
                    ToolOrderDog backDog = new ToolOrderDog(call.Id, call.Name, arguments);
                    long backCardSeq;
                    backDog.CardSeq = cardSeqs.TryGetValue(call.Id, out backCardSeq) ? backCardSeq : -1;
                    // 不参与本批轮询（后置段执行）——IsClosed 直置，防 allDone 判定空等
                    backDog.IsClosed = true;
                    _timebackBackDog = backDog;
                    _dogs.Add(backDog);
                    continue;
                }
                ToolOrderDog dog = new ToolOrderDog(call.Id, call.Name, arguments);
                long pendingCardSeq;
                dog.CardSeq = cardSeqs.TryGetValue(call.Id, out pendingCardSeq) ? pendingCardSeq : -1;
                if (call.Name.StartsWith("host-", StringComparison.Ordinal))
                {
                    // host-* 延迟直执登记——批次末尾执行（顺序保证：同批 mau-proj 等先完成产物落地）
                    _hostDogs.Add(dog);
                    dog.IsClosed = true;
                }
                else if (IsBuiltinTool(call.Name))
                {
                    // 内置工具会话内直执——不需 OA（R0.2 分层：Note 状态在会话实例；time/random/info 宿主直执）
                    dog.Result = ExecuteBuiltin(call.Name, arguments);
                    dog.IsClosed = true;
                }
                else
                {
                    dog.Post(_oa, ToolOwnerId);
                    if (dog.OfficeId == 0)
                    {
                        // Post 失败——诚实失败（直执面已移除 2026-09-04——OA 不可用即 ERR，不静默降级）
                        dog.Result = "ERR|OA_POST_FAIL|工单提交失败（OA 不可用）: " + call.Name;
                        dog.IsClosed = true;
                        LogStore.Add("CatHome4", 2, "工具 " + call.Name + " 工单提交失败（OA 不可用）——诚实 ERR", "TOOL");
                    }
                }
                _dogs.Add(dog);
            }
            _phase = ChatPhase.ToolBatchRunning;
            _phaseFrames = 0;
        }

        /// <summary>
        /// 工具可用性判定——特权面 + 池校验 + 当前授权集实时查询（design-ch4-tools §三·十一：注入面 = 提示，授权面 = 真相）。
        /// 授权集由组合根注入的 AuthorizedToolNamesProvider 提供（Admin 域实时解析 cat.cfg 名单）；未接线 → 回落 _tools 快照（旧语义）+ 一次告警。
        /// 公开面——漂移观测等诊断消费（info tools_drift.added 只报三面全过者；莎 2026-09-28）。
        /// </summary>
        /// <param name="name">工具名</param>
        /// <returns>true=可调用</returns>
        public bool IsToolAllowed(string name)
        {
            // 特权面——仅默认会话可调（判据 = 注册面组级标记，单一真相源；design-ch4-host-restart §二）
            if (IsPrivilegedTool(name) && !IsDefaultSession)
            {
                return false;
            }
            // 池校验——工具须在运行时注册表内（未加载 / 已退役 = 拒）
            if (ToolRegistry.Find(name) == null)
            {
                return false;
            }
            // 当前授权集——实时查询（改配置 / 热重载即时生效，无需新会话）
            string[] authorized = ResolveAuthorizedToolNames();
            for (int i = 0; i < authorized.Length; i = i + 1)
            {
                if (string.Equals(authorized[i], name, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }
        /// <summary>
        /// 解析当前授权工具名——委托链（Admin 域实时解析当前 cat.cfg 名单）；未接线回落会话注入面快照。
        /// </summary>
        /// <returns>授权工具名数组（非 null）</returns>
        private string[] ResolveAuthorizedToolNames()
        {
            if (AuthorizedToolNamesProvider == null)
            {
                if (!_authorizedProviderWarned)
                {
                    _authorizedProviderWarned = true;
                    LogStore.Add("CatHome4", 2, "授权面提供者未接线——工具判定回落会话声明面快照", "TOOL");
                }
                return DeclaredToolNames;
            }
            string[] names = AuthorizedToolNamesProvider(_catKey);
            if (names == null)
            {
                return new string[0];
            }
            return names;
        }

        /// <summary>
        /// 载荷注入猫 key——arguments JSON 合并 catId 字段（会话标识 ≡ 猫 key，唯一标识；非对象/解析失败原样透传）。
        /// </summary>
        /// <param name="arguments">LLM 原始参数 JSON</param>
        /// <returns>注入后 JSON</returns>
        private string InjectCatId(string arguments)
        {
            if (arguments == null || arguments.Length == 0)
            {
                return arguments;
            }
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(arguments))
                {
                    if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    {
                        return arguments;
                    }
                    Dictionary<string, object> map = new Dictionary<string, object>();
                    foreach (JsonProperty p in doc.RootElement.EnumerateObject())
                    {
                        map[p.Name] = p.Value.Clone();
                    }
                    map["catId"] = _catKey;
                    return JsonUtil.Serialize(map);
                }
            }
            catch (Exception)
            {
                // 参数 JSON 损坏——原样透传（下游 BAD_ARGS 校验可见拒绝）
                return arguments;
            }
        }
        /// <summary>
        /// ToolBatchRunning 相位推进——逐 Dog 帧轮询；allDone 或帧超限 → ToolBatchDone 动作段（host-* 延迟直执 → 收集回执 → 续轮/收敛）。
        /// </summary>
        private void PumpToolBatch()
        {
            _phaseFrames = _phaseFrames + 1;
            bool allDone = true;
            for (int i = 0; i < _dogs.Count; i = i + 1)
            {
                _dogs[i].Tick(_oa);
                if (!_dogs[i].IsClosed && !_dogs[i].IsTimedOut)
                {
                    allDone = false;
                }
            }
            if (!allDone && _phaseFrames < ToolBatchWaitFrames)
            {
                return;
            }
            // [段2b] host-* 延迟直执——批次其他工具完成后宿主直执（忙时豁免：主线程串行——宿主 ExecuteReload 事务三段式兜底）
            for (int h = 0; h < _hostDogs.Count; h = h + 1)
            {
                ToolOrderDog dog = _hostDogs[h];
                bool savedBusy = _toolBatchActive;
                _toolBatchActive = false;
                string hr = RunTool(dog.Name, dog.ArgsJson);
                _toolBatchActive = savedBusy;
                if (hr == null || hr.Length == 0)
                {
                    hr = "ERR|EMPTY_RESULT|工具执行无结果";
                }
                dog.Result = hr;
            }
            // [段2b-2] timeback back 后置执行（A106 批内次序）——本批其余工具（含 host-* 延迟直执）全部完成之后、
            // 结果回填之前执行：前文末条仍为本批 assistant 声明 → 回收区间上界与释放条数预算同源可对账
            RunDeferredTimebackBack();
            // [段3] 收集——Closed 取回执；TimeOut/帧超限 → 诚实 ERR（OA 链路失败即报错，不直执）
            for (int i = 0; i < _dogs.Count; i = i + 1)
            {
                ToolOrderDog dog = _dogs[i];
                if (!dog.IsClosed && !dog.IsTimedOut)
                {
                    dog.IsTimedOut = true;
                }
                if (dog.IsTimedOut)
                {
                    LogStore.Add("CatHome4", 2, "工具 " + dog.Name + " 工单 #" + dog.OfficeId + " 超时（无人认领）——诚实 ERR", "TOOL");
                    dog.Result = "ERR|OA_TIMEOUT|工单超时无人认领: " + dog.Name;
                    dog.IsClosed = true;
                }
                if (dog.Result == null || dog.Result.Length == 0)
                {
                    dog.Result = "ERR|EMPTY_RESULT|工具执行无结果";
                }
                // B4 对话区：工具结果实时推送 SSE（tool 事件——先行"进行中"卡原位替换为完整卡；参数/结果视图截断同 history）
                if (_httpHost != null)
                {
                    // A69 视图层报错中文注释——真实前文（dog.Result）保持原文
                    string viewResult = ErrorNote.Apply(dog.Result);
                    string toolJson = "{\"name\":" + JsonUtil.Serialize(dog.Name) + ",\"arguments\":" + JsonUtil.Serialize(dog.ArgsJson) + ",\"result\":" + JsonUtil.Serialize(viewResult) + ",\"toolIndex\":" + (i + 1).ToString() + ",\"toolTotal\":" + _dogs.Count.ToString() + "}";
                    _httpHost.PushView("toolcard", toolJson, dog.CardSeq, 0);
                    dog.CardSeq = -1;
                }
                AppendMessage(_context.AddToolResult(dog.ToolCallId, dog.Name, dog.Result));
                NoteTimebackEvent();
                _viewStore.OnToolResult(LastMessage(), ViewTimestamp(), _context.GetMessageCount() - 1);
            }
            _toolBatchActive = false;
            // [段2d-0] 图片注入——本批 image-inject 登记合并为一条 user 注入消息（落在作用域区间内：back 回收时一并删除）
            FlushImageInjections();
            // [段2d] timeback 回卷——本批请求了 back 则在此执行（工具结果已全部回填：截断 + 结论注入 + 工具主动 done）
            ApplyTimebackBack();
            // [段2e] timeback 状态自述——回收后作用域已关（自然跳过）；未关且累计满 10 事件则追加一条 assistant 自述
            FlushTimebackNotice();
            // [段2c] 宿主重启检测——majordomo-restart 成功回执 → 登记重启请求 + 停机态（A72：本轮走常规结束流程，不强制中断）
            for (int r = 0; r < _dogs.Count; r = r + 1)
            {
                ToolOrderDog rd = _dogs[r];
                if (rd.Name == "majordomo-restart" && rd.Result != null && !rd.Result.StartsWith("ERR|", StringComparison.Ordinal))
                {
                    string restartReq;
                    if (!DataBox.TryGet<string>("global", "host_restart_request", out restartReq) || restartReq == null || restartReq.Length == 0)
                    {
                        restartReq = "{}";
                    }
                    // 工具主动 done——本轮结束语义为「工具收尾」（QQ 转发面据此续约来源：回执轮才是回复轮）
                    _toolDone = true;
                    RestartRequest(restartReq);
                }
            }
            // A72——停机态丢弃在途排队消息（宿主即将重启，不再开新轮）
            DropPendingIfRestarting();
            // 单向数据流改造——忙时插话：工具批完成有排队消息 → 插入 Ctx + user 事件 + 直接续轮（工具结果 + 插话同轮可见）
            if (_pending.Count > 0)
            {
                PendingMessage next = _pending.Dequeue();
                AppendMessage(_context.AddUserMessage(next.Content));
                _viewStore.OnUserMessage(LastMessage(), ViewTimestamp(), _context.GetMessageCount() - 1);
                if (_httpHost != null)
                {
                    string userJson = "{\"content\":" + JsonUtil.Serialize(next.Content) + ",\"source\":\"" + next.Source + "\"}";
                    _httpHost.PushView("user", userJson, -1, 0);
                }
                _round = 0;
                LaunchLlm();
                return;
            }
            _round = _round + 1;
            // 续轮——StartLlm 动作段（无收敛上限——LLM 未给出最终回复前持续工具循环；终止条件 = 正常回复 / 空回复续传 / API 错误中止）
            LaunchLlm();
        }
        /// <summary>
        /// 错误中止收尾——LLM API 错误重试耗尽后调用（保留断点上下文 + 错误气泡；不走 CloseRound——中止非正常完成语义）。
        /// 对齐 PauseFinalize：落盘断点（落盘保真）+ 视图序号复位 + 状态复位 Idle + chat_state=idle + 推 error 事件（前端 seal + 错误气泡）。
        /// </summary>
        private void AbortRoundError()
        {
            SealReasonStream();
            _textStreamSeq = 0;
            // 运行态——中止前结算当前态（失败轮同出统计：L2 摘要留档；不推 roundsum 气泡——中止非正常完成语义）
            PhaseSettle();
            LogStore.Add("LLM", 2, "本轮运行态统计（中止）: " + BuildRunStateSummary(), "LLM");
            LogStore.Add("LLM", 3, "LLM 错误（重试耗尽——本轮中止，上下文保持断点）: " + TrimDisplay(_llmErrorText, 300), "LLM");
            // [段1] 前文落盘——落盘保真
            LlmMessage[] toSave = _context.GetMessages();
            _lastStats.EntryCount = toSave.Length;
            _store.AppendMeta(_lastStats);
            // [段2] 错误可见——视图块落盘（持久化）+ 前端 error 事件（文本取清空前原值）
            // A69 视图层报错中文注释——错误原文仍进日志与前文面，仅视图块追加中文注释
            string viewError = ErrorNote.Apply(_llmErrorText);
            _viewStore.AppendError(viewError, ViewTimestamp());
            if (_httpHost != null)
            {
                string errJson = "{\"type\":\"error\",\"text\":" + JsonUtil.Serialize(viewError) + "}";
                _httpHost.PushView("error", errJson, -1, 0);
            }
            // [段3] 重试耗尽终态（A94——本轮推过 retry 气泡则补 failed 终态，保留报错原文；最终错误详情仍归 error 气泡）
            if (_retrySeq != 0)
            {
                PushRetryView("failed");
            }
            // [段4] 状态复位——Idle（不推 chatdone/roundsum/Note 拉起——错误中止非正常完成语义）
            _round = 0;
            _phase = ChatPhase.Idle;
            _phaseFrames = 0;
            ResetRetryView();
            _llmBusy = false;
            _llmError = false;
            _llmErrorText = "";
            _llmResultText = "";
            _llmReasoning = "";
            _llmToolCallsJson = "";
            _emptyReplyRetry = 0;
            _streamClosedRetry = false;
            SetChatState("idle");
        }

        /// <summary>
        /// Done 相位——前文落盘（落盘保真）+ chat_state=idle + PushChatDone + 复位（原 HandleChat 段4）。
        /// </summary>
        private void CloseRound()
        {
            // E3 真实 usage 统计——轮末落盘（info 自查/前端显示数据源；零估算）
            _lastStats.EntryCount = _context.GetMessageCount();
            _lastStats.LastPromptTokens = _usagePrompt;
            _lastStats.LastCacheHitTokens = _usageCacheHit;
            _lastStats.LastCompletionTokens = _usageCompletion;
            _lastStats.LastContextTokens = _contextTokens;
            _store.AppendMeta(_lastStats);
            // M4a Note 自动拉起提前——剩余≥2 条时以 user 名义推下一轮（最后 1 条不拉起——LLM 完成后自然结束；Q2 顺序：Note 未完成 = 本轮未结束——不 roundsum/chatdone；全部完成天然跳过——防无限循环闸门）
            if (_noteTasks != null && _noteTasks.Length > 0 && _noteCurrent + 1 < _noteTasks.Length)
            {
                int remain = _noteTasks.Length - _noteCurrent;
                // 运行态——Note 拉起轮同样结算（本轮统计留档——design-ch4-llm §2.1 终止语义）
                PhaseSettle();
                PostUserMessage("[Note 未完成] 剩余 " + remain + " 条\n当前任务：" + _noteTasks[_noteCurrent], "system");
                _viewStore.Save();
                _round = 0;
                _phase = ChatPhase.Idle;
                return;
            }
            // roundsum 轮末统计——相位结算 + 载荷构建（Appender 内落盘）+ SSE 推送（本轮 Token 消耗 + 工具次数 + 总耗时 + 四态用时）
            PhaseSettle();
            string roundsumJson = BuildRoundSumJson();
            _viewStore.AppendRoundSummary(roundsumJson, ViewTimestamp());
            if (_httpHost != null)
            {
                _httpHost.PushView("roundsum", roundsumJson, -1, 0);
            }
            SetChatState("idle");
            // B4 对话区：会话终态事件——前端定型（llm done 仅一轮结束；chatdone 才是整次会话结束；count = 原始消息数——实时同步状态区）
            // E3 扩展——chatdone 带真实 usage（命中/非命中/输出/前文长度；前端状态栏同步显示）
            if (_httpHost != null)
            {
                string doneJson = "{\"type\":\"chatdone\",\"count\":" + _context.GetMessages().Length.ToString()
                    + ",\"stats\":{\"prompt\":" + _usagePrompt.ToString()
                    + ",\"cacheHit\":" + _usageCacheHit.ToString()
                    + ",\"completion\":" + _usageCompletion.ToString()
                    + ",\"context\":" + _contextTokens.ToString()
                    + ",\"sessionPrompt\":" + _sessionPrompt.ToString()
                    + ",\"sessionCompletion\":" + _sessionCompletion.ToString()
                    + ",\"sessionCacheHit\":" + _sessionCacheHit.ToString() + "}}";
                _httpHost.PushView("control", doneJson, -1, 0);
            }
            // Q 本轮结束系统通知——配置 app.round_notify 可开关（默认开启）；正文优先末轮回复前 40 字符，空则回退 Token 统计
            if (_roundNotify != null)
            {
                string notifyOn = "true";
                ConfigStore cfg = null;
                if (DataBox.TryResolve<ConfigStore>(out cfg) && cfg != null)
                {
                    notifyOn = cfg.Get("app.round_notify", "true");
                }
                if (notifyOn == "true" || notifyOn == "1")
                {
                    string body = TrimDisplay(_llmResultText, 40);
                    if (body.Length == 0)
                    {
                        body = "Token：输入 " + _usagePrompt.ToString() + " · 输出 " + _usageCompletion.ToString() + " · 工具 " + _toolCallCount.ToString() + " 次";
                    }
                    _roundNotify(_displayName, body);
                }
            }
            // M4c Note 清空——无剩余任务（正常结束路径；拉起已提前到 roundsum 之前——Q2 顺序调整）
            _noteTasks = null;
            _noteCurrent = 0;
            _noteDone = 0;
            PushNoteState();
            _round = 0;
            _phase = ChatPhase.Idle;
            ResetRetryView();
        }

        /// <summary>
        /// 前文截断到指定条数——截断唯一共用实现（人工回滚 / timeback 回卷同源；design-ch4-timeback §12.5）。
        /// 执行：截断上下文 → 前文落盘（截断重写：append-only 的合法例外）→ 轮级计数与统计条数复位。
        /// 不含：会话级统计归零（调用方决定）/ Note 处置 / 视图处置 / 前端通知——各路径语义不同，分头保留。
        /// </summary>
        /// <param name="keepCount">保留条数（保留 [0..keepCount-1]）</param>
        private void TruncateMessages(int keepCount)
        {
            // [段1] 截断上下文——保留前 keepCount 条（ReplaceMessages 安全面——格式修复幂等）
            LlmMessage[] all = _context.GetMessages();
            int count = keepCount;
            if (count < 0)
            {
                count = 0;
            }
            if (count > all.Length)
            {
                count = all.Length;
            }
            LlmMessage[] keep = new LlmMessage[count];
            for (int i = 0; i < count; i = i + 1)
            {
                keep[i] = all[i];
            }
            _context.ReplaceMessages(keep);
            // [段2] 前文落盘——落盘保真（截断重写：append-only 的合法例外）
            LlmMessage[] toSave = _context.GetMessages();
            _lastStats.EntryCount = toSave.Length;
            _store.Rewrite(toSave, _lastStats);
            // [段3] 轮级计数复位——新起点零统计起算（会话级累计不动——同会话延续）
            _usagePrompt = 0;
            _usageCompletion = 0;
            _usageCacheHit = 0;
            _contextTokens = 0;
            _toolCallCount = 0;
        }

        /// <summary>
        /// 回滚——从指定正式回复节点重新开始（P6b：裁剪唯一通道；该节点后消息全部丢弃）。
        /// 校验：仅 Idle；msgIndex 指向 assistant 正式回复（Content>0——工具声明轮天然排除）。执行：截断上下文 → 落盘 → 统计/视图/Note 复位 → 视图重建 → session_reset 推送。
        /// </summary>
        /// <param name="msgIndex">真实前文消息索引（指向 assistant 正式回复——前端 text 块 MsgIndex）</param>
        /// <returns>ERR| 前缀失败 / 成功摘要</returns>
        public string Rollback(int msgIndex)
        {
            if (_phase != ChatPhase.Idle)
            {
                return "ERR|ROLLBACK_BUSY|会话忙——回滚仅空闲时执行";
            }
            LlmMessage[] all = _context.GetMessages();
            if (msgIndex < 0 || msgIndex >= all.Length)
            {
                return "ERR|ROLLBACK_INDEX|节点索引越界: " + msgIndex.ToString();
            }
            LlmMessage target = all[msgIndex];
            if (target.Role != LlmRole.Assistant || target.Content == null || target.Content.Length == 0)
            {
                return "ERR|ROLLBACK_NODE|节点不是正式回复（仅 assistant 回复可作切点）";
            }
            // [段1] 截断前文到切点——共用实现（timeback 回卷同源；含落盘与轮级计数复位）
            TruncateMessages(msgIndex + 1);
            // [段2] 最近轮统计重置——新起点零统计起算（会话级累计不动：回滚属同会话延续；
            //        原实现调 ResetStats() 会清会话级 token 累计——与 glossary「回滚不归零」口径冲突，2026-09-28 修正）
            _lastStats = new SessionStats();
            // [段4] 视图——从新前文完全重建 + roundsum 清空（roundsum 非真实前文派生；RebuildView 的 LoadInjectReport 会读回旧 view.json 统计——重建后清空并落盘，防下次启动读回）
            RebuildView();
            _viewStore.ClearRoundSums();
            _viewStore.Save();
            // [段5] Note 任务清空——防旧任务自动拉起新轮
            _noteTasks = null;
            _noteCurrent = 0;
            _noteDone = 0;
            _noteJustCompleted = false;
            PushNoteState();
            // [段6] 前端通知——session_reset（前端清空气泡重拉 history；渲染层零改动）
            PushSessionReset();
            SetChatState("idle");
            LogStore.Add("CatHome4", 1, "已回滚到节点 " + msgIndex.ToString() + "（保留 " + (msgIndex + 1).ToString() + " 条消息）", "CHAT");
            return "rollback | 已截断到节点 " + msgIndex.ToString() + "（保留 " + (msgIndex + 1).ToString() + " 条消息）";
        }

        /// <summary>
        /// 读取 JSON 对象字符串属性——防御式（缺字段返回空串）。
        /// </summary>
        /// <param name="obj">JSON 对象</param>
        /// <param name="prop">属性名</param>
        /// <returns>属性值</returns>
        private static string GetStringProp(JsonElement obj, string prop)
        {
            JsonElement value;
            if (obj.TryGetProperty(prop, out value) && value.ValueKind == JsonValueKind.String)
            {
                string got = value.GetString();
                if (got != null)
                {
                    return got;
                }
            }
            return "";
        }

        /// <summary>
        /// 截断显示文本——日志防刷屏（保留头部 + 截断提示）。
        /// </summary>
        /// <param name="text">原文</param>
        /// <param name="max">上限</param>
        /// <returns>截断文本</returns>
        private static string TrimDisplay(string text, int max)
        {
            if (text == null || text.Length <= max)
            {
                if (text == null)
                {
                    return "";
                }
                return text;
            }
            return text.Substring(0, max) + "...";
        }
    }
}
using System;
using System.Text.Json;

namespace Mau.Runtime
{
    /// <summary>
    /// 审计存储——并入 Log 后的兼容薄壳（design-ch4-observe §二/§七 O2）。
    /// 环形缓冲与独立 MD 落盘退役——Record 转发 LogStore（Type=audit.*、Payload=props JSON、category=AUDIT）；
    /// Snapshot/Total/Query 数据源 = LogStore 的 audit.* 过滤重建（AuditQuery 无感兼容）。
    /// persistable=false（trace 类）不转发——保留"trace 不落盘"语义（AuditEvent 纪律）。
    /// </summary>
    public sealed class AuditStore
    {
        /// <summary>
        /// 构造——兼容签名（容量/时间源已并入 Log——参数保留接受但不使用）
        /// </summary>
        /// <param name="capacity">兼容参数（环形退役——忽略）</param>
        /// <param name="now">兼容参数（时间源归 LogStore——忽略）</param>
        public AuditStore(int capacity = 10000, Func<DateTime>? now = null)
        {
        }

        /// <summary>
        /// 当前帧号——兼容属性（LogStore 即时取 FlowRunner.GlobalFrame）
        /// </summary>
        public long CurrentFrame
        {
            get { return FlowRunner.GlobalFrame; }
        }

        /// <summary>
        /// 程序级默认实例——兼容入口（生成物/宿主 Record 调用点不变）
        /// </summary>
        public static AuditStore? Default
        {
            get;
            set;
        }

        /// <summary>
        /// 重置程序级默认实例——置空（D26 统一 Reset 契约；宿主切换/测试隔离调用）
        /// </summary>
        public static void Reset()
        {
            Default = null;
        }

        /// <summary>
        /// 记录审计事件——转发 LogStore（统一观测真源）；trace 不转发。
        /// 危险等级：默认 1（INFO）——WARN/ERR 由错误路径直写 LogStore.Add(level=2/3)（生成物 Record 无等级通道——2026-08-18 定案）。
        /// </summary>
        /// <param name="source">来源（CommandBus/OA/FlowRunner/ConfigStore/...）</param>
        /// <param name="category">类别（cmd.set/oa.post/flow.register/app.start/...）</param>
        /// <param name="frame">全局帧号（-1=用 LogStore 当前帧）</param>
        /// <param name="props">属性键值数组（可空）</param>
        /// <param name="persistable">是否进 Log（默认 true；trace 类 false）</param>
        public void Record(string source, string category, long frame, AuditProp[]? props, bool persistable = true)
        {
            if (category == null)
            {
                category = "";
            }
            long actualFrame = FlowRunner.GlobalFrame;
            if (frame >= 0)
            {
                actualFrame = frame;
            }
            string payload = BuildPayload(props);
            string message = BuildMessage(category, props);
            // trace.* 语义独立于 persistable 标志（旧调用传 false 表示"不落盘"——新语义由 isTrace 决定 skipDisk）
            bool isTrace = category.StartsWith("trace.", StringComparison.Ordinal);
            if (!persistable && !isTrace)
            {
                // persistable=false（signal.* 等高频）——完全出局（调用侧显式声明）
                return;
            }
            // trace.sample 高频采样——完全出局（不转发不落盘——D5 L0 语义；10+/帧 若进内存会淹没查询面）
            if (isTrace && category.StartsWith("trace.sample", StringComparison.Ordinal))
            {
                return;
            }
            // L0-TRACE 语义（D5）：trace.* 仅内存真源可见（AuditQuery/sys.trace 可查）——磁盘与 Console 通道跳过
            if (isTrace)
            {
                LogStore.Add(source, 1, message, "AUDIT", "audit." + source + "." + category, payload, 0, actualFrame, true);
                return;
            }
            // 全参数 V2——Type=audit.{source}.{category}；模块=source；类别=AUDIT（log_all.txt 投影可见）
            LogStore.Add(source, 1, message, "AUDIT", "audit." + source + "." + category, payload, 0, actualFrame, false);
        }

        /// <summary>
        /// 兼容重载——无 frame 参数（保留给旧调用形态）
        /// </summary>
        /// <param name="source">来源</param>
        /// <param name="category">类别</param>
        /// <param name="props">属性</param>
        /// <param name="persistable">是否进 Log</param>
        public void Record(string source, string category, AuditProp[]? props, bool persistable = true)
        {
            Record(source, category, -1, props, persistable);
        }

        /// <summary>
        /// 审计事件快照——从 LogStore audit.* 过滤重建（时间序 旧→新；AuditQuery 数据源兼容）
        /// </summary>
        /// <returns>事件数组</returns>
        public AuditEvent[] Snapshot()
        {
            System.Collections.Generic.List<LogStore.LogEntry> all = LogStore.AllLog;
            System.Collections.Generic.List<AuditEvent> result = new System.Collections.Generic.List<AuditEvent>();
            lock (LogStore.Sync)
            {
                for (int i = 0; i < all.Count; i++)
                {
                    LogStore.LogEntry entry = all[i];
                    if (!entry.Type.StartsWith("audit.", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    string source = entry.Module;
                    string category = entry.Type;
                    // Type = "audit.{source}.{category}"——剥前缀还原 category（Module 即 source——可靠锚）
                    string prefix = "audit." + source + ".";
                    if (category.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        category = category.Substring(prefix.Length);
                    }
                    else
                    {
                        int dot = category.IndexOf('.');
                        if (dot >= 0)
                        {
                            category = category.Substring(dot + 1);
                        }
                    }
                    AuditProp[] props = ParsePayload(entry.Payload);
                    result.Add(new AuditEvent(i + 1, entry.Frame, entry.Time, source, category, props, true));
                }
            }
            return result.ToArray();
        }

        /// <summary>
        /// 累计事件总数——audit.* 条目数
        /// </summary>
        public long Total
        {
            get
            {
                long count = 0;
                System.Collections.Generic.List<LogStore.LogEntry> all = LogStore.AllLog;
                lock (LogStore.Sync)
                {
                    for (int i = 0; i < all.Count; i++)
                    {
                        if (all[i].Type.StartsWith("audit.", StringComparison.Ordinal))
                        {
                            count = count + 1;
                        }
                    }
                }
                return count;
            }
        }

        /// <summary>
        /// 环形缓冲容量——退役恒 0（查询层标注兼容）
        /// </summary>
        public int RingCapacity
        {
            get { return 0; }
        }

        /// <summary>
        /// 环形满覆盖次数——退役恒 0（无环形=无萎缩窗口）
        /// </summary>
        public long RingOverflowCount
        {
            get { return 0; }
        }

        /// <summary>
        /// 配置审计根目录——已并入 Log（ConfigureRuns 承担落盘）；保留签名幂等 no-op
        /// </summary>
        public void ConfigureAudit(string root, string mode, int loadCount = 0, long startFrame = 0, int retainDays = 7, bool persistEnabled = true)
        {
            // 落盘职责已并入 LogStore.ConfigureRuns——本方法仅兼容保留
        }

        /// <summary>
        /// 关闭——幂等 no-op（写者生命周期归 LogStore）
        /// </summary>
        public void Shutdown()
        {
            // 写者归 LogStore.CloseWriters——宿主退出统一调
        }

        /// <summary>
        /// 帧号推进——兼容 no-op（LogStore 帧号 = FlowRunner.GlobalFrame 直接取）
        /// </summary>
        /// <param name="frame">当前帧</param>
        public void TickFrame(long frame)
        {
            // 并入 Log 后帧号即时可取——无需内部缓存
        }

        /// <summary>
        /// flow.tick 事件开关——兼容属性（默认关；开启时 FlowRunner 每帧 Record flow.tick）
        /// </summary>
        public bool EnableTickEvents
        {
            get;
            set;
        }

        /// <summary>
        /// 值摘要——旧格式 str:{原长}:"{截断16} "（运维/审计可见性格式——ConfigStore/RuntimeLog 埋点断言依赖）
        /// </summary>
        /// <param name="value">原值</param>
        /// <returns>摘要文本</returns>
        public static string Summarize(string value)
        {
            if (value == null)
            {
                value = "";
            }
            int fullLen = value.Length;
            string show = value;
            if (show.Length > 16)
            {
                show = show.Substring(0, 16);
            }
            return "str:" + fullLen.ToString() + ":\"" + show + "\"";
        }

        /// <summary>
        /// 事件 MD 格式化——## E{seq} | F{frame} | {time} | {source} | {category} + 属性行（AuditQuery 渲染兼容）
        /// </summary>
        /// <param name="ev">事件</param>
        /// <returns>MD 文本</returns>
        public static string FormatEvent(AuditEvent ev)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append("## E");
            // 序号补零——旧格式 E0000001 兼容（SysCommand 断言依赖）
            sb.Append(ev.Seq.ToString("D7"));
            sb.Append(" | F");
            sb.Append(ev.Frame.ToString());
            sb.Append(" | ");
            sb.Append(ev.Time);
            sb.Append(" | ");
            sb.Append(ev.Source);
            sb.Append(" | ");
            sb.Append(ev.Category);
            sb.AppendLine();
            for (int i = 0; i < ev.Props.Length; i++)
            {
                sb.Append("- ");
                sb.Append(ev.Props[i].Key);
                sb.Append(": ");
                sb.Append(ev.Props[i].Value);
                sb.AppendLine();
            }
            return sb.ToString();
        }

        /// <summary>
        /// 构建载荷 JSON——props 序列化（密钥纪律：埋点侧已标记化）
        /// </summary>
        /// <param name="props">属性数组</param>
        /// <returns>JSON 文本</returns>
        private static string BuildPayload(AuditProp[]? props)
        {
            if (props == null || props.Length == 0)
            {
                return "";
            }
            System.Collections.Generic.List<object> list = new System.Collections.Generic.List<object>();
            for (int i = 0; i < props.Length; i = i + 1)
            {
                list.Add(new { k = props[i].Key, v = props[i].Value });
            }
            try
            {
                return JsonUtil.Serialize(list);
            }
            catch (Exception)
            {
                // 序列化失败——按无载荷处理（审计非关键路径降级，不阻断主记录）
                return "";
            }
        }        /// <summary>
                 /// 构建消息——category + 首三个属性摘要（log.all 可读性）
                 /// </summary>
                 /// <param name="category">事件类别</param>
                 /// <param name="props">属性数组</param>
                 /// <returns>消息文本</returns>
        private static string BuildMessage(string category, AuditProp[]? props)
        {
            if (category == "flow.register")
            {
                return "注册流程 #" + FindProp(props, "flowId") + "「" + FindProp(props, "name") + "」（" + FindProp(props, "kind") + "）";
            }
            if (category == "flow.unregister")
            {
                return "注销流程 #" + FindProp(props, "flowId");
            }
            if (category == "cmd.register")
            {
                string result = FindProp(props, "result");
                if (result == "rejected")
                {
                    return "注册指令总线被拒 #" + FindProp(props, "owner") + "（" + FindProp(props, "keys") + "）：" + FindProp(props, "reason");
                }
                return "注册指令总线 #" + FindProp(props, "owner") + "：" + FindProp(props, "keys");
            }
            if (category == "cmd.set")
            {
                string result = FindProp(props, "result");
                string payload = FindProp(props, "payload");
                if (result == "rejected")
                {
                    return "指令投递被拒 " + FindProp(props, "key") + "（来源 " + FindProp(props, "source") + "）：" + FindProp(props, "reason");
                }
                if (payload.Length > 0)
                {
                    return "指令投递 " + FindProp(props, "key") + "（来源 " + FindProp(props, "source") + "）：" + payload;
                }
                return "指令投递 " + FindProp(props, "key") + "（来源 " + FindProp(props, "source") + "）";
            }
            if (category == "cmd.consume")
            {
                return "消费指令 #" + FindProp(props, "owner") + "：" + FindProp(props, "keys");
            }
            if (category == "cmd.clean")
            {
                return "清理指令 #" + FindProp(props, "owner") + "（" + FindProp(props, "reason") + "）：" + FindProp(props, "keys");
            }
            if (category == "oa.post")
            {
                return "提交工单 #" + FindProp(props, "officeId") + "：" + FindProp(props, "type") + "/" + FindProp(props, "name") + "（owner #" + FindProp(props, "ownerId") + "，超时 " + FindProp(props, "timeout") + " 帧）";
            }
            if (category == "oa.claim")
            {
                return "认领工单 #" + FindProp(props, "officeId") + "（worker #" + FindProp(props, "workerId") + "）";
            }
            if (category == "oa.complete")
            {
                return "完成工单 #" + FindProp(props, "officeId") + "（worker #" + FindProp(props, "workerId") + "，回执 " + FindProp(props, "result") + "）";
            }
            if (category == "oa.settle")
            {
                return "结算工单 #" + FindProp(props, "officeId") + "：" + FindProp(props, "reason");
            }
            if (category == "cfg.change")
            {
                return "配置变更 " + FindProp(props, "key") + " = " + FindProp(props, "value");
            }
            if (category == "signal.post")
            {
                return "信号置位：" + FindProp(props, "name") + "（帧 " + FindProp(props, "frame") + "）";
            }
            if (category == "signal.consume")
            {
                return "信号消费：" + FindProp(props, "name") + "（帧 " + FindProp(props, "frame") + "）";
            }
            if (category == "log.error")
            {
                return "运行时错误：" + FindProp(props, "message");
            }
            string message = category;
            if (props != null)
            {
                int max = 3;
                if (props.Length < max)
                {
                    max = props.Length;
                }
                for (int i = 0; i < max; i++)
                {
                    message = message + " | " + props[i].Key + "=" + props[i].Value;
                }
            }
            return message;
        }

        /// <summary>
        /// 查找属性值——按 key 精确匹配（缺失返回空串）
        /// </summary>
        /// <param name="props">属性数组</param>
        /// <param name="key">属性键</param>
        /// <returns>属性值；未找到空串</returns>
        private static string FindProp(AuditProp[]? props, string key)
        {
            if (props == null)
            {
                return "";
            }
            for (int i = 0; i < props.Length; i = i + 1)
            {
                if (props[i].Key == key)
                {
                    return props[i].Value;
                }
            }
            return "";
        }

        /// <summary>
        /// 解析载荷 JSON——还原 AuditProp 数组（Snapshot 重建用；解析失败返回空）
        /// </summary>
        /// <param name="payload">JSON 文本</param>
        /// <returns>属性数组</returns>
        private static AuditProp[] ParsePayload(string payload)
        {
            if (payload == null || payload.Length == 0)
            {
                return Array.Empty<AuditProp>();
            }
            try
            {
                using (JsonDocument doc = JsonUtil.ParseStrict(payload))
                {
                    JsonElement root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Array)
                    {
                        return Array.Empty<AuditProp>();
                    }
                    System.Collections.Generic.List<AuditProp> list = new System.Collections.Generic.List<AuditProp>();
                    for (int i = 0; i < root.GetArrayLength(); i++)
                    {
                        JsonElement item = root[i];
                        string key = "";
                        string value = "";
                        JsonElement k;
                        if (item.TryGetProperty("k", out k) && k.ValueKind == JsonValueKind.String)
                        {
                            string? got = k.GetString();
                            if (got != null)
                            {
                                key = got;
                            }
                        }
                        JsonElement v;
                        if (item.TryGetProperty("v", out v) && v.ValueKind == JsonValueKind.String)
                        {
                            string? got = v.GetString();
                            if (got != null)
                            {
                                value = got;
                            }
                        }
                        list.Add(new AuditProp(key, value));
                    }
                    return list.ToArray();
                }
            }
            catch (Exception)
            {
                // 损坏载荷——返回空数组（快照重建容错：审计降级不阻断）
                return Array.Empty<AuditProp>();
            }
        }
    }
}
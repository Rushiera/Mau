using System;
using System.Collections.Generic;
using System.Text;

namespace Mau.Runtime
{
    /// <summary>
    /// SysCommand 查询指令实现分部——sys.audit/sys.cmd/sys.keys/sys.oa/sys.trace/sys.logs/sys.cmdlog/sys.olog/sys.box/sys.conf/sys.flow/sys.query/sys.summary。
    /// P7b partial 拆分——自 SysCommand.cs 原样搬移，逻辑零改动。
    /// </summary>
    public sealed partial class SysCommand
    {
        /// <summary>
        /// sys.audit——审计流查询（多条件：类别/来源/帧范围/tail）
        /// </summary>
        /// <param name="args">参数</param>
        /// <returns>MD 输出</returns>
        private string SysAudit(Dictionary<string, string> args)
        {
            string? category = Arg(args, "category");
            string? source = Arg(args, "source");
            long from = ArgLong(args, "from", 0);
            long to = ArgLong(args, "to", long.MaxValue);
            int tail = ArgInt(args, "tail", 0);
            AuditEvent[] events = _audit.Segment(from, to, category, source);
            events = Tail(events, tail);
            return Header("SYS.AUDIT", args) + _audit.FormatEvents(events);
        }

        /// <summary>
        /// sys.cmd——指定 key 的全部投递/消费记录（cmd.* 类别）
        /// </summary>
        /// <param name="args">参数</param>
        /// <returns>MD 输出</returns>
        private string SysCmd(Dictionary<string, string> args)
        {
            string? key = Arg(args, "key");
            if (key == null)
            {
                return "sys.cmd 需要 key=<CommandKey>\n" + Usage();
            }
            int tail = ArgInt(args, "tail", 0);
            AuditEvent[] byKey = _audit.FindByProp("key", key);
            AuditEvent[] byKeys = _audit.FindByProp("keys", key);
            Dictionary<long, AuditEvent> unique = new Dictionary<long, AuditEvent>();
            for (int i = 0; i < byKey.Length; i = i + 1)
            {
                unique[byKey[i].Seq] = byKey[i];
            }
            for (int i = 0; i < byKeys.Length; i = i + 1)
            {
                unique[byKeys[i].Seq] = byKeys[i];
            }
            List<AuditEvent> merged = new List<AuditEvent>(unique.Values);
            merged.Sort(CompareBySeq);
            AuditEvent[] events = merged.ToArray();
            events = FilterCategoryPrefix(events, "cmd.");
            events = Tail(events, tail);
            return Header("SYS.CMD", args) + _audit.FormatEvents(events);
        }

        /// <summary>
        /// sys.keys——key 注册清单（实时 + 历史）
        /// </summary>
        /// <param name="args">参数</param>
        /// <returns>MD 输出</returns>
        private string SysKeys(Dictionary<string, string> args)
        {
            string? owner = Arg(args, "owner");
            StringBuilder sb = new StringBuilder();
            sb.Append(Header("SYS.KEYS", args));
            // [段1] 实时注册态
            ICommandBus? bus;
            if (DataBox.TryResolve<ICommandBus>(out bus))
            {
                CommandSnapshot snap = bus.GetSnapshot();
                sb.AppendLine("## 当前注册（实时）");
                sb.AppendLine("- owners=" + snap.RegisteredOwnerCount + " keys=" + snap.RegisteredKeyCount
                    + " frozen=" + snap.FrozenKeyCount + " pending=" + snap.PendingKeyCount
                    + " rejected=" + snap.RejectedInputCount + " accepting=" + snap.IsAcceptingInput);
                string[] keyLines = bus.GetKeyDic();
                for (int i = 0; i < keyLines.Length; i = i + 1)
                {
                    sb.AppendLine("- " + keyLines[i]);
                }
            }
            // [段2] 历史注册事件
            AuditEvent[] events = _audit.Segment(0, long.MaxValue, "cmd.register", null);
            if (owner != null)
            {
                List<AuditEvent> filtered = new List<AuditEvent>();
                for (int i = 0; i < events.Length; i = i + 1)
                {
                    if (HasProp(events[i], "owner", owner))
                    {
                        filtered.Add(events[i]);
                    }
                }
                events = filtered.ToArray();
            }
            sb.AppendLine("## 注册历史（审计）");
            sb.Append(_audit.FormatEvents(events));
            return sb.ToString();
        }

        /// <summary>
        /// sys.oa——工单生命周期（id= 单号全链路；list= 全部 oa.* 事件）
        /// </summary>
        /// <param name="args">参数</param>
        /// <returns>MD 输出</returns>
        private string SysOa(Dictionary<string, string> args)
        {
            string? id = Arg(args, "id");
            StringBuilder sb = new StringBuilder();
            sb.Append(Header("SYS.OA", args));
            // [段1] 实时工单统计
            IOA? oa;
            if (DataBox.TryResolve<IOA>(out oa))
            {
                OAView view = oa.GetSnapshot();
                sb.AppendLine("## 当前工单（实时）");
                sb.AppendLine("- open=" + view.OpenCount + " work=" + view.WorkCount
                    + " closed=" + view.ClosedCount + " timeout=" + view.TimeoutCount + " version=" + view.Version);
            }
            // [段2] 生命周期查询
            if (id != null)
            {
                AuditEvent[] events = _audit.FindByProp("officeId", id);
                events = FilterCategoryPrefix(events, "oa.");
                sb.AppendLine("## 生命周期（审计）");
                sb.Append(_audit.FormatEvents(events));
                return sb.ToString();
            }
            // [段3] 工单历史
            string? status = Arg(args, "status");
            AuditEvent[] all = _audit.Segment(0, long.MaxValue, null, "OA");
            if (status != null && status.Length > 0)
            {
                List<AuditEvent> filtered = new List<AuditEvent>();
                for (int i = 0; i < all.Length; i = i + 1)
                {
                    if (HasProp(all[i], "reason", status) || HasProp(all[i], "result", status))
                    {
                        filtered.Add(all[i]);
                    }
                }
                all = filtered.ToArray();
            }
            sb.AppendLine("## 工单历史（审计）");
            sb.Append(_audit.FormatEvents(all));
            return sb.ToString();
        }

        /// <summary>
        /// sys.trace——生成物命题/变迁执行序列（trace.* 事件，flow 过滤）
        /// </summary>
        /// <param name="args">参数</param>
        /// <returns>MD 输出</returns>
        private string SysTrace(Dictionary<string, string> args)
        {
            string? flow = Arg(args, "flow");
            int tail = ArgInt(args, "tail", 0);
            AuditEvent[] events;
            if (flow != null)
            {
                events = _audit.FindByProp("flow", flow);
            }
            else
            {
                events = _audit.Segment(0, long.MaxValue, null, "Flow");
            }
            events = FilterCategoryPrefix(events, "trace.");
            events = Tail(events, tail);
            return Header("SYS.TRACE", args) + _audit.FormatEvents(events);
        }

        /// <summary>
        /// sys.logs——日志查询（log.* 事件）
        /// </summary>
        /// <param name="args">参数</param>
        /// <returns>MD 输出</returns>
        private string SysLogs(Dictionary<string, string> args)
        {
            int tail = ArgInt(args, "tail", 0);
            AuditEvent[] events = _audit.Segment(0, long.MaxValue, null, "RuntimeLog");
            events = FilterCategoryPrefix(events, "log.");
            events = Tail(events, tail);
            return Header("SYS.LOGS", args) + _audit.FormatEvents(events);
        }

        /// <summary>
        /// sys.cmdlog——C 类 Log 查询（Command 总线投递留痕）
        /// </summary>
        /// <param name="args">参数（tail=N 尾截断）</param>
        /// <returns>MD 输出</returns>
        private string SysCmdLog(Dictionary<string, string> args)
        {
            return SysCategoryLog("SYS.CMDLOG", "CMD", args);
        }

        /// <summary>
        /// sys.olog——O 类 Log 查询（OA 工单生命周期）
        /// </summary>
        /// <param name="args">参数（tail=N 尾截断）</param>
        /// <returns>MD 输出</returns>
        private string SysOLog(Dictionary<string, string> args)
        {
            return SysCategoryLog("SYS.OLOG", "OA", args);
        }

        /// <summary>
        /// 按类别查询 LogStore——锁定复制 → 类别过滤 → tail 截断 → MD 输出
        /// </summary>
        /// <param name="title">输出标题</param>
        /// <param name="category">类别（CMD/OA）</param>
        /// <param name="args">参数</param>
        /// <returns>MD 输出</returns>
        private string SysCategoryLog(string title, string category, Dictionary<string, string> args)
        {
            int tail = ArgInt(args, "tail", 0);
            List<LogStore.LogEntry> all;
            lock (LogStore.Sync)
            {
                all = new List<LogStore.LogEntry>(LogStore.AllLog);
            }
            List<LogStore.LogEntry> filtered = new List<LogStore.LogEntry>();
            for (int i = 0; i < all.Count; i = i + 1)
            {
                if (all[i].Category == category)
                {
                    filtered.Add(all[i]);
                }
            }
            int start = 0;
            if (tail > 0 && filtered.Count > tail)
            {
                start = filtered.Count - tail;
            }
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(Header(title, args));
            if (filtered.Count == 0)
            {
                sb.AppendLine("（无 " + category + " 类日志）");
                return sb.ToString();
            }
            for (int i = start; i < filtered.Count; i = i + 1)
            {
                LogStore.LogEntry entry = filtered[i];
                string lv = LogStore.LevelText(entry.Level).Substring(0, 1);
                sb.AppendLine("#" + entry.Frame + " [" + lv + "] " + entry.Module + " | " + entry.Time + " | " + entry.Message);
            }
            return sb.ToString();
        }

        /// <summary>
        /// sys.box——DataBox 当前键值（scope/key 过滤；无过滤 = 全量）
        /// </summary>
        /// <param name="args">参数</param>
        /// <returns>MD 输出</returns>
        private string SysBox(Dictionary<string, string> args)
        {
            string? scope = Arg(args, "scope");
            string? key = Arg(args, "key");
            DataBoxSnapshot snapshot = DataBox.Capture();
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("## SYS.BOX");
            bool any = false;
            for (int i = 0; i < snapshot.Data.Length; i = i + 1)
            {
                DataBoxDataEntry entry = snapshot.Data[i];
                if (scope != null && entry.Scope != scope)
                {
                    continue;
                }
                if (key != null && entry.Key != key)
                {
                    continue;
                }
                sb.AppendLine("- " + entry.Scope + " | " + entry.Key + " | " + SafeValue(entry.Value));
                any = true;
            }
            if (!any)
            {
                sb.AppendLine("（无匹配）");
            }
            return sb.ToString();
        }

        /// <summary>
        /// sys.conf——配置变更记录（cfg.* 事件；密钥仅标记）
        /// </summary>
        /// <param name="args">参数</param>
        /// <returns>MD 输出</returns>
        private string SysConf(Dictionary<string, string> args)
        {
            string? key = Arg(args, "key");
            int tail = ArgInt(args, "tail", 0);
            AuditEvent[] events = _audit.Segment(0, long.MaxValue, null, "ConfigStore");
            if (key != null)
            {
                List<AuditEvent> filtered = new List<AuditEvent>();
                for (int i = 0; i < events.Length; i = i + 1)
                {
                    if (HasProp(events[i], "key", key))
                    {
                        filtered.Add(events[i]);
                    }
                }
                events = filtered.ToArray();
            }
            events = FilterCategoryPrefix(events, "cfg.");
            events = Tail(events, tail);
            return Header("SYS.CONF", args) + _audit.FormatEvents(events);
        }

        /// <summary>
        /// sys.flow——实体生命周期记录（flow.* 事件）
        /// </summary>
        /// <param name="args">参数</param>
        /// <returns>MD 输出</returns>
        private string SysFlow(Dictionary<string, string> args)
        {
            int tail = ArgInt(args, "tail", 0);
            StringBuilder sb = new StringBuilder();
            sb.Append(Header("SYS.FLOW", args));
            // [段1] 实时实体清单
            FlowRunner? runner;
            if (DataBox.TryResolve<FlowRunner>(out runner))
            {
                HostSnapshot snap = runner.GetStatus();
                sb.AppendLine("## 当前实体（实时）");
                if (snap.Flows != null)
                {
                    for (int i = 0; i < snap.Flows.Length; i = i + 1)
                    {
                        sb.AppendLine("- #" + snap.Flows[i].Id + " " + snap.Flows[i].Name
                            + " (" + snap.Flows[i].TypeName + ") [" + snap.Flows[i].Kind + "]");
                    }
                }
                sb.AppendLine("- 帧: " + snap.Frame);
            }
            // [段2] 历史生命周期
            AuditEvent[] events = _audit.Segment(0, long.MaxValue, null, "FlowRunner");
            events = FilterCategoryPrefix(events, "flow.");
            events = Tail(events, tail);
            sb.AppendLine("## 生命周期（审计）");
            sb.Append(_audit.FormatEvents(events));
            return sb.ToString();
        }

        /// <summary>
        /// sys.query——经统一查询通道执行查询（任何线程可调——Main 域自动投递）
        /// </summary>
        /// <param name="args">参数（name=处理器名 + 处理器自有参数）</param>
        /// <returns>MD 输出</returns>
        private string SysQuery(Dictionary<string, string> args)
        {
            if (_queryBus == null)
            {
                return "错误: QueryBus 未绑定（构造 SysCommand 时注入 queryBus）\n" + Usage();
            }
            string? name = Arg(args, "name");
            if (name == null)
            {
                return "用法: sys.query name=<处理器名> [key=value...]\n已注册: " + string.Join(" ", _queryBus.Names());
            }
            string result;
            if (_queryBus.TryExecute(name, args, out result))
            {
                return result;
            }
            return result;
        }

        /// <summary>
        /// sys.summary——快照聚合视图（经 QueryBus 自动投递主线程）
        /// </summary>
        /// <param name="args">参数</param>
        /// <returns>MD 输出</returns>
        private string SysSummary(Dictionary<string, string> args)
        {
            if (_queryBus == null)
            {
                return "错误: QueryBus 未绑定（构造 SysCommand 时注入 queryBus）\n" + Usage();
            }
            string result;
            if (_queryBus.TryExecute("summary", args, out result))
            {
                return result;
            }
            return result;
        }

        /// <summary>
        /// summary 聚合处理器——快照聚合视图（Main 域：经 QueryBus 自动投递主线程）
        /// </summary>
        /// <param name="args">参数（未使用）</param>
        /// <returns>MD 聚合视图</returns>
        private string BuildSummary(Dictionary<string, string> args)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("## SYS.SUMMARY");
            // [段1] 宿主帧号 + 实体
            FlowRunner? runner;
            if (DataBox.TryResolve<FlowRunner>(out runner))
            {
                HostSnapshot snap = runner.GetStatus();
                sb.AppendLine("- frame=" + snap.Frame);
                string flowCount = "0";
                if (snap.Flows != null)
                {
                    flowCount = snap.Flows.Length.ToString();
                }
                sb.AppendLine("- flows=" + flowCount);
            }
            else
            {
                sb.AppendLine("- frame=（无 FlowRunner）");
            }
            // [段2] Command 域
            ICommandBus? bus;
            if (DataBox.TryResolve<ICommandBus>(out bus))
            {
                CommandSnapshot snap = bus.GetSnapshot();
                sb.AppendLine("- cmd owners=" + snap.RegisteredOwnerCount + " keys=" + snap.RegisteredKeyCount
                    + " frozen=" + snap.FrozenKeyCount + " pending=" + snap.PendingKeyCount
                    + " rejected=" + snap.RejectedInputCount);
            }
            // [段3] OA 域
            IOA? oa;
            if (DataBox.TryResolve<IOA>(out oa))
            {
                OAView view = oa.GetSnapshot();
                sb.AppendLine("- oa open=" + view.OpenCount + " work=" + view.WorkCount
                    + " closed=" + view.ClosedCount + " timeout=" + view.TimeoutCount);
            }
            // [段4] DataBox scope 摘要
            DataBoxSnapshot box = DataBox.Capture();
            Dictionary<string, int> scopeCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < box.Data.Length; i = i + 1)
            {
                DataBoxDataEntry entry = box.Data[i];
                if (scopeCounts.ContainsKey(entry.Scope))
                {
                    scopeCounts[entry.Scope] = scopeCounts[entry.Scope] + 1;
                }
                else
                {
                    scopeCounts[entry.Scope] = 1;
                }
            }
            sb.AppendLine("- box scopes=" + scopeCounts.Count + " keys=" + box.Data.Length);
            string[] scopeNames = new string[scopeCounts.Count];
            scopeCounts.Keys.CopyTo(scopeNames, 0);
            for (int i = 0; i < scopeNames.Length; i = i + 1)
            {
                sb.AppendLine("  - " + scopeNames[i] + "=" + scopeCounts[scopeNames[i]]);
            }
            // [段5] 审计统计摘要
            AuditStat[] stats = _audit.Stats(null, null, 0, long.MaxValue);
            long total = 0;
            for (int i = 0; i < stats.Length; i = i + 1)
            {
                total = total + stats[i].Count;
            }
            sb.AppendLine("- audit events=" + total + " categories=" + stats.Length);
            // [段5b] 环形窗口可见性——满覆盖计数（P7b 缺口4：窗口萎缩信号；查询窗口非历史全量）
            long overflow = 0;
            if (AuditStore.Default != null)
            {
                overflow = AuditStore.Default.RingOverflowCount;
            }
            sb.AppendLine("- ring overflow=" + overflow);
            return sb.ToString();
        }
    }
}
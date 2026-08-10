using System;
using System.Collections.Generic;
using System.Text;

namespace Mau.Runtime
{
    /// <summary>
    /// sys.* 指令解析器——审计查询的统一指令入口（design-mau-audit.md §八）。
    /// 基座能力：任何宿主复用（--cmd 观测 / UI Console / 未来管道——同一套指令）。
    /// 查询内核 = 读取三形态（AuditQuery）+ DataBox 当前态；输出统一 MD 键值格式。
    /// </summary>
    public sealed class SysCommand
    {
        /// <summary>审计查询——读取三形态内核</summary>
        private readonly AuditQuery _audit;

        /// <summary>
        /// 构造指令解析器——绑定审计查询（宿主 DataBox.Bind&lt;AuditQuery&gt; 后由 audit 积木族共用）
        /// </summary>
        /// <param name="audit">审计查询</param>
        public SysCommand(AuditQuery audit)
        {
            if (audit == null)
            {
                throw new ArgumentNullException("audit");
            }
            _audit = audit;
        }

        /// <summary>
        /// 执行指令——首词为指令名，其余 key=value 参数（裸词 = 布尔开关 true）
        /// </summary>
        /// <param name="line">指令行（如 "sys.audit category=cmd.set tail=5"）</param>
        /// <returns>MD 键值输出；未知指令返回用法提示</returns>
        public string Execute(string line)
        {
            if (line == null || line.Trim().Length == 0)
            {
                return Usage();
            }
            string[] words = line.Split(' ');
            string cmd = words[0];
            Dictionary<string, string> args = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 1; i < words.Length; i = i + 1)
            {
                if (words[i].Length == 0)
                {
                    continue;
                }
                int eq = words[i].IndexOf('=');
                if (eq > 0)
                {
                    args[words[i].Substring(0, eq)] = words[i].Substring(eq + 1);
                }
                else
                {
                    args[words[i]] = "true";
                }
            }
            if (cmd == "sys.audit")
            {
                return SysAudit(args);
            }
            if (cmd == "sys.cmd")
            {
                return SysCmd(args);
            }
            if (cmd == "sys.keys")
            {
                return SysKeys(args);
            }
            if (cmd == "sys.oa")
            {
                return SysOa(args);
            }
            if (cmd == "sys.trace")
            {
                return SysTrace(args);
            }
            if (cmd == "sys.logs")
            {
                return SysLogs(args);
            }
            if (cmd == "sys.box")
            {
                return SysBox(args);
            }
            if (cmd == "sys.conf")
            {
                return SysConf(args);
            }
            if (cmd == "sys.flow")
            {
                return SysFlow(args);
            }
            return "未知指令: " + cmd + "\n" + Usage();
        }

        /// <summary>
        /// 用法提示
        /// </summary>
        /// <returns>指令清单文本</returns>
        public static string Usage()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("## SYS USAGE");
            sb.AppendLine("- sys.audit [category=] [source=] [tail=N] [from=] [to=]");
            sb.AppendLine("- sys.cmd key=<CommandKey> [tail=N]");
            sb.AppendLine("- sys.keys [owner=<flowId>]");
            sb.AppendLine("- sys.oa id=<单ID> | list=true [status=]");
            sb.AppendLine("- sys.trace flow=<实体名> [tail=N]");
            sb.AppendLine("- sys.logs [tail=N]");
            sb.AppendLine("- sys.box [scope=] [key=]");
            sb.AppendLine("- sys.conf [key=] [tail=N]");
            sb.AppendLine("- sys.flow [tail=N]");
            return sb.ToString();
        }

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
            // 匹配 key 属性（cmd.set）+ keys 属性（cmd.register/cmd.consume）——同一 key 的完整历史
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
        /// sys.keys——key 注册清单（cmd.register 事件，accepted/rejected/原因）
        /// </summary>
        /// <param name="args">参数</param>
        /// <returns>MD 输出</returns>
        private string SysKeys(Dictionary<string, string> args)
        {
            string? owner = Arg(args, "owner");
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
            return Header("SYS.KEYS", args) + _audit.FormatEvents(events);
        }

        /// <summary>
        /// sys.oa——工单生命周期（id= 单号全链路；list= 全部 oa.* 事件）
        /// </summary>
        /// <param name="args">参数</param>
        /// <returns>MD 输出</returns>
        private string SysOa(Dictionary<string, string> args)
        {
            string? id = Arg(args, "id");
            if (id != null)
            {
                AuditEvent[] events = _audit.FindByProp("officeId", id);
                events = FilterCategoryPrefix(events, "oa.");
                return Header("SYS.OA", args) + _audit.FormatEvents(events);
            }
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
            return Header("SYS.OA", args) + _audit.FormatEvents(all);
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
        /// sys.conf——配置变更记录（cfg.* 事件，最近 N 条；密钥仅标记）
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
            AuditEvent[] events = _audit.Segment(0, long.MaxValue, null, "FlowRunner");
            events = FilterCategoryPrefix(events, "flow.");
            events = Tail(events, tail);
            return Header("SYS.FLOW", args) + _audit.FormatEvents(events);
        }

        /// <summary>
        /// 输出头——指令名 + 参数回显
        /// </summary>
        /// <param name="name">指令名</param>
        /// <param name="args">参数</param>
        /// <returns>MD 头文本</returns>
        private static string Header(string name, Dictionary<string, string> args)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("## " + name);
            if (args.Count > 0)
            {
                sb.AppendLine("- args: " + string.Join(" ", ArgLines(args)));
            }
            return sb.ToString();
        }

        /// <summary>
        /// 参数序列化为 key=value 文本数组
        /// </summary>
        /// <param name="args">参数</param>
        /// <returns>文本数组</returns>
        private static string[] ArgLines(Dictionary<string, string> args)
        {
            string[] keys = new string[args.Count];
            args.Keys.CopyTo(keys, 0);
            string[] result = new string[keys.Length];
            for (int i = 0; i < keys.Length; i = i + 1)
            {
                result[i] = keys[i] + "=" + args[keys[i]];
            }
            return result;
        }

        /// <summary>
        /// 取参数——不存在返回 null
        /// </summary>
        /// <param name="args">参数表</param>
        /// <param name="name">参数名</param>
        /// <returns>值或 null</returns>
        private static string? Arg(Dictionary<string, string> args, string name)
        {
            if (args.TryGetValue(name, out string? value))
            {
                return value;
            }
            return null;
        }

        /// <summary>
        /// 取整数参数——缺失/非法返回默认值
        /// </summary>
        /// <param name="args">参数表</param>
        /// <param name="name">参数名</param>
        /// <param name="defaultValue">默认值</param>
        /// <returns>整数值</returns>
        private static int ArgInt(Dictionary<string, string> args, string name, int defaultValue)
        {
            string? raw = Arg(args, name);
            if (raw != null && int.TryParse(raw, out int value))
            {
                return value;
            }
            return defaultValue;
        }

        /// <summary>
        /// 取长整数参数——缺失/非法返回默认值
        /// </summary>
        /// <param name="args">参数表</param>
        /// <param name="name">参数名</param>
        /// <param name="defaultValue">默认值</param>
        /// <returns>长整数值</returns>
        private static long ArgLong(Dictionary<string, string> args, string name, long defaultValue)
        {
            string? raw = Arg(args, name);
            if (raw != null && long.TryParse(raw, out long value))
            {
                return value;
            }
            return defaultValue;
        }

        /// <summary>
        /// 事件尾截断——保留最后 N 条（tail&lt;=0 = 全部）
        /// </summary>
        /// <param name="events">事件数组</param>
        /// <param name="tail">保留条数</param>
        /// <returns>截断后数组</returns>
        private static AuditEvent[] Tail(AuditEvent[] events, int tail)
        {
            if (tail <= 0 || events.Length <= tail)
            {
                return events;
            }
            AuditEvent[] result = new AuditEvent[tail];
            Array.Copy(events, events.Length - tail, result, 0, tail);
            return result;
        }

        /// <summary>
        /// 按类别前缀过滤
        /// </summary>
        /// <param name="events">事件数组</param>
        /// <param name="prefix">类别前缀（如 "cmd."）</param>
        /// <returns>过滤后数组</returns>
        private static AuditEvent[] FilterCategoryPrefix(AuditEvent[] events, string prefix)
        {
            List<AuditEvent> result = new List<AuditEvent>();
            for (int i = 0; i < events.Length; i = i + 1)
            {
                if (events[i].Category.StartsWith(prefix, StringComparison.Ordinal))
                {
                    result.Add(events[i]);
                }
            }
            return result.ToArray();
        }

        /// <summary>
        /// 事件是否含指定属性键值
        /// </summary>
        /// <param name="ev">事件</param>
        /// <param name="key">属性键</param>
        /// <param name="value">属性值</param>
        /// <returns>命中为真</returns>
        private static bool HasProp(AuditEvent ev, string key, string value)
        {
            for (int i = 0; i < ev.Props.Length; i = i + 1)
            {
                if (ev.Props[i].Key == key && ev.Props[i].Value == value)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 值安全文本化——null 显示（null）
        /// </summary>
        /// <param name="value">值</param>
        /// <returns>文本</returns>
        private static string SafeValue(object? value)
        {
            if (value == null)
            {
                return "（null）";
            }
            string? s = System.Convert.ToString(value);
            if (s == null)
            {
                return "";
            }
            return s;
        }
/// <summary>
/// 事件按序号升序比较——合并查询结果排序用
/// </summary>
/// <param name = "a">事件 A</param>
/// <param name = "b">事件 B</param>
/// <returns>比较结果</returns>
private static int CompareBySeq(AuditEvent a, AuditEvent b)
{
    if (a.Seq < b.Seq)
    {
        return -1;
    }

    if (a.Seq > b.Seq)
    {
        return 1;
    }

    return 0;
}    }
}

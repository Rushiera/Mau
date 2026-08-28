using System;
using System.Collections.Generic;
using System.Text;

namespace Mau.Runtime
{
    /// <summary>
    /// SysCommand 静态工具分部——输出头/参数解析/事件过滤与排序。
    /// P7b partial 拆分——自 SysCommand.cs 原样搬移，逻辑零改动。
    /// </summary>
    public sealed partial class SysCommand
    {
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
            string? value;
            if (args.TryGetValue(name, out value))
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
            int value;
            if (raw != null && int.TryParse(raw, out value))
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
            long value;
            if (raw != null && long.TryParse(raw, out value))
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
            string? s = Convert.ToString(value);
            if (s == null)
            {
                return "";
            }
            return s;
        }

        /// <summary>
        /// 事件按序号升序比较——合并查询结果排序用
        /// </summary>
        /// <param name="a">事件 A</param>
        /// <param name="b">事件 B</param>
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
        }
    }
}
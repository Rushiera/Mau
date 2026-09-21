using System;
using System.Globalization;
using Mau.Runtime;

namespace CH4
{
    /// <summary>
    /// 延迟指令族——delay.* 指令解析与执行（design-ch4-delay §5.1）。
    /// 通道：POST /api/v1/command（Program.DispatchCommand 与 AdminService 每猫/majordomo 路由三处共用本入口）。
    /// 分隔符 |（沿用 QuickCat 双参先例）——内容可含空格与 | 之外字符，无需转义。
    /// 结果文本写日志（HTTP 通道只回投递回执；前端以 GET /api/v1/delay 列表为准）。
    /// </summary>
    public static class DelayCommand
    {
        /// <summary>相对时长上限（秒）——10 年（防溢出；人工登记不设业务上限）</summary>
        private const long MaxSecondsAhead = 315360000;

        /// <summary>
        /// 指令处理——识别 delay.* 前缀并执行。
        /// </summary>
        /// <param name="catKey">归属会话（猫 key）</param>
        /// <param name="line">指令行</param>
        /// <returns>true=已识别（无论成败）；false=非本族指令</returns>
        public static bool Handle(string catKey, string line)
        {
            if (line == null || !line.StartsWith("delay.", StringComparison.Ordinal))
            {
                return false;
            }
            string result = Execute(catKey, line);
            int level = 1;
            if (result.StartsWith("ERR|", StringComparison.Ordinal))
            {
                level = 2;
            }
            LogStore.Add("CatHome4", level, "延迟指令: " + line + " → " + result, "DELAY");
            return true;
        }

        /// <summary>
        /// 指令分发——前缀识别 + 参数解析。
        /// </summary>
        /// <param name="catKey">归属会话（猫 key）</param>
        /// <param name="line">指令行</param>
        /// <returns>结果文本（ok / ERR| 前缀）</returns>
        private static string Execute(string catKey, string line)
        {
            if (line == "delay.list")
            {
                return DelayQueue.BuildListText(catKey);
            }
            if (line.StartsWith("delay.add|", StringComparison.Ordinal))
            {
                return AddRelative(catKey, line);
            }
            if (line.StartsWith("delay.addat|", StringComparison.Ordinal))
            {
                return AddAbsolute(catKey, line);
            }
            if (line.StartsWith("delay.set|", StringComparison.Ordinal))
            {
                return SetDue(catKey, line);
            }
            if (line.StartsWith("delay.cancel|", StringComparison.Ordinal))
            {
                return CancelEntry(catKey, line);
            }
            return "ERR|BAD_ARGS|未知延迟指令（支持 delay.add|<秒>|<内容> · delay.addat|<unixms>|<内容> · delay.set|<id>|<unixms> · delay.cancel|<id> · delay.list）";
        }

        /// <summary>
        /// delay.add|&lt;秒&gt;|&lt;内容&gt;——相对时长登记（0 = 立即；已过时刻按立即注入处理）。
        /// </summary>
        /// <param name="catKey">归属会话（猫 key）</param>
        /// <param name="line">指令行</param>
        /// <returns>结果文本</returns>
        private static string AddRelative(string catKey, string line)
        {
            string head;
            string tail;
            if (!SplitPair(line.Substring(10), out head, out tail))
            {
                return "ERR|BAD_ARGS|delay.add 需要 <秒>|<内容>";
            }
            long seconds;
            if (!long.TryParse(head, NumberStyles.Integer, CultureInfo.InvariantCulture, out seconds))
            {
                return "ERR|BAD_ARGS|秒数非法: " + head;
            }
            if (seconds < 0)
            {
                return "ERR|BAD_ARGS|秒数不能为负";
            }
            if (seconds > MaxSecondsAhead)
            {
                return "ERR|BAD_ARGS|秒数过大（上限 " + MaxSecondsAhead.ToString() + "）";
            }
            if (tail.Trim().Length == 0)
            {
                return "ERR|BAD_ARGS|延迟条目内容为空";
            }
            long dueAt = DelayQueue.Now() + seconds * 1000;
            return DelayQueue.Add(catKey, tail, "delay", dueAt);
        }

        /// <summary>
        /// delay.addat|&lt;unixms&gt;|&lt;内容&gt;——绝对时刻登记（前端改时刻 / 精确设定）。
        /// </summary>
        /// <param name="catKey">归属会话（猫 key）</param>
        /// <param name="line">指令行</param>
        /// <returns>结果文本</returns>
        private static string AddAbsolute(string catKey, string line)
        {
            string head;
            string tail;
            if (!SplitPair(line.Substring(12), out head, out tail))
            {
                return "ERR|BAD_ARGS|delay.addat 需要 <unixms>|<内容>";
            }
            long dueAt;
            if (!long.TryParse(head, NumberStyles.Integer, CultureInfo.InvariantCulture, out dueAt))
            {
                return "ERR|BAD_ARGS|时刻非法: " + head;
            }
            if (dueAt <= 0)
            {
                return "ERR|BAD_ARGS|时刻非法（需 Unix 毫秒）";
            }
            if (tail.Trim().Length == 0)
            {
                return "ERR|BAD_ARGS|延迟条目内容为空";
            }
            return DelayQueue.Add(catKey, tail, "delay", dueAt);
        }

        /// <summary>
        /// delay.set|&lt;id&gt;|&lt;unixms&gt;——改触发时刻。
        /// </summary>
        /// <param name="catKey">归属会话（猫 key）</param>
        /// <param name="line">指令行</param>
        /// <returns>结果文本</returns>
        private static string SetDue(string catKey, string line)
        {
            string head;
            string tail;
            if (!SplitPair(line.Substring(10), out head, out tail))
            {
                return "ERR|BAD_ARGS|delay.set 需要 <id>|<unixms>";
            }
            long id;
            if (!long.TryParse(head, NumberStyles.Integer, CultureInfo.InvariantCulture, out id))
            {
                return "ERR|BAD_ARGS|条目序号非法: " + head;
            }
            long dueAt;
            if (!long.TryParse(tail.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out dueAt))
            {
                return "ERR|BAD_ARGS|时刻非法: " + tail.Trim();
            }
            return DelayQueue.SetDueAt(catKey, id, dueAt);
        }

        /// <summary>
        /// delay.cancel|&lt;id&gt;——取消条目。
        /// </summary>
        /// <param name="catKey">归属会话（猫 key）</param>
        /// <param name="line">指令行</param>
        /// <returns>结果文本</returns>
        private static string CancelEntry(string catKey, string line)
        {
            string idText = line.Substring(13).Trim();
            long id;
            if (!long.TryParse(idText, NumberStyles.Integer, CultureInfo.InvariantCulture, out id))
            {
                return "ERR|BAD_ARGS|条目序号非法: " + idText;
            }
            return DelayQueue.Cancel(catKey, id);
        }

        /// <summary>
        /// 拆两段——首个 | 前为 head，其后全段为 tail（内容可含 |）。
        /// </summary>
        /// <param name="rest">待拆文本</param>
        /// <param name="head">首段（Trim 后）</param>
        /// <param name="tail">余段（原样）</param>
        /// <returns>true=含分隔符</returns>
        private static bool SplitPair(string rest, out string head, out string tail)
        {
            head = "";
            tail = "";
            if (rest == null)
            {
                return false;
            }
            int sep = rest.IndexOf('|');
            if (sep < 0)
            {
                return false;
            }
            head = rest.Substring(0, sep).Trim();
            tail = rest.Substring(sep + 1);
            return true;
        }
    }
}

using System;
using System.Security.Cryptography;
using System.Text;

namespace CH4
{
    /// <summary>
    /// 视图块——视图层持久化最小单元（A156 块契约）。
    /// 并列架构：块在事件发生点一次性定稿落盘，不由真实前文重建派生；ID = 块自哈希（永不变更）。
    /// </summary>
    internal sealed class ViewBlock
    {
        /// <summary>块键——建块时即知的稳定句柄（工具卡 tool:&lt;toolCallId&gt; / 前文派生 msg:&lt;index&gt; / 独立块 容器:&lt;序号&gt;）；原位替换与去重的定位依据</summary>
        public string Key { get; set; }

        /// <summary>块 ID——块自哈希（renderType|ts|origin|src|durMs|payload 的 SHA256 十六进制）；定稿后永不变更（A156 I1）</summary>
        public string Id { get; set; }

        /// <summary>创建时间戳——Unix 毫秒（= 消息块创建时刻：前文派生取消息 CreatedAt，独立块取生成时刻；同刻由写入侧单调补差保证全序）</summary>
        public long Timestamp { get; set; }

        /// <summary>渲染类型——user/text/reason/toolcard/roundsum/error/retry/void/inject_report（前端按此分敛渲染）</summary>
        public string RenderType { get; set; }

        /// <summary>渲染载荷——JSON 字符串（按渲染类型结构不同）</summary>
        public string Payload { get; set; }

        /// <summary>前文来源——{MsgIndex, Hash}；null = 非前文派生（独立块）。关系字段：仅用于对账与追溯，不承担派生职责</summary>
        public ViewOrigin Origin { get; set; }

        /// <summary>来源类别——front（前文派生）/ independent（独立块）；后端自述，前端不消费</summary>
        public string Src { get; set; }

        /// <summary>运行时长——工具卡专用（毫秒）；-1 = 不适用或未定稿</summary>
        public long DurMs { get; set; }

        /// <summary>块生命周期状态——final（定稿）/ voided（timeback 区间标记移出）；实时区 pending 不落盘</summary>
        public string State { get; set; }
        /// <summary>生命周期状态——pending（已建块、未定稿；仅实时区先行卡，不落盘）</summary>
        public const string StatePending = "pending";
        /// <summary>生命周期状态——final（块 ID 已定，此后不再变更）</summary>
        public const string StateFinal = "final";
        /// <summary>生命周期状态——voided（timeback 区间标记移出；视图层保留原文可展开）</summary>
        public const string StateVoided = "voided";
        /// <summary>是否已定稿——块 ID 出现即定稿（A157：定稿哨兵由布尔标志升为 ID 有无）。</summary>
        public bool IsFinaled
        {
            get
            {
                return Id != null && Id.Length > 0;
            }
        }
        /// <summary>
        /// 建 pending 块——键 / 事件时刻 / 载荷 / 来源就位，ID 与运行时长待定稿（A157：两区共用构造内核）。
        /// 实时区先行卡与持久区落盘块走同一入口，避免同一语义两处拼装。
        /// </summary>
        /// <param name="key">块键（建块即定的稳定句柄）</param>
        /// <param name="renderType">渲染类型</param>
        /// <param name="payloadJson">渲染载荷 JSON</param>
        /// <param name="timestamp">创建时间戳（Unix 毫秒，含单调补差后的值）</param>
        /// <param name="origin">前文来源（null = 独立块）</param>
        /// <param name="src">来源类别（front / independent）</param>
        /// <returns>未定稿的视图块</returns>
        public static ViewBlock BuildPending(string key, string renderType, string payloadJson, long timestamp, ViewOrigin origin, string src)
        {
            ViewBlock block = new ViewBlock();
            block.Key = key;
            block.RenderType = renderType;
            block.Payload = payloadJson;
            block.Timestamp = timestamp;
            block.Origin = origin;
            block.Src = src;
            block.DurMs = -1;
            block.State = StatePending;
            return block;
        }
        /// <summary>
        /// 定稿——补运行时长与块自哈希 ID，状态转 final（A157：ID 出现即定稿）。
        /// 已定稿块拒绝重复定稿——同一块不得二次改写 ID。
        /// </summary>
        /// <param name="durMs">运行时长（毫秒；-1 = 不适用）</param>
        /// <returns>true = 本次定稿；false = 已定稿（拒绝）</returns>
        public bool Finalize(long durMs)
        {
            if (IsFinaled)
            {
                return false;
            }
            DurMs = durMs;
            Id = ComputeId(RenderType, Timestamp, durMs, Payload);
            State = StateFinal;
            return true;
        }
        /// <summary>
        /// 块自哈希——块 ID（renderType|ts|durMs|payload 的 SHA256 十六进制；定稿后永不变更）。
        /// A158 期三：口径为内容身份——origin / src 属「来源关系」字段，不参与哈希
        /// （实时面推送时前文尚无对应消息、给不出 origin；同一块跨两区必须同 ID）。
        /// </summary>
        /// <param name="renderType">渲染类型</param>
        /// <param name="timestamp">块时间戳（含单调补差后值）</param>
        /// <param name="durMs">运行时长</param>
        /// <param name="payloadJson">渲染载荷 JSON</param>
        /// <returns>SHA256 十六进制串</returns>
        public static string ComputeId(string renderType, long timestamp, long durMs, string payloadJson)
        {
            string raw = (renderType == null ? "" : renderType)
                + "\u0001" + timestamp.ToString()
                + "\u0001" + durMs.ToString()
                + "\u0001" + (payloadJson == null ? "" : payloadJson);
            return Sha256Hex(raw);
        }
        /// <summary>
        /// SHA256 十六进制——哈希统一出口（前文消息哈希与块自哈希共用）。
        /// </summary>
        /// <param name="raw">待哈希原文</param>
        /// <returns>十六进制串</returns>
        public static string Sha256Hex(string raw)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < bytes.Length; i = i + 1)
                {
                    sb.Append(bytes[i].ToString("x2"));
                }
                return sb.ToString();
            }
        }
    }

    /// <summary>
    /// 前文来源——块与真实前文消息的关系字段（A156）。
    /// </summary>
    internal sealed class ViewOrigin
    {
        /// <summary>真实前文消息索引（-1 = 无对应消息）</summary>
        public int MsgIndex { get; set; }

        /// <summary>前文消息内容哈希（SHA256 十六进制）——对账哨兵与转发锚点定位用</summary>
        public string Hash { get; set; }
    }
}

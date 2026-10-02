using System;

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

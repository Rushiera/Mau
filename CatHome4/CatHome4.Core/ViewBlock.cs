namespace CH4
{
    /// <summary>
    /// 视图块——视图层最小单元（A156 块契约 · A165 阶段 1 契约 v2）。
    /// 并列架构：块在事件发生点一次性定稿落盘，不由真实前文重建派生。
    /// v2 契约：协议条目只带业务字段（ts / type / payload / msgIndex / round）——同步标识面
    ///（块键 / 块 ID / 生命周期状态 / 来源类别 / 运行时长）全部退役；后端内部定位自持，不进协议。
    /// </summary>
    internal sealed class ViewBlock
    {
        /// <summary>创建时间戳——Unix 毫秒（前文派生取消息 CreatedAt，独立块取生成时刻；同刻由写入侧单调补差保证全序）</summary>
        public long Timestamp { get; set; }

        /// <summary>渲染类型——持久族 user/text/gap_text/reason/toolcard/retry/error/inject_report/roundsum（A188 起间隙文本独立 gap_text）；临时族 thinksse/replysse/toolrun（A187 命名——不含生命周期语义；段级全空占位 empty 不入本表）</summary>
        public string RenderType { get; set; }

        /// <summary>渲染载荷——JSON 字符串（按渲染类型结构不同；字段面按「给全 / 命名统一 / 去冗余 / 扁平」原则）</summary>
        public string Payload { get; set; }

        /// <summary>业务定位字段——前文消息索引（-1 = 非前文派生；顶尾补差与存档取用）</summary>
        public int MsgIndex { get; set; }

        /// <summary>业务定位字段——所属轮次（0 = 无轮，如加载阶段；QQ 转发按轮次定位的游标依据）</summary>
        public int Round { get; set; }
    }
}

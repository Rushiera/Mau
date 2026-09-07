using System;

namespace CH4
{
    /// <summary>
    /// 视图块——F4 视图持久化最小单元（真实前文 → 修饰视图的确定性产物）。
    /// ID = 时间戳:内容哈希——时空双索引（同内容被时间区分，同时刻被内容区分；持久化稳定）。
    /// </summary>
    internal sealed class ViewBlock
    {
        /// <summary>创建时间戳——Unix 毫秒（真实时序权威：增量=消息 CreatedAt/CloseRound 时刻，重建=消息 CreatedAt，注入报告=0）</summary>
        public long Timestamp { get; set; }

        /// <summary>内容哈希——真实前文单块完整字段 SHA256 十六进制</summary>
        public string Hash { get; set; }

        /// <summary>渲染类型——user/text/reason/toolcard/control（前端按此分敛渲染）</summary>
        public string RenderType { get; set; }

        /// <summary>渲染载荷——JSON 字符串（按渲染类型结构不同）</summary>
        public string Payload { get; set; }

        /// <summary>唯一 ID——时间戳:哈希（持久化稳定；前端不参与定位）</summary>
        public string Id
        {
            get
            {
                return Timestamp.ToString() + ":" + (Hash ?? "");
            }
        }
    }
}

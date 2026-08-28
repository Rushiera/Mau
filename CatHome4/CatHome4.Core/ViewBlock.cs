using System;

namespace CH4
{
    /// <summary>
    /// 视图块——F4 视图持久化最小单元（真实前文 → 修饰视图的确定性产物）。
    /// ID = 帧号:内容哈希——时空双索引（同内容被帧区分，同帧被内容区分；持久化稳定）。
    /// </summary>
    internal sealed class ViewBlock
    {
        /// <summary>创建帧号——宿主主循环帧（FlowRunner.GlobalFrame；重建时取当前帧——逻辑准确即可）</summary>
        public long Frame { get; set; }

        /// <summary>内容哈希——真实前文单块完整字段 SHA256 十六进制</summary>
        public string Hash { get; set; }

        /// <summary>渲染类型——user/text/reason/toolcard/control（前端按此分敛渲染）</summary>
        public string RenderType { get; set; }

        /// <summary>渲染载荷——JSON 字符串（按渲染类型结构不同）</summary>
        public string Payload { get; set; }

        /// <summary>唯一 ID——帧号:哈希（持久化稳定；前端不参与定位）</summary>
        public string Id
        {
            get
            {
                return Frame.ToString() + ":" + (Hash ?? "");
            }
        }
    }
}

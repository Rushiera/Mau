using System;

namespace Mau.Runtime
{
    /// <summary>
    /// 文本替换三态枚举——text-replace v2 的 P3 锚点三态诊断（design-ch4-text-tools.md §六）
    /// </summary>
    public enum TextReplaceStatus
    {
        /// <summary>唯一命中并替换成功</summary>
        Ok,
        /// <summary>锚点未找到——带差异字节定位</summary>
        NotFound,
        /// <summary>锚点多次出现——带候选行号列表</summary>
        Ambiguous
    }

    /// <summary>
    /// 文本替换结果——三态诊断载荷（状态 + 数量 + 差异定位 + 候选 + 目标段读回）
    /// </summary>
    public sealed class TextReplaceOutcome
    {
        /// <summary>三态状态</summary>
        public TextReplaceStatus Status;

        /// <summary>替换数量（Ok 态 >0）</summary>
        public int Count;

        /// <summary>未找到时首个差异字节位置（0 起；-1=无法定位）</summary>
        public int DiffByteIndex;

        /// <summary>未找到时期望片段（差异位置前后 ≤10 字符）</summary>
        public string Expected = "";

        /// <summary>未找到时实际片段（差异位置前后 ≤10 字符）</summary>
        public string Actual = "";

        /// <summary>多次出现时候选行号（前 5 个，1 起）</summary>
        public string[] CandidateLines = Array.Empty<string>();

        /// <summary>成功时目标段读回（替换点 ±3 行）</summary>
        public string Snippet = "";
    }
}

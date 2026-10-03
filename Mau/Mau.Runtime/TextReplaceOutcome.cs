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
        /// <summary>成功时单次匹配最大跨度（字符数；regex 模式有效——A146 跨度出声）</summary>
        public int MaxSpanChars;
        /// <summary>成功时单次匹配最大跨度（行数；最长匹配内换行数 + 1——regex 模式有效·A146）</summary>
        public int MaxSpanLines;
        /// <summary>成功时全部匹配跨度合计（字符数；regex 模式有效——A146）</summary>
        public int TotalSpanChars;
        /// <summary>成功时单次匹配跨度超阈值（regex 越界吞块出声判据——A146）</summary>
        public bool SpanWarned;
        /// <summary>未找到时疑似实体写法（锚点或定位处出现 lt / gt / amp / quot 转义形态——A149 出声判据）</summary>
        public bool EntitySuspect;
    }

    /// <summary>
    /// 文本替换契约常量——显式删除标记的单一真相源（积木 BRIK-TEXT-004 与宿主摘要投影共同消费）
    /// </summary>
    public static class TextReplaceSpec
    {
        /// <summary>
        /// 删除标记——new 精确等于本串时按空文本落盘（等价删除匹配文本；四模式通用）。
        /// 显式标记替代空串语义：区分「显式删除」与「漏传 / 生成错误」（空串一律 BAD_ARGS 拒绝）。
        /// </summary>
        public const string DeleteKey = "黑暗剑+22";
        /// <summary>regex 单次匹配跨度出声阈值（行）——最长匹配跨过此值即出声提示（A146）</summary>
        public const int SpanWarnLines = 10;
        /// <summary>regex 单次匹配跨度出声阈值（字符）——单次匹配超过此值即出声提示（A146）</summary>
        public const int SpanWarnChars = 1000;
    }
}

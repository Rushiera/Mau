// ═══════════════════════════════════════════════════
// 积木: text.md_parse
// ID:   BRIK-TEXT-001
// 类别: TEXT
// 作用: Markdown 解析为纯数据段列表（标题/列表/引用/代码/表格/分隔线/字符画）
// 依赖: 无
// 引用: System.Collections.Generic · Mau.Runtime
// 原理: 薄壳——解析逻辑唯一实现已下沉 Mau.Runtime.MarkdownParser（G.3 MD 渲染下沉 2026-08-11 D.3：
//       依赖者 UI（CH4.UI）增量渲染直接复用 MarkdownParser，零重复实现）
// 常用: CH4 UI 渲染 / 工具输出格式化 / 文档预览
// ═══════════════════════════════════════════════════
using System.Collections.Generic;

using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 文本积木——text.md_parse Markdown 解析为纯数据段（薄壳——逻辑在 Mau.Runtime.MarkdownParser）
    /// </summary>
    public static class TextMdParseBrick
    {
        /// <summary>
        /// 解析标题、段落、列表、引用、代码、表格、分隔线和 Box Drawing
        /// </summary>
        /// <param name="markdown">Markdown 文本</param>
        /// <param name="parts">按输入顺序排列的独立段列表</param>
        /// <returns>true=成功</returns>
        public static bool MdParse(string? markdown, out List<MarkdownPart> parts)
        {
            return MarkdownParser.Parse(markdown, out parts);
        }
    }
}
// #MAU_CHECKSUM:SHA256:60E3CC159282CF4FBF92C47A2A1D27BA7B352FD5F66F163690B55D8E65BCB66C

// ═══════════════════════════════════════════════════
// 积木: math.result_preview
// ID:   BRIK-MATH-003
// 类别: MATH
// 作用: 生成单行、最多十五 UTF-16 字符头部的结果预览
// 依赖: math.format_size
// 引用: System
// 原理: 头部截断（含代理对边界）+ 换行替换 + 长度经 format_size 格式化
// 常用: 工具结果摘要展示
// ═══════════════════════════════════════════════════
using System;

namespace Mau.Bricks
{
    /// <summary>
    /// 数学积木——math.result_preview 生成单行结果预览（依赖 math.format_size）
    /// </summary>
    public static class MathResultPreviewBrick
    {
        /// <summary>
        /// 生成单行、最多十五 UTF-16 字符头部的结果预览
        /// </summary>
        /// <param name="result">完整工具结果</param>
        /// <param name="preview">预览文本——OK 或空结果返回空字符串，否则返回预览</param>
        /// <returns>true=成功</returns>
        public static bool ResultPreview(string? result, out string preview)
        {
            if (string.IsNullOrEmpty(result))
            {
                preview = "";
                return true;
            }
            if (result == "OK" || result.StartsWith("OK\n", StringComparison.Ordinal)
                || result.StartsWith("OK\r", StringComparison.Ordinal))
            {
                preview = "";
                return true;
            }
            int headLength = result.Length;
            if (headLength > 15)
            {
                headLength = 15;
                if (headLength < result.Length && headLength > 0
                    && char.IsHighSurrogate(result[headLength - 1])
                    && char.IsLowSurrogate(result[headLength]))
                {
                    headLength = headLength + 1;
                }
            }
            string head = result.Substring(0, headLength).Replace('\r', ' ').Replace('\n', ' ');
            string sizeText = "";
            MathFormatSizeBrick.FormatSize(result.Length, out sizeText);
            preview = "（" + head + "…总" + sizeText + "）";
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:A8D1AAE8A9C1ADE7C6339B54991C1918D5377CA5B4E69CF1C169729E36EF232B

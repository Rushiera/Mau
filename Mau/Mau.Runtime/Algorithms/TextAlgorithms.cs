// ═══════════════════════════════════════════════════
// 算法面: TextAlgorithms（文本匹配 / 差异定位——纯静态无状态）
// 面: Mau.Runtime/Algorithms——规格 Project/Mau/design-mau-algorithms.md
// 准入四红线: 无状态无 IO / 无业务词汇 / 给事实不给判断 / ≥2 消费者
// 消费者: text-replace / text-read_between / text-grep 积木
// ═══════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Mau.Runtime
{
    /// <summary>
    /// 文本匹配模式（原语级——不含业务模式；如「全部替换」由调用方解释）
    /// </summary>
    public enum TextMatchMode
    {
        /// <summary>
        /// 区分大小写的字面匹配
        /// </summary>
        Exact,

        /// <summary>
        /// 不区分大小写的字面匹配
        /// </summary>
        IgnoreCase,

        /// <summary>
        /// 正则匹配
        /// </summary>
        Regex
    }

    /// <summary>
    /// 差异点（最长公共前缀对齐结果——纯事实）
    /// </summary>
    public readonly struct TextDiffPoint
    {
        /// <summary>
        /// 期望 / 实际分叉处的字节位（最佳对齐位 + 最长公共前缀长度）
        /// </summary>
        public readonly int ByteIndex;

        /// <summary>
        /// 与锚点的最长公共前缀长度
        /// </summary>
        public readonly int CommonLength;

        /// <summary>
        /// 构造差异点
        /// </summary>
        /// <param name="byteIndex">分叉处字节位</param>
        /// <param name="commonLength">最长公共前缀长度</param>
        public TextDiffPoint(int byteIndex, int commonLength)
        {
            ByteIndex = byteIndex;
            CommonLength = commonLength;
        }
    }

    /// <summary>
    /// 匹配跨度（起始索引 + 长度——纯事实）
    /// </summary>
    public readonly struct TextMatchSpan
    {
        /// <summary>
        /// 命中起始索引
        /// </summary>
        public readonly int Index;

        /// <summary>
        /// 命中长度
        /// </summary>
        public readonly int Length;

        /// <summary>
        /// 构造匹配跨度
        /// </summary>
        /// <param name="index">命中起始索引</param>
        /// <param name="length">命中长度</param>
        public TextMatchSpan(int index, int length)
        {
            Index = index;
            Length = length;
        }
    }

    /// <summary>
    /// 文本算法——匹配命中 / 差异点定位（纯静态无状态；给事实不给判断）
    /// </summary>
    public static class TextAlgorithms
    {
        /// <summary>
        /// 返回 pattern 在 text 中的全部命中起始索引（Exact / IgnoreCase 字面 · Regex 正则）
        /// </summary>
        /// <param name="text">被搜索文本</param>
        /// <param name="pattern">匹配串（mode=Regex 时为正则表达式）</param>
        /// <param name="mode">匹配模式（原语级）</param>
        /// <returns>命中起始索引数组（空数组 = 无命中）</returns>
        public static int[] FindOccurrences(string text, string pattern, TextMatchMode mode)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(pattern))
            {
                return new int[0];
            }

            if (mode == TextMatchMode.Regex)
            {
                MatchCollection matches = Regex.Matches(text, pattern, RegexOptions.None, TimeSpan.FromSeconds(5));
                int[] result = new int[matches.Count];
                for (int i = 0; i < matches.Count; i = i + 1)
                {
                    result[i] = matches[i].Index;
                }

                return result;
            }

            StringComparison cmp = mode == TextMatchMode.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            List<int> list = new List<int>();
            int start = 0;
            while (start <= text.Length - pattern.Length)
            {
                int found = text.IndexOf(pattern, start, cmp);
                if (found < 0)
                {
                    break;
                }

                list.Add(found);
                start = found + pattern.Length;
            }

            return list.ToArray();
        }

        /// <summary>
        /// 差异点定位——取与目标串最长公共前缀的最佳对齐位（未命中时给出定位事实）
        /// </summary>
        /// <param name="text">被搜索文本</param>
        /// <param name="target">目标串</param>
        /// <returns>差异点（分叉字节位 + 最长公共前缀长度）</returns>
        public static TextDiffPoint LocateBestMatch(string text, string target)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(target))
            {
                return new TextDiffPoint(0, 0);
            }

            string first = target.Substring(0, 1);
            int bestPos = 0;
            int bestCommon = -1;
            int scan = text.IndexOf(first, StringComparison.Ordinal);
            while (scan >= 0)
            {
                int common = 0;
                while (common < target.Length && scan + common < text.Length && text[scan + common] == target[common])
                {
                    common = common + 1;
                }

                if (common > bestCommon)
                {
                    bestCommon = common;
                    bestPos = scan;
                }

                if (common >= target.Length)
                {
                    break;
                }

                scan = text.IndexOf(first, scan + 1, StringComparison.Ordinal);
            }

            if (bestCommon < 0)
            {
                bestCommon = 0;
            }

            return new TextDiffPoint(bestPos + bestCommon, bestCommon);
        }

        /// <summary>
        /// 匹配跨度——返回每个命中的起始索引与长度（供跨度记账等事实）
        /// </summary>
        /// <param name="text">被搜索文本</param>
        /// <param name="pattern">匹配串（mode=Regex 时为正则表达式）</param>
        /// <param name="mode">匹配模式（原语级）</param>
        /// <returns>命中跨度数组（空数组 = 无命中）</returns>
        public static TextMatchSpan[] FindMatches(string text, string pattern, TextMatchMode mode)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(pattern))
            {
                return new TextMatchSpan[0];
            }

            if (mode == TextMatchMode.Regex)
            {
                MatchCollection matches = Regex.Matches(text, pattern, RegexOptions.None, TimeSpan.FromSeconds(5));
                TextMatchSpan[] result = new TextMatchSpan[matches.Count];
                for (int i = 0; i < matches.Count; i = i + 1)
                {
                    result[i] = new TextMatchSpan(matches[i].Index, matches[i].Length);
                }

                return result;
            }

            int[] indexes = FindOccurrences(text, pattern, mode);
            TextMatchSpan[] spans = new TextMatchSpan[indexes.Length];
            for (int i = 0; i < indexes.Length; i = i + 1)
            {
                spans[i] = new TextMatchSpan(indexes[i], pattern.Length);
            }

            return spans;
        }

        /// <summary>
        /// 全部替换——按 mode 替换 text 中所有匹配（Regex 支持 $1 捕获组）；返回替换后全文
        /// </summary>
        /// <param name="text">原文</param>
        /// <param name="pattern">匹配串（mode=Regex 时为正则表达式）</param>
        /// <param name="replacement">替换文本</param>
        /// <param name="mode">匹配模式（原语级）</param>
        /// <returns>替换后全文（无命中 = 原文）</returns>
        public static string ReplaceAll(string text, string pattern, string replacement, TextMatchMode mode)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(pattern))
            {
                return text;
            }

            if (mode == TextMatchMode.Regex)
            {
                return Regex.Replace(text, pattern, replacement, RegexOptions.None, TimeSpan.FromSeconds(5));
            }

            if (mode == TextMatchMode.IgnoreCase)
            {
                return text.Replace(pattern, replacement, StringComparison.OrdinalIgnoreCase);
            }

            return text.Replace(pattern, replacement, StringComparison.Ordinal);
        }

        /// <summary>
        /// 是否包含任一子串（中性——tokens 由调用方给定）
        /// </summary>
        /// <param name="text">待检文本</param>
        /// <param name="tokens">子串清单</param>
        /// <returns>true=含任一提及子串</returns>
        public static bool ContainsAny(string text, string[] tokens)
        {
            if (string.IsNullOrEmpty(text) || tokens == null || tokens.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < tokens.Length; i = i + 1)
            {
                if (!string.IsNullOrEmpty(tokens[i]) && text.IndexOf(tokens[i], StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}

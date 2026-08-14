using System;
using System.Collections.Generic;
using Mau.Contracts;

namespace Mau.Translator
{
    /// <summary>
    /// 默认符号外观 v3-default——生成可靠性优先，ASCII 最大化（design-mau-v3 §四）。
    /// 主字形 14 个 + 多字符字形 3 个（:= 与 &lt;- 与 @）。' 与 " 是词法定界（内置于词法器），不进外观表。
    /// 退役符号（v2）：τ / ω / ∈ / ∧ / ≔ / ⊔ / ⟳ / ⌕——新语料不再出现。
    /// </summary>
    public sealed class DefaultAppearance : ISymbolAppearance
    {
        /// <summary>
        /// 字形 → TokenId 单字符表（Ordinal 精确匹配）
        /// </summary>
        private static readonly Dictionary<char, uint> SingleGlyphs = new Dictionary<char, uint>
        {
            { '§', TokenIds.Section },
            { '=', TokenIds.Eq },
            { ':', TokenIds.Colon },
            { '⇐', TokenIds.In },
            { '↻', TokenIds.Sample },
            { '→', TokenIds.Arrow },
            { '|', TokenIds.Branch },
            { '&', TokenIds.And },
            { '{', TokenIds.SetOpen },
            { '}', TokenIds.SetClose },
            { ',', TokenIds.Sep },
            { '[', TokenIds.ParamOpen },
            { ']', TokenIds.ParamClose },
            { '@', TokenIds.Sample },
        };

        /// <summary>
        /// 多字符字形表（词法器先查此处再查单字符——最长匹配）
        /// </summary>
        private static readonly Dictionary<string, uint> MultiGlyphs = new Dictionary<string, uint>(StringComparer.Ordinal)
        {
            { ":=", TokenIds.Declare },
            { "<-", TokenIds.In },
        };

        /// <summary>
        /// TokenId → 默认字形（诊断回显/生成物注释）
        /// </summary>
        private static readonly Dictionary<uint, string> Glyphs = new Dictionary<uint, string>
        {
            { TokenIds.Section, "§" },
            { TokenIds.Declare, ":=" },
            { TokenIds.Eq, "=" },
            { TokenIds.Colon, ":" },
            { TokenIds.In, "⇐" },
            { TokenIds.Sample, "↻" },
            { TokenIds.Arrow, "→" },
            { TokenIds.Branch, "|" },
            { TokenIds.And, "&" },
            { TokenIds.SetOpen, "{" },
            { TokenIds.SetClose, "}" },
            { TokenIds.Sep, "," },
            { TokenIds.ParamOpen, "[" },
            { TokenIds.ParamClose, "]" },
        };

        /// <summary>
        /// 外观表名——诊断携带
        /// </summary>
        public string AppearanceName
        {
            get { return "v3-default"; }
        }

        /// <summary>
        /// TokenId → 默认字形——非符号 token 返回空串
        /// </summary>
        /// <param name="tokenId">内核 TokenId</param>
        /// <returns>默认字形；无字形返回 ""</returns>
        public string Glyph(uint tokenId)
        {
            string? glyph;
            if (Glyphs.TryGetValue(tokenId, out glyph) && glyph != null)
            {
                return glyph;
            }
            return "";
        }

        /// <summary>
        /// 字形 → TokenId——先多字符字形后单字符字形（最长匹配）
        /// </summary>
        /// <param name="glyph">外观字符（1-2 字符）</param>
        /// <param name="tokenId">命中时输出内核 TokenId</param>
        /// <returns>true=属于本外观白名单</returns>
        public bool TryMap(string glyph, out uint tokenId)
        {
            if (glyph == null)
            {
                tokenId = 0;
                return false;
            }
            // [段1] 多字符字形优先——最长匹配（:= 与 <- 优先于单字符）
            if (glyph.Length == 2)
            {
                if (MultiGlyphs.TryGetValue(glyph, out tokenId))
                {
                    return true;
                }
            }
            // [段2] 单字符字形
            if (glyph.Length == 1)
            {
                if (SingleGlyphs.TryGetValue(glyph[0], out tokenId))
                {
                    return true;
                }
            }
            tokenId = 0;
            return false;
        }
    }
}

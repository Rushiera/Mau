using System;

namespace Mau.Contracts
{
    /// <summary>
    /// 符号外观接口——翻译器只认 TokenId，字符经本接口双向映射（design-mau-v3 §四）。
    /// 我的方言 = 我选择的外观实现；换拼写 = 换实现，内核零改动。
    /// 词法器调用约定：先查多字符字形（2 字符），再查单字符字形。
    /// </summary>
    public interface ISymbolAppearance
    {
        /// <summary>
        /// TokenId → 默认字形——生成物注释/诊断回显用（值 token 返回空串）
        /// </summary>
        /// <param name="tokenId">内核 TokenId</param>
        /// <returns>默认字形；非符号 token 返回 ""</returns>
        string Glyph(uint tokenId);

        /// <summary>
        /// 字形 → TokenId——词法映射（单字符字形 + 多字符字形/别名）
        /// </summary>
        /// <param name="glyph">外观字符</param>
        /// <param name="tokenId">命中时输出内核 TokenId</param>
        /// <returns>true=属于本外观白名单；false=非法字符</returns>
        bool TryMap(string glyph, out uint tokenId);

        /// <summary>
        /// 外观表名——诊断信息携带（如 "外观: v3-default"）
        /// </summary>
        string AppearanceName { get; }
    }
}

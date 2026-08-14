using System;

namespace Mau.Translator
{
    /// <summary>
    /// v3 词法 Token——TokenId + 源码坐标 + 值。
    /// 符号 token 的 Value 为空串；值 token（Name/Num/Str/Word）的 Value 承载内容。
    /// </summary>
    public sealed class TokenV3
    {
        /// <summary>
        /// 内核 TokenId（TokenIds 表）
        /// </summary>
        public uint Id;

        /// <summary>
        /// 行号——1-based
        /// </summary>
        public int Line;

        /// <summary>
        /// 列号——1-based
        /// </summary>
        public int Col;

        /// <summary>
        /// 值内容——Name/Num/Str/Word 承载；符号 token 为 ""
        /// </summary>
        public string Value;

        /// <summary>
        /// 构造 token
        /// </summary>
        /// <param name="id">TokenId</param>
        /// <param name="line">行号</param>
        /// <param name="col">列号</param>
        /// <param name="value">值内容</param>
        public TokenV3(uint id, int line, int col, string value)
        {
            Id = id;
            Line = line;
            Col = col;
            Value = value;
        }
    }
}

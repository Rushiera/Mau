using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Mau.Contracts;

namespace Mau.Translator
{
    /// <summary>
    /// Token 流黄金哈希——黄金哨兵的对象是 TokenId 流（design-mau-v3 §三）。
    /// 换外观字符不漂移（语义不变则哈希不变）；语料/生成器改动漂移 = 改动影响面反证。
    /// </summary>
    public static class TokenFlowV3
    {
        /// <summary>
        /// Token 流序列化——"id:value|id:value|..."（值 token 带内容，符号 token 纯 id）
        /// </summary>
        /// <param name="tokens">词法 Token 流</param>
        /// <returns>规范序列化文本</returns>
        public static string Serialize(List<TokenV3> tokens)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < tokens.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append('|');
                }
                TokenV3 t = tokens[i];
                sb.Append(t.Id);
                if (t.Value.Length > 0)
                {
                    sb.Append(':');
                    sb.Append(t.Value);
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// 源文本黄金哈希——词法 → 序列化 → SHA256（十六进制大写）。
        /// 词法失败返回空串（调用方先判 Success）。
        /// </summary>
        /// <param name="source">.mau 源文本</param>
        /// <param name="appearance">符号外观表</param>
        /// <returns>64 位十六进制哈希；词法失败返回 ""</returns>
        public static string Hash(string source, ISymbolAppearance appearance)
        {
            LexResultV3 lex = MauLexerV3.Lex(source, appearance);
            if (!lex.Success)
            {
                return "";
            }
            string flow = Serialize(lex.Tokens);
            byte[] bytes = Encoding.UTF8.GetBytes(flow);
            byte[] hash = SHA256.HashData(bytes);
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < hash.Length; i++)
            {
                sb.Append(hash[i].ToString("X2"));
            }
            return sb.ToString();
        }
    }
}

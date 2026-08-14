using System;
using System.Collections.Generic;
using Mau.Contracts;
using Mau.Translator;
using Xunit;

namespace Mau.Translator.Tests
{
    /// <summary>
    /// v3 词法测试——外观字符流 → Token 流（白名单词法 + 四种元素 + 错误码 E0xx）
    /// </summary>
    public class MauLexerV3Tests
    {
        /// <summary>
        /// 词法扫描——默认外观
        /// </summary>
        /// <param name="source">源文本</param>
        /// <returns>词法结果</returns>
        private static LexResultV3 Lex(string source)
        {
            return MauLexerV3.Lex(source, new DefaultAppearance());
        }

        /// <summary>
        /// Token 流文本化——断言失败时输出诊断详情
        /// </summary>
        /// <param name="result">词法结果</param>
        /// <returns>token 摘要</returns>
        private static string FormatTokens(LexResultV3 result)
        {
            string s = "";
            for (int i = 0; i < result.Tokens.Count; i++)
            {
                TokenV3 t = result.Tokens[i];
                s = s + t.Id + "(" + t.Value + ") ";
            }
            return s;
        }

        /// <summary>
        /// 诊断文本化——断言失败时输出错误详情
        /// </summary>
        /// <param name="result">词法结果</param>
        /// <returns>诊断摘要</returns>
        private static string FormatDiags(LexResultV3 result)
        {
            string s = "";
            for (int i = 0; i < result.Diagnostics.Count; i++)
            {
                s = s + result.Diagnostics[i].Code + ":" + result.Diagnostics[i].Message + "\n";
            }
            return s;
        }

        /// <summary>
        /// 状态机声明——§ 'S_Talk' = { 'Idle', 'Thinking' } 全 token 流
        /// </summary>
        [Fact]
        public void Lex_StateMachine_Decl()
        {
            LexResultV3 result = Lex("§ 'S_Talk' = { 'Idle', 'Thinking' }");
            Assert.True(result.Success, FormatDiags(result));
            List<TokenV3> tokens = result.Tokens;
            Assert.Equal(TokenIds.Section, tokens[0].Id);
            Assert.Equal(TokenIds.Name, tokens[1].Id);
            Assert.Equal("S_Talk", tokens[1].Value);
            Assert.Equal(TokenIds.Eq, tokens[2].Id);
            Assert.Equal(TokenIds.SetOpen, tokens[3].Id);
            Assert.Equal(TokenIds.Name, tokens[4].Id);
            Assert.Equal("Idle", tokens[4].Value);
            Assert.Equal(TokenIds.Sep, tokens[5].Id);
            Assert.Equal(TokenIds.Name, tokens[6].Id);
            Assert.Equal("Thinking", tokens[6].Value);
            Assert.Equal(TokenIds.SetClose, tokens[7].Id);
            Assert.Equal(TokenIds.Eof, tokens[8].Id);
        }

        /// <summary>
        /// 导线全形态——属性 [t=10] + 参数 ['file.read'["path"]] + 分叉 |
        /// </summary>
        [Fact]
        public void Lex_Wire_FullShape()
        {
            LexResultV3 result = Lex("§ 'T_Start' [t=10] : 'P_Go' & 'S_X' = 'Y' → 'file.read'[\"path\"] | 'S_X' = 'F'");
            Assert.True(result.Success, FormatDiags(result));
            List<TokenV3> tokens = result.Tokens;
            // § 'T_Start' [ t = 10 ] : 'P_Go' & 'S_X' = 'Y' → 'file.read' [ "path" ] | 'S_X' = 'F' Eof
            Assert.Equal(TokenIds.Section, tokens[0].Id);
            Assert.Equal("T_Start", tokens[1].Value);
            Assert.Equal(TokenIds.ParamOpen, tokens[2].Id);
            Assert.Equal("t", tokens[3].Value);
            Assert.Equal(TokenIds.Eq, tokens[4].Id);
            Assert.Equal("10", tokens[5].Value);
            Assert.Equal(TokenIds.ParamClose, tokens[6].Id);
            Assert.Equal(TokenIds.Colon, tokens[7].Id);
            Assert.Equal("P_Go", tokens[8].Value);
            Assert.Equal(TokenIds.And, tokens[9].Id);
            Assert.Equal("S_X", tokens[10].Value);
            Assert.Equal(TokenIds.Eq, tokens[11].Id);
            Assert.Equal("Y", tokens[12].Value);
            Assert.Equal(TokenIds.Arrow, tokens[13].Id);
            Assert.Equal("file.read", tokens[14].Value);
            Assert.Equal(TokenIds.ParamOpen, tokens[15].Id);
            Assert.Equal(TokenIds.Str, tokens[16].Id);
            Assert.Equal("path", tokens[16].Value);
            Assert.Equal(TokenIds.ParamClose, tokens[17].Id);
            Assert.Equal(TokenIds.Branch, tokens[18].Id);
            Assert.Equal(TokenIds.Eof, tokens[tokens.Count - 1].Id);
        }

        /// <summary>
        /// 注释与空段丢弃——注释行不产 token，段间空白跳过
        /// </summary>
        [Fact]
        public void Lex_Comment_Discarded()
        {
            LexResultV3 result = Lex("// 这是一行注释\n§ 'S_A' = { 'X' }\n\n// 空段无语义\n§ 'S_B' = { 'Y' }");
            Assert.True(result.Success, FormatDiags(result));
            int names = 0;
            for (int i = 0; i < result.Tokens.Count; i++)
            {
                if (result.Tokens[i].Id == TokenIds.Name)
                {
                    names = names + 1;
                }
            }
            Assert.Equal(4, names);
        }

        /// <summary>
        /// 多字符字形 := 与别名——显式 ASCII 构造的 &lt;- 与 @
        /// </summary>
        [Fact]
        public void Lex_MultiGlyphs()
        {
            string asciiIn = ((char)60).ToString() + ((char)45).ToString();
            LexResultV3 result1 = Lex("'S_X' := { 'A' }");
            Assert.True(result1.Success, FormatDiags(result1));
            Assert.Equal(TokenIds.Declare, result1.Tokens[1].Id);
            LexResultV3 result2 = Lex("'P_X' " + asciiIn);
            Assert.True(result2.Success, FormatDiags(result2));
            Assert.Equal(TokenIds.In, result2.Tokens[1].Id);
            LexResultV3 result3 = Lex("> @key");
            Assert.True(result3.Success, FormatDiags(result3));
            Assert.Equal(TokenIds.Capture, result3.Tokens[0].Id);
            Assert.Equal(TokenIds.Name, result3.Tokens[1].Id);
            Assert.Equal("@key", result3.Tokens[1].Value);
        }

        /// <summary>
        /// 负例——非法字符 τ（v2 退役符号）→ E001
        /// </summary>
        [Fact]
        public void Lex_Reject_V2Retired()
        {
            LexResultV3 result = Lex("§ 'S_X' = { 'A' } τ");
            Assert.False(result.Success);
            Assert.Equal("E001", result.Diagnostics[0].Code);
        }

        /// <summary>
        /// 负例——专有名词含中文 → E002
        /// </summary>
        [Fact]
        public void Lex_Reject_BadName()
        {
            LexResultV3 result = Lex("§ 'S_状态' = { 'A' }");
            Assert.False(result.Success);
            Assert.Equal("E002", result.Diagnostics[0].Code);
        }

        /// <summary>
        /// 负例——字符串未闭合 → E003
        /// </summary>
        [Fact]
        public void Lex_Reject_UnclosedString()
        {
            LexResultV3 result = Lex("§ 'T_X' : 'P' → 'brick'[\"oops]");
            Assert.False(result.Success);
            Assert.Equal("E003", result.Diagnostics[0].Code);
        }

        /// <summary>
        /// 负例——参数未闭合 → E004
        /// </summary>
        [Fact]
        public void Lex_Reject_UnclosedParam()
        {
            LexResultV3 result = Lex("§ 'T_X' : 'P' → 'brick'[oops");
            Assert.False(result.Success);
            Assert.Equal("E004", result.Diagnostics[0].Code);
        }

        /// <summary>
        /// 行号跟踪——第二行错误报行 2
        /// </summary>
        [Fact]
        public void Lex_Line_Tracking()
        {
            LexResultV3 result = Lex("§ 'S_A' = { 'X' }\n§ 'S_B' = { 'Y' } τ");
            Assert.False(result.Success);
            Assert.Equal(2, result.Diagnostics[0].Line);
        }
    }
}

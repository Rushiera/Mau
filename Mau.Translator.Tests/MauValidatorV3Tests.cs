using System;
using Mau.Translator;
using Xunit;

namespace Mau.Translator.Tests
{
    /// <summary>
    /// v3 验证器测试——名称唯一 + 引用完整性（错误码 E2xx）
    /// </summary>
    public class MauValidatorV3Tests
    {
        /// <summary>
        /// 全链路——词法 + 解析 + 验证
        /// </summary>
        /// <param name="source">源文本</param>
        /// <returns>IR 文档（含词法/解析/验证诊断）</returns>
        private static MauDocV3 Compile(string source)
        {
            LexResultV3 lex = MauLexerV3.Lex(source, new DefaultAppearance());
            MauDocV3 doc = new MauDocV3();
            if (!lex.Success)
            {
                doc.Diagnostics.AddRange(lex.Diagnostics);
                doc.Success = false;
                return doc;
            }
            doc = MauParserV3.Parse(lex.Tokens);
            if (!doc.Success)
            {
                return doc;
            }
            MauValidatorV3.Validate(doc);
            doc.Success = doc.Diagnostics.Count == 0;
            return doc;
        }

        /// <summary>
        /// 正例——完整 FSM 网络全引用有效
        /// </summary>
        [Fact]
        public void Validate_AllRefsValid()
        {
            MauDocV3 doc = Compile(
                "§ 'S_Talk' = { 'Idle', 'Thinking' }\n" +
                "§ 'P_Go' ⇐\n" +
                "§ 'P_Q' ↻ [3]: 'probe.sink'[\"x\", 0] > @q\n" +
                "§ 'T_Start' : 'P_Go' & 'S_Talk' = 'Idle' → 'probe.sink'[\"x\", 0] | 'S_Talk' = 'Thinking' | 'S_Talk' = 'Idle'");
            Assert.True(doc.Success);
        }

        /// <summary>
        /// 负例——跨单元重名 → E200
        /// </summary>
        [Fact]
        public void Validate_Reject_DuplicateName()
        {
            MauDocV3 doc = Compile("§ 'S_A' = { 'X' }\n§ 'P_S_A' ⇐\n§ 'T_S_A' : 'P_S_A' → | 'S_A' = 'X'");
            // 'S_A' 状态机与 'T_S_A' 导线不同名——构造真重名：两个传感器同名
            MauDocV3 doc2 = Compile("§ 'S_A' = { 'X' }\n§ 'P_X' ⇐\n§ 'P_X' ⇐");
            Assert.False(doc2.Success);
            Assert.Equal("E200", doc2.Diagnostics[0].Code);
        }

        /// <summary>
        /// 负例——传感器引用不存在 → E201
        /// </summary>
        [Fact]
        public void Validate_Reject_MissingSensor()
        {
            MauDocV3 doc = Compile("§ 'S_A' = { 'X', 'Y' }\n§ 'T_Go' : 'P_NoSuch' → | 'S_A' = 'Y'");
            Assert.False(doc.Success);
            Assert.Equal("E201", doc.Diagnostics[0].Code);
        }

        /// <summary>
        /// 负例——状态机引用不存在 → E202
        /// </summary>
        [Fact]
        public void Validate_Reject_MissingStateMachine()
        {
            MauDocV3 doc = Compile("§ 'S_A' = { 'X', 'Y' }\n§ 'P_Go' ⇐\n§ 'T_Go' : 'P_Go' → | 'S_NoSuch' = 'Y'");
            Assert.False(doc.Success);
            Assert.Equal("E202", doc.Diagnostics[0].Code);
        }

        /// <summary>
        /// 负例——状态值不存在 → E203
        /// </summary>
        [Fact]
        public void Validate_Reject_MissingStateValue()
        {
            MauDocV3 doc = Compile("§ 'S_A' = { 'X', 'Y' }\n§ 'P_Go' ⇐\n§ 'T_Go' : 'P_Go' → | 'S_A' = 'Z'");
            Assert.False(doc.Success);
            Assert.Equal("E203", doc.Diagnostics[0].Code);
        }
        /// <summary>
        /// B1 正例——全局盒裸词（无 @ 前缀）动作参数——外部写源豁免 E407/E402
        /// </summary>
        [Fact]
        public void Validate_GlobalBoxArg_ExternalWriter_Allowed()
        {
            MauDocV3 doc = Compile(
                "§ 'S_A' = { 'X', 'Y' }\n" +
                "§ 'P_Go' ⇐\n" +
                "§ 'T_Go' : 'P_Go' & 'S_A' = 'X' → 'probe.sink'[ExtKey, 0] | 'S_A' = 'Y' | 'S_A' = 'X'");
            Assert.True(doc.Success);
        }

        /// <summary>
        /// 负例——私有盒（@ 前缀）读无写源仍被拦 → E407
        /// </summary>
        [Fact]
        public void Validate_Reject_PrivateBoxNoWriter()
        {
            MauDocV3 doc = Compile(
                "§ 'S_A' = { 'X', 'Y' }\n" +
                "§ 'P_Go' ⇐\n" +
                "§ 'T_Go' : 'P_Go' & 'S_A' = 'X' → 'probe.sink'[@NoWriter, 0] | 'S_A' = 'Y' | 'S_A' = 'X'");
            Assert.False(doc.Success);
            Assert.Equal("E407", doc.Diagnostics[0].Code);
        }

        /// <summary>
        /// 正例——Command 传感器（⇚ 外部指令源）完整声明 + 导线沿引用
        /// </summary>
        [Fact]
        public void Validate_CmdSensor_Valid()
        {
            MauDocV3 doc = Compile(
                "§ 'S_A' = { 'X', 'Y' }\n" +
                "§ 'P_Cmd' ⇚ \"CmdKey\"\n" +
                "§ 'T_Go' : 'P_Cmd' & 'S_A' = 'X' → 'probe.sink'[\"x\", 0] | 'S_A' = 'Y' | 'S_A' = 'X'");
            Assert.True(doc.Success);
        }

        /// <summary>
        /// 负例——Command key 重名 → E206
        /// </summary>
        [Fact]
        public void Validate_Reject_DuplicateCmdKey()
        {
            MauDocV3 doc = Compile(
                "§ 'S_A' = { 'X' }\n" +
                "§ 'P_C1' ⇚ \"SameKey\"\n" +
                "§ 'P_C2' ⇚ \"SameKey\"");
            Assert.False(doc.Success);
            Assert.Equal("E206", doc.Diagnostics[0].Code);
        }
    }
}

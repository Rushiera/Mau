using System.Collections.Generic;
using Mau.Translator;
using Xunit;

namespace Mau.Translator.Tests
{
    /// <summary>
    /// 验证层 v2 测试——MauValidatorV2 引用完整性行为验证
    /// </summary>
    public class MauValidatorV2Tests
    {
        /// <summary>
        /// 完整语料——验证通过
        /// </summary>
        [Fact]
        public void ValidateTalkCat_Passes()
        {
            string source = "§'Mau' 2.0\n" +
                "§'Mau.Runtime' 2.0\n" +
                "§'S_Talk' = { 'Idle', 'Building', 'Thinking', 'Done', 'Failed' }\n" +
                "§'P_Init'\n" +
                "§'R_ReplySlot': 1\n" +
                "§⇐ 'P_Init'\n" +
                "§'T_Start'[τ=10]: 'S_Talk' = 'Building' ∧ 'R_ReplySlot' + 'llm.chat'['x'] → 'S_Talk' = 'Thinking' | 'S_Talk' = 'Failed'\n" +
                "§'M_Pump'[ω=1]: 'P_ChunkReady' := 'llm.read_chunk'['requestId']\n" +
                "§'T_Classify': 'P_ChunkReady' + 'llm.is_end'['requestId'] → 'S_Talk' = 'Done' | 'S_Talk' = 'Thinking'\n";

            ValidateResultV2 result = Validate(source);

            Assert.True(result.Success, FormatDiags(result.Diagnostics));
        }

        /// <summary>
        /// 写入源冲突——信号 + 事实双写入（E201）
        /// </summary>
        [Fact]
        public void Validate_PropWriterConflict_E201()
        {
            string source = "§'Mau' 2.0\n" +
                "§'S_X' = { 'Done' }\n" +
                "§'P_Shared'\n" +
                "§⇐ 'P_Shared'\n" +
                "§'T_A': 'P_Init' → 'P_Shared'\n";

            ValidateResultV2 result = Validate(source);

            Assert.False(result.Success);
            Assert.Equal("E201", result.Diagnostics[0].Code);
        }

        /// <summary>
        /// 条件引用无写入源命题（E202）
        /// </summary>
        [Fact]
        public void Validate_CondRefNoSource_E202()
        {
            string source = "§'Mau' 2.0\n" +
                "§'S_X' = { 'Done' }\n" +
                "§'T_A': 'P_Ghost' → 'S_X' = 'Done'\n";

            ValidateResultV2 result = Validate(source);

            Assert.False(result.Success);
            Assert.Equal("E202", result.Diagnostics[0].Code);
        }

        /// <summary>
        /// 版本过高（E203）
        /// </summary>
        [Fact]
        public void Validate_VersionTooHigh_E203()
        {
            string source = "§'Mau' 3.0\n" +
                "§'S_X' = { 'Done' }\n" +
                "§'P_A'\n" +
                "§⇐ 'P_A'\n" +
                "§'T_A': 'P_A' → 'S_X' = 'Done'\n";

            ValidateResultV2 result = Validate(source);

            Assert.False(result.Success);
            Assert.Equal("E203", result.Diagnostics[0].Code);
        }

        /// <summary>
        /// 缺少版本声明（E203）
        /// </summary>
        [Fact]
        public void Validate_NoVersion_E203()
        {
            string source = "§'S_X' = { 'Done' }\n";

            ValidateResultV2 result = Validate(source);

            Assert.False(result.Success);
            Assert.Equal("E203", result.Diagnostics[0].Code);
        }

        /// <summary>
        /// 嵌套绑定——父状态隐式加入（案例 14.2 形态——无须显式列出 Active）
        /// </summary>
        [Fact]
        public void Validate_BindImplicitParentState_Ok()
        {
            string source = "§'Mau' 2.0\n" +
                "§'S_Talk' = { 'Idle' }\n" +
                "§'S_Active' ∈ 'S_Talk.Active'\n" +
                "§'S_Active' = { 'Thinking' }\n";

            ValidateResultV2 result = Validate(source);

            Assert.True(result.Success, FormatDiags(result.Diagnostics));
        }

        /// <summary>
        /// 绑定子机未声明（E204）
        /// </summary>
        [Fact]
        public void Validate_BindChildMissing_E204()
        {
            string source = "§'Mau' 2.0\n" +
                "§'S_Talk' = { 'Idle', 'Active' }\n" +
                "§'S_Active' ∈ 'S_Talk.Active'\n";

            ValidateResultV2 result = Validate(source);

            Assert.False(result.Success);
            Assert.Equal("E204", result.Diagnostics[0].Code);
        }

        /// <summary>
        /// 嵌套并列——同一父状态双子机（E204）
        /// </summary>
        [Fact]
        public void Validate_NestedParallel_E204()
        {
            string source = "§'Mau' 2.0\n" +
                "§'S_Talk' = { 'Idle', 'Active' }\n" +
                "§'S_A' ∈ 'S_Talk.Active'\n" +
                "§'S_A' = { 'Thinking' }\n" +
                "§'S_B' ∈ 'S_Talk.Active'\n" +
                "§'S_B' = { 'Waiting' }\n";

            ValidateResultV2 result = Validate(source);

            Assert.False(result.Success);
            Assert.Equal("E204", result.Diagnostics[0].Code);
        }

        /// <summary>
        /// 资源引用不存在（E205）
        /// </summary>
        [Fact]
        public void Validate_ResourceMissing_E205()
        {
            string source = "§'Mau' 2.0\n" +
                "§'S_X' = { 'Done' }\n" +
                "§'P_A'\n" +
                "§⇐ 'P_A'\n" +
                "§'T_A': 'R_Ghost' → 'S_X' = 'Done'\n";

            ValidateResultV2 result = Validate(source);

            Assert.False(result.Success);
            Assert.Equal("E205", result.Diagnostics[0].Code);
        }

        /// <summary>
        /// 控制律重名（E206）
        /// </summary>
        [Fact]
        public void Validate_LawDuplicate_E206()
        {
            string source = "§'Mau' 2.0\n" +
                "§'S_X' = { 'Done' }\n" +
                "§'P_A'\n" +
                "§⇐ 'P_A'\n" +
                "§'T_A': 'P_A' → 'S_X' = 'Done'\n" +
                "§'T_A': 'P_A' → 'S_X' = 'Done'\n";

            ValidateResultV2 result = Validate(source);

            Assert.False(result.Success);
            Assert.Equal("E206", result.Diagnostics[0].Code);
        }

        /// <summary>
        /// ∥ 无 ⋈（E207）
        /// </summary>
        [Fact]
        public void Validate_WorkerNoJoin_E207()
        {
            string source = "§'Mau' 2.0\n" +
                "§'S_X' = { 'Done' }\n" +
                "§'P_A'\n" +
                "§⇐ 'P_A'\n" +
                "§'T_A'[∥]: 'P_A' + 'llm.chat'['x'] → 'S_X' = 'Done'\n";

            ValidateResultV2 result = Validate(source);

            Assert.False(result.Success);
            Assert.Equal("E207", result.Diagnostics[0].Code);
        }

        /// <summary>
        /// 测量目标被多写入源竞争——信号 + 测量冲突（E201）
        /// </summary>
        [Fact]
        public void Validate_MeasureTargetConflict_E201()
        {
            string source = "§'Mau' 2.0\n" +
                "§'P_Ready'\n" +
                "§⇐ 'P_Ready'\n" +
                "§'M_Pump'[ω=1]: 'P_Ready' := 'llm.read_chunk'['requestId']\n";

            ValidateResultV2 result = Validate(source);

            Assert.False(result.Success);
            Assert.Equal("E201", result.Diagnostics[0].Code);
        }

        /// <summary>
        /// 未知语法糖（E209）
        /// </summary>
        [Fact]
        public void Validate_UnknownSugar_E209()
        {
            string source = "§'Mau' 2.0\n" +
                "§'S_X' = { 'Done' }\n" +
                "§'P_A'\n" +
                "§⇐ 'P_A'\n" +
                "§'T_A': 'P_A' → 'S_X' = 'Done'\n" +
                "§'LOOP'['T_A']\n";

            ValidateResultV2 result = Validate(source);

            Assert.False(result.Success);
            Assert.Equal("E209", result.Diagnostics[0].Code);
        }

        /// <summary>
        /// 语法糖引用不存在（E209）
        /// </summary>
        [Fact]
        public void Validate_SugarRefMissing_E209()
        {
            string source = "§'Mau' 2.0\n" +
                "§'SEQ'['T_Ghost']\n";

            ValidateResultV2 result = Validate(source);

            Assert.False(result.Success);
            Assert.Equal("E209", result.Diagnostics[0].Code);
        }

        /// <summary>
        /// 输入边界目标被多写入源竞争——事实 + 信号冲突（E201）
        /// </summary>
        [Fact]
        public void Validate_BoundaryTargetConflict_E201()
        {
            string source = "§'Mau' 2.0\n" +
                "§'S_X' = { 'Done' }\n" +
                "§'P_User'\n" +
                "§⇐ 'msg' → 'P_User'\n" +
                "§'T_A': 'P_A' → 'P_User'\n";

            ValidateResultV2 result = Validate(source);

            Assert.False(result.Success);
            Assert.Equal("E201", result.Diagnostics[0].Code);
        }

        // ==================== 工具 ====================

        /// <summary>
        /// 词法 + 解析 + 验证全链路
        /// </summary>
        /// <param name="source">.mau 源文本</param>
        /// <returns>验证结果</returns>
        private static ValidateResultV2 Validate(string source)
        {
            LexResultV2 lex = MauLexerV2.Lex(source);
            if (!lex.Success)
            {
                ValidateResultV2 fail = new ValidateResultV2();
                fail.Diagnostics.AddRange(lex.Diagnostics);
                fail.Success = false;
                return fail;
            }
            ParseResultV2 parsed = MauParserV2.Parse(lex);
            if (!parsed.Success)
            {
                ValidateResultV2 fail = new ValidateResultV2();
                fail.Diagnostics.AddRange(parsed.Diagnostics);
                fail.Success = false;
                return fail;
            }
            return MauValidatorV2.Validate(parsed.Doc);
        }

        /// <summary>
        /// 诊断格式化——失败断言输出诊断详情
        /// </summary>
        /// <param name="diags">诊断列表</param>
        /// <returns>诊断摘要</returns>
        private static string FormatDiags(List<MauDiagnostic> diags)
        {
            string s = "";
            for (int i = 0; i < diags.Count; i++)
            {
                s = s + diags[i].Code + ":" + diags[i].Message + "\n";
            }
            return s;
        }
    }
}

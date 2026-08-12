using System.Collections.Generic;
using Mau.Translator;
using Xunit;

namespace Mau.Translator.Tests
{
    /// <summary>
    /// 编译门面 v2 测试——五阶段流水线端到端
    /// </summary>
    public class MauCompilerV2Tests
    {
        /// <summary>
        /// TalkCat 完整语料——编译成功 + 生成物完整
        /// </summary>
        [Fact]
        public void CompileTalkCat_FullPipeline()
        {
            string source = "§'Mau' 2.0\n" +
                "§'Mau.Runtime' 2.0\n" +
                "§@'CH4.Contracts.ICat'\n" +
                "§$'sessionKey', 'prompt'\n" +
                "§'S_Talk' = { 'Idle', 'Building', 'Done', 'Failed' }\n" +
                "§'S_Active' ∈ 'S_Talk.Active'\n" +
                "§'S_Active' = { 'Thinking', 'Streaming', 'ToolWait' }\n" +
                "§'P_Init', 'P_CmdArrived', 'P_StopCmd'\n" +
                "§'R_ReplySlot': 1\n" +
                "§⇐ 'P_Init'\n" +
                "§'T_Init'[τ=5]: 'P_Init' + 'llm.ctx_set_system'['sessionKey', 'prompt'] → 'S_Talk' = 'Building' | 'S_Talk' = 'Failed'\n" +
                "§'T_Start'[τ=10]: 'S_Talk' = 'Building' ∧ 'R_ReplySlot' + 'llm.completions'['model', 'messagesJson', 'toolsJson'] → 'S_Active' = 'Thinking' | 'S_Talk' = 'Failed'\n" +
                "§'M_Pump'[ω=1]: 'P_ChunkReady' := 'llm.read_chunk'['requestId']\n" +
                "§'T_Classify': 'P_ChunkReady' + 'llm.is_end'['requestId'] → 'S_Active' = 'Streaming' | 'S_Active' = 'Thinking'\n" +
                "§'T_CheckTool'[τ=5]: 'S_Active' = 'Streaming' + 'llm.is_tool'['requestId'] → 'S_Active' = 'ToolWait' | 'S_Active' = 'Thinking'\n" +
                "§'T_Finish': 'S_Active' = 'Streaming' → 'S_Talk' = 'Done'\n" +
                "§'T_Reset': 'S_Talk' = 'Done' ∨ 'S_Talk' = 'Failed' → 'S_Talk' = 'Idle'\n";

            CompileResultV2 result = MauCompilerV2.Compile(source, "FL_Talk");

            Assert.True(result.Success, FormatDiags(result.Diagnostics));
            Assert.Contains("public sealed class FL_Talk", result.GeneratedCode);
            Assert.Contains("private enum S_Talk_State", result.GeneratedCode);
            Assert.Contains("public void Tick(int frame)", result.GeneratedCode);
            Assert.Contains("public void FireInit()", result.GeneratedCode);
            // 嵌套 None 哨兵
            Assert.Contains("private enum S_Active_State { None, Thinking, Streaming, ToolWait }", result.GeneratedCode);
            // GetStatePath
            Assert.Contains("GetTalkStatePath()", result.GeneratedCode);
            // 注入字段
            Assert.Contains("private string? _sessionKey;", result.GeneratedCode);
        }

        /// <summary>
        /// 词法错误——E0xx 冒泡
        /// </summary>
        [Fact]
        public void Compile_LexError_Propagates()
        {
            string source = "§'Mau' 2.0\n§'P_测试'\n";

            CompileResultV2 result = MauCompilerV2.Compile(source, "FL_X");

            Assert.False(result.Success);
            Assert.Equal("E001", result.Diagnostics[0].Code);
        }

        /// <summary>
        /// 语法错误——E1xx 冒泡
        /// </summary>
        [Fact]
        public void Compile_ParseError_Propagates()
        {
            string source = "§'Mau' 2.0\n§'S_X' = { }\n";

            CompileResultV2 result = MauCompilerV2.Compile(source, "FL_X");

            Assert.False(result.Success);
            Assert.StartsWith("E1", result.Diagnostics[0].Code);
        }

        /// <summary>
        /// 验证错误——E2xx 冒泡
        /// </summary>
        [Fact]
        public void Compile_ValidateError_Propagates()
        {
            string source = "§'Mau' 2.0\n" +
                "§'S_X' = { 'Done' }\n" +
                "§'T_A': 'P_Ghost' → 'S_X' = 'Done'\n";

            CompileResultV2 result = MauCompilerV2.Compile(source, "FL_X");

            Assert.False(result.Success);
            Assert.Equal("E202", result.Diagnostics[0].Code);
        }

        /// <summary>
        /// 分析错误——E3xx 冒泡
        /// </summary>
        [Fact]
        public void Compile_AnalyzeError_Propagates()
        {
            string source = "§'Mau' 2.0\n" +
                "§'S_X' = { 'A', 'B', 'C' }\n" +
                "§'P_Init'\n" +
                "§⇐ 'P_Init'\n" +
                "§'T_Go': 'P_Init' → 'S_X' = 'A'\n" +
                "§'T_AB': 'S_X' = 'A' → 'S_X' = 'B'\n" +
                "§'T_CB': 'S_X' = 'C' → 'S_X' = 'B'\n";

            CompileResultV2 result = MauCompilerV2.Compile(source, "FL_X");

            Assert.False(result.Success);
            Assert.Equal("E301", result.Diagnostics[0].Code);
        }

        /// <summary>
        /// 语法糖端到端——SEQ 展开后编译成功
        /// </summary>
        [Fact]
        public void Compile_SugarSeq_EndToEnd()
        {
            string source = "§'Mau' 2.0\n" +
                "§'S_X' = { 'A', 'B', 'C' }\n" +
                "§'P_Init'\n" +
                "§⇐ 'P_Init'\n" +
                "§'T_A': 'P_Init' → 'S_X' = 'A'\n" +
                "§'T_B': 'S_X' = 'A' → 'S_X' = 'B'\n" +
                "§'T_C': 'S_X' = 'B' → 'S_X' = 'C'\n" +
                "§'SEQ'['T_A' → 'T_B' → 'T_C']\n";

            CompileResultV2 result = MauCompilerV2.Compile(source, "FL_Seq");

            Assert.True(result.Success, FormatDiags(result.Diagnostics));
            // 隐式状态机进入生成物
            Assert.Contains("S_Seq_State", result.GeneratedCode);
            Assert.Contains("S_Seq_Enter_Step1();", result.GeneratedCode);
        }

        /// <summary>
        /// 关键路径报告——编译成功附带
        /// </summary>
        [Fact]
        public void Compile_KeyPathReport_Attached()
        {
            string source = "§'Mau' 2.0\n" +
                "§'S_X' = { 'A', 'B', 'C' }\n" +
                "§'P_Init'\n" +
                "§⇐ 'P_Init'\n" +
                "§'T_Go': 'P_Init' → 'S_X' = 'A'\n" +
                "§'T_AB': 'S_X' = 'A' → 'S_X' = 'B'\n" +
                "§'T_BC': 'S_X' = 'B' → 'S_X' = 'C'\n";

            CompileResultV2 result = MauCompilerV2.Compile(source, "FL_X");

            Assert.True(result.Success, FormatDiags(result.Diagnostics));
            Assert.Contains("关键路径", result.KeyPathReport);
        }

        /// <summary>
        /// 诊断格式化
        /// </summary>
        /// <param name="diags">诊断列表</param>
        /// <returns>摘要</returns>
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

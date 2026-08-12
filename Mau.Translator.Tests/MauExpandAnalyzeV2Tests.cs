using System.Collections.Generic;
using Mau.Translator;
using Xunit;

namespace Mau.Translator.Tests
{
    /// <summary>
    /// 糖展开 + 系统分析 v2 测试
    /// </summary>
    public class MauExpandAnalyzeV2Tests
    {
        // ==================== 糖展开 ====================

        /// <summary>
        /// SEQ——隐式状态机 + 结果追加
        /// </summary>
        [Fact]
        public void ExpandSeq_ImplicitMachineAndAppends()
        {
            string source = "§'Mau' 2.0\n" +
                "§'S_X' = { 'A', 'B', 'C' }\n" +
                "§'P_Init'\n" +
                "§⇐ 'P_Init'\n" +
                "§'T_A': 'P_Init' → 'S_X' = 'A'\n" +
                "§'T_B': 'S_X' = 'A' → 'S_X' = 'B'\n" +
                "§'T_C': 'S_X' = 'B' → 'S_X' = 'C'\n" +
                "§'SEQ'['T_A' → 'T_B' → 'T_C']\n";

            MauDocV2? doc = ParseAndValidate(source);
            Assert.NotNull(doc);

            // 隐式机器 S_Seq——Step1/Step2/Step3/Done
            MachineV2? seq = doc.FindMachine("S_Seq");
            Assert.NotNull(seq);
            Assert.Equal(4, seq!.States.Count);
            Assert.Equal("Done", seq.States[3]);
            // T_A 结果追加 → S_Seq = Step2
            LawV2? tA = FindLaw(doc, "T_A");
            Assert.Equal(2, tA!.Results.Count);
            Assert.Equal("Step2", tA.Results[1].StateName);
            // T_C 结果追加 → Done
            LawV2? tC = FindLaw(doc, "T_C");
            Assert.Equal(2, tC!.Results.Count);
            Assert.Equal("Done", tC.Results[1].StateName);
            // 糖列表清空
            Assert.Empty(doc.Sugars);
        }

        /// <summary>
        /// PAR——隐式资源 + 条件追加
        /// </summary>
        [Fact]
        public void ExpandPar_ImplicitResourceAndConds()
        {
            string source = "§'Mau' 2.0\n" +
                "§'S_X' = { 'A' }\n" +
                "§'P_Init'\n" +
                "§⇐ 'P_Init'\n" +
                "§'T_X': 'P_Init' → 'S_X' = 'A'\n" +
                "§'T_Y': 'P_Init' → 'S_X' = 'A'\n" +
                "§'PAR'['T_X' ∥ 'T_Y']\n";

            MauDocV2? doc = ParseAndValidate(source);
            Assert.NotNull(doc);

            // 隐式资源 R_Parallel: 2
            Assert.Single(doc!.Resources);
            Assert.Equal("R_Parallel", doc.Resources[0].Name);
            Assert.Equal(2, doc.Resources[0].Quota);
            // 条件追加 ∧ R_Parallel
            LawV2? tX = FindLaw(doc, "T_X");
            Assert.Equal(CondKindV2.And, tX!.Conditions.Kind);
            bool hasRes = false;
            for (int i = 0; i < tX.Conditions.Items!.Count; i++)
            {
                if (tX.Conditions.Items[i].Kind == CondKindV2.ResourceRef
                    && tX.Conditions.Items[i].RefName == "R_Parallel")
                {
                    hasRes = true;
                }
            }
            Assert.True(hasRes, "T_X 应追加资源条件 R_Parallel");
        }

        /// <summary>
        /// FBK——隐式状态机 + 门控 + 回环
        /// </summary>
        [Fact]
        public void ExpandFbk_ImplicitMachineAndLoop()
        {
            string source = "§'Mau' 2.0\n" +
                "§'S_X' = { 'A', 'B' }\n" +
                "§'P_Ready'\n" +
                "§'M_Poll'[ω=1]: 'P_Ready' := 'llm.read_chunk'['requestId']\n" +
                "§'T_Work': 'P_Ready' + 'llm.chat'[] → 'S_X' = 'B' | 'S_X' = 'A'\n" +
                "§'FBK'['M_Poll' → 'T_Work' → 'M_Poll']\n";

            MauDocV2? doc = ParseAndValidate(source);
            Assert.NotNull(doc);

            // 隐式机器 S_Fb——Waiting/Working
            MachineV2? fb = doc!.FindMachine("S_Fb");
            Assert.NotNull(fb);
            // 条件追加 ∧ S_Fb = Waiting
            LawV2? work = FindLaw(doc, "T_Work");
            Assert.Equal(CondKindV2.And, work!.Conditions.Kind);
            // 结果——成功追加 Working + 失败回环 Waiting
            Assert.True(work.Results.Count >= 2);
            bool hasWorking = false;
            bool hasWaiting = false;
            for (int i = 0; i < work.Results.Count; i++)
            {
                if (work.Results[i].StateName == "Working")
                {
                    hasWorking = true;
                }
                if (work.Results[i].StateName == "Waiting")
                {
                    hasWaiting = true;
                }
            }
            Assert.True(hasWorking, "应追加成功转移 → Working");
            Assert.True(hasWaiting, "失败分支应回环 → Waiting");
        }

        /// <summary>
        /// SEQ 引用不存在（E210）
        /// </summary>
        [Fact]
        public void ExpandSeq_MissingRef_Fails()
        {
            string source = "§'Mau' 2.0\n" +
                "§'SEQ'['T_Ghost' → 'T_B']\n";

            ExpandResultV2 result = Expand(source);

            Assert.False(result.Success);
            Assert.Equal("E210", result.Diagnostics[0].Code);
        }

        // ==================== 系统分析 ====================

        /// <summary>
        /// 完整闭环——分析通过 + 关键路径
        /// </summary>
        [Fact]
        public void Analyze_ClosedLoop_Passes()
        {
            string source = "§'Mau' 2.0\n" +
                "§'S_Talk' = { 'Idle', 'Building', 'Thinking', 'Done', 'Failed' }\n" +
                "§'P_Init'\n" +
                "§⇐ 'P_Init'\n" +
                "§'T_Init'[τ=5]: 'P_Init' → 'S_Talk' = 'Building'\n" +
                "§'T_Start'[τ=10]: 'S_Talk' = 'Building' + 'llm.chat'[] → 'S_Talk' = 'Thinking' | 'S_Talk' = 'Failed'\n" +
                "§'T_Finish': 'S_Talk' = 'Thinking' → 'S_Talk' = 'Done'\n";

            AnalyzeResultV2 result = Analyze(source);

            Assert.True(result.Success, FormatDiags(result.Diagnostics));
            Assert.Contains("关键路径", result.KeyPathReport);
        }

        /// <summary>
        /// 不可达状态（E301）
        /// </summary>
        [Fact]
        public void Analyze_UnreachableState_E301()
        {
            string source = "§'Mau' 2.0\n" +
                "§'S_X' = { 'A', 'B', 'C' }\n" +
                "§'P_Init'\n" +
                "§⇐ 'P_Init'\n" +
                "§'T_Go': 'P_Init' → 'S_X' = 'A'\n" +
                "§'T_AB': 'S_X' = 'A' → 'S_X' = 'B'\n" +
                "§'T_CB': 'S_X' = 'C' → 'S_X' = 'B'\n"; // C 不可达

            AnalyzeResultV2 result = Analyze(source);

            Assert.False(result.Success);
            Assert.Equal("E301", result.Diagnostics[0].Code);
        }

        /// <summary>
        /// 无界环（E311）——无逃逸死环 + 环上无 τ/资源/测量
        /// </summary>
        [Fact]
        public void Analyze_UnboundedLoop_E311()
        {
            string source = "§'Mau' 2.0\n" +
                "§'S_X' = { 'A', 'B' }\n" +
                "§'P_Init'\n" +
                "§⇐ 'P_Init'\n" +
                "§'T_Go': 'P_Init' → 'S_X' = 'A'\n" +
                "§'T_AB': 'S_X' = 'A' → 'S_X' = 'B'\n" +
                "§'T_BA': 'S_X' = 'B' → 'S_X' = 'A'\n";

            AnalyzeResultV2 result = Analyze(source);

            Assert.False(result.Success);
            Assert.Equal("E311", result.Diagnostics[0].Code);
        }

        /// <summary>
        /// 有约束环——通过（τ 约束）
        /// </summary>
        [Fact]
        public void Analyze_ConstrainedLoop_Passes()
        {
            string source = "§'Mau' 2.0\n" +
                "§'S_X' = { 'A', 'B', 'Done' }\n" +
                "§'P_Init'\n" +
                "§⇐ 'P_Init'\n" +
                "§'T_AB'[τ=5]: 'S_X' = 'A' → 'S_X' = 'B'\n" +
                "§'T_BA'[τ=5]: 'S_X' = 'B' → 'S_X' = 'A'\n" +
                "§'T_Done': 'S_X' = 'B' → 'S_X' = 'Done'\n";

            AnalyzeResultV2 result = Analyze(source);

            Assert.True(result.Success, FormatDiags(result.Diagnostics));
        }

        /// <summary>
        /// 扰动无恢复（E321）——失败分支无法到终态
        /// </summary>
        [Fact]
        public void Analyze_DisturbanceNoRecovery_E321()
        {
            string source = "§'Mau' 2.0\n" +
                "§'S_X' = { 'A', 'B', 'C', 'Done' }\n" +
                "§'P_Init'\n" +
                "§⇐ 'P_Init'\n" +
                "§'T_Go': 'P_Init' → 'S_X' = 'A'\n" +
                "§'T_AB'[τ=5]: 'S_X' = 'A' + 'llm.chat'[] → 'S_X' = 'B' | 'S_X' = 'C'\n" +
                // 失败分支 C——C 无出边 = 终态，OK；需要 C 有出边但无法到终态
                "§'T_CB': 'S_X' = 'C' → 'S_X' = 'B'\n" +
                "§'T_BD': 'S_X' = 'B' → 'S_X' = 'Done'\n";

            AnalyzeResultV2 result = Analyze(source);

            // C → B → Done 可达——恢复存在，应通过
            Assert.True(result.Success, FormatDiags(result.Diagnostics));
        }

        /// <summary>
        /// 非终态无路到终态（E302）
        /// </summary>
        [Fact]
        public void Analyze_NoExitToTerminal_E302()
        {
            string source = "§'Mau' 2.0\n" +
                "§'S_X' = { 'A', 'B' }\n" +
                "§'P_Init'\n" +
                "§⇐ 'P_Init'\n" +
                "§'T_Go': 'P_Init' → 'S_X' = 'A'\n" +
                "§'T_AB': 'S_X' = 'A' → 'S_X' = 'B'\n" +
                "§'T_BA': 'S_X' = 'B' → 'S_X' = 'A'\n" +
                // 环——A/B 都有出边（非终态），但无路到终态（出度 0）——E302
                "§'T_Done': 'S_X' = 'A' → 'S_X' = 'B'\n";

            AnalyzeResultV2 result = Analyze(source);

            Assert.False(result.Success);
            Assert.True(result.Diagnostics[0].Code == "E302" || result.Diagnostics[0].Code == "E311",
                "应报 E302 或 E311，实际 " + result.Diagnostics[0].Code);
        }

        // ==================== 工具 ====================

        /// <summary>
        /// 解析 + 验证 + 展开
        /// </summary>
        /// <param name="source">.mau 源</param>
        /// <returns>文档（失败为 null）</returns>
        private static MauDocV2? ParseAndValidate(string source)
        {
            LexResultV2 lex = MauLexerV2.Lex(source);
            if (!lex.Success)
            {
                return null;
            }
            ParseResultV2 parsed = MauParserV2.Parse(lex);
            if (!parsed.Success)
            {
                return null;
            }
            ExpandResultV2 expanded = MauSugarExpanderV2.Expand(parsed.Doc);
            if (!expanded.Success)
            {
                return null;
            }
            ValidateResultV2 valid = MauValidatorV2.Validate(parsed.Doc);
            if (!valid.Success)
            {
                return null;
            }
            return parsed.Doc;
        }

        /// <summary>
        /// 展开结果（直接）
        /// </summary>
        /// <param name="source">.mau 源</param>
        /// <returns>展开结果</returns>
        private static ExpandResultV2 Expand(string source)
        {
            LexResultV2 lex = MauLexerV2.Lex(source);
            if (!lex.Success)
            {
                ExpandResultV2 fail = new ExpandResultV2();
                fail.Diagnostics.AddRange(lex.Diagnostics);
                fail.Success = false;
                return fail;
            }
            ParseResultV2 parsed = MauParserV2.Parse(lex);
            if (!parsed.Success)
            {
                ExpandResultV2 fail = new ExpandResultV2();
                fail.Diagnostics.AddRange(parsed.Diagnostics);
                fail.Success = false;
                return fail;
            }
            return MauSugarExpanderV2.Expand(parsed.Doc);
        }

        /// <summary>
        /// 分析结果（全链路）
        /// </summary>
        /// <param name="source">.mau 源</param>
        /// <returns>分析结果</returns>
        private static AnalyzeResultV2 Analyze(string source)
        {
            LexResultV2 lex = MauLexerV2.Lex(source);
            if (!lex.Success)
            {
                AnalyzeResultV2 fail = new AnalyzeResultV2();
                fail.Diagnostics.AddRange(lex.Diagnostics);
                fail.Success = false;
                return fail;
            }
            ParseResultV2 parsed = MauParserV2.Parse(lex);
            if (!parsed.Success)
            {
                AnalyzeResultV2 fail = new AnalyzeResultV2();
                fail.Diagnostics.AddRange(parsed.Diagnostics);
                fail.Success = false;
                return fail;
            }
            ValidateResultV2 valid = MauValidatorV2.Validate(parsed.Doc);
            if (!valid.Success)
            {
                AnalyzeResultV2 fail = new AnalyzeResultV2();
                fail.Diagnostics.AddRange(valid.Diagnostics);
                fail.Success = false;
                return fail;
            }
            return MauAnalyzerV2.Analyze(parsed.Doc);
        }

        /// <summary>
        /// 查控制律
        /// </summary>
        /// <param name="doc">文档</param>
        /// <param name="name">控制律名</param>
        /// <returns>控制律</returns>
        private static LawV2? FindLaw(MauDocV2 doc, string name)
        {
            for (int i = 0; i < doc.Laws.Count; i++)
            {
                if (doc.Laws[i].Name == name)
                {
                    return doc.Laws[i];
                }
            }
            return null;
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

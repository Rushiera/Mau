using System;
using Mau.Translator;
using Xunit;

namespace Mau.Translator.Tests
{
    /// <summary>
    /// v3 分析器测试——自检仪四分析（无界环/不可达/无恢复/关键路径）
    /// </summary>
    public class MauAnalyzerV3Tests
    {
        /// <summary>
        /// 全链路——词法 + 解析 + 验证 + 分析
        /// </summary>
        /// <param name="source">源文本</param>
        /// <returns>分析结果</returns>
        private static AnalysisResultV3 Analyze(string source)
        {
            LexResultV3 lex = MauLexerV3.Lex(source, new DefaultAppearance());
            if (!lex.Success)
            {
                AnalysisResultV3 fail = new AnalysisResultV3();
                fail.Errors.AddRange(lex.Diagnostics);
                fail.Success = false;
                return fail;
            }
            MauDocV3 doc = MauParserV3.Parse(lex.Tokens);
            if (!doc.Success)
            {
                AnalysisResultV3 fail = new AnalysisResultV3();
                fail.Errors.AddRange(doc.Diagnostics);
                fail.Success = false;
                return fail;
            }
            if (!MauValidatorV3.Validate(doc))
            {
                AnalysisResultV3 fail = new AnalysisResultV3();
                fail.Errors.AddRange(doc.Diagnostics);
                fail.Success = false;
                return fail;
            }
            return MauAnalyzerV3.Analyze(doc);
        }

        /// <summary>
        /// 正例——事件驱动循环（对话态机）+ 失败侧有恢复路径，全过
        /// </summary>
        [Fact]
        public void Analyze_EventLoop_Pass()
        {
            AnalysisResultV3 result = Analyze(
                "§ 'S_Talk' = { 'Idle', 'Thinking', 'Done' }\n" +
                "§ 'P_Go' ⇐\n" +
                "§ 'P_Fail' ⇐\n" +
                "§ 'T_Start' : 'P_Go' & 'S_Talk' = 'Idle' → 'llm.chat'[] | 'S_Talk' = 'Thinking' | 'S_Talk' = 'Done'\n" +
                "§ 'T_Retry' : 'P_Fail' & 'S_Talk' = 'Thinking' → 'llm.chat'[] | 'S_Talk' = 'Thinking' | 'S_Talk' = 'Done'");
            Assert.True(result.Success, FormatErrors(result));
            Assert.True(result.Reports.Count > 0, "关键路径报告应产出");
        }

        /// <summary>
        /// 负例——无界环（纯状态断言环，无事件刹车）→ E300
        /// </summary>
        [Fact]
        public void Analyze_Reject_UnboundedLoop()
        {
            AnalysisResultV3 result = Analyze(
                "§ 'S_A' = { 'X', 'Y' }\n" +
                "§ 'T_Loop' : 'S_A' = 'X' → | 'S_A' = 'Y' | 'S_A' = 'Y'\n" +
                "§ 'T_Loop2' : 'S_A' = 'Y' → | 'S_A' = 'X' | 'S_A' = 'X'");
            Assert.False(result.Success);
            Assert.Equal("E300", result.Errors[0].Code);
        }

        /// <summary>
        /// 正例——有界环（事件刹车——传感器沿在环上）通过
        /// </summary>
        [Fact]
        public void Analyze_BoundedLoop_Pass()
        {
            AnalysisResultV3 result = Analyze(
                "§ 'S_A' = { 'X', 'Y' }\n" +
                "§ 'P_Tick' ⇐\n" +
                "§ 'T_Loop' : 'P_Tick' & 'S_A' = 'X' → | 'S_A' = 'Y' | 'S_A' = 'Y'\n" +
                "§ 'T_Loop2' : 'S_A' = 'Y' → | 'S_A' = 'X' | 'S_A' = 'X'");
            // T_Loop2 是纯状态断言环边——但 T_Loop 的边有事件刹车，环整体判断……
            // 无事件子图 = { T_Loop2 的边 }——X→X 自环来自 T_Loop2（Y→X），Y 从 T_Loop（事件边）进入。
            // 无事件子图中 Y→X 的边存在（T_Loop2），X 无出边（T_Loop 有事件被排除）——无环 → 通过
            Assert.True(result.Success, FormatErrors(result));
        }

        /// <summary>
        /// 负例——不可达状态（无任何转移链到达）→ E301
        /// </summary>
        [Fact]
        public void Analyze_Reject_Unreachable()
        {
            AnalysisResultV3 result = Analyze(
                "§ 'S_A' = { 'X', 'Y', 'Dead' }\n" +
                "§ 'P_Go' ⇐\n" +
                "§ 'T_Go' : 'P_Go' & 'S_A' = 'X' → | 'S_A' = 'Y' | 'S_A' = 'Y'");
            Assert.False(result.Success);
            Assert.Equal("E301", result.Errors[0].Code);
        }

        /// <summary>
        /// 负例——失败侧无恢复路径（失败态在非终态环内空转）→ E302
        /// </summary>
        [Fact]
        public void Analyze_Reject_NoRecovery()
        {
            AnalysisResultV3 result = Analyze(
                "§ 'S_A' = { 'X', 'Y', 'Done' }\n" +
                "§ 'P_Go' ⇐\n" +
                "§ 'P_Tick' ⇐\n" +
                "§ 'T_Go' : 'P_Go' & 'S_A' = 'X' → 'brick'[] | 'S_A' = 'Done' | 'S_A' = 'Y'\n" +
                "§ 'T_Loop' : 'P_Tick' & 'S_A' = 'Y' → | 'S_A' = 'Y' | 'S_A' = 'Y'");
            // 失败侧 Y——出度 1（自环），BFS 到不了终态 Done → E302（事件自环不触发 E300）
            Assert.False(result.Success);
            Assert.Equal("E302", result.Errors[0].Code);
        }

        /// <summary>
        /// 正例——失败侧目标是终态（Done）通过（终态本身 = 正常归宿）
        /// </summary>
        [Fact]
        public void Analyze_FailToTerminal_Pass()
        {
            AnalysisResultV3 result = Analyze(
                "§ 'S_A' = { 'X', 'Done' }\n" +
                "§ 'P_Go' ⇐\n" +
                "§ 'T_Go' : 'P_Go' & 'S_A' = 'X' → 'brick'[] | 'S_A' = 'Done' | 'S_A' = 'Done'");
            // Done 出度 0 是终态——失败侧目标 = 终态。但 E302 判据当前是"出度 0 且非终态"——终态集 = 出度 0 的全部。
            // 当前实现：失败侧目标出度 0 → 报错——这里会误报！Done 是设计终态（正常失败归宿）。
            Assert.True(result.Success, FormatErrors(result));
        }

        /// <summary>
        /// 错误文本化
        /// </summary>
        /// <param name="result">分析结果</param>
        /// <returns>错误摘要</returns>
        private static string FormatErrors(AnalysisResultV3 result)
        {
            string s = "";
            for (int i = 0; i < result.Errors.Count; i++)
            {
                s = s + result.Errors[i].Code + ":" + result.Errors[i].Message + "\n";
            }
            return s;
        }
    }
}

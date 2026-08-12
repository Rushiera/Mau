using System.Collections.Generic;
using Mau.Translator;
using Xunit;

namespace Mau.Translator.Tests
{
    /// <summary>
    /// 生成层 v2 测试——MauGeneratorV2 生成物结构验证
    /// </summary>
    public class MauGeneratorV2Tests
    {
        /// <summary>
        /// 枚举状态机——平面机生成
        /// </summary>
        [Fact]
        public void Generate_MachineEnumAndEnter()
        {
            string code = Generate(Source());

            // 枚举 + 单值字段
            Assert.Contains("private enum S_Talk_State { Idle, Building, Thinking, Done, Failed }", code);
            Assert.Contains("private S_Talk_State _S_Talk_State;", code);
            // Enter 族——单值赋值
            Assert.Contains("_S_Talk_State = S_Talk_State.Idle;", code);
            Assert.Contains("private void S_Talk_Enter_Idle()", code);
            // 构造函数初始置位
            Assert.Contains("S_Talk_Enter_Idle();", code);
        }

        /// <summary>
        /// 嵌套子机——None 哨兵 + 父进子置/父离子清
        /// </summary>
        [Fact]
        public void Generate_NestedMachineNoneSentinel()
        {
            string code = Generate(
                "§'Mau' 2.0\n" +
                "§'S_Talk' = { 'Idle', 'Active', 'Done' }\n" +
                "§'S_Active' ∈ 'S_Talk.Active'\n" +
                "§'S_Active' = { 'Thinking', 'Streaming' }\n" +
                "§'P_A'\n" +
                "§⇐ 'P_A'\n" +
                "§'T_Go': 'P_A' → 'S_Talk' = 'Active'\n" +
                "§'T_Done': 'S_Active' = 'Thinking' → 'S_Talk' = 'Done'\n");

            // 子机枚举首位 None
            Assert.Contains("private enum S_Active_State { None, Thinking, Streaming }", code);
            // 父进 Active → 子机初始
            Assert.Contains("S_Active_Enter_Thinking();", code);
            // 父进其他状态 → 子机 None
            Assert.Contains("_S_Active_State = S_Active_State.None;", code);
            // GetStatePath
            Assert.Contains("return \"Active.\" + _S_Active_State.ToString();", code);
        }

        /// <summary>
        /// 控制律——守卫/信号消费/资源/结果
        /// </summary>
        [Fact]
        public void Generate_LawGuardAndConsume()
        {
            string code = Generate(Source());

            // 守卫——状态断言 + 资源（原子括号包裹）
            Assert.Contains("(_S_Talk_State == S_Talk_State.Building) && (R_Slot_Count > 0)", code);
            // 信号消费——P_A 置 false
            Assert.Contains("P_A = false;", code);
            // 资源获取/释放
            Assert.Contains("R_Slot_Count = R_Slot_Count - 1;", code);
            Assert.Contains("R_Slot_Count = R_Slot_Count + 1;", code);
            // 结果转移
            Assert.Contains("S_Talk_Enter_Thinking();", code);
            Assert.Contains("S_Talk_Enter_Failed();", code);
        }

        /// <summary>
        /// 控制律——τ 时限 Cube
        /// </summary>
        [Fact]
        public void Generate_LawTimeoutCube()
        {
            string code = Generate(Source());

            Assert.Contains("private readonly Cube T_Start_Cube = new Cube(10);", code);
            Assert.Contains("T_Start_Cube.Start();", code);
            Assert.Contains("T_Start_Cube.TickFrame();", code);
            Assert.Contains("T_Start_Cube.IsExpired()", code);
            Assert.Contains("T_Start_Cube.IsIdle()", code);
        }

        /// <summary>
        /// 控制律——多路结果 switch
        /// </summary>
        [Fact]
        public void Generate_LawMultiResultSwitch()
        {
            string code = Generate(
                "§'Mau' 2.0\n" +
                "§'S_Ui' = { 'Idle', 'Open', 'Toggle' }\n" +
                "§'P_Cmd'\n" +
                "§⇐ 'P_Cmd'\n" +
                "§'T_Route'[τ=5]: 'P_Cmd' + 'cmd.active_key'['key', 'A', 'B'] → 'S_Ui' = 'Open' | 'S_Ui' = 'Toggle' | 'S_Ui' = 'Idle'\n");

            // switch 分发（多路——名称返回积木）
            Assert.Contains("switch (matched)", code);
        }

        /// <summary>
        /// 测量——帧门控 + 条件赋值
        /// </summary>
        [Fact]
        public void Generate_MeasureFrameGate()
        {
            string code = Generate(Source());

            Assert.Contains("private void M_Pump_Sample()", code);
            Assert.Contains("P_Ready = result;", code);
            Assert.Contains("M_Pump_FrameCounter = M_Pump_FrameCounter + 1;", code);
        }

        /// <summary>
        /// 边界——Fire 信号 + 载荷
        /// </summary>
        [Fact]
        public void Generate_FireSignalAndPayload()
        {
            string code = Generate(
                "§'Mau' 2.0\n" +
                "§$'sessionKey'\n" +
                "§'P_Init'\n" +
                "§'S_X' = { 'Done' }\n" +
                "§⇐ 'P_Init'['sessionKey']\n" +
                "§'T_A': 'P_Init' → 'S_X' = 'Done'\n");

            Assert.Contains("public void FireInit(string sessionKey)", code);
            Assert.Contains("_sessionKey = sessionKey;", code);
            Assert.Contains("P_Init = true;", code);
        }

        /// <summary>
        /// 命题注册——事实置位
        /// </summary>
        [Fact]
        public void Generate_PropRegisterFact()
        {
            string code = Generate(
                "§'Mau' 2.0\n" +
                "§'P_Rec'\n" +
                "§'S_X' = { 'Done' }\n" +
                "§'P_A'\n" +
                "§⇐ 'P_A'\n" +
                "§'T_A': 'P_A' → 'P_Rec'\n");

            Assert.Contains("P_Rec = true;", code);
        }

        // ==================== 工具 ====================

        /// <summary>
        /// 标准测试语料
        /// </summary>
        /// <returns>.mau 源</returns>
        private static string Source()
        {
            return "§'Mau' 2.0\n" +
                "§'S_Talk' = { 'Idle', 'Building', 'Thinking', 'Done', 'Failed' }\n" +
                "§'P_A', 'P_Ready'\n" +
                "§'R_Slot': 1\n" +
                "§⇐ 'P_A'\n" +
                "§'M_Pump'[ω=1]: 'P_Ready' := 'llm.read_chunk'['requestId']\n" +
                "§'T_Start'[τ=10]: 'S_Talk' = 'Building' ∧ 'R_Slot' ∧ 'P_A' + 'llm.chat'['x'] → 'S_Talk' = 'Thinking' | 'S_Talk' = 'Failed'\n";
        }

        /// <summary>
        /// 词法 + 解析 + 验证 + 生成全链路
        /// </summary>
        /// <param name="source">.mau 源文本</param>
        /// <returns>生成物源码</returns>
        private static string Generate(string source)
        {
            LexResultV2 lex = MauLexerV2.Lex(source);
            if (!lex.Success)
            {
                return FormatDiags(lex.Diagnostics);
            }
            ParseResultV2 parsed = MauParserV2.Parse(lex);
            if (!parsed.Success)
            {
                return FormatDiags(parsed.Diagnostics);
            }
            ValidateResultV2 valid = MauValidatorV2.Validate(parsed.Doc);
            if (!valid.Success)
            {
                return FormatDiags(valid.Diagnostics);
            }
            return MauGeneratorV2.Generate(parsed.Doc, "FL_Talk");
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

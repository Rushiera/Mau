using System;
using System.Collections.Generic;
using Mau.Translator;
using Xunit;

namespace Mau.Translator.Tests
{
    /// <summary>
    /// v3 解析器测试——Token 流 → FSM 网络 IR（四柱 + 段结构 + 错误码 E1xx）
    /// </summary>
    public class MauParserV3Tests
    {
        /// <summary>
        /// 词法 + 解析一体化——默认外观
        /// </summary>
        /// <param name="source">源文本</param>
        /// <returns>IR 文档</returns>
        private static MauDocV3 Parse(string source)
        {
            LexResultV3 lex = MauLexerV3.Lex(source, new DefaultAppearance());
            if (!lex.Success)
            {
                MauDocV3 fail = new MauDocV3();
                fail.Diagnostics.AddRange(lex.Diagnostics);
                fail.Success = false;
                return fail;
            }
            return MauParserV3.Parse(lex.Tokens);
        }

        /// <summary>
        /// 诊断文本化——断言失败时输出错误详情
        /// </summary>
        /// <param name="doc">IR 文档</param>
        /// <returns>诊断摘要</returns>
        private static string FormatDiags(MauDocV3 doc)
        {
            string s = "";
            for (int i = 0; i < doc.Diagnostics.Count; i++)
            {
                s = s + doc.Diagnostics[i].Code + ":" + doc.Diagnostics[i].Message + "\n";
            }
            return s;
        }

        /// <summary>
        /// 四柱全形态综合语料——状态机 + 传感器双形态 + 槽 + 导线（属性/多条件/多结果）
        /// </summary>
        [Fact]
        public void Parse_AllFourPillars()
        {
            MauDocV3 doc = Parse(
                "§ 'S_Talk' = { 'Idle', 'Thinking', 'Done' }\n" +
                "§ 'P_Start' ⇐\n" +
                "§ 'P_Queue' ↻ [5] 'data.box_is'[\"tools_done\"]\n" +
                "§ 'R_Slot' : 4\n" +
                "§ 'T_Begin' [t=10, par, !] : 'P_Start' & 'S_Talk' = 'Idle' → 'llm.chat'[\"hi\"] | 'S_Talk' = 'Thinking' | 'S_Talk' = 'Done'");
            Assert.True(doc.Success, FormatDiags(doc));

            Assert.Single(doc.StateMachines);
            StateMachineDefV3 sm = doc.StateMachines[0];
            Assert.Equal("S_Talk", sm.Name);
            Assert.Equal(3, sm.States.Count);
            Assert.Equal("Idle", sm.States[0]);

            Assert.Equal(2, doc.Sensors.Count);
            SensorDefV3 passive = doc.Sensors[0];
            Assert.Equal("P_Start", passive.Name);
            Assert.True(passive.Passive);
            SensorDefV3 active = doc.Sensors[1];
            Assert.Equal("P_Queue", active.Name);
            Assert.False(active.Passive);
            Assert.Equal(5, active.EveryFrames);
            Assert.Equal("data.box_is", active.BrickName);
            Assert.Single(active.BrickArgs);

            Assert.Single(doc.Slots);
            Assert.Equal("R_Slot", doc.Slots[0].Name);
            Assert.Equal(4, doc.Slots[0].Capacity);

            Assert.Single(doc.Wires);
            WireDefV3 wire = doc.Wires[0];
            Assert.Equal("T_Begin", wire.Name);
            Assert.Equal(10, wire.Timeout);
            Assert.True(wire.Parallel);
            Assert.True(wire.Logging);
            Assert.False(wire.Join);
            Assert.Equal(2, wire.Conditions.Count);
            Assert.False(wire.Conditions[0].IsStateAssert);
            Assert.Equal("P_Start", wire.Conditions[0].SensorName);
            Assert.True(wire.Conditions[1].IsStateAssert);
            Assert.Equal("S_Talk", wire.Conditions[1].StateName);
            Assert.Equal("Idle", wire.Conditions[1].StateValue);
            Assert.Equal("llm.chat", wire.BrickName);
            Assert.Equal(2, wire.Results.Count);
            Assert.Equal("Thinking", wire.Results[0].StateValue);
            Assert.Equal("Done", wire.Results[1].StateValue);
        }

        /// <summary>
        /// 空段与注释段跳过——§ 后无内容无语义
        /// </summary>
        [Fact]
        public void Parse_EmptySections_Skipped()
        {
            MauDocV3 doc = Parse(
                "// 注释段\n§\n§ 'S_A' = { 'X' }\n§\n§ 'P_Y' ⇐");
            Assert.True(doc.Success, FormatDiags(doc));
            Assert.Single(doc.StateMachines);
            Assert.Single(doc.Sensors);
        }

        /// <summary>
        /// 纯转移导线——无动作积木，条件触发即转移
        /// </summary>
        [Fact]
        public void Parse_Wire_PureTransition()
        {
            MauDocV3 doc = Parse("§ 'S_A' = { 'X', 'Y' }\n§ 'P_Go' ⇐\n§ 'T_Next' : 'P_Go' → | 'S_A' = 'Y'");
            Assert.True(doc.Success, FormatDiags(doc));
            WireDefV3 wire = doc.Wires[0];
            Assert.Equal("", wire.BrickName);
            Assert.Single(wire.Results);
        }

        /// <summary>
        /// 别名外观——&lt;- 与 @（显式 ASCII 构造）
        /// </summary>
        [Fact]
        public void Parse_AliasGlyphs()
        {
            string asciiIn = ((char)60).ToString() + ((char)45).ToString();
            MauDocV3 doc = Parse("§ 'P_X' " + asciiIn + "\n§ 'P_Q' @ [2] 'file.tree'[]");
            Assert.True(doc.Success, FormatDiags(doc));
            Assert.True(doc.Sensors[0].Passive);
            Assert.False(doc.Sensors[1].Passive);
            Assert.Equal(2, doc.Sensors[1].EveryFrames);
        }

        /// <summary>
        /// 负例——未知单元前缀 → E100
        /// </summary>
        [Fact]
        public void Parse_Reject_UnknownPrefix()
        {
            MauDocV3 doc = Parse("§ 'X_Foo' : 1");
            Assert.False(doc.Success);
            Assert.Equal("E100", doc.Diagnostics[0].Code);
        }

        /// <summary>
        /// 负例——状态机缺 { → E102
        /// </summary>
        [Fact]
        public void Parse_Reject_StateMissingBrace()
        {
            MauDocV3 doc = Parse("§ 'S_A' = 'X'");
            Assert.False(doc.Success);
            Assert.Equal("E102", doc.Diagnostics[0].Code);
        }

        /// <summary>
        /// 负例——导线缺箭头 → E103
        /// </summary>
        [Fact]
        public void Parse_Reject_WireMissingArrow()
        {
            MauDocV3 doc = Parse("§ 'P_Go' ⇐\n§ 'T_X' : 'P_Go' 'brick'[] | 'S_A' = 'Y'");
            Assert.False(doc.Success);
            Assert.Equal("E103", doc.Diagnostics[0].Code);
        }

        /// <summary>
        /// 负例——槽容量 0 → E104
        /// </summary>
        [Fact]
        public void Parse_Reject_SlotZero()
        {
            MauDocV3 doc = Parse("§ 'R_X' : 0");
            Assert.False(doc.Success);
            Assert.Equal("E104", doc.Diagnostics[0].Code);
        }

        /// <summary>
        /// 负例——传感器缺端口符号 → E105
        /// </summary>
        [Fact]
        public void Parse_Reject_SensorMissingPort()
        {
            MauDocV3 doc = Parse("§ 'P_X' = 'Y'");
            Assert.False(doc.Success);
            Assert.Equal("E105", doc.Diagnostics[0].Code);
        }
    }
}

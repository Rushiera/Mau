using System.Collections.Generic;
using Mau.Translator;
using Xunit;

namespace Mau.Translator.Tests
{
    /// <summary>
    /// 解析层 v2 测试——MauParserV2 段 → IR 行为验证
    /// </summary>
    public class MauParserV2Tests
    {
        /// <summary>
        /// TalkCat 完整语料——全单元 IR 构建 + 类型推导
        /// </summary>
        [Fact]
        public void ParseTalkCat_FullIrAndKinds()
        {
            string source = "§'Mau' 2.0\n" +
                "§'Mau.Runtime' 2.0\n" +
                "§@'CH4.Contracts.ICat'\n" +
                "§$'sessionKey', 'prompt'\n" +
                "§'S_Talk' = { 'Idle', 'Building', 'Thinking', 'Streaming', 'ToolWait', 'Done', 'Failed', 'Stopped' }\n" +
                "§'S_Active' ∈ 'S_Talk.Active'\n" +
                "§'S_Active' = { 'Thinking', 'Streaming', 'ToolWait' }\n" +
                "§'P_Init', 'P_CmdArrived', 'P_StopCmd'\n" +
                "§'R_ReplySlot': 1\n" +
                "§⇐ 'P_Init'\n" +
                "§'T_Init'[τ=5]: 'P_Init' + 'llm.ctx_set_system'['sessionKey', 'prompt'] → 'S_Talk' = 'Idle' | 'S_Talk' = 'Failed'\n" +
                "§'T_Start'[τ=10]: 'S_Talk' = 'Building' ∧ 'R_ReplySlot' + 'llm.completions'['model', 'messagesJson', 'toolsJson'] → 'S_Talk' = 'Thinking' | 'S_Talk' = 'Failed'\n" +
                "§'M_Pump'[ω=1]: 'P_ChunkReady' := 'llm.read_chunk'['requestId']\n" +
                "§'T_Classify'[ω=1, τ=5]: 'P_ChunkReady' + 'llm.is_end'['requestId'] → 'S_Talk' = 'Streaming' | 'S_Talk' = 'Thinking'\n";

            ParseResultV2 result = Parse(source);

            Assert.True(result.Success, FormatDiags(result.Diagnostics));
            MauDocV2 doc = result.Doc;

            // 文件头
            Assert.Equal("2.0", doc.SyntaxVersion);
            Assert.Equal("2.0", doc.RuntimeVersion);
            Assert.Contains("CH4.Contracts.ICat", doc.Interfaces);
            Assert.Equal(2, doc.Injections.Count);

            // 状态机 + 嵌套绑定（Active 隐式加入——嵌套父状态无须显式列出）
            Assert.Equal(2, doc.Machines.Count);
            Assert.Equal("S_Talk", doc.Machines[0].Name);
            Assert.Equal(9, doc.Machines[0].States.Count);
            Assert.Contains("Active", doc.Machines[0].States);
            Assert.Equal("Idle", doc.Machines[0].States[0]);
            Assert.Single(doc.Machines[0].Binds);
            Assert.Equal("S_Active", doc.Machines[0].Binds[0].ChildName);
            Assert.Equal("S_Talk", doc.Machines[0].Binds[0].ParentName);
            Assert.Equal("Active", doc.Machines[0].Binds[0].ParentState);

            // 命题类型推导——信号/条件/事实
            Assert.Equal(PropKindV2.Signal, doc.FindProposition("P_Init")!.Kind);
            Assert.Equal(PropKindV2.Condition, doc.FindProposition("P_ChunkReady")!.Kind);

            // 资源
            Assert.Single(doc.Resources);
            Assert.Equal("R_ReplySlot", doc.Resources[0].Name);
            Assert.Equal(1, doc.Resources[0].Quota);

            // 边界
            Assert.Single(doc.Boundaries);
            Assert.Equal(BoundaryDirV2.In, doc.Boundaries[0].Dir);
            Assert.Equal("P_Init", doc.Boundaries[0].SignalName);

            // 测量
            Assert.Single(doc.Measures);
            Assert.Equal("M_Pump", doc.Measures[0].Name);
            Assert.Equal(1, doc.Measures[0].Frame);
            Assert.Equal("P_ChunkReady", doc.Measures[0].Target);
            Assert.Equal("llm.read_chunk", doc.Measures[0].Sample.BrickName);

            // 控制律
            Assert.Equal(3, doc.Laws.Count);
            LawV2 init = doc.Laws[0];
            Assert.Equal("T_Init", init.Name);
            Assert.Equal(5, init.Attrs.Timeout);
            // 单条件段——顶层即条件本身
            Assert.Equal(CondKindV2.PropRef, init.Conditions.Kind);
            Assert.Equal("P_Init", init.Conditions.RefName);
            Assert.Single(init.Ops);
            Assert.Equal("llm.ctx_set_system", init.Ops[0].BrickName);
            Assert.Equal(2, init.Ops[0].Params.Count);
            Assert.Equal(ResultKindV2.StateTransfer, init.ResultKind);
            Assert.Equal(2, init.Results.Count);
            Assert.Equal("S_Talk", init.Results[0].MachineName);
            Assert.Equal("Idle", init.Results[0].StateName);
            Assert.Equal("Failed", init.Results[1].StateName);

            LawV2 start = doc.Laws[1];
            Assert.Equal(10, start.Attrs.Timeout);
            Assert.Equal(CondKindV2.And, start.Conditions.Kind);
            Assert.Equal(CondKindV2.StateEquals, start.Conditions.Items![0].Kind);
            Assert.Equal("S_Talk", start.Conditions.Items[0].MachineName);
            Assert.Equal("Building", start.Conditions.Items[0].StateName);
            Assert.Equal(CondKindV2.ResourceRef, start.Conditions.Items[1].Kind);
            Assert.Equal("R_ReplySlot", start.Conditions.Items[1].RefName);

            LawV2 classify = doc.Laws[2];
            Assert.Equal(1, classify.Attrs.PollFrame);
            Assert.Equal(5, classify.Attrs.Timeout);
        }

        /// <summary>
        /// 析取条件 + 多路结果——cmd.match 路由
        /// </summary>
        [Fact]
        public void ParseLaw_OrConditionAndMultiResults()
        {
            string source = "§'S_Ui' = { 'Idle', 'Open', 'Toggle' }\n" +
                "§'P_CmdArrived'\n" +
                "§'T_Route'[τ=5]: 'P_CmdArrived' ∨ 'S_Ui' = 'Idle' + 'cmd.match'['key', 'A', 'B', 'C'] → 'S_Ui' = 'Open' | 'S_Ui' = 'Toggle' | 'S_Ui' = 'Idle'\n";

            ParseResultV2 result = Parse(source);

            Assert.True(result.Success, FormatDiags(result.Diagnostics));
            LawV2 law = result.Doc.Laws[0];
            Assert.Equal(CondKindV2.Or, law.Conditions.Kind);
            Assert.Equal(2, law.Conditions.Items!.Count);
            Assert.Equal(CondKindV2.PropRef, law.Conditions.Items[0].Kind);
            Assert.Equal(CondKindV2.StateEquals, law.Conditions.Items[1].Kind);
            Assert.Single(law.Ops);
            Assert.Equal("cmd.match", law.Ops[0].BrickName);
            Assert.Equal(3, law.Results.Count);
            Assert.Equal("Open", law.Results[0].StateName);
            Assert.Equal("Toggle", law.Results[1].StateName);
            Assert.Equal("Idle", law.Results[2].StateName);
        }

        /// <summary>
        /// 命题注册结果——事实推导
        /// </summary>
        [Fact]
        public void ParseLaw_PropRegisterFact()
        {
            string source = "§'P_ErrorRecorded'\n§'T_Fail'[τ=5]: 'P_Bad' + 'log.write'['x'] → 'P_ErrorRecorded'\n";

            ParseResultV2 result = Parse(source);

            Assert.True(result.Success, FormatDiags(result.Diagnostics));
            LawV2 law = result.Doc.Laws[0];
            Assert.Equal(ResultKindV2.PropRegister, law.ResultKind);
            Assert.Equal("P_ErrorRecorded", law.Results[0].PropName);
            // 事实推导
            Assert.Equal(PropKindV2.Fact, result.Doc.FindProposition("P_ErrorRecorded")!.Kind);
        }

        /// <summary>
        /// 捕获赋值——别名 := 积木调用
        /// </summary>
        [Fact]
        public void ParseLaw_CaptureAssign()
        {
            string source = "§'T_X'[τ=5]: 'P_A' + 'dataJson' := 'llm.ctx_push'['sessionKey'] → 'S_Ui' = 'Done'\n";

            ParseResultV2 result = Parse(source);

            Assert.True(result.Success, FormatDiags(result.Diagnostics));
            LawV2 law = result.Doc.Laws[0];
            Assert.Single(law.Ops);
            Assert.Equal("dataJson", law.Ops[0].CaptureName);
            Assert.Equal("llm.ctx_push", law.Ops[0].BrickName);
        }

        /// <summary>
        /// OA 边界——⇐ 端口映射 / ⇒ 发送
        /// </summary>
        [Fact]
        public void ParseBoundary_OaMappings()
        {
            string source = "§'P_UserMessage'\n§'P_ReplySent'\n§⇐ 'msg' → 'P_UserMessage'\n§⇒ 'reply' → 'reply_key'\n";

            ParseResultV2 result = Parse(source);

            Assert.True(result.Success, FormatDiags(result.Diagnostics));
            Assert.Equal(2, result.Doc.Boundaries.Count);
            BoundaryV2 input = result.Doc.Boundaries[0];
            Assert.Equal(BoundaryDirV2.In, input.Dir);
            Assert.Equal("msg", input.SignalName);
            Assert.Equal("P_UserMessage", input.MappedName);
            Assert.Equal(PropKindV2.Signal, result.Doc.FindProposition("P_UserMessage")!.Kind);
            BoundaryV2 output = result.Doc.Boundaries[1];
            Assert.Equal(BoundaryDirV2.Out, output.Dir);
            Assert.Equal("reply", output.SignalName);
            Assert.Equal("reply_key", output.MappedName);
        }

        /// <summary>
        /// Fire 签名载荷——⇐ 'P_Init'['sessionKey', 'prompt']
        /// </summary>
        [Fact]
        public void ParseBoundary_FireParams()
        {
            string source = "§'P_Init'\n§⇐ 'P_Init'['sessionKey', 'prompt']\n";

            ParseResultV2 result = Parse(source);

            Assert.True(result.Success, FormatDiags(result.Diagnostics));
            BoundaryV2 b = result.Doc.Boundaries[0];
            Assert.Equal(2, b.Params.Count);
            Assert.Equal("sessionKey", b.Params[0].Text);
            Assert.Equal("prompt", b.Params[1].Text);
        }

        /// <summary>
        /// 语法糖——SEQ 引用序列
        /// </summary>
        [Fact]
        public void ParseSugar_SeqRefs()
        {
            string source = "§'SEQ'['T_A' → 'T_B' → 'T_C']\n";

            ParseResultV2 result = Parse(source);

            Assert.True(result.Success, FormatDiags(result.Diagnostics));
            Assert.Single(result.Doc.Sugars);
            Assert.Equal("SEQ", result.Doc.Sugars[0].Name);
            Assert.Equal(3, result.Doc.Sugars[0].RefNames.Count);
            Assert.Equal("T_A", result.Doc.Sugars[0].RefNames[0]);
            Assert.Equal("T_C", result.Doc.Sugars[0].RefNames[2]);
        }

        /// <summary>
        /// 测量 ω=N——帧门控
        /// </summary>
        [Fact]
        public void ParseMeasure_FrameN()
        {
            string source = "§'M_Poll'[ω=3]: 'P_Ready' := 'data.box_is'['k', 1]\n";

            ParseResultV2 result = Parse(source);

            Assert.True(result.Success, FormatDiags(result.Diagnostics));
            Assert.Equal(3, result.Doc.Measures[0].Frame);
            Assert.Equal(PropKindV2.Condition, result.Doc.FindProposition("P_Ready")!.Kind);
        }

        /// <summary>
        /// 条件引用未声明命题——无写入源（验证层 E202 兜底），不隐式声明
        /// </summary>
        [Fact]
        public void ParseConditionRef_NoImplicitProp()
        {
            string source = "§'T_X'[τ=5]: 'P_Implicit' + 'llm.go'[] → 'S_X' = 'Done'\n§'S_X' = { 'Done' }\n";

            ParseResultV2 result = Parse(source);

            Assert.True(result.Success, FormatDiags(result.Diagnostics));
            // 条件引用不创建命题——无写入源（验证层报 E202）
            Assert.Null(result.Doc.FindProposition("P_Implicit"));
        }

        /// <summary>
        /// 错误用例——控制律缺 →（E101）
        /// </summary>
        [Fact]
        public void ParseLaw_MissingArrow_Error()
        {
            string source = "§'T_X'[τ=5]: 'P_A' + 'llm.go'[]\n";

            ParseResultV2 result = Parse(source);

            Assert.False(result.Success);
            Assert.Equal("E101", result.Diagnostics[0].Code);
        }

        /// <summary>
        /// 错误用例——结果侧混用（E101）
        /// </summary>
        [Fact]
        public void ParseLaw_MixedResults_Error()
        {
            string source = "§'P_Rec'\n§'S_X' = { 'Done' }\n§'T_X'[τ=5]: 'P_A' → 'S_X' = 'Done' | 'P_Rec'\n";

            ParseResultV2 result = Parse(source);

            Assert.False(result.Success);
            Assert.True(result.Diagnostics.Count == 1);
            Assert.Equal("E101", result.Diagnostics[0].Code);
        }

        /// <summary>
        /// 错误用例——嵌套绑定父状态机未声明（E101）
        /// </summary>
        [Fact]
        public void ParseBind_MissingParent_Error()
        {
            string source = "§'S_Active' ∈ 'S_Talk.Active'\n";

            ParseResultV2 result = Parse(source);

            Assert.False(result.Success);
            Assert.Equal("E101", result.Diagnostics[0].Code);
        }

        // ==================== 工具 ====================

        /// <summary>
        /// 词法 + 解析全链路
        /// </summary>
        /// <param name="source">.mau 源文本</param>
        /// <returns>解析结果</returns>
        private static ParseResultV2 Parse(string source)
        {
            LexResultV2 lex = MauLexerV2.Lex(source);
            if (!lex.Success)
            {
                ParseResultV2 fail = new ParseResultV2();
                fail.Diagnostics.AddRange(lex.Diagnostics);
                fail.Success = false;
                return fail;
            }
            return MauParserV2.Parse(lex);
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

using System;
using System.Collections.Generic;
using Mau.Translator;
using Xunit;

namespace Mau.Translator.Tests
{
    /// <summary>
    /// 词法层 v2 测试——MauLexerV2 七步流水线行为验证
    /// </summary>
    public class MauLexerV2Tests
    {
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

        /// <summary>
        /// TalkCat 综合语料——全类型段分类
        /// </summary>
        [Fact]
        public void LexTalkCatSections_ClassifiesAllKinds()
        {
            string source = "§'Mau' 2.0\n" +
                "§'Mau.Runtime' 2.0\n" +
                "§@'CH4.Contracts.ICat'\n" +
                "§$'sessionKey', 'prompt'\n" +
                "§\n" +
                "§'S_Talk' = { 'Idle', 'Building', 'Thinking', 'Streaming', 'ToolWait', 'Done', 'Failed', 'Stopped' }\n" +
                "§'S_Active' ∈ 'S_Talk.Active'\n" +
                "§'S_Active' = { 'Thinking', 'Streaming', 'ToolWait' }\n" +
                "§\n" +
                "§'P_Init', 'P_CmdArrived', 'P_StopCmd'\n" +
                "§\n" +
                "§'R_ReplySlot': 1\n" +
                "§\n" +
                "§⇐ 'P_Init'\n" +
                "§\n" +
                "§'T_Init'[τ=5]: 'P_Init' + 'llm.ctx_set_system'['sessionKey', 'prompt'] → 'S_Talk' = 'Idle' | 'S_Talk' = 'Failed'\n" +
                "§'M_Pump'[ω=1]: 'P_ChunkReady' := 'llm.read_chunk'['requestId']\n" +
                "§'T_Classify'[ω=1, τ=5]: 'P_ChunkReady' + 'llm.is_end'['requestId'] → 'S_Talk' = 'Streaming' | 'S_Talk' = 'Thinking'\n";

            LexResultV2 result = MauLexerV2.Lex(source);

            Assert.True(result.Success, FormatDiags(result.Diagnostics));
            Assert.Empty(result.Diagnostics);
            Assert.True(result.Sections.Count == 13, "段数应为 13，实际 " + result.Sections.Count.ToString());
            Assert.Equal(MauSectionKindV2.Version, result.Sections[0].Kind);
            Assert.Equal(MauSectionKindV2.Version, result.Sections[1].Kind);
            Assert.Equal(MauSectionKindV2.Interface, result.Sections[2].Kind);
            Assert.Equal(MauSectionKindV2.Inject, result.Sections[3].Kind);
            Assert.Equal(MauSectionKindV2.MachineDef, result.Sections[4].Kind);
            Assert.Equal(MauSectionKindV2.MachineBind, result.Sections[5].Kind);
            Assert.Equal(MauSectionKindV2.MachineDef, result.Sections[6].Kind);
            Assert.Equal(MauSectionKindV2.Propositions, result.Sections[7].Kind);
            Assert.Equal(MauSectionKindV2.Resource, result.Sections[8].Kind);
            Assert.Equal(MauSectionKindV2.BoundaryIn, result.Sections[9].Kind);
            Assert.Equal(MauSectionKindV2.Law, result.Sections[10].Kind);
            Assert.Equal(MauSectionKindV2.Measure, result.Sections[11].Kind);
            Assert.Equal(MauSectionKindV2.Law, result.Sections[12].Kind);
        }

        /// <summary>
        /// 命名表——'...' 按首次出现顺序编号（同名复用索引；参数容器内引用不入命名表）
        /// </summary>
        [Fact]
        public void LexTalkCat_NamesInOrder()
        {
            string source = "§'Mau' 2.0\n§'S_Talk' = { 'Idle', 'Building' }\n§'T_Init'[τ=5]: 'P_Init' + 'llm.go'['x'] → 'S_Talk' = 'Idle'";

            LexResultV2 result = MauLexerV2.Lex(source);

            Assert.True(result.Success, FormatDiags(result.Diagnostics));
            Assert.Equal(7, result.Names.Count);
            Assert.Equal("Mau", result.Names[0]);
            Assert.Equal("S_Talk", result.Names[1]);
            Assert.Equal("Idle", result.Names[2]);
            Assert.Equal("Building", result.Names[3]);
            Assert.Equal("T_Init", result.Names[4]);
            Assert.Equal("P_Init", result.Names[5]);
            Assert.Equal("llm.go", result.Names[6]);
            // 参数容器内引用——['x'] 由参数表承载（A1；A0=[τ=5] 属性容器）
            Assert.True(result.Params.Count == 2, "Params=" + result.Params.Count.ToString());
            Assert.Equal(MauParamKindV2.Ref, result.Params[1].Items![0].Kind);
            Assert.Equal("x", result.Params[1].Items![0].Text);
        }

        /// <summary>
        /// 字符串保护——多行 + 含 § 的字符串原样保留
        /// </summary>
        [Fact]
        public void LexStrings_ProtectMultilineAndSection()
        {
            string source = "§'T_Push'[τ=5]: 'P_Ready' + 'llm.ctx_push'['sessionKey', \"第一行\n第二行§含节符\"] → 'S_X' = 'Done'";

            LexResultV2 result = MauLexerV2.Lex(source);

            Assert.True(result.Success, FormatDiags(result.Diagnostics));
            Assert.Empty(result.Diagnostics);
            Assert.True(result.Sections.Count == 1);
            Assert.Equal(MauSectionKindV2.Law, result.Sections[0].Kind);
            // 字符串表——内容原样（含换行与 §）
            bool found = false;
            for (int i = 0; i < result.Params.Count; i++)
            {
                for (int j = 0; j < result.Params[i].Items!.Count; j++)
                {
                    MauParamV2 item = result.Params[i].Items![j];
                    if (item.Kind == MauParamKindV2.String && item.Text.Contains("§", StringComparison.Ordinal))
                    {
                        found = true;
                    }
                }
            }
            Assert.True(found, "字符串内容应原样保留（含 § 与换行）");
        }

        /// <summary>
        /// 注释丢弃——// 到段尾；空段跳过
        /// </summary>
        [Fact]
        public void LexComments_DroppedAndEmptySkipped()
        {
            string source = "§// 标题注释\n§'P_A'\n§\n§// 另一注释 'P_B'\n§'P_C', 'P_D'";

            LexResultV2 result = MauLexerV2.Lex(source);

            Assert.True(result.Success);
            Assert.True(result.Sections.Count == 2, "注释段与空段应跳过，实际 " + result.Sections.Count.ToString());
            Assert.Equal(MauSectionKindV2.Propositions, result.Sections[0].Kind);
            Assert.Equal(MauSectionKindV2.Propositions, result.Sections[1].Kind);
            Assert.Equal("P_A", result.Names[0]);
            Assert.Equal("P_C", result.Names[1]);
            Assert.Equal("P_D", result.Names[2]);
        }

        /// <summary>
        /// 命名白名单——中文命名拒绝（E001）
        /// </summary>
        [Fact]
        public void LexInvalidName_ChineseRejected()
        {
            string source = "§'P_测试'";

            LexResultV2 result = MauLexerV2.Lex(source);

            Assert.False(result.Success);
            Assert.True(result.Diagnostics.Count == 1);
            Assert.Equal("E001", result.Diagnostics[0].Code);
        }

        /// <summary>
        /// 参数引用白名单——非法字符拒绝（E002）
        /// </summary>
        [Fact]
        public void LexInvalidRef_NumberPrefixRejected()
        {
            string source = "§'T_X'[τ=5]: 'P_A' + 'llm.go'['bad@name'] → 'S_X' = 'Done'";

            LexResultV2 result = MauLexerV2.Lex(source);

            Assert.False(result.Success);
            Assert.True(result.Diagnostics.Count == 1);
            Assert.Equal("E002", result.Diagnostics[0].Code);
        }

        /// <summary>
        /// 符号集外字符——旧语义词残留拒绝（E003）
        /// </summary>
        [Fact]
        public void LexInvalidSymbol_ChineseWordRejected()
        {
            string source = "§'T_X': 前置 'P_A' → 'S_X' = 'Done'";

            LexResultV2 result = MauLexerV2.Lex(source);

            Assert.False(result.Success);
            bool hasE003 = false;
            for (int i = 0; i < result.Diagnostics.Count; i++)
            {
                if (result.Diagnostics[i].Code == "E003")
                {
                    hasE003 = true;
                }
            }
            Assert.True(hasE003, "旧语义词（前置）应报 E003");
        }

        /// <summary>
        /// 字符串未闭合（E004）
        /// </summary>
        [Fact]
        public void LexUnclosedString_E004()
        {
            string source = "§'T_X'[τ=5]: 'P_A' + 'llm.go'[\"abc] → 'S_X' = 'Done'";

            LexResultV2 result = MauLexerV2.Lex(source);

            Assert.False(result.Success);
            Assert.Equal("E004", result.Diagnostics[0].Code);
        }

        /// <summary>
        /// 参数种类——引用/数值/字符串/数组/属性全识别
        /// </summary>
        [Fact]
        public void LexParamElements_AllKinds()
        {
            string source = "§'T_Route'[τ=5, !]: 'P_Cmd' + 'cmd.match'['activeKey', \"stop\", 300, [\"a\",\"b\"]] → 'S_Ui' = 'Open' | 'S_Ui' = 'Idle'";

            LexResultV2 result = MauLexerV2.Lex(source);

            Assert.True(result.Success, FormatDiags(result.Diagnostics));
            Assert.Empty(result.Diagnostics);
            Assert.True(result.Sections.Count == 1);
            Assert.Equal(MauSectionKindV2.Law, result.Sections[0].Kind);

            // 参数表：A0=[τ=5, !] 属性容器；A1=['activeKey', "stop", 300, ["a","b"]] 参数容器
            Assert.True(result.Params.Count == 2, "Params=" + result.Params.Count.ToString());
            MauParamV2 attrs = result.Params[0];
            Assert.Equal(MauParamKindV2.AttrTau, attrs.Items![0].Kind);
            Assert.Equal("5", attrs.Items[0].Text);
            Assert.Equal(MauParamKindV2.AttrLog, attrs.Items[1].Kind);

            MauParamV2 args = result.Params[1];
            Assert.Equal(MauParamKindV2.Ref, args.Items![0].Kind);
            Assert.Equal("activeKey", args.Items[0].Text);
            Assert.Equal(MauParamKindV2.String, args.Items[1].Kind);
            Assert.Equal("stop", args.Items[1].Text);
            Assert.Equal(MauParamKindV2.Number, args.Items[2].Kind);
            Assert.Equal("300", args.Items[2].Text);
            Assert.Equal(MauParamKindV2.Array, args.Items[3].Kind);
            Assert.Equal(MauParamKindV2.String, args.Items[3].Items![0].Kind);
            Assert.Equal("a", args.Items[3].Items![0].Text);
            Assert.Equal("b", args.Items[3].Items![1].Text);
        }

        /// <summary>
        /// 测量段——ω 属性 + := 采样
        /// </summary>
        [Fact]
        public void LexMeasure_OmegaAttrAndSample()
        {
            string source = "§'M_Pump'[ω=1]: 'P_Ready' := 'llm.read_chunk'['requestId']";

            LexResultV2 result = MauLexerV2.Lex(source);

            Assert.True(result.Success);
            Assert.Equal(MauSectionKindV2.Measure, result.Sections[0].Kind);
            Assert.Equal(MauParamKindV2.AttrOmega, result.Params[0].Items![0].Kind);
            Assert.Equal("1", result.Params[0].Items![0].Text);
        }

        /// <summary>
        /// 语法糖段——SEQ 含 →（糖引用在参数表，不进命名表）
        /// </summary>
        [Fact]
        public void LexSugar_SeqDetected()
        {
            string source = "§'SEQ'['T_A' → 'T_B' → 'T_C']";

            LexResultV2 result = MauLexerV2.Lex(source);

            Assert.True(result.Success);
            Assert.Equal(MauSectionKindV2.Sugar, result.Sections[0].Kind);
            Assert.Equal("SEQ", result.Names[0]);
            Assert.True(result.Names.Count == 1, "糖参数引用应在参数表，命名表只有 SEQ");
            Assert.True(result.Params.Count == 1);
            Assert.Equal(MauParamKindV2.Ref, result.Params[0].Items![0].Kind);
            Assert.Equal("T_A", result.Params[0].Items![0].Text);
            Assert.Equal("T_B", result.Params[0].Items![1].Text);
            Assert.Equal("T_C", result.Params[0].Items![2].Text);
        }

        /// <summary>
        /// 边界输出段——⇒
        /// </summary>
        [Fact]
        public void LexBoundaryOut_Detected()
        {
            string source = "§⇒ 'reply' → 'reply_key'";

            LexResultV2 result = MauLexerV2.Lex(source);

            Assert.True(result.Success);
            Assert.Equal(MauSectionKindV2.BoundaryOut, result.Sections[0].Kind);
        }

        /// <summary>
        /// 多状态机并行 + 嵌套绑定
        /// </summary>
        [Fact]
        public void LexMachines_MultiAndBind()
        {
            string source = "§'S_Talk' = { 'Idle', 'Active' }\n§'S_Tool' = { 'Wait', 'Run' }\n§'S_Active' ∈ 'S_Talk.Active'\n§'S_Active' = { 'Thinking', 'Streaming' }";

            LexResultV2 result = MauLexerV2.Lex(source);

            Assert.True(result.Success);
            Assert.True(result.Sections.Count == 4);
            Assert.Equal(MauSectionKindV2.MachineDef, result.Sections[0].Kind);
            Assert.Equal(MauSectionKindV2.MachineDef, result.Sections[1].Kind);
            Assert.Equal(MauSectionKindV2.MachineBind, result.Sections[2].Kind);
            Assert.Equal(MauSectionKindV2.MachineDef, result.Sections[3].Kind);
            // 嵌套绑定引用——'S_Talk.Active' 点号命名
            Assert.Contains("S_Talk.Active", result.Names);
        }

        /// <summary>
        /// 未配对命名——缺闭合单引号（E001）
        /// </summary>
        [Fact]
        public void LexUnclosedName_E001()
        {
            string source = "§'P_A";

            LexResultV2 result = MauLexerV2.Lex(source);

            Assert.False(result.Success);
            Assert.Equal("E001", result.Diagnostics[0].Code);
        }
    }
}

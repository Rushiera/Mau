using Mau.Contracts;
using Mau.Translator;
using Xunit;

namespace Mau.Translator.Tests
{
    /// <summary>
    /// 解析器测试——MauParser 行为验证
    /// </summary>
    public class MauParserTests
    {
        /// <summary>
        /// 解析 file_convert 源文本——3 命题 + 1 变迁
        /// </summary>
        [Fact]
        public void ParseFileConvertSucceeds()
        {
            string source = FileConvertSource();
            ParseResult result = MauParser.Parse(source);

            Assert.Empty(result.Diagnostics);
            Assert.Equal("0.1", result.Document.Version);
            Assert.Equal("Mau.Runtime/v0.1", result.Document.BaseName);
            Assert.True(result.Document.Propositions.Count == 3);
            Assert.True(result.Document.Transitions.Count == 1);

            IrProposition? input = result.Document.FindProposition("P_Input");
            Assert.NotNull(input);
            Assert.Equal(PropositionKind.Signal, input!.Kind);

            IrTransition? convert = result.Document.FindTransition("T_Convert");
            Assert.NotNull(convert);
            Assert.Equal("file.convert", convert!.BrickName);
            Assert.Equal("300", convert.TimeoutFrames.ToString());
            Assert.Equal("Total", convert.TimeoutMode);
            Assert.Equal(2, convert.Params.Count);
            Assert.Equal("input", convert.Params[0].PortName);
            Assert.Equal("output", convert.Params[1].PortName);
            Assert.Equal("P_Done", convert.PostOk[0]);
            Assert.Equal("P_Failed", convert.PostError[0]);
        }

        /// <summary>
        /// 缺文件头——E101
        /// </summary>
        [Fact]
        public void ParseMissingHeaderReportsE101()
        {
            string source = "命题:\n  P_X 信号\n";
            ParseResult result = MauParser.Parse(source);

            Assert.Single(result.Diagnostics);
            Assert.Equal("E101", result.Diagnostics[0].Code);
        }

        /// <summary>
        /// 未知命题类型——E105
        /// </summary>
        [Fact]
        public void ParseUnknownTypeReportsE105()
        {
            string source = "Mau 0.1\n命题:\n  P_X 飞猪\n";
            ParseResult result = MauParser.Parse(source);

            Assert.Single(result.Diagnostics);
            Assert.Equal("E105", result.Diagnostics[0].Code);
        }

        /// <summary>
        /// 时限格式非法——E110
        /// </summary>
        [Fact]
        public void ParseBadTimeoutReportsE110()
        {
            string source = "Mau 0.1\n变迁 T_X:\n  时限: 负三帧\n";
            ParseResult result = MauParser.Parse(source);

            Assert.Single(result.Diagnostics);
            Assert.Equal("E110", result.Diagnostics[0].Code);
        }

        /// <summary>
        /// file_convert 标准源文本
        /// </summary>
        /// <returns>源文本</returns>
        private static string FileConvertSource()
        {
            return "Mau 0.1\n" +
                "基座: Mau.Runtime/v0.1\n" +
                "\n" +
                "命题:\n" +
                "  P_Input   信号\n" +
                "  P_Done    事实\n" +
                "  P_Failed  事实\n" +
                "\n" +
                "变迁 T_Convert:\n" +
                "  前置: P_Input\n" +
                "  动作: file.convert\n" +
                "  参数: input, output\n" +
                "  时限: 300帧\n" +
                "  后置: P_Done / P_Failed\n";
        }
    }
}

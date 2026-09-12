#pragma warning disable xUnit1051 // 探针为纯内存同步调用（ParseText/Formatter 毫秒级）——取消令牌无实际收益
using System;
using System.IO;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Options;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace Mau.Development.Tests
{
    /// <summary>
    /// A22 实验探针——写侧规整三路对照（NormalizeWhitespace / 节点级 Formatter / 文档级 Formatter）。
    /// 断言：老路径复现 token 形态漂移（null! / XML doc 属性空格）；Formatter 两路保真。
    /// 诊断全文落到 %TEMP%/a22_probe.txt（实验用，非交付物）。
    /// </summary>
    public class WriteSideProbeTests
    {
        /// <summary>
        /// 探针样本——含 null!、可空标注、XML doc name 属性、集合初始化
        /// </summary>
        private const string Sample = "using System;\r\nusing System.Collections.Generic;\r\n\r\nnamespace Probe\r\n{\r\n    public sealed class Probe\r\n    {\r\n        /// <summary>\r\n        /// 探针方法\r\n        /// </summary>\r\n        /// <param name=\"x\">输入</param>\r\n        /// <returns>长度</returns>\r\n        public int Run(string x)\r\n        {\r\n            string s = null!;\r\n            int n = x!.Length;\r\n            List<int> list = new List<int>();\r\n            return s.Length + n + list.Count;\r\n        }\r\n    }\r\n}\r\n";

        /// <summary>
        /// 三路对照——跑完把全文与命中表写入临时诊断文件
        /// </summary>
        [Fact]
        public void ThreeWayProbe()
        {
            SyntaxTree tree = CSharpSyntaxTree.ParseText(Sample);
            ClassDeclarationSyntax? cls = null;
            foreach (SyntaxNode node in tree.GetRoot().DescendantNodes())
            {
                ClassDeclarationSyntax? candidate = node as ClassDeclarationSyntax;
                if (candidate != null)
                {
                    cls = candidate;
                    break;
                }
            }
            if (cls == null)
            {
                throw new InvalidOperationException("探针样本解析失败");
            }

            string a = cls.NormalizeWhitespace("    ", "\r\n").ToFullString();
            string b = NodeFormat(cls);
            string c = DocumentFormat(Sample);

            StringBuilder diag = new StringBuilder();
            diag.AppendLine("=== A: NormalizeWhitespace ===");
            diag.AppendLine(a);
            diag.AppendLine("=== B: Formatter(node) ===");
            diag.AppendLine(b);
            diag.AppendLine("=== C: Formatter(document) ===");
            diag.AppendLine(c);
            diag.AppendLine("=== hits ===");
            diag.AppendLine("A null!=" + Has(a, "null!") + " | A null !=" + Has(a, "null !") + " | A name=\"=" + Has(a, "name=\"") + " | A name = \"=" + Has(a, "name = \""));
            diag.AppendLine("B null!=" + Has(b, "null!") + " | B null !=" + Has(b, "null !") + " | B name=\"=" + Has(b, "name=\"") + " | B name = \"=" + Has(b, "name = \""));
            diag.AppendLine("C null!=" + Has(c, "null!") + " | C null !=" + Has(c, "null !") + " | C name=\"=" + Has(c, "name=\"") + " | C name = \"=" + Has(c, "name = \""));
            diag.AppendLine("B first-indent=" + FirstIndent(b) + " | B lines=" + CountLines(b) + " | src lines=" + CountLines(Sample));
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "a22_probe.txt"), diag.ToString(), new UTF8Encoding(false));

            // 事实面（A 路 = 老写侧复现漂移）
            Assert.True(Has(a, "null !"), "老路径未复现 null! 漂移");
            Assert.True(Has(a, "name = \""), "老路径未复现 XML doc 属性空格漂移");

            // 方案面（B 路 = 候选写侧保真）
            Assert.True(Has(b, "null!"), "节点级 Formatter 丢失 null!");
            Assert.False(Has(b, "null !"), "节点级 Formatter 引入 null !");
            Assert.True(Has(b, "name=\""), "节点级 Formatter 丢失 name=\"x\" 形态");
            Assert.False(Has(b, "name = \""), "节点级 Formatter 引入 name = \"x\"");
        }

        /// <summary>
        /// 节点级 Formatter——候选写侧规整路径（与 A6 同源配置）
        /// </summary>
        /// <param name="node">目标节点</param>
        /// <returns>规整后文本</returns>
        private static string NodeFormat(SyntaxNode node)
        {
            using (AdhocWorkspace workspace = new AdhocWorkspace())
            {
                OptionSet options = FormatOptions(workspace);
                SyntaxNode formatted = Formatter.Format(node, workspace, options);
                return formatted.ToFullString();
            }
        }

        /// <summary>
        /// 文档级 Formatter——A6 cs.format 现行路径
        /// </summary>
        /// <param name="text">源码文本</param>
        /// <returns>规整后文本</returns>
        private static string DocumentFormat(string text)
        {
            using (AdhocWorkspace workspace = new AdhocWorkspace())
            {
                OptionSet options = FormatOptions(workspace);
                SourceText sourceText = SourceText.From(text, Encoding.UTF8);
                Project project = workspace.AddProject("probe", LanguageNames.CSharp);
                Document document = project.AddDocument("Probe.cs", sourceText);
                Document formatted = Formatter.FormatAsync(document, options).GetAwaiter().GetResult();
                return formatted.GetTextAsync().GetAwaiter().GetResult().ToString();
            }
        }

        /// <summary>
        /// 共用格式选项——4 空格 / 显式 CRLF（与桥 FormatText 同配置）
        /// </summary>
        /// <param name="workspace">工作区</param>
        /// <returns>选项集</returns>
        private static OptionSet FormatOptions(AdhocWorkspace workspace)
        {
            return workspace.Options
                .WithChangedOption(FormattingOptions.UseTabs, LanguageNames.CSharp, false)
                .WithChangedOption(FormattingOptions.TabSize, LanguageNames.CSharp, 4)
                .WithChangedOption(FormattingOptions.IndentationSize, LanguageNames.CSharp, 4)
                .WithChangedOption(FormattingOptions.NewLine, LanguageNames.CSharp, "\r\n");
        }

        /// <summary>
        /// 子串命中
        /// </summary>
        /// <param name="text">文本</param>
        /// <param name="needle">子串</param>
        /// <returns>命中</returns>
        private static bool Has(string text, string needle)
        {
            return text.IndexOf(needle, StringComparison.Ordinal) >= 0;
        }

        /// <summary>
        /// 首个非空行前导空格数
        /// </summary>
        /// <param name="text">文本</param>
        /// <returns>空格数</returns>
        private static int FirstIndent(string text)
        {
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i = i + 1)
            {
                if (lines[i].Trim().Length == 0)
                {
                    continue;
                }
                int index = 0;
                while (index < lines[i].Length && lines[i][index] == ' ')
                {
                    index = index + 1;
                }
                return index;
            }
            return -1;
        }

        /// <summary>
        /// 行数
        /// </summary>
        /// <param name="text">文本</param>
        /// <returns>行数</returns>
        private static int CountLines(string text)
        {
            return text.Replace("\r\n", "\n").Split('\n').Length;
        }
    }
}

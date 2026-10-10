using System;
using System.IO;
using System.Text;
using Mau.Development;
using Mau.Runtime;
using Xunit;

namespace Mau.Development.Tests
{
    /// <summary>
    /// 入口类型标签 / 项目计数 + cs-format 行尾策略（项目属性 eol）测试。
    /// </summary>
    public class BridgeScopeEolTests : IDisposable
    {
        /// <summary>
        /// 临时工程根（受控根）
        /// </summary>
        private readonly string _root;

        /// <summary>
        /// 创建夹具——建临时受控根目录
        /// </summary>
        public BridgeScopeEolTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "mau_scope_eol_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        /// <summary>
        /// 清理——删除临时根
        /// </summary>
        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }

        /// <summary>
        /// 入口类型标签——解决方案计项目数、标签 solution；单项目入口标签 single。
        /// </summary>
        [Fact]
        public void SolutionProjectCountAndScopeLabels()
        {
            string sln = Path.Combine(_root, "Probe.sln");
            string nl = "\r\n";
            string text = "Microsoft Visual Studio Solution File, Format Version 12.00" + nl
                + "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"A\", \"A\\A.csproj\", \"{11111111-1111-1111-1111-111111111111}\"" + nl
                + "EndProject" + nl
                + "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"B\", \"B\\B.csproj\", \"{22222222-2222-2222-2222-222222222222}\"" + nl
                + "EndProject" + nl;
            File.WriteAllText(sln, text, new UTF8Encoding(false));
            Assert.Equal(2, MauRoslynBridge.CountSolutionProjects(sln));
            Assert.Equal("solution", MauRoslynBridge.BuildScopeLabel(sln));
            Assert.Equal("single", MauRoslynBridge.BuildScopeLabel(Path.Combine(_root, "A.csproj")));
        }

        /// <summary>
        /// 项目行尾探测——.editorconfig / .gitattributes 命中与未命中三态；段适用面判据。
        /// </summary>
        [Fact]
        public void ProjectEolFromEditorConfigAndGitAttributes()
        {
            string sub = Path.Combine(_root, "src");
            Directory.CreateDirectory(sub);
            string csFile = Path.Combine(sub, "Probe.cs");
            File.WriteAllText(csFile, "class Probe { }" + "\r\n", new UTF8Encoding(false));
            string source;
            Assert.Equal("", MauRoslynBridge.DetectProjectNewline(csFile, out source));

            string editorPath = Path.Combine(_root, ".editorconfig");
            File.WriteAllText(editorPath, "root = true" + "\n" + "[*]" + "\n" + "end_of_line = crlf" + "\n" + "[*.cs]" + "\n" + "end_of_line = lf" + "\n", new UTF8Encoding(false));
            Assert.Equal("lf", MauRoslynBridge.DetectProjectNewline(csFile, out source));
            Assert.Equal(editorPath, source);

            File.Delete(editorPath);
            string attrPath = Path.Combine(_root, ".gitattributes");
            File.WriteAllText(attrPath, "*.cs text eol=crlf" + "\n", new UTF8Encoding(false));
            Assert.Equal("crlf", MauRoslynBridge.DetectProjectNewline(csFile, out source));
            Assert.Equal(attrPath, source);

            File.Delete(attrPath);
            Assert.Equal("", MauRoslynBridge.DetectProjectNewline(csFile, out source));

            Assert.True(MauRoslynBridge.SectionAppliesToCs("*"));
            Assert.True(MauRoslynBridge.SectionAppliesToCs("*.cs"));
            Assert.True(MauRoslynBridge.SectionAppliesToCs("{*.cs,*.csx}"));
            Assert.False(MauRoslynBridge.SectionAppliesToCs("*.csproj"));
        }

        /// <summary>
        /// cs-format apply——项目属性 eol=lf 生效：回执声明策略且落盘无 CR。
        /// </summary>
        [Fact]
        public void FormatApplyFollowsProjectEolLf()
        {
            File.WriteAllText(Path.Combine(_root, ".editorconfig"), "root = true" + "\n" + "[*.cs]" + "\n" + "end_of_line = lf" + "\n", new UTF8Encoding(false));
            string csFile = Path.Combine(_root, "Eol.cs");
            string nl = "\r\n";
            string text = "namespace P" + nl + "{" + nl
                + "    /// <summary>" + nl + "    /// 探针类" + nl + "    /// </summary>" + nl
                + "    public class Eol" + nl + "    {" + nl + "    }" + nl + "}" + nl;
            File.WriteAllText(csFile, text, new UTF8Encoding(false));
            MauRoslynBridge bridge = NewBridge();
            string args = "{\"path\":\"" + Escape(csFile) + "\",\"mode\":\"apply\"}";
            string result = "";
            bridge.Invoke("format", args, out result);
            Assert.Contains("\"eol\":\"lf\"", result);
            Assert.Contains("项目属性 eol=lf", result);
            string after = File.ReadAllText(csFile);
            Assert.False(after.Contains("\r"), "落盘仍有 CR（未按项目属性归一）");
        }

        /// <summary>
        /// cs-format——无项目属性声明时回执声明「按文件现状多数归一」（eol=file）。
        /// </summary>
        [Fact]
        public void FormatReportsFileStrategyWhenNoProjectEol()
        {
            string csFile = Path.Combine(_root, "Plain.cs");
            File.WriteAllText(csFile, "namespace P" + "\n" + "{" + "\n" + "public class Eol" + "\n" + "{" + "\n" + "}" + "\n" + "}" + "\n", new UTF8Encoding(false));
            MauRoslynBridge bridge = NewBridge();
            string args = "{\"path\":\"" + Escape(csFile) + "\",\"mode\":\"check\"}";
            string result = "";
            bridge.Invoke("format", args, out result);
            Assert.Contains("\"eol\":\"file\"", result);
            Assert.Contains("按各文件现状多数归一", result);
        }

        /// <summary>
        /// 建桥——受控根 = 临时根
        /// </summary>
        /// <returns>桥实例</returns>
        private MauRoslynBridge NewBridge()
        {
            WorkspaceConfig.RootEntry entry = new WorkspaceConfig.RootEntry();
            entry.Id = "tmp";
            entry.Path = _root;
            entry.Writable = true;
            return new MauRoslynBridge(new WorkspaceConfig.RootEntry[] { entry });
        }

        /// <summary>
        /// JSON 字符串转义（路径参数）
        /// </summary>
        /// <param name="text">原文</param>
        /// <returns>转义文本</returns>
        private static string Escape(string text)
        {
            return text.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}

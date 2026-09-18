using System;
using System.IO;
using System.Text;
using Mau.Development;
using Mau.Runtime;
using Xunit;

namespace Mau.Development.Tests
{
    /// <summary>
    /// A22 端到端实验——cs-* 写侧落盘保真（token 形态 / BOM / 未触碰区字节稳定 / 与 cs.format 基线同源）。
    /// 夹具：临时工程（Nullable enable + BOM + CRLF 探针文件）。
    /// </summary>
    public class WriteSideEndToEndTests : IDisposable
    {
        /// <summary>
        /// 临时工程根（受控根）
        /// </summary>
        private readonly string _root;

        /// <summary>
        /// 探针源文件（带 BOM / CRLF）
        /// </summary>
        private readonly string _target;

        /// <summary>
        /// 探针工程文件
        /// </summary>
        private readonly string _projectPath;

        /// <summary>
        /// 编码桥实例（受控根 = 临时根）
        /// </summary>
        private readonly MauRoslynBridge _bridge;

        /// <summary>
        /// 创建夹具——建临时工程 + 带 BOM 探针文件 + 建桥
        /// </summary>
        public WriteSideEndToEndTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "mau_a22_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            _projectPath = Path.Combine(_root, "A22.csproj");
            _target = Path.Combine(_root, "A22Probe.cs");
            File.WriteAllText(_projectPath, ProjectText(), new UTF8Encoding(false));
            File.WriteAllText(_target, ProbeText(), new UTF8Encoding(true));
            WorkspaceConfig.RootEntry entry = new WorkspaceConfig.RootEntry();
            entry.Id = "tmp";
            entry.Path = _root;
            entry.Writable = true;
            _bridge = new MauRoslynBridge(new WorkspaceConfig.RootEntry[] { entry });
        }

        /// <summary>
        /// 清理——删临时根
        /// </summary>
        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_root))
                {
                    Directory.Delete(_root, true);
                }
            }
            catch (IOException)
            {
            }
        }

        /// <summary>
        /// patch——body 内 null! 形态保持 + 整文件 BOM 保持 + 未触碰成员字节稳定
        /// </summary>
        [Fact]
        public void PatchKeepsTokenShapeAndBom()
        {
            string keepBefore = KeepBlock();
            string result = Call("patch", "\"class\":\"A22Probe\",\"method\":\"Target\",\"body\":\"{\\n    string s = null!;\\n    return s.Length;\\n}\"");
            Assert.StartsWith("{\"ok\":true", result);
            string after = File.ReadAllText(_target);
            Assert.True(HasBom(_target), "patch 落盘剥离了文件 BOM");
            Assert.Contains("null!", after);
            Assert.DoesNotContain("null !", after);
            Assert.Contains(keepBefore, after);
        }

        /// <summary>
        /// member insert——新成员 XML doc 属性形态保持（name="x" 不被写成 name = "x"）+ BOM 保持
        /// </summary>
        [Fact]
        public void MemberInsertKeepsDocAttributeShape()
        {
            string code = "/// <summary>新成员</summary>\\n/// <param name=\\\"x\\\">输入</param>\\n/// <returns>长度</returns>\\npublic int Added(string x)\\n{\\nreturn x.Length;\\n}";
            string result = Call("member", "\"class\":\"A22Probe\",\"op\":\"insert\",\"position\":\"end\",\"code\":\"" + code + "\"");
            Assert.StartsWith("{\"ok\":true", result);
            string after = File.ReadAllText(_target);
            Assert.True(HasBom(_target), "member insert 落盘剥离了文件 BOM");
            Assert.Contains("name=\"x\"", after);
            Assert.DoesNotContain("name = \"x\"", after);
        }

        /// <summary>
        /// patch 后跑 cs.format 干跑——写侧落盘与全仓 Formatter 基线同源（零差异）
        /// </summary>
        [Fact]
        public void PatchResultMatchesFormatBaseline()
        {
            string patch = Call("patch", "\"class\":\"A22Probe\",\"method\":\"Target\",\"body\":\"{\\n    string s = null!;\\n    return s.Length;\\n}\"");
            Assert.StartsWith("{\"ok\":true", patch);
            string check = "{\"path\":\"" + Escape(_target) + "\",\"mode\":\"check\"}";
            string result = "";
            _bridge.Invoke("format", check, out result);
            Assert.DoesNotContain("A22Probe.cs", result);
        }

        /// <summary>
        /// 调用桥工具——path 自动补
        /// </summary>
        /// <param name="tool">工具名</param>
        /// <param name="body">参数字段（不含 path）</param>
        /// <returns>结果文本</returns>
        private string Call(string tool, string body)
        {
            string args = "{\"path\":\"" + Escape(_projectPath) + "\"," + body + "}";
            string result = "";
            _bridge.Invoke(tool, args, out result);
            return result;
        }

        /// <summary>
        /// 未触碰成员（Keep）的原文块——字节稳定对照
        /// </summary>
        /// <returns>文本块</returns>
        private static string KeepBlock()
        {
            return "        public string Keep(string x)\r\n        {\r\n            return x + \"-keep\";\r\n        }\r\n";
        }

        /// <summary>
        /// JSON 路径转义
        /// </summary>
        /// <param name="path">路径</param>
        /// <returns>转义文本</returns>
        private static string Escape(string path)
        {
            return path.Replace("\\", "\\\\");
        }

        /// <summary>
        /// BOM 探测
        /// </summary>
        /// <param name="path">文件路径</param>
        /// <returns>有 BOM</returns>
        private static bool HasBom(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            return bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        }

        /// <summary>
        /// 探针工程文件文本
        /// </summary>
        /// <returns>csproj 文本</returns>
        private static string ProjectText()
        {
            return "<Project Sdk=\"Microsoft.NET.Sdk\">\r\n  <PropertyGroup>\r\n    <TargetFramework>net8.0</TargetFramework>\r\n    <Nullable>enable</Nullable>\r\n    <ImplicitUsings>disable</ImplicitUsings>\r\n    <OutputType>Library</OutputType>\r\n  </PropertyGroup>\r\n</Project>\r\n";
        }

        /// <summary>
        /// 探针源文件文本（CRLF——落盘按 UTF8Encoding(true) 写 BOM）
        /// </summary>
        /// <returns>源码文本</returns>
        private static string ProbeText()
        {
            return "namespace A22\r\n{\r\n    /// <summary>\r\n    /// 探针\r\n    /// </summary>\r\n    public sealed class A22Probe\r\n    {\r\n        /// <summary>\r\n        /// 目标方法\r\n        /// </summary>\r\n        /// <param name=\"x\">输入</param>\r\n        /// <returns>长度</returns>\r\n        public int Target(string x)\r\n        {\r\n            string s = x!;\r\n            return s.Length;\r\n        }\r\n\r\n        /// <summary>\r\n        /// 保留方法\r\n        /// </summary>\r\n        /// <param name=\"x\">输入</param>\r\n        /// <returns>标记</returns>\r\n        public string Keep(string x)\r\n        {\r\n            return x + \"-keep\";\r\n        }\r\n    }\r\n}\r\n";
        }
    }
}

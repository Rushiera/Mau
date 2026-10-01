using System;
using System.IO;
using System.Text;
using Mau.Development;
using Mau.Runtime;
using Xunit;

namespace Mau.Development.Tests
{
    /// <summary>
    /// cs-patch 锚点覆盖面测试——构造函数支持（".ctor" / 类名两种锚点）与不支持类型（属性访问器）的定向提示。
    /// 口径：patch 锚点与成员级（read / member / comment）一致；不支持的类型必须报「不在支持面」而非「名字不存在」。
    /// </summary>
    public class BridgePatchCtorTests : IDisposable
    {
        /// <summary>
        /// 临时工程根（受控根）
        /// </summary>
        private readonly string _root;

        /// <summary>
        /// 探针源文件路径
        /// </summary>
        private readonly string _sourcePath;

        /// <summary>
        /// 探针工程文件路径
        /// </summary>
        private readonly string _projectPath;

        /// <summary>
        /// 编码桥实例（受控根 = 临时根）
        /// </summary>
        private readonly MauRoslynBridge _bridge;

        /// <summary>
        /// 创建夹具——写临时工程 + 建桥
        /// </summary>
        public BridgePatchCtorTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "mau_bridge_ctor_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            _projectPath = Path.Combine(_root, "CtorProbe.csproj");
            _sourcePath = Path.Combine(_root, "Probe.cs");
            File.WriteAllText(_projectPath, ProjectText(), new UTF8Encoding(false));
            File.WriteAllText(_sourcePath, SourceText(), new UTF8Encoding(false));
            WorkspaceConfig.RootEntry entry = new WorkspaceConfig.RootEntry();
            entry.Id = "tmp";
            entry.Path = _root;
            entry.Writable = true;
            _bridge = new MauRoslynBridge(new WorkspaceConfig.RootEntry[] { entry });
        }

        /// <summary>
        /// 释放夹具——尽力删除临时根（工作区可能持有句柄）
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
            catch (Exception ex)
            {
                // 测试夹具清理——工作区可能持有句柄，尽力删除
                Console.WriteLine("[测试清理] 工作区目录删除失败: " + ex.Message);
            }
        }

        /// <summary>
        /// patch——".ctor" 锚点改构造函数体（跨工具锚点口径统一）
        /// </summary>
        [Fact]
        public void PatchConstructorByDotCtorAnchor()
        {
            string result = InvokePatch("CtorProbe", ".ctor", CtorBody());
            Assert.StartsWith("{\"ok\":true", result);
            string text = File.ReadAllText(_sourcePath);
            Assert.Contains("public CtorProbe()\r\n        {\r\n            _value = 2;\r\n        }\r\n", text);
            Assert.False(text.Contains("_value = 1;"), "旧构造体残留");
        }

        /// <summary>
        /// patch——类名锚点同样命中构造函数（与 cs-read / cs-member 同口径）
        /// </summary>
        [Fact]
        public void PatchConstructorByClassNameAnchor()
        {
            string result = InvokePatch("CtorProbe", "CtorProbe", CtorBody());
            Assert.StartsWith("{\"ok\":true", result);
            Assert.Contains("_value = 2;", File.ReadAllText(_sourcePath));
            Assert.False(File.ReadAllText(_sourcePath).Contains("_value = 1;"), "旧构造体残留");
        }

        /// <summary>
        /// patch——多构造函数歧义：报 AMBIGUOUS 并列出 ".ctor(...)" 候选签名，且不落盘
        /// </summary>
        [Fact]
        public void PatchAmbiguousConstructorsReportSignatures()
        {
            string result = InvokePatch("MultiCtorProbe", ".ctor", CtorBody());
            Assert.StartsWith("ERR|AMBIGUOUS", result);
            Assert.Contains(".ctor()", result);
            Assert.Contains(".ctor(int value)", result);
            Assert.False(File.ReadAllText(_sourcePath).Contains("_value = 2;"), "歧义时不得落盘");
        }

        /// <summary>
        /// patch——属性锚点：报 METHOD_NOT_FOUND 且提示「不在 cs-patch 支持面」（区分名字错与类型不支持）
        /// </summary>
        [Fact]
        public void PatchPropertyAnchorReportsUnsupportedHint()
        {
            string result = InvokePatch("PropProbe", "Title", CtorBody());
            Assert.StartsWith("ERR|METHOD_NOT_FOUND", result);
            Assert.Contains("不在 cs-patch 支持面", result);
        }

        /// <summary>
        /// patch——属性访问器名（get_X）命中同一提示（访问器不是独立成员节点）
        /// </summary>
        [Fact]
        public void PatchPropertyAccessorNameReportsUnsupportedHint()
        {
            string result = InvokePatch("PropProbe", "get_Title", CtorBody());
            Assert.StartsWith("ERR|METHOD_NOT_FOUND", result);
            Assert.Contains("不在 cs-patch 支持面", result);
        }

        /// <summary>
        /// patch——表达式体构造函数：报 ARROW_BODY（块体先行展开），措辞覆盖构造函数
        /// </summary>
        [Fact]
        public void PatchArrowBodyConstructorReportsArrowBody()
        {
            string result = InvokePatch("ArrowCtorProbe", ".ctor", CtorBody());
            Assert.StartsWith("ERR|ARROW_BODY", result);
            Assert.Contains("构造函数", result);
        }

        /// <summary>
        /// 调用 patch 工具（方法体替换）
        /// </summary>
        /// <param name="className">类名</param>
        /// <param name="method">锚点名（方法名 / ".ctor" / 类名）</param>
        /// <param name="body">方法体（含大括号）</param>
        /// <returns>工具结果文本</returns>
        private string InvokePatch(string className, string method, string body)
        {
            string args = "{\"path\":\"" + Escape(_projectPath) + "\",\"class\":\"" + className + "\",\"method\":\"" + method
                + "\",\"body\":\"" + Escape(body) + "\"}";
            string result = "";
            _bridge.Invoke("patch", args, out result);
            return result;
        }

        /// <summary>
        /// JSON 字符串转义（body 参数）
        /// </summary>
        /// <param name="text">原文</param>
        /// <returns>转义文本</returns>
        private static string Escape(string text)
        {
            return text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n");
        }

        /// <summary>
        /// 构造函数体样例——LF 行尾 + 零缩进（模拟最差调用方）
        /// </summary>
        /// <returns>块体源码</returns>
        private static string CtorBody()
        {
            return "{\n_value = 2;\n}";
        }

        /// <summary>
        /// 探针工程文件文本
        /// </summary>
        /// <returns>csproj 文本</returns>
        private static string ProjectText()
        {
            string nl = "\r\n";
            return "<Project Sdk=\"Microsoft.NET.Sdk\">" + nl + nl + "  <PropertyGroup>" + nl
                + "    <TargetFramework>net10.0</TargetFramework>" + nl
                + "    <Nullable>enable</Nullable>" + nl + "  </PropertyGroup>" + nl + nl + "</Project>" + nl;
        }

        /// <summary>
        /// 探针源文件文本——四类锚点场景：单构造函数 / 多构造函数 / 属性 / 表达式体构造函数（CRLF + 规范缩进）
        /// </summary>
        /// <returns>Probe.cs 文本</returns>
        private static string SourceText()
        {
            string nl = "\r\n";
            return "namespace CtorProbe" + nl + "{" + nl
                + "    /// <summary>" + nl + "    /// 单构造函数探针" + nl + "    /// </summary>" + nl
                + "    public class CtorProbe" + nl + "    {" + nl
                + "        /// <summary>" + nl + "        /// 配置值" + nl + "        /// </summary>" + nl
                + "        private int _value;" + nl + nl
                + "        /// <summary>" + nl + "        /// 构造函数" + nl + "        /// </summary>" + nl
                + "        public CtorProbe()" + nl + "        {" + nl + "            _value = 1;" + nl + "        }" + nl
                + "    }" + nl + nl
                + "    /// <summary>" + nl + "    /// 多构造函数探针" + nl + "    /// </summary>" + nl
                + "    public class MultiCtorProbe" + nl + "    {" + nl
                + "        /// <summary>" + nl + "        /// 配置值" + nl + "        /// </summary>" + nl
                + "        private int _value;" + nl + nl
                + "        /// <summary>" + nl + "        /// 无参构造函数" + nl + "        /// </summary>" + nl
                + "        public MultiCtorProbe()" + nl + "        {" + nl + "            _value = 7;" + nl + "        }" + nl + nl
                + "        /// <summary>" + nl + "        /// 带参构造函数" + nl + "        /// </summary>" + nl
                + "        /// <param name=\"value\">初值</param>" + nl
                + "        public MultiCtorProbe(int value)" + nl + "        {" + nl + "            _value = value;" + nl + "        }" + nl
                + "    }" + nl + nl
                + "    /// <summary>" + nl + "    /// 属性探针" + nl + "    /// </summary>" + nl
                + "    public class PropProbe" + nl + "    {" + nl
                + "        /// <summary>" + nl + "        /// 标题" + nl + "        /// </summary>" + nl
                + "        public string Title" + nl + "        {" + nl
                + "            get" + nl + "            {" + nl + "                return \"t\";" + nl + "            }" + nl
                + "        }" + nl
                + "    }" + nl + nl
                + "    /// <summary>" + nl + "    /// 表达式体构造函数探针" + nl + "    /// </summary>" + nl
                + "    public class ArrowCtorProbe" + nl + "    {" + nl
                + "        /// <summary>" + nl + "        /// 配置值" + nl + "        /// </summary>" + nl
                + "        private int _value;" + nl + nl
                + "        /// <summary>" + nl + "        /// 表达式体构造函数（不可 patch——须先展开为块体）" + nl + "        /// </summary>" + nl
                + "        public ArrowCtorProbe() => _value = 3;" + nl
                + "    }" + nl + "}" + nl;
        }
    }
}

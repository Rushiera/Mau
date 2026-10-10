using System;
using System.IO;
using System.Text;
using Mau.Development;
using Mau.Runtime;
using Xunit;

namespace Mau.Development.Tests
{
    /// <summary>
    /// 入口矩阵测试——cs-* 全组三态入口（csproj / .sln / 目录）同级别可达：
    /// 单项目族（read / patch / member / comment）在多项目入口上按类声明定位；诊断码分列（ENTRY_SUPPORTED / ENTRY_EMPTY / ENTRY_AMBIGUOUS / CLASS_AMBIGUOUS / CLASS_NOT_FOUND）。
    /// </summary>
    public class BridgeEntryMatrixTests : IDisposable
    {
        /// <summary>
        /// 临时受控根
        /// </summary>
        private readonly string _root;

        /// <summary>
        /// 编码桥实例（受控根 = 临时根）
        /// </summary>
        private readonly MauRoslynBridge _bridge;

        /// <summary>
        /// 创建夹具——Solo.sln（App + Lib 两项目）+ 多 sln 目录 + 空入口目录 + 非入口文件
        /// </summary>
        public BridgeEntryMatrixTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "mau_bridge_entry_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            string app = Path.Combine(_root, "App");
            string lib = Path.Combine(_root, "Lib");
            string amb = Path.Combine(_root, "Amb");
            string noEntrySub = Path.Combine(_root, "NoEntry", "Sub");
            Directory.CreateDirectory(app);
            Directory.CreateDirectory(lib);
            Directory.CreateDirectory(amb);
            Directory.CreateDirectory(noEntrySub);
            File.WriteAllText(Path.Combine(_root, "Solo.sln"), SolutionText(), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(app, "App.csproj"), ProjectText(), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(lib, "Lib.csproj"), ProjectText(), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(app, "AppFoo.cs"), SourceText("AppFoo", "app"), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(app, "Shared.cs"), SourceText("Shared", "shared-app"), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(lib, "LibBar.cs"), SourceText("LibBar", "lib"), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(lib, "Shared.cs"), SourceText("Shared", "shared-lib"), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(amb, "Alpha.sln"), SolutionText(), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(amb, "Beta.sln"), SolutionText(), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(noEntrySub, "Sub.csproj"), ProjectText(), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(_root, "Notes.txt"), "not an entry" + Environment.NewLine, new UTF8Encoding(false));
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
        /// 读面（cs-read）接 .sln 入口——多项目内按类声明定位到所属项目
        /// </summary>
        [Fact]
        public void SolutionEntryReadsClassInFirstProject()
        {
            string result = Read("Solo.sln", "AppFoo");
            Assert.StartsWith("{\"ok\":true,\"tool\":\"cs-read\"", result);
            Assert.Contains("AppFoo", result);
        }

        /// <summary>
        /// 读面接 .sln 入口——类住在第二个项目时同样命中（定位跨项目）
        /// </summary>
        [Fact]
        public void SolutionEntryReadsClassInSecondProject()
        {
            string result = Read("Solo.sln", "LibBar");
            Assert.StartsWith("{\"ok\":true,\"tool\":\"cs-read\"", result);
            Assert.Contains("LibBar", result);
        }

        /// <summary>
        /// 类名在多项目内重复命中 ⇒ CLASS_AMBIGUOUS，候选逐条给出「项目 csproj · 文件:L行」
        /// </summary>
        [Fact]
        public void SolutionEntryAmbiguousClassListsCandidates()
        {
            string result = Read("Solo.sln", "Shared");
            Assert.StartsWith("ERR|CLASS_AMBIGUOUS|", result);
            Assert.Contains("App/App.csproj", result);
            Assert.Contains("Lib/Lib.csproj", result);
            Assert.Contains("Shared.cs:L", result);
        }

        /// <summary>
        /// 类名在多项目入口内不存在 ⇒ CLASS_NOT_FOUND，且报出已扫项目数（不静默）
        /// </summary>
        [Fact]
        public void SolutionEntryMissingClassReportsScannedCount()
        {
            string result = Read("Solo.sln", "NoSuchClass");
            Assert.StartsWith("ERR|CLASS_NOT_FOUND|", result);
            Assert.Contains("已全扫", result);
        }

        /// <summary>
        /// 目录入口认顶层 .sln——顶层无 csproj 时不再报「目录内无 csproj」
        /// </summary>
        [Fact]
        public void DirectoryEntryUsesTopLevelSolution()
        {
            string result = Invoke("list", "{\"path\":\"" + Escape(_root) + "\"}");
            Assert.Contains("\"items\":2", result);
            Assert.Contains("AppFoo", result);
            Assert.Contains("LibBar", result);
        }

        /// <summary>
        /// 目录入口含多个 .sln ⇒ ENTRY_AMBIGUOUS（列出候选，不猜）
        /// </summary>
        [Fact]
        public void DirectoryWithTwoSolutionsReportsAmbiguous()
        {
            string result = Invoke("check", "{\"path\":\"" + Escape(Path.Combine(_root, "Amb")) + "\"}");
            Assert.StartsWith("ERR|ENTRY_AMBIGUOUS|", result);
            Assert.Contains("Alpha.sln", result);
            Assert.Contains("Beta.sln", result);
        }

        /// <summary>
        /// 路径存在但类型不符 ⇒ ENTRY_UNSUPPORTED（与 BAD_PATH 分列，给出可用形态）
        /// </summary>
        [Fact]
        public void ExistingNonEntryFileReportsUnsupported()
        {
            string result = Invoke("check", "{\"path\":\"" + Escape(Path.Combine(_root, "Notes.txt")) + "\"}");
            Assert.StartsWith("ERR|ENTRY_UNSUPPORTED|", result);
            Assert.Contains("支持 csproj / .sln / 目录", result);
        }

        /// <summary>
        /// 目录顶层无 csproj 无 .sln ⇒ ENTRY_EMPTY，附子目录项目提示
        /// </summary>
        [Fact]
        public void DirectoryWithoutEntryReportsEmpty()
        {
            string result = Invoke("check", "{\"path\":\"" + Escape(Path.Combine(_root, "NoEntry")) + "\"}");
            Assert.StartsWith("ERR|ENTRY_EMPTY|", result);
            Assert.Contains("子目录发现 1 个项目", result);
        }

        /// <summary>
        /// 写面（cs-patch）接 .sln 入口——改到目标项目所属文件，另一项目逐字节不变
        /// </summary>
        [Fact]
        public void PatchReachesTargetProjectThroughSolution()
        {
            string libFile = Path.Combine(_root, "Lib", "LibBar.cs");
            string appFile = Path.Combine(_root, "App", "AppFoo.cs");
            string appBefore = File.ReadAllText(appFile);
            string args = "{\"path\":\"" + Escape(Path.Combine(_root, "Solo.sln")) + "\",\"class\":\"LibBar\",\"method\":\"Ping\",\"body\":\"{\\nreturn \\\"patched\\\";\\n}\"}";
            string result = Invoke("patch", args);
            Assert.StartsWith("{\"ok\":true", result);
            Assert.Contains("return \"patched\";", File.ReadAllText(libFile));
            Assert.Equal(appBefore, File.ReadAllText(appFile));
        }

        /// <summary>
        /// cs-format 传 .sln ⇒ ENTRY_UNSUPPORTED（消灭原「未找到目标 .cs 文件」的静默假绿）
        /// </summary>
        [Fact]
        public void FormatRejectsSolutionEntry()
        {
            string result = Invoke("format", "{\"path\":\"" + Escape(Path.Combine(_root, "Solo.sln")) + "\",\"mode\":\"check\"}");
            Assert.StartsWith("ERR|ENTRY_UNSUPPORTED|", result);
            Assert.Contains("支持 .cs 文件 / 目录 / csproj", result);
        }

        /// <summary>
        /// cs-format 传不存在路径 ⇒ PATH_NOT_FOUND（不再回落成空产物 + OK）
        /// </summary>
        [Fact]
        public void FormatMissingPathReportsBadPath()
        {
            string result = Invoke("format", "{\"path\":\"" + Escape(Path.Combine(_root, "Missing.cs")) + "\",\"mode\":\"check\"}");
            Assert.StartsWith("ERR|PATH_NOT_FOUND|", result);
            Assert.Contains("路径不存在", result);
        }

        /// <summary>
        /// 单项目入口（csproj）行为不变——直接定位，不做跨项目扫描
        /// </summary>
        [Fact]
        public void ProjectEntryStillWorks()
        {
            string result = Read(Path.Combine("App", "App.csproj"), "AppFoo");
            Assert.StartsWith("{\"ok\":true,\"tool\":\"cs-read\"", result);
        }

        /// <summary>
        /// read 调用——路径取临时根下相对路径
        /// </summary>
        /// <param name="relativePath">相对临时根的路径</param>
        /// <param name="className">类名</param>
        /// <returns>工具结果文本</returns>
        private string Read(string relativePath, string className)
        {
            string path = Path.Combine(_root, relativePath);
            string args = "{\"path\":\"" + Escape(path) + "\",\"class\":\"" + className + "\"}";
            return Invoke("read", args);
        }

        /// <summary>
        /// 调用编码桥
        /// </summary>
        /// <param name="tool">工具名（read / patch / list / check / format）</param>
        /// <param name="args">完整参数 JSON</param>
        /// <returns>工具结果文本</returns>
        private string Invoke(string tool, string args)
        {
            string result = "";
            _bridge.Invoke(tool, args, out result);
            return result;
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

        /// <summary>
        /// 探针源码文本——单个类 + 一个方法（Ping）
        /// </summary>
        /// <param name="className">类名</param>
        /// <param name="returnText">方法返回字面量</param>
        /// <returns>C# 源文本</returns>
        private static string SourceText(string className, string returnText)
        {
            string nl = "\r\n";
            return "namespace Probe" + nl + "{" + nl
                + "    /// <summary>" + nl + "    /// 探针类" + nl + "    /// </summary>" + nl
                + "    public class " + className + nl + "    {" + nl
                + "        /// <summary>" + nl + "        /// 探针方法" + nl + "        /// </summary>" + nl
                + "        /// <returns>固定文本</returns>" + nl
                + "        public string Ping()" + nl + "        {" + nl
                + "            return \"" + returnText + "\";" + nl + "        }" + nl
                + "    }" + nl + "}" + nl;
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
                + "    <Nullable>enable</Nullable>" + nl
                + "  </PropertyGroup>" + nl + nl + "</Project>" + nl;
        }

        /// <summary>
        /// 解决方案文本——含 App / Lib 两个项目声明（相对本文件所在目录）
        /// </summary>
        /// <returns>sln 文本（App/Lib 项目行；Amb 目录两份同名文本仅作候选计数，不解析）</returns>
        private static string SolutionText()
        {
            string nl = "\r\n";
            return "Microsoft Visual Studio Solution File, Format Version 12.00" + nl
                + "# Visual Studio Version 17" + nl
                + "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"App\", \"App\\App.csproj\", \"{11111111-1111-1111-1111-111111111111}\"" + nl
                + "EndProject" + nl
                + "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"Lib\", \"Lib\\Lib.csproj\", \"{22222222-2222-2222-2222-222222222222}\"" + nl
                + "EndProject" + nl
                + "Global" + nl
                + "EndGlobal" + nl;
        }
    }
}

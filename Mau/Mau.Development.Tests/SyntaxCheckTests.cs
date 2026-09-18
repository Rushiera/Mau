using System;
using System.IO;
using Mau.Development;
using Mau.Runtime;
using Xunit;

namespace Mau.Development.Tests
{
    /// <summary>
    /// cs-check 语法层契约回归（check 降级轮）——check 只报语法诊断
    /// （逐树 SyntaxTree.GetDiagnostics，不触引用集 / 语义模型）；程序集引用与编译裁决归 cs-build。
    /// </summary>
    public sealed class SyntaxCheckTests
    {
        /// <summary>
        /// 仓库根（受控根）
        /// </summary>
        private readonly string _root;

        /// <summary>
        /// 编码桥实例（受控根 = 仓库根）
        /// </summary>
        private readonly MauRoslynBridge _bridge;

        /// <summary>
        /// 创建夹具——定位仓库根 + 建桥
        /// </summary>
        public SyntaxCheckTests()
        {
            _root = RepoRoot();
            WorkspaceConfig.RootEntry entry = new WorkspaceConfig.RootEntry();
            entry.Id = "mau";
            entry.Path = _root;
            entry.Writable = true;
            _bridge = new MauRoslynBridge(new WorkspaceConfig.RootEntry[] { entry });
        }

        /// <summary>
        /// 仓库根探测——测试输出目录向上找含 Bricks 的层级
        /// </summary>
        /// <returns>含 Bricks 目录的层级路径</returns>
        private static string RepoRoot()
        {
            string dir = AppContext.BaseDirectory;
            while (dir.Length > 3 && !Directory.Exists(Path.Combine(dir, "Bricks")))
            {
                dir = Path.GetDirectoryName(dir) ?? "";
            }
            return dir;
        }

        /// <summary>
        /// 桥调用——check 输出文本
        /// </summary>
        /// <param name="csproj">csproj 绝对路径</param>
        /// <returns>结果文本</returns>
        private string Check(string csproj)
        {
            string result;
            _bridge.Invoke("check", "{\"path\":\"" + csproj.Replace("\\", "\\\\") + "\"}", out result);
            return result;
        }

        /// <summary>
        /// 干净项目——check 报语法层通过，措辞注明权威归 cs-build
        /// </summary>
        [Fact]
        public void CleanProjectReportsSyntaxLayerOk()
        {
            string csproj = Path.Combine(_root, "Mau", "Mau.Development", "Mau.Development.csproj");
            Assert.True(File.Exists(csproj), "目标工程缺失: " + csproj);
            string result = Check(csproj);
            // 结构化返回（2026-09-18）：首行 JSON 元数据头（ok / tool / 计数）——正文只承载诊断行
            Assert.StartsWith("{\"ok\":true,\"tool\":\"cs-check\"", result);
            Assert.Contains("\"errors\":0", result);
            Assert.Contains("\"files\":", result);
        }

        /// <summary>
        /// 语法错误项目——check 必须报 FAIL|CHECK（哨兵可失败性：语法错必须被语法层捕获）
        /// </summary>
        [Fact]
        public void SyntaxErrorProjectFails()
        {
            string dir = Path.Combine(_root, "CatTemp", "SyntaxProbe");
            string csproj = Path.Combine(dir, "SyntaxProbe.csproj");
            try
            {
                Directory.CreateDirectory(dir);
                string projText = "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
                    "  <PropertyGroup>\n" +
                    "    <TargetFramework>net8.0</TargetFramework>\n" +
                    "    <ImplicitUsings>disable</ImplicitUsings>\n" +
                    "    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>\n" +
                    "  </PropertyGroup>\n" +
                    "  <ItemGroup>\n" +
                    "    <Compile Include=\"Bad.cs\" />\n" +
                    "  </ItemGroup>\n" +
                    "</Project>\n";
                File.WriteAllText(csproj, projText);
                File.WriteAllText(Path.Combine(dir, "Bad.cs"), "public class Bad\n{\n    public void M(\n    {\n    }\n}\n");
                string result = Check(csproj);
                // 结构化返回（2026-09-18）：首行 JSON 头 ok=false + errors 计数；正文为定界诊断行
                Assert.StartsWith("{\"ok\":false,\"tool\":\"cs-check\"", result);
                Assert.Contains("\"errors\":", result);
                Assert.Contains("Bad.cs:", result);
            }
            finally
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                }
            }
        }
    }
}
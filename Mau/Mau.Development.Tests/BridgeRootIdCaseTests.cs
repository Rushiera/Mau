using System;
using System.IO;
using System.Text;
using Mau.Development;
using Mau.Runtime;
using Xunit;

namespace Mau.Development.Tests
{
    /// <summary>
    /// 受控根 id 寻址大小写回归（A211）——`id:` 前缀比对忽略大小写，对齐全局规范
    /// （`FileSystemService.Resolve` / `WorkspaceConfig.ResolveInjectFile` 均以小写识别根 id，读取比较点统一 `OrdinalIgnoreCase`）。
    /// 约定：根 id 一律小写；LLM 工具路径写 `MAU:` / `Mau:` 与 `mau:` 等价。
    /// </summary>
    public class BridgeRootIdCaseTests : IDisposable
    {
        /// <summary>
        /// 临时受控根
        /// </summary>
        private readonly string _root;

        /// <summary>
        /// 编码桥实例（受控根 id = 小写 mau）
        /// </summary>
        private readonly MauRoslynBridge _bridge;

        /// <summary>
        /// 创建夹具——小写 id 受控根 + 单项目探针
        /// </summary>
        public BridgeRootIdCaseTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "mau_bridge_rootid_" + Guid.NewGuid().ToString("N"));
            string app = Path.Combine(_root, "App");
            Directory.CreateDirectory(app);
            File.WriteAllText(Path.Combine(app, "App.csproj"), ProjectText(), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(app, "AppFoo.cs"), SourceText(), new UTF8Encoding(false));
            WorkspaceConfig.RootEntry entry = new WorkspaceConfig.RootEntry();
            entry.Id = "mau";
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
                Console.WriteLine("[测试清理] 工作区目录删除失败: " + ex.Message);
            }
        }

        /// <summary>
        /// 根 id 前缀全大写命中——`MAU:App/App.csproj` 等价 `mau:...`（A211 回归锚点）
        /// </summary>
        [Fact]
        public void UpperCaseRootIdPrefixResolves()
        {
            string result = Invoke("list", "{\"path\":\"MAU:App/App.csproj\"}");
            Assert.StartsWith("{\"ok\":true", result);
            Assert.Contains("AppFoo", result);
        }

        /// <summary>
        /// 根 id 前缀大小写混写命中——`Mau:App/App.csproj` 等价 `mau:...`
        /// </summary>
        [Fact]
        public void MixedCaseRootIdPrefixResolves()
        {
            string result = Invoke("list", "{\"path\":\"Mau:App/App.csproj\"}");
            Assert.StartsWith("{\"ok\":true", result);
        }

        /// <summary>
        /// 根 id 前缀全小写命中——正例基线（修复前后皆成立）
        /// </summary>
        [Fact]
        public void LowerCaseRootIdPrefixResolves()
        {
            string result = Invoke("list", "{\"path\":\"mau:App/App.csproj\"}");
            Assert.StartsWith("{\"ok\":true", result);
        }

        /// <summary>
        /// 调用编码桥
        /// </summary>
        /// <param name="tool">工具名（read / list / check 等）</param>
        /// <param name="args">完整参数 JSON</param>
        /// <returns>工具结果文本</returns>
        private string Invoke(string tool, string args)
        {
            string result = "";
            _bridge.Invoke(tool, args, out result);
            return result;
        }

        /// <summary>
        /// 探针源码文本——单类
        /// </summary>
        /// <returns>C# 源文本</returns>
        private static string SourceText()
        {
            string nl = "\r\n";
            return "namespace Probe" + nl + "{" + nl
                + "    /// <summary>" + nl + "    /// 探针类" + nl + "    /// </summary>" + nl
                + "    public class AppFoo" + nl + "    {" + nl
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
    }
}

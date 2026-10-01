using System;
using System.IO;
using System.Text;
using Mau.Development;
using Mau.Runtime;
using Xunit;

namespace Mau.Development.Tests
{
    /// <summary>
    /// cs-find 符号查找测试——按名字反查声明（类 / 方法 / 构造函数 / 属性 / 字段）：
    /// 输出「[项目] 文件:行: 种类 全名」，精确匹配优先，包含匹配补充；无命中报 ERR。
    /// </summary>
    public class BridgeFindTests : IDisposable
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
        public BridgeFindTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "mau_bridge_find_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            _projectPath = Path.Combine(_root, "FindProbe.csproj");
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
        /// 名字反查——类与构造函数命中，输出含所属项目与文件:行；精确匹配排在包含匹配之前
        /// </summary>
        [Fact]
        public void FindLocatesClassAndConstructor()
        {
            string result = Invoke("FindProbe");
            Assert.StartsWith("{\"ok\":true,\"tool\":\"cs-find\"", result);
            Assert.Contains("[FindProbe] Probe.cs:", result);
            Assert.Contains(": 类 FindProbe", result);
            Assert.Contains(": 构造函数 FindProbe.FindProbe", result);
            Assert.Contains(": 类 FindProbeExtra", result);
            string[] lines = result.Replace("\r\n", "\n").Split('\n');
            int exactLine = -1;
            int partialLine = -1;
            for (int i = 0; i < lines.Length; i = i + 1)
            {
                if (lines[i].EndsWith(": 类 FindProbe", StringComparison.Ordinal))
                {
                    exactLine = i;
                }
                if (lines[i].EndsWith(": 类 FindProbeExtra", StringComparison.Ordinal))
                {
                    partialLine = i;
                }
            }
            Assert.True(exactLine >= 0 && partialLine > exactLine, "精确匹配未排在包含匹配之前");
        }

        /// <summary>
        /// 名字反查——方法 / 属性 / 字段三类成员命中
        /// </summary>
        [Fact]
        public void FindLocatesMethodsPropertiesAndFields()
        {
            Assert.Contains(": 方法 FindProbe.HitTarget", Invoke("HitTarget"));
            Assert.Contains(": 属性 FindProbe.Title", Invoke("Title"));
            Assert.Contains(": 字段 FindProbe._value", Invoke("_value"));
        }

        /// <summary>
        /// 包含匹配——不完整名字列出同族声明（精确在前、包含在后）
        /// </summary>
        [Fact]
        public void FindPartialMatchListsSiblings()
        {
            string result = Invoke("HitTarget");
            Assert.Contains(": 方法 FindProbe.HitTarget", result);
            Assert.Contains(": 方法 FindProbeExtra.HitTargetExtra", result);
        }

        /// <summary>
        /// 无命中——报 SYMBOL_NOT_FOUND（失败可见，不给空成功）
        /// </summary>
        [Fact]
        public void FindMissingSymbolReportsNotFound()
        {
            string result = Invoke("NoSuchSymbolXyz");
            Assert.StartsWith("ERR|SYMBOL_NOT_FOUND", result);
        }

        /// <summary>
        /// 参数面——缺 name 拒绝（必填）
        /// </summary>
        [Fact]
        public void FindRequiresNameArg()
        {
            string args = "{\"path\":\"" + Escape(_projectPath) + "\"}";
            string result = "";
            _bridge.Invoke("find", args, out result);
            Assert.StartsWith("ERR|BAD_ARGS", result);
        }

        /// <summary>
        /// 参数面——未知参数拒绝（零容忍）
        /// </summary>
        [Fact]
        public void FindRejectsUnknownArg()
        {
            string args = "{\"path\":\"" + Escape(_projectPath) + "\",\"name\":\"FindProbe\",\"foo\":\"1\"}";
            string result = "";
            _bridge.Invoke("find", args, out result);
            Assert.StartsWith("ERR|BAD_ARGS", result);
        }

        /// <summary>
        /// 调用 cs.find（path + name）
        /// </summary>
        /// <param name="name">查找值</param>
        /// <returns>工具结果文本</returns>
        private string Invoke(string name)
        {
            string args = "{\"path\":\"" + Escape(_projectPath) + "\",\"name\":\"" + name + "\"}";
            string result = "";
            _bridge.Invoke("find", args, out result);
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
        /// 探针源文件文本——两类（精确名 / 包含名）+ 构造函数 + 属性 + 字段（CRLF + 规范缩进）
        /// </summary>
        /// <returns>Probe.cs 文本</returns>
        private static string SourceText()
        {
            string nl = "\r\n";
            return "namespace FindProbe" + nl + "{" + nl
                + "    /// <summary>" + nl + "    /// 查找探针类" + nl + "    /// </summary>" + nl
                + "    public class FindProbe" + nl + "    {" + nl
                + "        /// <summary>" + nl + "        /// 配置值" + nl + "        /// </summary>" + nl
                + "        private int _value;" + nl + nl
                + "        /// <summary>" + nl + "        /// 构造函数" + nl + "        /// </summary>" + nl
                + "        public FindProbe()" + nl + "        {" + nl + "            _value = 1;" + nl + "        }" + nl + nl
                + "        /// <summary>" + nl + "        /// 命中方法" + nl + "        /// </summary>" + nl
                + "        /// <returns>固定文本</returns>" + nl
                + "        public string HitTarget()" + nl + "        {" + nl + "            return \"hit\";" + nl + "        }" + nl + nl
                + "        /// <summary>" + nl + "        /// 标题属性" + nl + "        /// </summary>" + nl
                + "        public string Title" + nl + "        {" + nl
                + "            get" + nl + "            {" + nl + "                return \"t\";" + nl + "            }" + nl
                + "        }" + nl
                + "    }" + nl + nl
                + "    /// <summary>" + nl + "    /// 第二探针类（包含匹配面）" + nl + "    /// </summary>" + nl
                + "    public class FindProbeExtra" + nl + "    {" + nl
                + "        /// <summary>" + nl + "        /// 命中方法（另一类）" + nl + "        /// </summary>" + nl
                + "        /// <returns>固定文本</returns>" + nl
                + "        public string HitTargetExtra()" + nl + "        {" + nl + "            return \"extra\";" + nl + "        }" + nl
                + "    }" + nl + "}" + nl;
        }
    }
}

using System;
using System.IO;
using System.Text;
using Mau.Development;
using Mau.Runtime;
using Xunit;

namespace Mau.Development.Tests
{
    /// <summary>
    /// 入口寻路邻近候选测试——路径不存在时只补报错文本（同级 / 上一级各扫 1 级），不改变寻路结果（无静默切换）。
    /// </summary>
    public class BridgeEntryHintTests : IDisposable
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
        /// 创建夹具——构造 Repo（含 sln 与子项目）与 Empty（无候选）两个面
        /// </summary>
        public BridgeEntryHintTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "mau_bridge_hint_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            string repo = Path.Combine(_root, "Repo");
            string sub = Path.Combine(repo, "CatHome4");
            string empty = Path.Combine(_root, "Empty");
            Directory.CreateDirectory(sub);
            Directory.CreateDirectory(empty);
            File.WriteAllText(Path.Combine(repo, "CatHome4.sln"), SolutionText(), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(sub, "CatHome4.csproj"), ProjectText(), new UTF8Encoding(false));
            WorkspaceConfig.RootEntry outer = new WorkspaceConfig.RootEntry();
            outer.Id = "outer";
            outer.Path = Path.GetDirectoryName(_root) ?? _root;
            outer.Writable = false;
            WorkspaceConfig.RootEntry entry = new WorkspaceConfig.RootEntry();
            entry.Id = "tmp";
            entry.Path = _root;
            entry.Writable = true;
            _bridge = new MauRoslynBridge(new WorkspaceConfig.RootEntry[] { outer, entry });
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
        /// 路径不存在（多项目入口）——报错文本列同级与上一级候选（同名优先），寻路结果不变（仍 ERR）
        /// </summary>
        [Fact]
        public void MissingPathReportsNeighborCandidates()
        {
            string missing = Path.Combine(_root, "Repo", "CatHome4", "CatHome4.sln");
            string result = Invoke("check", missing);
            Assert.StartsWith("ERR|BAD_PATH|", result);
            Assert.Contains("路径不存在", result);
            Assert.Contains("邻近候选（仅提示，不自动切换）", result);
            Assert.Contains("同级: tmp:Repo/CatHome4/CatHome4.csproj", result);
            Assert.Contains("上一级同名: tmp:Repo/CatHome4.sln", result);
        }

        /// <summary>
        /// 邻近面无 sln / csproj——报错文本保持原样（不产空提示段）
        /// </summary>
        [Fact]
        public void MissingPathWithoutNeighborsKeepsPlainMessage()
        {
            string missing = Path.Combine(_root, "Empty", "Missing.sln");
            string result = Invoke("check", missing);
            Assert.StartsWith("ERR|BAD_PATH|", result);
            Assert.Contains("路径不存在", result);
            Assert.False(result.Contains("邻近候选"), "无候选时不得产出提示段");
        }

        /// <summary>
        /// 单项目入口（cs-patch）同享邻近候选——只进报错文本，不落盘、不切换
        /// </summary>
        [Fact]
        public void SingleProjectEntryReportsNeighborCandidates()
        {
            string missing = Path.Combine(_root, "Repo", "CatHome4", "CatHome4.sln");
            string args = "{\"path\":\"" + Escape(missing) + "\",\"class\":\"Foo\",\"method\":\"Bar\",\"body\":\"{\\n}\"}";
            string result = "";
            _bridge.Invoke("patch", args, out result);
            Assert.StartsWith("ERR|BAD_PATH|", result);
            Assert.Contains("上一级同名: tmp:Repo/CatHome4.sln", result);
        }

        /// <summary>
        /// 根重叠——候选以最具体的根（路径最长者）描述，而非首个别名匹配
        /// </summary>
        [Fact]
        public void OverlappingRootsPickMostSpecificRoot()
        {
            string missing = Path.Combine(_root, "Repo", "CatHome4", "CatHome4.sln");
            string result = Invoke("check", missing);
            Assert.Contains("上一级同名: tmp:Repo/CatHome4.sln", result);
            Assert.False(result.Contains("outer:"), "根重叠时误用外层根描述");
        }

        /// <summary>
        /// 调用工具（路径参数）
        /// </summary>
        /// <param name="tool">工具名（check / patch 等）</param>
        /// <param name="path">路径参数（绝对路径）</param>
        /// <returns>工具结果文本</returns>
        private string Invoke(string tool, string path)
        {
            string args = "{\"path\":\"" + Escape(path) + "\"}";
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
        /// 最小解决方案文本（本测试只验寻路提示，不解析内容）
        /// </summary>
        /// <returns>sln 文本</returns>
        private static string SolutionText()
        {
            string nl = "\r\n";
            return "Microsoft Visual Studio Solution File, Format Version 12.00" + nl + "# Visual Studio Version 17" + nl;
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
                + "  </PropertyGroup>" + nl + nl + "</Project>" + nl;
        }
    }
}

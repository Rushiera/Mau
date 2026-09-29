using System;
using System.IO;
using System.Text;
using Mau.Development;
using Mau.Runtime;
using Xunit;

namespace Mau.Development.Tests
{
    /// <summary>
    /// 分部类跨分部聚合测试——读/写/歧义/落点（partial 类的成员可能住在任意分部）
    /// </summary>
    public class BridgePartialTests : IDisposable
    {
        /// <summary>
        /// 临时工程根（受控根）
        /// </summary>
        private readonly string _root;

        /// <summary>
        /// 第 1 分部源文件
        /// </summary>
        private readonly string _partOne;

        /// <summary>
        /// 第 2 分部源文件
        /// </summary>
        private readonly string _partTwo;

        /// <summary>
        /// 探针工程文件
        /// </summary>
        private readonly string _projectPath;

        /// <summary>
        /// 编码桥实例（受控根 = 临时根）
        /// </summary>
        private readonly MauRoslynBridge _bridge;

        /// <summary>
        /// 创建夹具——两分部临时工程 + 建桥
        /// </summary>
        public BridgePartialTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "mau_bridge_part_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            _projectPath = Path.Combine(_root, "PartProbe.csproj");
            _partOne = Path.Combine(_root, "Probe.cs");
            _partTwo = Path.Combine(_root, "Probe.Extra.cs");
            File.WriteAllText(_projectPath, ProjectText(), new UTF8Encoding(false));
            File.WriteAllText(_partOne, PartOneText(), new UTF8Encoding(false));
            File.WriteAllText(_partTwo, PartTwoText(), new UTF8Encoding(false));
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
        /// 调用编码桥——path 自动补，其余参数由调用方给出
        /// </summary>
        /// <param name="tool">工具名（read/patch/comment/member/list）</param>
        /// <param name="argumentsBody">参数字段（不含 path）</param>
        /// <returns>工具结果文本</returns>
        private string Call(string tool, string argumentsBody)
        {
            string args = "{\"path\":\"" + Escape(_projectPath) + "\"," + argumentsBody + "}";
            string result = "";
            _bridge.Invoke(tool, args, out result);
            return result;
        }

        /// <summary>
        /// read——成员只住在第 2 分部时也能命中（输出标注真实来源文件）
        /// </summary>
        [Fact]
        public void ReadFindsMemberInSecondPart()
        {
            string result = Call("read", "\"class\":\"Probe\",\"member\":\"Extra\"");
            // 结构化返回（2026-09-18）：首行 JSON 头（file/class/member/start/end）+ 正文源码
            Assert.StartsWith("{\"ok\":true,\"tool\":\"cs-read\"", result);
            Assert.Contains("Probe.Extra.cs", result);
            Assert.Contains("public string Extra()", result);
        }

        /// <summary>
        /// patch——方法住在第 2 分部 ⇒ 只改第 2 分部（第 1 分部逐字节不变）
        /// </summary>
        [Fact]
        public void PatchWritesToOwningPart()
        {
            string before = File.ReadAllText(_partOne);
            string result = Call("patch", "\"class\":\"Probe\",\"method\":\"Extra\",\"body\":\"{\\nreturn \\\"patched\\\";\\n}\"");
            Assert.StartsWith("{\"ok\":true", result);
            Assert.Contains("return \"patched\";", File.ReadAllText(_partTwo));
            Assert.Equal(before, File.ReadAllText(_partOne));
        }

        /// <summary>
        /// comment——注释写在成员所属分部（不动其它分部）
        /// </summary>
        [Fact]
        public void CommentWritesToOwningPart()
        {
            string before = File.ReadAllText(_partOne);
            string result = Call("comment", "\"class\":\"Probe\",\"member\":\"Extra\",\"type\":\"summary\",\"text\":\"跨分部注释\"");
            Assert.StartsWith("{\"ok\":true", result);
            Assert.Contains("/// <summary>跨分部注释</summary>", File.ReadAllText(_partTwo));
            Assert.Equal(before, File.ReadAllText(_partOne));
        }

        /// <summary>
        /// member delete——删除第 2 分部成员，第 1 分部不受损
        /// </summary>
        [Fact]
        public void DeleteRemovesMemberFromOwningPart()
        {
            string before = File.ReadAllText(_partOne);
            string result = Call("member", "\"class\":\"Probe\",\"op\":\"delete\",\"member\":\"Extra\"");
            Assert.StartsWith("{\"ok\":true", result);
            string partTwo = File.ReadAllText(_partTwo);
            Assert.DoesNotContain("Extra()", partTwo);
            Assert.Contains("public partial class Probe", partTwo);
            Assert.Equal(before, File.ReadAllText(_partOne));
        }

        /// <summary>
        /// member insert——after 锚点住在第 2 分部 ⇒ 新成员落第 2 分部
        /// </summary>
        [Fact]
        public void InsertAfterAnchorLandsInAnchorPart()
        {
            string result = Call("member", "\"class\":\"Probe\",\"op\":\"insert\",\"position\":\"after\",\"anchor\":\"Extra\",\"code\":\"public string Added()\\n{\\nreturn \\\"added\\\";\\n}\"");
            Assert.StartsWith("{\"ok\":true", result);
            Assert.Contains("public string Added()", File.ReadAllText(_partTwo));
            Assert.DoesNotContain("Added", File.ReadAllText(_partOne));
        }

        /// <summary>
        /// 跨分部同名重载 ⇒ AMBIGUOUS 候选带各自分部文件（此前会误命中首个分部的成员）
        /// </summary>
        [Fact]
        public void AmbiguousAcrossPartsListsBothFiles()
        {
            string result = Call("read", "\"class\":\"Probe\",\"member\":\"Same\"");
            Assert.StartsWith("ERR|AMBIGUOUS", result);
            Assert.Contains("Probe.cs", result);
            Assert.Contains("Probe.Extra.cs", result);
        }

        /// <summary>
        /// list——分部类概览逐分部输出全部成员（不再只显示首个分部）
        /// </summary>
        [Fact]
        public void ListOverviewShowsAllParts()
        {
            string result = Call("list", "\"class\":\"Probe\"");
            Assert.Contains("第 1 分部", result);
            Assert.Contains("第 2 分部", result);
            Assert.Contains("'First'", result);
            Assert.Contains("'Extra'", result);
        }

        /// <summary>
        /// JSON 字符串转义
        /// </summary>
        /// <param name="text">原文</param>
        /// <returns>转义文本</returns>
        private static string Escape(string text)
        {
            return text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n");
        }

        /// <summary>
        /// 探针工程文件文本
        /// </summary>
        /// <returns>csproj 文本</returns>
        private static string ProjectText()
        {
            string nl = "\r\n";
            return "<Project Sdk=\"Microsoft.NET.Sdk\">" + nl + nl + "  <PropertyGroup>" + nl
                + "    <TargetFramework>net8.0</TargetFramework>" + nl
                + "    <Nullable>enable</Nullable>" + nl + "  </PropertyGroup>" + nl + nl + "</Project>" + nl;
        }

        /// <summary>
        /// 第 1 分部源文件——First + 同名重载 Same()
        /// </summary>
        /// <returns>Probe.cs 文本</returns>
        private static string PartOneText()
        {
            string nl = "\r\n";
            return "namespace FmtProbe" + nl + "{" + nl
                + "    /// <summary>" + nl + "    /// 探针类（分部 1）" + nl + "    /// </summary>" + nl
                + "    public partial class Probe" + nl + "    {" + nl
                + "        /// <summary>" + nl + "        /// 第 1 分部方法" + nl + "        /// </summary>" + nl
                + "        /// <returns>固定文本</returns>" + nl
                + "        public string First()" + nl + "        {" + nl + "            return \"first\";" + nl + "        }" + nl + nl
                + "        /// <summary>" + nl + "        /// 同名重载（分部 1）" + nl + "        /// </summary>" + nl
                + "        /// <returns>固定文本</returns>" + nl
                + "        public string Same()" + nl + "        {" + nl + "            return \"same-1\";" + nl + "        }" + nl
                + "    }" + nl + "}" + nl;
        }

        /// <summary>
        /// 第 2 分部源文件——Extra + 同名重载 Same(int)
        /// </summary>
        /// <returns>Probe.Extra.cs 文本</returns>
        private static string PartTwoText()
        {
            string nl = "\r\n";
            return "namespace FmtProbe" + nl + "{" + nl
                + "    /// <summary>" + nl + "    /// 探针类（分部 2）" + nl + "    /// </summary>" + nl
                + "    public partial class Probe" + nl + "    {" + nl
                + "        /// <summary>" + nl + "        /// 第 2 分部方法" + nl + "        /// </summary>" + nl
                + "        /// <returns>固定文本</returns>" + nl
                + "        public string Extra()" + nl + "        {" + nl + "            return \"extra\";" + nl + "        }" + nl + nl
                + "        /// <summary>" + nl + "        /// 同名重载（分部 2）" + nl + "        /// </summary>" + nl
                + "        /// <param name=\"value\">输入值</param>" + nl
                + "        /// <returns>固定文本</returns>" + nl
                + "        public string Same(int value)" + nl + "        {" + nl + "            return \"same-2:\" + value;" + nl + "        }" + nl
                + "    }" + nl + "}" + nl;
        }
    }
}

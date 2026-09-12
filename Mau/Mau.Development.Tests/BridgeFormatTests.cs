using System;
using System.IO;
using System.Text;
using Mau.Development;
using Mau.Runtime;
using Xunit;

namespace Mau.Development.Tests
{
    /// <summary>
    /// 编码桥写侧格式铁律测试——行尾归一 / 缩进对齐 / 尾部换行三条规范（不依赖事后格式重整器）
    /// </summary>
    public class BridgeFormatTests : IDisposable
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
        public BridgeFormatTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "mau_bridge_fmt_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            _projectPath = Path.Combine(_root, "FmtProbe.csproj");
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
        /// member insert（position=end）——零缩进 LF 代码 ⇒ 落盘 CRLF + 类成员缩进 + 类闭合括号独占一行
        /// </summary>
        [Fact]
        public void MemberInsertEndNormalizesFormat()
        {
            string result = InvokeMember("end", "", InsertCode());
            Assert.StartsWith("OK", result);
            string text = File.ReadAllText(_sourcePath);
            Assert.True(AllCrlf(text), "落盘出现非 CRLF 行尾（混行）");
            Assert.False(text.Contains("}    }"), "类闭合括号与成员闭合括号同行");
            Assert.Contains("}\r\n    }\r\n}", text);
            Assert.Contains("        /// <summary>\r\n        /// 插入方法\r\n        /// </summary>\r\n        /// <returns>固定文本</returns>\r\n", text);
            Assert.Contains("\r\n        public string Added()\r\n        {\r\n            return \"added\";\r\n        }\r\n", text);
        }

        /// <summary>
        /// member insert——code 含多个成员声明 ⇒ BAD_ARGS 拒绝且不落盘（静默截断防护）
        /// </summary>
        [Fact]
        public void MemberInsertRejectsMultipleDeclarations()
        {
            string before = File.ReadAllText(_sourcePath);
            string code = "        public string Added()\r\n        {\r\n            return \"added\";\r\n        }\r\n\r\n        public string Added2()\r\n        {\r\n            return \"added2\";\r\n        }\r\n";
            string result = InvokeMember("end", "", code);
            Assert.StartsWith("ERR|BAD_ARGS", result);
            Assert.Contains("一次一成员", result);
            Assert.Equal(before, File.ReadAllText(_sourcePath));
        }

        /// <summary>
        /// member insert（position=after）——插入点后续成员须独占一行（尾部换行铁律）
        /// </summary>
        [Fact]
        public void MemberInsertAfterKeepsNextMemberOnOwnLine()
        {
            string result = InvokeMember("after", "First", InsertCode());
            Assert.StartsWith("OK", result);
            string text = File.ReadAllText(_sourcePath);
            Assert.True(AllCrlf(text), "落盘出现非 CRLF 行尾（混行）");
            Assert.Contains("            return \"first\";\r\n        }\r\n", text);
            Assert.Contains("}\r\n    }\r\n}", text);
        }

        /// <summary>
        /// patch——零缩进 LF 方法体 ⇒ 落盘 CRLF + 方法缩进对齐 + 方法闭合括号后换行保真
        /// </summary>
        [Fact]
        public void PatchBodyNormalizesNewLineAndIndent()
        {
            string result = InvokePatch("First", PatchBody());
            Assert.StartsWith("OK", result);
            string text = File.ReadAllText(_sourcePath);
            Assert.True(AllCrlf(text), "落盘出现非 CRLF 行尾（混行）");
            Assert.Contains("        public string First()\r\n        {\r\n            // [段1] 改写体\r\n            return \"patched\";\r\n        }\r\n    }\r\n}", text);
        }

        /// <summary>
        /// comment——注释续行缩进保留（顶格即格式违规）
        /// </summary>
        [Fact]
        public void CommentRewriteKeepsContinuationIndent()
        {
            string result = InvokeComment("First", "改写摘要");
            Assert.StartsWith("OK", result);
            string text = File.ReadAllText(_sourcePath);
            Assert.Contains("        /// <summary>改写摘要</summary>\r\n        /// <returns>固定文本</returns>\r\n", text);
            Assert.False(text.Contains("\r\n/// "), "注释续行顶格");
        }

        /// <summary>
        /// comment——多行 text 逐行补 /// 前缀（续行不补即沦为代码行 CS1022）
        /// </summary>
        [Fact]
        public void CommentMultiLineTextEmitsContinuationPrefixes()
        {
            string result = InvokeComment("First", "第一行\n第二行");
            Assert.StartsWith("OK", result);
            string text = File.ReadAllText(_sourcePath);
            Assert.True(AllCrlf(text), "落盘出现非 CRLF 行尾（混行）");
            Assert.Contains("        /// <summary>第一行\r\n        /// 第二行</summary>\r\n", text);
        }

        /// <summary>
        /// comment——多行 doc 重复覆写不累积缩进（续行缩进剥离保证幂等）
        /// </summary>
        [Fact]
        public void CommentMultiLineRewriteIsIdempotent()
        {
            InvokeComment("First", "甲\n乙");
            string first = File.ReadAllText(_sourcePath);
            InvokeComment("First", "甲\n乙");
            string second = File.ReadAllText(_sourcePath);
            Assert.Equal(first, second);
        }

        /// <summary>
        /// patch——容错接受完整方法声明（自动取方法体，省「BAD_BODY 后重发」往返）
        /// </summary>
        [Fact]
        public void PatchAcceptsFullMethodDeclaration()
        {
            string result = InvokePatch("First", "public string First()\n{\nreturn \"from-full\";\n}");
            Assert.StartsWith("OK", result);
            string text = File.ReadAllText(_sourcePath);
            Assert.True(AllCrlf(text), "落盘出现非 CRLF 行尾（混行）");
            Assert.Contains("        public string First()\r\n        {\r\n            return \"from-full\";\r\n        }\r\n    }\r\n}", text);
        }

        /// <summary>
        /// rename——改 identifier 不动版面（缩进 / 注释 / 行尾全保真）
        /// </summary>
        [Fact]
        public void MemberRenameKeepsFormatAndNewLines()
        {
            string result = InvokeRename("First", "Renamed");
            Assert.StartsWith("OK", result);
            string text = File.ReadAllText(_sourcePath);
            Assert.True(AllCrlf(text), "落盘出现非 CRLF 行尾（混行）");
            Assert.Contains("        /// 原有方法\r\n        /// </summary>\r\n", text);
            Assert.Contains("        public string Renamed()\r\n        {\r\n            return \"first\";\r\n        }\r\n", text);
            Assert.False(text.Contains("First"), "旧名残留");
        }

        /// <summary>
        /// delete——成员连注释整段移除，类骨架与行尾不受损（不留残行）
        /// </summary>
        [Fact]
        public void MemberDeleteRemovesMemberAndComment()
        {
            string result = InvokeDelete("First");
            Assert.StartsWith("OK", result);
            string text = File.ReadAllText(_sourcePath);
            Assert.True(AllCrlf(text), "落盘出现非 CRLF 行尾（混行）");
            Assert.False(text.Contains("First"), "成员残留");
            Assert.False(text.Contains("原有方法"), "注释残留");
            Assert.Contains("    public class Probe\r\n    {\r\n    }\r\n}\r\n", text);
        }

        /// <summary>
        /// 分部类——类概览报声明处数；成员不在检索分部时给可行动提示（不静默死路）
        /// </summary>
        [Fact]
        public void PartialClassReportsPartsAndHintsOnMissingMember()
        {
            File.WriteAllText(_sourcePath, PartialSourceText(), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(_root, "Probe.Extra.cs"), ExtraPartText(), new UTF8Encoding(false));
            string listed = InvokeList("Probe");
            Assert.Contains("partial 合并 2 处", listed);
            string missing = InvokeRead("NoSuchMember");
            Assert.StartsWith("ERR|MEMBER_NOT_FOUND", missing);
            Assert.Contains("分部类", missing);
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
            catch
            {
            }
        }

        /// <summary>
        /// 调用 member 工具（op=insert）
        /// </summary>
        /// <param name="position">插入位置</param>
        /// <param name="anchor">锚点成员（position=after/before 时用）</param>
        /// <param name="code">成员源码</param>
        /// <returns>工具结果文本</returns>
        private string InvokeMember(string position, string anchor, string code)
        {
            string args = "{\"path\":\"" + Escape(_projectPath) + "\",\"class\":\"Probe\",\"op\":\"insert\",\"position\":\"" + position
                + "\",\"anchor\":\"" + anchor + "\",\"code\":\"" + Escape(code) + "\"}";
            string result = "";
            _bridge.Invoke("member", args, out result);
            return result;
        }

        /// <summary>
        /// 调用 patch 工具（方法体替换）
        /// </summary>
        /// <param name="method">方法名</param>
        /// <param name="body">方法体（含大括号）</param>
        /// <returns>工具结果文本</returns>
        private string InvokePatch(string method, string body)
        {
            string args = "{\"path\":\"" + Escape(_projectPath) + "\",\"class\":\"Probe\",\"method\":\"" + method
                + "\",\"body\":\"" + Escape(body) + "\"}";
            string result = "";
            _bridge.Invoke("patch", args, out result);
            return result;
        }

        /// <summary>
        /// 调用 comment 工具（XML 注释增改）
        /// </summary>
        /// <param name="member">成员名</param>
        /// <param name="text">注释文本</param>
        /// <returns>工具结果文本</returns>
        private string InvokeComment(string member, string text)
        {
            string args = "{\"path\":\"" + Escape(_projectPath) + "\",\"class\":\"Probe\",\"member\":\"" + member
                + "\",\"type\":\"summary\",\"text\":\"" + Escape(text) + "\"}";
            string result = "";
            _bridge.Invoke("comment", args, out result);
            return result;
        }

        /// <summary>
        /// 调用 member 工具（op=rename）
        /// </summary>
        /// <param name="oldName">旧成员名</param>
        /// <param name="newName">新成员名</param>
        /// <returns>工具结果文本</returns>
        private string InvokeRename(string oldName, string newName)
        {
            string args = "{\"path\":\"" + Escape(_projectPath) + "\",\"class\":\"Probe\",\"op\":\"rename\",\"oldName\":\"" + oldName
                + "\",\"newName\":\"" + newName + "\"}";
            string result = "";
            _bridge.Invoke("member", args, out result);
            return result;
        }

        /// <summary>
        /// 调用 member 工具（op=delete）
        /// </summary>
        /// <param name="member">待删成员名</param>
        /// <returns>工具结果文本</returns>
        private string InvokeDelete(string member)
        {
            string args = "{\"path\":\"" + Escape(_projectPath) + "\",\"class\":\"Probe\",\"op\":\"delete\",\"member\":\"" + member + "\"}";
            string result = "";
            _bridge.Invoke("member", args, out result);
            return result;
        }

        /// <summary>
        /// 调用 list 工具（类概览）
        /// </summary>
        /// <param name="className">类名</param>
        /// <returns>工具结果文本</returns>
        private string InvokeList(string className)
        {
            string args = "{\"path\":\"" + Escape(_projectPath) + "\",\"class\":\"" + className + "\"}";
            string result = "";
            _bridge.Invoke("list", args, out result);
            return result;
        }

        /// <summary>
        /// 调用 read 工具（成员源码）
        /// </summary>
        /// <param name="member">成员名</param>
        /// <returns>工具结果文本</returns>
        private string InvokeRead(string member)
        {
            string args = "{\"path\":\"" + Escape(_projectPath) + "\",\"class\":\"Probe\",\"member\":\"" + member + "\"}";
            string result = "";
            _bridge.Invoke("read", args, out result);
            return result;
        }

        /// <summary>
        /// JSON 字符串转义（code / body / text 参数）
        /// </summary>
        /// <param name="text">原文</param>
        /// <returns>转义文本</returns>
        private static string Escape(string text)
        {
            return text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n");
        }

        /// <summary>
        /// 行尾一致性检查——全部 CRLF
        /// </summary>
        /// <param name="text">落盘文本</param>
        /// <returns>true=无裸 LF（无混行）</returns>
        private static bool AllCrlf(string text)
        {
            for (int i = 0; i < text.Length; i = i + 1)
            {
                if (text[i] == '\n' && (i == 0 || text[i - 1] != '\r'))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 插入成员样例——LF 行尾 + 零缩进（模拟最差调用方）
        /// </summary>
        /// <returns>成员源码</returns>
        private static string InsertCode()
        {
            return "/// <summary>\n/// 插入方法\n/// </summary>\n/// <returns>固定文本</returns>\npublic string Added()\n{\nreturn \"added\";\n}";
        }

        /// <summary>
        /// 方法体样例——LF 行尾 + 零缩进
        /// </summary>
        /// <returns>块体源码</returns>
        private static string PatchBody()
        {
            return "{\n// [段1] 改写体\nreturn \"patched\";\n}";
        }

        /// <summary>
        /// 分部类源文件（第 1 分部）
        /// </summary>
        /// <returns>Probe.cs 文本</returns>
        private static string PartialSourceText()
        {
            string nl = "\r\n";
            return "namespace FmtProbe" + nl + "{" + nl
                + "    /// <summary>" + nl + "    /// 探针类（分部 1）" + nl + "    /// </summary>" + nl
                + "    public partial class Probe" + nl + "    {" + nl
                + "        /// <summary>" + nl + "        /// 原有方法" + nl + "        /// </summary>" + nl
                + "        /// <returns>固定文本</returns>" + nl
                + "        public string First()" + nl + "        {" + nl + "            return \"first\";" + nl + "        }" + nl
                + "    }" + nl + "}" + nl;
        }

        /// <summary>
        /// 分部类源文件（第 2 分部）
        /// </summary>
        /// <returns>Probe.Extra.cs 文本</returns>
        private static string ExtraPartText()
        {
            string nl = "\r\n";
            return "namespace FmtProbe" + nl + "{" + nl
                + "    /// <summary>" + nl + "    /// 探针类（分部 2）" + nl + "    /// </summary>" + nl
                + "    public partial class Probe" + nl + "    {" + nl
                + "        /// <summary>" + nl + "        /// 另一半方法" + nl + "        /// </summary>" + nl
                + "        /// <returns>固定文本</returns>" + nl
                + "        public string Extra()" + nl + "        {" + nl + "            return \"extra\";" + nl + "        }" + nl
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
                + "    <TargetFramework>net8.0</TargetFramework>" + nl
                + "    <Nullable>enable</Nullable>" + nl + "  </PropertyGroup>" + nl + nl + "</Project>" + nl;
        }

        /// <summary>
        /// 探针源文件文本——CRLF + 规范缩进
        /// </summary>
        /// <returns>Probe.cs 文本</returns>
        private static string SourceText()
        {
            string nl = "\r\n";
            return "namespace FmtProbe" + nl + "{" + nl
                + "    /// <summary>" + nl + "    /// 探针类" + nl + "    /// </summary>" + nl
                + "    public class Probe" + nl + "    {" + nl
                + "        /// <summary>" + nl + "        /// 原有方法" + nl + "        /// </summary>" + nl
                + "        /// <returns>固定文本</returns>" + nl
                + "        public string First()" + nl + "        {" + nl + "            return \"first\";" + nl + "        }" + nl
                + "    }" + nl + "}" + nl;
        }
    }
}

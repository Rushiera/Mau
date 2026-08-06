// ═══════════════════════════════════════════════
// 测试: Mau.Bricks.Data + Mau.Bricks.Text + Mau.Bricks.Shell
// 引用: Mau.Bricks.Tests → Mau.Bricks.Data / Mau.Bricks.Text / Mau.Bricks.Shell
// 原理: CH3 测试随迁改写——直接调用静态积木方法验证
// 常用: 积木库移植正确性回归
// ═══════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.IO;
using Mau.Bricks;
using Xunit;

namespace Mau.Bricks.Tests
{
    /// <summary>
    /// 数据积木测试——Snapshot 分节编码/解码 + 值盒存储
    /// </summary>
    public sealed class DataBrickTests
    {
        /// <summary>
        /// Snapshot 编码/解码往返
        /// </summary>
        [Fact]
        public void SnapshotEncodeDecodeRoundTrip()
        {
            Dictionary<string, List<string>> sections = new Dictionary<string, List<string>>();
            sections["Core"] = new List<string> { "Frame=1", "State=Running" };
            sections["OA"] = new List<string> { "Open=2", "Work=1" };

            string encoded = DataBrick.SnapshotEncode(sections);
            Dictionary<string, List<string>> decoded = DataBrick.SnapshotDecode(encoded);

            Assert.Equal(2, decoded.Count);
            Assert.Equal("Frame=1", decoded["Core"][0]);
            Assert.Equal("Work=1", decoded["OA"][1]);
        }

        /// <summary>
        /// 空字典编码返回空串，空串解码返回空字典
        /// </summary>
        [Fact]
        public void SnapshotEmptyRoundTrip()
        {
            Assert.Equal("", DataBrick.SnapshotEncode(null));
            Assert.Empty(DataBrick.SnapshotDecode(""));
            Assert.Empty(DataBrick.SnapshotDecode(null));
        }

        /// <summary>
        /// 标题形态行被转义保护
        /// </summary>
        [Fact]
        public void SnapshotEscapesHeaderLikeLines()
        {
            Dictionary<string, List<string>> sections = new Dictionary<string, List<string>>();
            sections["S"] = new List<string> { "[LooksLikeSection]", "plain" };

            string encoded = DataBrick.SnapshotEncode(sections);
            Dictionary<string, List<string>> decoded = DataBrick.SnapshotDecode(encoded);

            Assert.Equal("[LooksLikeSection]", decoded["S"][0]);
        }

        /// <summary>
        /// 重复节名解码抛 FormatException
        /// </summary>
        [Fact]
        public void SnapshotRejectsDuplicateSections()
        {
            Assert.Throws<FormatException>(delegate
            {
                DataBrick.SnapshotDecode("[A]\nx\n[A]\ny");
            });
        }

        /// <summary>
        /// 值盒单值写入/读取 + 作用域隔离
        /// </summary>
        [Fact]
        public void BoxSetGetIsolatedByScope()
        {
            BoxBrick.Clear("test-a");
            BoxBrick.Clear("test-b");

            Assert.True(BoxBrick.Set("test-a", "count", 5));
            Assert.True(BoxBrick.Get("test-a", "count", 0, out int valueA));
            Assert.True(BoxBrick.Get("test-b", "count", 0, out int valueB));

            Assert.Equal(5, valueA);
            Assert.Equal(0, valueB);
        }

        /// <summary>
        /// 值盒数据包 JSON 写入/读取
        /// </summary>
        [Fact]
        public void BoxDicRoundTrip()
        {
            BoxBrick.Clear("test-dic");

            Assert.True(BoxBrick.SetDic("test-dic", "tools", "{\"io\":14,\"aux\":2}"));
            Assert.True(BoxBrick.GetDic("test-dic", "tools", out string json));
            Assert.Contains("\"io\":14", json);

            Assert.True(BoxBrick.GetDic("test-dic", "missing", out string missing));
            Assert.Equal("{}", missing);
        }
    }

    /// <summary>
    /// 文本积木测试——Markdown 解析
    /// </summary>
    public sealed class TextBrickTests
    {
        /// <summary>
        /// 标题/列表/代码块/表格解析
        /// </summary>
        [Fact]
        public void MdParseHandlesCommonBlocks()
        {
            string md = "# 标题\n\n- 项目A\n- 项目B\n\n```\ncode\n```\n\n| A | B |\n|---|---|\n| 1 | 2 |\n";

            Assert.True(TextBrick.MdParse(md, out List<MarkdownPart> parts));
            Assert.NotEmpty(parts);

            bool foundH1 = false;
            bool foundCode = false;
            bool foundTable = false;
            for (int i = 0; i < parts.Count; i = i + 1)
            {
                if (parts[i].ColorType == MarkdownColor.H1)
                {
                    foundH1 = true;
                }
                if (parts[i].ColorType == MarkdownColor.CodeBlock)
                {
                    foundCode = true;
                }
                if (parts[i].ColorType == MarkdownColor.Table)
                {
                    foundTable = true;
                }
            }
            Assert.True(foundH1);
            Assert.True(foundCode);
            Assert.True(foundTable);
        }

        /// <summary>
        /// 空文本返回空列表
        /// </summary>
        [Fact]
        public void MdParseEmptyReturnsEmpty()
        {
            Assert.True(TextBrick.MdParse(null, out List<MarkdownPart> parts));
            Assert.Empty(parts);
        }
    }

    /// <summary>
    /// Shell 积木测试——只验证配置与参数校验，不实际执行命令
    /// </summary>
    public sealed class ShellBrickTests
    {
        /// <summary>
        /// 非法命令返回错误输出
        /// </summary>
        [Fact]
        public void ShellExecRejectsInvalidCommand()
        {
            bool ok = ShellBrick.Exec("", 20, out string output);
            Assert.False(ok);
            Assert.StartsWith("ERR|INVALID_COMMAND", output);
        }
    }
}

    /// <summary>
    /// LLM/Approval/Office/Log 积木测试
    /// </summary>
    public sealed class ExtendedBrickTests
    {
        /// <summary>
        /// 上下文管理——System 保留 + 截断 + sessionKey 隔离
        /// </summary>
        [Fact]
        public void ContextBrickKeepsSystemAndTrims()
        {
            ContextBrick.CtxClear("test-ctx");
            Assert.True(ContextBrick.CtxSetSystem("test-ctx", "你是一只猫"));
            Assert.True(ContextBrick.CtxPushUser("test-ctx", "你好"));
            Assert.True(ContextBrick.CtxPushAssistant("test-ctx", "喵"));
            Assert.True(ContextBrick.CtxCount("test-ctx", out int count));
            Assert.Equal(3, count);

            Assert.True(ContextBrick.CtxBuildPrompt("test-ctx", out string prompt));
            Assert.Contains("你是一只猫", prompt);

            Assert.True(ContextBrick.CtxTrim("test-ctx", 5, out int removed));
            Assert.True(removed > 0);
            Assert.True(ContextBrick.CtxCount("test-ctx", out int after));
            Assert.True(after <= 2);
        }

        /// <summary>
        /// 上下文隔离——不同 sessionKey 互不串话
        /// </summary>
        [Fact]
        public void ContextBrickSessionKey_IsolatesSessions()
        {
            ContextBrick.CtxClear("ctx-a");
            ContextBrick.CtxClear("ctx-b");
            Assert.True(ContextBrick.CtxPushUser("ctx-a", "A 的消息"));
            Assert.True(ContextBrick.CtxPushUser("ctx-b", "B 的消息"));

            Assert.True(ContextBrick.CtxBuildPrompt("ctx-a", out string promptA));
            Assert.True(ContextBrick.CtxBuildPrompt("ctx-b", out string promptB));
            Assert.Contains("A 的消息", promptA);
            Assert.DoesNotContain("B 的消息", promptA);
            Assert.Contains("B 的消息", promptB);
            Assert.DoesNotContain("A 的消息", promptB);
        }

        /// <summary>
        /// 审批——请求/解析/拒绝/快照
        /// </summary>
        [Fact]
        public void ApprovalBrickRequestResolveReject()
        {
            Assert.True(ApprovalBrick.Request("ap-1", "允许吗？",
                new string[] { "允许", "拒绝" }, 0, 20));
            Assert.False(ApprovalBrick.Request("ap-1", "重复", 
                new string[] { "允许", "拒绝" }, 0, 20));
            Assert.True(ApprovalBrick.PendingCount(out int count));
            Assert.Equal(1, count);

            Assert.True(ApprovalBrick.Resolve("ap-1", 1, out ApprovalResult result));
            Assert.Equal(1, result.SelectedIndex);
            Assert.Equal("拒绝", result.SelectedLabel);

            Assert.True(ApprovalBrick.Request("ap-2", "拒绝测试？",
                new string[] { "允许", "拒绝" }, 0, 20));
            Assert.True(ApprovalBrick.Reject("ap-2"));
            Assert.True(ApprovalBrick.PendingCount(out int after));
            Assert.Equal(0, after);
        }

        /// <summary>
        /// 日志——写入/读取/清空
        /// </summary>
        [Fact]
        public void LogBrickWriteGetClear()
        {
            LogBrick.Clear();
            Assert.True(LogBrick.Write("TEST", 0, "hello"));
            Assert.True(LogBrick.Write("TEST", 3, "error"));
            Assert.True(LogBrick.Count(out int count));
            Assert.Equal(2, count);
            Assert.True(LogBrick.GetAll(out string logs));
            Assert.Contains("hello", logs);
            Assert.Contains("ERROR", logs);
            Assert.True(LogBrick.Clear());
            Assert.True(LogBrick.Count(out int cleared));
            Assert.Equal(0, cleared);
        }

        /// <summary>
        /// Excel 写入/读取往返
        /// </summary>
        [Fact]
        public void ExcelBrickWriteReadRoundTrip()
        {
            string path = Path.Combine(Path.GetTempPath(), "mau-excel-"
                + Guid.NewGuid().ToString("N") + ".xlsx");
            try
            {
                Assert.True(ExcelBrick.Write(path, "姓名\t年龄\n张三\t25\n", "Sheet1",
                    out string result));
                Assert.Contains("写入成功", result);
                Assert.True(ExcelBrick.Read(path, "", "tsv", out string content));
                Assert.Contains("张三", content);
                Assert.Contains("25", content);
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        /// <summary>
        /// Word 写入/读取往返
        /// </summary>
        [Fact]
        public void DocxBrickWriteReadRoundTrip()
        {
            string path = Path.Combine(Path.GetTempPath(), "mau-docx-"
                + Guid.NewGuid().ToString("N") + ".docx");
            try
            {
                Assert.True(DocxBrick.Write(path, "第一段\n第二段", out string result));
                Assert.Contains("写入成功", result);
                Assert.True(DocxBrick.Read(path, out string content));
                Assert.Contains("第一段", content);
                Assert.Contains("第二段", content);
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        /// <summary>
        /// 结构化消息——Tool 结果回执 + Assistant 工具声明 + messages JSON 导出（OpenAI 协议）
        /// </summary>
        [Fact]
        public void ContextBrickStructuredMessages_RoundTrip()
        {
            ContextBrick.CtxClear("ctx-struct");
            Assert.True(ContextBrick.CtxSetSystem("ctx-struct", "你是猫"));
            Assert.True(ContextBrick.CtxPushUser("ctx-struct", "读文件"));
            Assert.True(ContextBrick.CtxPushAssistantToolCalls("ctx-struct",
                "[{\"id\":\"call_1\",\"type\":\"function\",\"function\":{\"name\":\"file.read\",\"arguments\":\"{\\\"path\\\":\\\"a.txt\\\"}\"}}]"));
            Assert.True(ContextBrick.CtxPushTool("ctx-struct", "call_1", "文件内容"));

            Assert.True(ContextBrick.CtxBuildMessagesJson("ctx-struct", out string messagesJson));
            using (System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(messagesJson))
            {
                System.Text.Json.JsonElement root = doc.RootElement;
                Assert.Equal(System.Text.Json.JsonValueKind.Array, root.ValueKind);
                Assert.Equal(4, root.GetArrayLength());
                // [0] system
                Assert.Equal("system", root[0].GetProperty("role").GetString());
                // [2] assistant 带 tool_calls
                Assert.Equal("assistant", root[2].GetProperty("role").GetString());
                Assert.True(root[2].TryGetProperty("tool_calls", out _));
                // [3] tool 带 tool_call_id
                Assert.Equal("tool", root[3].GetProperty("role").GetString());
                Assert.Equal("call_1", root[3].GetProperty("tool_call_id").GetString());
                Assert.Equal("文件内容", root[3].GetProperty("content").GetString());
            }

            // 纯文本拼接模式跳过 Tool 角色（结构化回填不影响文本模式）
            Assert.True(ContextBrick.CtxBuildPrompt("ctx-struct", out string prompt));
            Assert.DoesNotContain("文件内容", prompt);
        }
    }

using System;
using System.Collections.Generic;
using Mau.Runtime;
using Xunit;

namespace Mau.Runtime.Tests
{
    /// <summary>
    /// 坏前文修复测试——ReplaceMessages 占位补全（S3：失败轮次/宿主崩溃导致的坏前文最小可用修复）。
    /// 核心语义：向 OpenAI 格式匹配，不做信息损失——tool_calls 声明无结果 → 补占位；孤立 tool → 丢弃。
    /// </summary>
    public sealed class ChatContextRepairTests
    {
        /// <summary>
        /// 构造 assistant 工具调用消息
        /// </summary>
        /// <param name="toolCallsJson">tool_calls JSON 数组</param>
        /// <returns>assistant 消息</returns>
        private static LlmMessage AssistantToolCalls(string toolCallsJson)
        {
            LlmMessage m = new LlmMessage();
            m.Role = LlmRole.Assistant;
            m.Content = "";
            m.ToolCallId = "";
            m.ToolName = "";
            m.ToolCallsJson = toolCallsJson;
            m.ReasoningContent = "思考中";
            return m;
        }

        /// <summary>
        /// 构造 tool 结果消息
        /// </summary>
        /// <param name="id">调用 ID</param>
        /// <param name="name">工具名</param>
        /// <param name="result">结果文本</param>
        /// <returns>tool 消息</returns>
        private static LlmMessage ToolResult(string id, string name, string result)
        {
            LlmMessage m = new LlmMessage();
            m.Role = LlmRole.Tool;
            m.Content = result;
            m.ToolCallId = id;
            m.ToolName = name;
            m.ToolCallsJson = "";
            m.ReasoningContent = "";
            return m;
        }

        /// <summary>
        /// 构造用户消息
        /// </summary>
        /// <param name="text">用户文本</param>
        /// <returns>user 消息</returns>
        private static LlmMessage User(string text)
        {
            LlmMessage m = new LlmMessage();
            m.Role = LlmRole.User;
            m.Content = text;
            m.ToolCallId = "";
            m.ToolName = "";
            m.ToolCallsJson = "";
            m.ReasoningContent = "";
            return m;
        }

        /// <summary>
        /// 构造 system 消息
        /// </summary>
        /// <param name="text">系统提示词</param>
        /// <returns>system 消息</returns>
        private static LlmMessage SystemMsg(string text)
        {
            LlmMessage m = new LlmMessage();
            m.Role = LlmRole.System;
            m.Content = text;
            m.ToolCallId = "";
            m.ToolName = "";
            m.ToolCallsJson = "";
            m.ReasoningContent = "";
            return m;
        }

        /// <summary>
        /// 正常配对——assistant tool_calls + tool 结果完整 → 原样保留
        /// </summary>
        [Fact]
        public void ReplaceMessages_CompletePair_KeepsAll()
        {
            ChatContext ctx = new ChatContext();
            string calls = "[{\"id\":\"call_1\",\"function\":{\"name\":\"text-read\",\"arguments\":\"{}\"}}]";
            LlmMessage[] input = new LlmMessage[]
            {
                SystemMsg("sys"),
                User("读文件"),
                AssistantToolCalls(calls),
                ToolResult("call_1", "text-read", "OK")
            };
            ctx.ReplaceMessages(input);
            LlmMessage[] result = ctx.GetMessages();
            Assert.Equal(4, result.Length);
            Assert.Equal(LlmRole.Tool, result[3].Role);
            Assert.Equal("call_1", result[3].ToolCallId);
            Assert.Equal("OK", result[3].Content);
        }

        /// <summary>
        /// 孤立 tool_calls 声明无结果 → 补占位 tool 消息（协议完整性——LLM 请求不 400）
        /// </summary>
        [Fact]
        public void ReplaceMessages_OrphanToolCalls_AddsPlaceholder()
        {
            ChatContext ctx = new ChatContext();
            string calls = "[{\"id\":\"call_1\",\"function\":{\"name\":\"text-read\",\"arguments\":\"{}\"}}]";
            LlmMessage[] input = new LlmMessage[]
            {
                SystemMsg("sys"),
                User("读文件"),
                AssistantToolCalls(calls)
                // 无 tool 结果——崩溃/失败轮次截断
            };
            ctx.ReplaceMessages(input);
            LlmMessage[] result = ctx.GetMessages();
            Assert.Equal(4, result.Length);
            Assert.Equal(LlmRole.Tool, result[3].Role);
            Assert.Equal("call_1", result[3].ToolCallId);
            Assert.Equal("text-read", result[3].ToolName);
            Assert.StartsWith("(前文修复)", result[3].Content);
        }

        /// <summary>
        /// 部分缺失——5 个声明只有 2 个结果 → 补 3 个占位，2 个真实结果保留
        /// </summary>
        [Fact]
        public void ReplaceMessages_PartialResults_FillsMissingOnly()
        {
            ChatContext ctx = new ChatContext();
            string calls = "[{\"id\":\"call_1\",\"function\":{\"name\":\"text-read\",\"arguments\":\"{}\"}},"
                + "{\"id\":\"call_2\",\"function\":{\"name\":\"text-write\",\"arguments\":\"{}\"}},"
                + "{\"id\":\"call_3\",\"function\":{\"name\":\"text-find\",\"arguments\":\"{}\"}},"
                + "{\"id\":\"call_4\",\"function\":{\"name\":\"text-grep\",\"arguments\":\"{}\"}},"
                + "{\"id\":\"call_5\",\"function\":{\"name\":\"text-tree\",\"arguments\":\"{}\"}}]";
            LlmMessage[] input = new LlmMessage[]
            {
                SystemMsg("sys"),
                User("批量操作"),
                AssistantToolCalls(calls),
                ToolResult("call_1", "text-read", "R1"),
                ToolResult("call_4", "text-grep", "R4")
            };
            ctx.ReplaceMessages(input);
            LlmMessage[] result = ctx.GetMessages();
            // system + user + assistant + 2 真实 + 3 占位 = 8
            Assert.Equal(8, result.Length);
            int realCount = 0;
            int placeholderCount = 0;
            for (int i = 0; i < result.Length; i++)
            {
                if (result[i].Role == LlmRole.Tool && result[i].Content.StartsWith("(前文修复)"))
                {
                    placeholderCount = placeholderCount + 1;
                }
                else if (result[i].Role == LlmRole.Tool)
                {
                    realCount = realCount + 1;
                }
            }
            Assert.Equal(2, realCount);
            Assert.Equal(3, placeholderCount);
        }

        /// <summary>
        /// 孤立 tool 结果（有 ID 但无任何 assistant 声明）→ 丢弃（协议不允许游离 tool 消息）
        /// </summary>
        [Fact]
        public void ReplaceMessages_OrphanToolResult_Drops()
        {
            ChatContext ctx = new ChatContext();
            LlmMessage[] input = new LlmMessage[]
            {
                SystemMsg("sys"),
                User("读文件"),
                ToolResult("call_ghost", "text-read", "OK")
                // 无对应 assistant tool_calls 声明
            };
            ctx.ReplaceMessages(input);
            LlmMessage[] result = ctx.GetMessages();
            Assert.Equal(2, result.Length);
            for (int i = 0; i < result.Length; i++)
            {
                Assert.NotEqual(LlmRole.Tool, result[i].Role);
            }
        }

        /// <summary>
        /// tool 无 ID → 丢弃（已有行为保持）
        /// </summary>
        [Fact]
        public void ReplaceMessages_ToolWithoutId_Drops()
        {
            ChatContext ctx = new ChatContext();
            LlmMessage[] input = new LlmMessage[]
            {
                SystemMsg("sys"),
                User("读文件"),
                ToolResult("", "text-read", "OK")
            };
            ctx.ReplaceMessages(input);
            LlmMessage[] result = ctx.GetMessages();
            Assert.Equal(2, result.Length);
        }

        /// <summary>
        /// 同 ID 重复结果 → 保留第一条（防重复 tool 消息）
        /// </summary>
        [Fact]
        public void ReplaceMessages_DuplicateToolResult_KeepsFirst()
        {
            ChatContext ctx = new ChatContext();
            string calls = "[{\"id\":\"call_1\",\"function\":{\"name\":\"text-read\",\"arguments\":\"{}\"}}]";
            LlmMessage[] input = new LlmMessage[]
            {
                SystemMsg("sys"),
                User("读文件"),
                AssistantToolCalls(calls),
                ToolResult("call_1", "text-read", "R1"),
                ToolResult("call_1", "text-read", "R2")
            };
            ctx.ReplaceMessages(input);
            LlmMessage[] result = ctx.GetMessages();
            int toolCount = 0;
            for (int i = 0; i < result.Length; i++)
            {
                if (result[i].Role == LlmRole.Tool)
                {
                    toolCount = toolCount + 1;
                    Assert.Equal("R1", result[i].Content);
                }
            }
            Assert.Equal(1, toolCount);
        }

        /// <summary>
        /// tool_calls JSON 解析失败 → 降级纯文本（保留 content/reasoning，不毒化请求）
        /// </summary>
        [Fact]
        public void ReplaceMessages_BrokenToolCallsJson_DowngradesToText()
        {
            ChatContext ctx = new ChatContext();
            LlmMessage[] input = new LlmMessage[]
            {
                SystemMsg("sys"),
                User("读文件"),
                AssistantToolCalls("{broken json")
            };
            ctx.ReplaceMessages(input);
            LlmMessage[] result = ctx.GetMessages();
            Assert.Equal(3, result.Length);
            Assert.Equal(LlmRole.Assistant, result[2].Role);
            Assert.Equal("", result[2].ToolCallsJson);
        }

        /// <summary>
        /// 缺字段消息——null 字段归一为空串（已有行为保持）
        /// </summary>
        [Fact]
        public void ReplaceMessages_NullFields_Normalized()
        {
            ChatContext ctx = new ChatContext();
            LlmMessage bad = new LlmMessage();
            bad.Role = LlmRole.User;
            // 其余字段默认 null——模拟缺字段（struct 默认值）
            LlmMessage[] input = new LlmMessage[] { bad };
            ctx.ReplaceMessages(input);
            LlmMessage[] result = ctx.GetMessages();
            Assert.Single(result);
            Assert.Equal("", result[0].Content);
            Assert.Equal("", result[0].ToolCallId);
            Assert.Equal("", result[0].ToolCallsJson);
        }

        /// <summary>
        /// system 唯一——多余 system 丢弃（已有行为保持）
        /// </summary>
        [Fact]
        public void ReplaceMessages_MultipleSystem_KeepsFirst()
        {
            ChatContext ctx = new ChatContext();
            LlmMessage[] input = new LlmMessage[]
            {
                SystemMsg("sys1"),
                SystemMsg("sys2"),
                User("你好")
            };
            ctx.ReplaceMessages(input);
            LlmMessage[] result = ctx.GetMessages();
            Assert.Equal(2, result.Length);
            Assert.Equal("sys1", result[0].Content);
        }
    }

    /// <summary>
    /// 会话前文损坏备份测试——TryLoad 损坏 JSON → 备份 .bad + 返回 false（S3）。
    /// </summary>
    public sealed class SessionStoreRepairTests
    {
        /// <summary>
        /// 损坏 JSON → TryLoad 返回 false + 原文件改名 .bad（可手工修复/追溯）
        /// </summary>
        [Fact]
        public void TryLoad_CorruptJson_BacksUpBad()
        {
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ch4_s3_" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(dir);
            string path = System.IO.Path.Combine(dir, "session.json");
            string badPath = path + ".bad";
            System.IO.File.WriteAllText(path, "{broken json");
            try
            {
                SessionStore store = new SessionStore(path);
                LlmMessage[] messages;
                bool ok = store.TryLoad(out messages);
                Assert.False(ok);
                Assert.True(System.IO.File.Exists(badPath), "损坏文件应备份为 .bad");
                Assert.False(System.IO.File.Exists(path), "原文件应移走（不重复解析坏文件）");
            }
            finally
            {
                System.IO.Directory.Delete(dir, true);
            }
        }

        /// <summary>
        /// 正常 JSON → TryLoad 返回 true + 原文件保留（备份不触发）
        /// </summary>
        [Fact]
        public void TryLoad_ValidJson_KeepsFile()
        {
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ch4_s3_" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(dir);
            string path = System.IO.Path.Combine(dir, "session.json");
            string badPath = path + ".bad";
            string json = "{\"Messages\":[{\"Role\":0,\"Content\":\"sys\"}]}";
            System.IO.File.WriteAllText(path, json);
            try
            {
                SessionStore store = new SessionStore(path);
                LlmMessage[] messages;
                bool ok = store.TryLoad(out messages);
                Assert.True(ok);
                Assert.Single(messages);
                Assert.False(System.IO.File.Exists(badPath), "正常加载不应产生 .bad 备份");
            }
            finally
            {
                System.IO.Directory.Delete(dir, true);
            }
        }
    }
}

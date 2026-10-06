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
            Assert.StartsWith("[系统自动修复]", result[3].Content);
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
                if (result[i].Role == LlmRole.Tool && result[i].Content.StartsWith("[系统自动修复]"))
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
            string json = "{\"t\":\"m\",\"Role\":0,\"Content\":\"sys\"}";
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
        /// <summary>
        /// 会话标识落盘记录——Rewrite/Append 写进 meta 行；读面不回填（身份 = 猫 key，由宿主构造注入）。
        /// </summary>
        [Fact]
        public void SessionId_RoundTrip()
        {
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ch4_sid_" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(dir);
            string path = System.IO.Path.Combine(dir, "session.json");
            try
            {
                SessionStore store = new SessionStore(path);
                store.SessionId = "639248494051468777";
                LlmMessage[] messages = new LlmMessage[1];
                messages[0].Role = LlmRole.User;
                messages[0].Content = "hi";
                store.Rewrite(messages);
                SessionStore reloaded = new SessionStore(path);
                LlmMessage[] got;
                Assert.True(reloaded.TryLoad(out got));
                Assert.Equal("", reloaded.SessionId);
                Assert.Contains("639248494051468777", System.IO.File.ReadAllText(path));
            }
            finally
            {
                System.IO.Directory.Delete(dir, true);
            }
        }
        /// <summary>
        /// 旧格式文件（非 JSONL——A47 不兼容）→ 按无前文处理；不可解析内容备份 .bad（数据不丢）。
        /// </summary>
        [Fact]
        public void LegacyJsonFile_NotCompatible_ReturnsFalse()
        {
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ch4_sid_" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(dir);
            string path = System.IO.Path.Combine(dir, "session.json");
            System.IO.File.WriteAllText(path, "{\"Messages\":[{\"Role\":0,\"Content\":\"sys\"}]}");
            try
            {
                SessionStore store = new SessionStore(path);
                LlmMessage[] messages;
                Assert.False(store.TryLoad(out messages));
                Assert.True(System.IO.File.Exists(path + ".bad"), "不可解析文件应备份 .bad（数据不丢）");
            }
            finally
            {
                System.IO.Directory.Delete(dir, true);
            }
        }

        /// <summary>
        /// 构造指定角色消息
        /// </summary>
        /// <param name="role">角色</param>
        /// <param name="content">正文</param>
        /// <returns>消息</returns>
        private static LlmMessage MakeMessage(LlmRole role, string content)
        {
            LlmMessage m = new LlmMessage();
            m.Role = role;
            m.Content = content;
            m.ToolCallId = "";
            m.ToolName = "";
            m.ToolCallsJson = "";
            m.ReasoningContent = "";
            return m;
        }

        /// <summary>
        /// 增量落盘——每消息 append 逐行 JSONL；读面按序恢复（元数据行不占消息索引）。
        /// </summary>
        [Fact]
        public void AppendMessages_RoundTrip()
        {
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ch4_a47_" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(dir);
            string path = System.IO.Path.Combine(dir, "session.jsonl");
            try
            {
                SessionStore store = new SessionStore(path);
                store.SessionId = "sid-a47";
                store.Append(MakeMessage(LlmRole.System, "sys"));
                store.Append(MakeMessage(LlmRole.User, "你好"));
                SessionStore reloaded = new SessionStore(path);
                LlmMessage[] got;
                Assert.True(reloaded.TryLoad(out got));
                Assert.Equal(2, got.Length);
                Assert.Equal("sys", got[0].Content);
                Assert.Equal("你好", got[1].Content);
                Assert.Equal("", reloaded.SessionId);
                Assert.Contains("sid-a47", System.IO.File.ReadAllText(path));
            }
            finally
            {
                System.IO.Directory.Delete(dir, true);
            }
        }

        /// <summary>
        /// 末行残缺补全——崩溃在半截 JSON：结构补齐为合法行 + Content 追加修复标注；已落盘内容不丢。
        /// </summary>
        [Fact]
        public void TruncatedLastLine_RepairedWithNote()
        {
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ch4_a47_" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(dir);
            string path = System.IO.Path.Combine(dir, "session.jsonl");
            string text = "{\"t\":\"meta\",\"SessionId\":\"sid-trunc\"}\n{\"t\":\"m\",\"Role\":0,\"Content\":\"sys\"}\n{\"t\":\"m\",\"Role\":1,\"Content\":\"半截回复";
            System.IO.File.WriteAllText(path, text);
            try
            {
                SessionStore store = new SessionStore(path);
                LlmMessage[] got;
                Assert.True(store.TryLoad(out got));
                Assert.Equal(2, got.Length);
                Assert.StartsWith("半截回复", got[1].Content);
                Assert.Contains("[系统自动修复]", got[1].Content);
                Assert.Equal("", store.SessionId);
            }
            finally
            {
                System.IO.Directory.Delete(dir, true);
            }
        }

        /// <summary>
        /// 非末行损坏——跳过坏行 + 告警，其余照常加载（不整文件作废）。
        /// </summary>
        [Fact]
        public void BrokenMiddleLine_SkippedOthersLoaded()
        {
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ch4_a47_" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(dir);
            string path = System.IO.Path.Combine(dir, "session.jsonl");
            string text = "{\"t\":\"m\",\"Role\":0,\"Content\":\"sys\"}\n{坏行\n{\"t\":\"m\",\"Role\":2,\"Content\":\"hi\"}";
            System.IO.File.WriteAllText(path, text);
            try
            {
                SessionStore store = new SessionStore(path);
                LlmMessage[] got;
                Assert.True(store.TryLoad(out got));
                Assert.Equal(2, got.Length);
                Assert.Equal("sys", got[0].Content);
                Assert.Equal("hi", got[1].Content);
            }
            finally
            {
                System.IO.Directory.Delete(dir, true);
            }
        }

        /// <summary>
        /// 工具调用无结果——读面保留声明行；ReplaceMessages 补配对占位（系统自动修复标注）。
        /// </summary>
        [Fact]
        public void ToolCallWithoutResult_PlaceholderRepaired()
        {
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ch4_a47_" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(dir);
            string path = System.IO.Path.Combine(dir, "session.jsonl");
            try
            {
                SessionStore store = new SessionStore(path);
                store.Append(MakeMessage(LlmRole.System, "sys"));
                LlmMessage decl = new LlmMessage();
                decl.Role = LlmRole.Assistant;
                decl.Content = "";
                decl.ToolCallId = "";
                decl.ToolName = "";
                decl.ToolCallsJson = "[{\"id\":\"call_1\",\"type\":\"function\",\"function\":{\"name\":\"text-read\",\"arguments\":\"{}\"}}]";
                decl.ReasoningContent = "";
                store.Append(decl);
                SessionStore reloaded = new SessionStore(path);
                LlmMessage[] got;
                Assert.True(reloaded.TryLoad(out got));
                Assert.Equal(2, got.Length);
                ChatContext ctx = new ChatContext();
                ctx.ReplaceMessages(got);
                LlmMessage[] repaired = ctx.GetMessages();
                Assert.Equal(3, repaired.Length);
                Assert.Equal(LlmRole.Tool, repaired[2].Role);
                Assert.Contains("[系统自动修复]", repaired[2].Content);
            }
            finally
            {
                System.IO.Directory.Delete(dir, true);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Mau.Runtime;
using CatHome4.Contracts;
using Xunit;

namespace CatHome4.Core.Tests
{
    /// <summary>
    /// 系统注入（sys 入口）测试——A204 后端面：类型值域 / 前缀加工 / 队列受理门禁 / 视图 src 标记 / 补差回落 / 人工缺省兼容 / 轮首取尽。
    /// </summary>
    [Collection("GlobalToolState")]
    public class ChatSessionSystemInjectTests
    {
        /// <summary>
        /// Mock LLM——固定纯文本回复（系统注入门禁与标记测试不涉工具）。
        /// </summary>
        private sealed class PlainLlm : ILlmRuntime
        {
            /// <summary>ChatStream 调用次数</summary>
            public int CallCount = 0;

            /// <summary>
            /// 流式对话——恒定文本回复。
            /// </summary>
            /// <param name="messages">消息序列</param>
            /// <param name="tools">工具定义</param>
            /// <param name="userId">用户标识</param>
            /// <param name="ct">取消令牌</param>
            /// <returns>事件流</returns>
            public async IAsyncEnumerable<LlmStreamEvent> ChatStream(LlmMessage[] messages, ToolSpec[] tools, string userId = "", [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
            {
                CallCount = CallCount + 1;
                yield return new LlmStreamEvent(LlmStreamKind.Text, "ok");
                await Task.Yield();
                yield return new LlmStreamEvent(LlmStreamKind.Done, "");
            }
        }

        /// <summary>
        /// 构造测试会话——临时前文 / 视图文件 + Mock LLM（工具面仅 Note）。
        /// </summary>
        /// <param name="llm">LLM 运行时</param>
        /// <param name="viewStore">输出——视图层实例（载荷断言用）</param>
        /// <returns>会话实体</returns>
        private static CH4.ChatSession CreateSession(PlainLlm llm, out CH4.SessionViewStore viewStore)
        {
            List<ToolSpec> regSpecs = new List<ToolSpec>();
            regSpecs.Add(new ToolSpec("Note", "Note 任务追踪", "{}"));
            CH4.ToolRegistry.Init(regSpecs.ToArray(), null, null);
            ChatContext ctx = new ChatContext();
            string tmp = Path.Combine(Path.GetTempPath(), "cat4sysinj_" + Guid.NewGuid().ToString("N") + ".jsonl");
            SessionStore store = new SessionStore(tmp);
            OA oa = new OA(new ThreadGuard());
            ToolSpec[] tools = new ToolSpec[] { new ToolSpec("Note", "Note 任务追踪", "{}") };
            viewStore = new CH4.SessionViewStore(Path.Combine(Path.GetTempPath(), "cat4sysinj_" + Guid.NewGuid().ToString("N") + ".view.json"));
            return new CH4.ChatSession("test-session", "test", ctx, store, llm, oa, tools, delegate (string name, string args) { return "ERR|NO_TOOL|" + name; }, viewStore);
        }

        /// <summary>
        /// 泵会话直到 Idle——上限 200 帧（防死循环）。
        /// </summary>
        /// <param name="session">会话</param>
        private static void PumpUntilIdle(CH4.ChatSession session)
        {
            for (int i = 0; i < 200; i = i + 1)
            {
                session.Pump();
                Thread.Sleep(5);
                if (session.IsIdle && i >= 2)
                {
                    break;
                }
            }
        }

        /// <summary>提取前文全部 user 消息内容（写入序）。</summary>
        /// <param name="session">会话</param>
        /// <returns>user 消息内容数组</returns>
        private static List<string> UserContents(CH4.ChatSession session)
        {
            List<string> list = new List<string>();
            LlmMessage[] all = session.Context.GetMessages();
            for (int i = 0; i < all.Length; i = i + 1)
            {
                if (all[i].Role == LlmRole.User)
                {
                    list.Add(all[i].Content);
                }
            }
            return list;
        }

        /// <summary>取视图层最后一条指定类型的载荷 JSON（无则空串）。</summary>
        /// <param name="viewStore">视图层</param>
        /// <param name="renderType">渲染类型</param>
        /// <returns>载荷 JSON</returns>
        private static string LastPayload(CH4.SessionViewStore viewStore, string renderType)
        {
            CH4.ViewBlock[] blocks = viewStore.GetBlocks();
            for (int i = blocks.Length - 1; i >= 0; i = i - 1)
            {
                if (blocks[i].RenderType == renderType)
                {
                    return blocks[i].Payload;
                }
            }
            return "";
        }

        /// <summary>取视图层指定类型载荷 JSON 数组（到达序）。</summary>
        /// <param name="viewStore">视图层</param>
        /// <param name="renderType">渲染类型</param>
        /// <returns>载荷 JSON 数组</returns>
        private static List<string> Payloads(CH4.SessionViewStore viewStore, string renderType)
        {
            List<string> list = new List<string>();
            CH4.ViewBlock[] blocks = viewStore.GetBlocks();
            for (int i = 0; i < blocks.Length; i = i + 1)
            {
                if (blocks[i].RenderType == renderType)
                {
                    list.Add(blocks[i].Payload);
                }
            }
            return list;
        }

        /// <summary>
        /// 类型值域——未知类型拒绝（参数面零容忍：不静默回落）。
        /// </summary>
        [Fact]
        public void PostSystemMessage_UnknownKind_Rejected()
        {
            PlainLlm llm = new PlainLlm();
            CH4.SessionViewStore viewStore;
            CH4.ChatSession session = CreateSession(llm, out viewStore);
            int before = session.Context.GetMessages().Length;
            string r = session.PostSystemMessage("majordomo", "内容");
            Assert.StartsWith("ERR|BAD_ARGS", r);
            Assert.Equal(before, session.Context.GetMessages().Length);
            Assert.Equal(0, llm.CallCount);
        }

        /// <summary>
        /// 空内容拒绝——空白串不入队。
        /// </summary>
        [Fact]
        public void PostSystemMessage_EmptyContent_Rejected()
        {
            PlainLlm llm = new PlainLlm();
            CH4.SessionViewStore viewStore;
            CH4.ChatSession session = CreateSession(llm, out viewStore);
            Assert.StartsWith("ERR|BAD_ARGS", session.PostSystemMessage("systemauto", "   "));
            Assert.StartsWith("ERR|BAD_ARGS", session.PostSystemMessage("systemauto", null));
        }

        /// <summary>
        /// systemauto 受理——行头前缀入前文 + 视图载荷落 src。
        /// </summary>
        [Fact]
        public void PostSystemMessage_SystemAuto_PrefixAndSrc()
        {
            PlainLlm llm = new PlainLlm();
            CH4.SessionViewStore viewStore;
            CH4.ChatSession session = CreateSession(llm, out viewStore);
            Assert.Equal("OK", session.PostSystemMessage("systemauto", "（系统自动 · 测试）内容"));
            PumpUntilIdle(session);
            List<string> users = UserContents(session);
            Assert.Single(users);
            Assert.StartsWith(CH4.ChatSession.SysInjectPrefix, users[0]);
            Assert.EndsWith("内容", users[0]);
            using (JsonDocument doc = JsonDocument.Parse(LastPayload(viewStore, "user")))
            {
                Assert.Equal("systemauto", doc.RootElement.GetProperty("src").GetString());
                Assert.StartsWith(CH4.ChatSession.SysInjectPrefix, doc.RootElement.GetProperty("text").GetString());
            }
        }

        /// <summary>
        /// 人工输入——视图载荷不落 src（缺省即人工，旧 view.json 零迁移）。
        /// </summary>
        [Fact]
        public void PostUserMessage_HumanPayload_NoSrcField()
        {
            PlainLlm llm = new PlainLlm();
            CH4.SessionViewStore viewStore;
            CH4.ChatSession session = CreateSession(llm, out viewStore);
            Assert.True(session.PostUserMessage("人工一行"));
            PumpUntilIdle(session);
            using (JsonDocument doc = JsonDocument.Parse(LastPayload(viewStore, "user")))
            {
                Assert.Equal("人工一行", doc.RootElement.GetProperty("text").GetString());
                JsonElement ignored;
                Assert.False(doc.RootElement.TryGetProperty("src", out ignored));
            }
        }

        /// <summary>
        /// 混合注入——系统与人工各按自身标记落字段，且前文顺序按入队序（FIFO）。
        /// </summary>
        [Fact]
        public void MixedInject_OrderAndMark()
        {
            PlainLlm llm = new PlainLlm();
            CH4.SessionViewStore viewStore;
            CH4.ChatSession session = CreateSession(llm, out viewStore);
            Assert.Equal("OK", session.PostSystemMessage("systemauto", "系统条"));
            Assert.True(session.PostUserMessage("人工条"));
            PumpUntilIdle(session);
            List<string> users = UserContents(session);
            Assert.Equal(2, users.Count);
            Assert.StartsWith(CH4.ChatSession.SysInjectPrefix, users[0]);
            Assert.Equal("人工条", users[1]);
            List<string> payloads = Payloads(viewStore, "user");
            Assert.Equal(2, payloads.Count);
            using (JsonDocument doc = JsonDocument.Parse(payloads[0]))
            {
                Assert.Equal("systemauto", doc.RootElement.GetProperty("src").GetString());
            }
            using (JsonDocument doc = JsonDocument.Parse(payloads[1]))
            {
                JsonElement ignored;
                Assert.False(doc.RootElement.TryGetProperty("src", out ignored));
            }
        }

        /// <summary>
        /// 轮首取尽——同帧多条入队合并为一轮（一次 LLM 调用，前文两条 user 依序）。
        /// </summary>
        [Fact]
        public void Queue_DrainAllInOneRound()
        {
            PlainLlm llm = new PlainLlm();
            CH4.SessionViewStore viewStore;
            CH4.ChatSession session = CreateSession(llm, out viewStore);
            Assert.True(session.PostUserMessage("第一条"));
            Assert.True(session.PostUserMessage("第二条"));
            PumpUntilIdle(session);
            Assert.Equal(1, llm.CallCount);
            List<string> users = UserContents(session);
            Assert.Equal(2, users.Count);
            Assert.Equal("第一条", users[0]);
            Assert.Equal("第二条", users[1]);
        }

        /// <summary>
        /// 视图来源投影——人工（user / 空 / null）空串；系统注入（含延迟族）一律 systemauto。
        /// </summary>
        [Fact]
        public void ViewSrcOf_HumanAndSystem()
        {
            Assert.Equal("", CH4.ChatSession.ViewSrcOf("user"));
            Assert.Equal("", CH4.ChatSession.ViewSrcOf(""));
            Assert.Equal("", CH4.ChatSession.ViewSrcOf(null));
            Assert.Equal("systemauto", CH4.ChatSession.ViewSrcOf("systemauto"));
            Assert.Equal("systemauto", CH4.ChatSession.ViewSrcOf("sleep"));
            Assert.Equal("systemauto", CH4.ChatSession.ViewSrcOf("timer"));
            Assert.Equal("systemauto", CH4.ChatSession.ViewSrcOf("delay"));
            Assert.Equal("systemauto", CH4.ChatSession.ViewSrcOf("restart"));
        }

        /// <summary>
        /// 补差回落——行头前缀判系统注入；前缀缺失 / 前缀非行头 = 人工。
        /// </summary>
        [Fact]
        public void SysSrcFromContent_PrefixFallback()
        {
            Assert.Equal("systemauto", CH4.ChatSession.SysSrcFromContent("[systemauto]（系统自动 · x）y"));
            Assert.Equal("", CH4.ChatSession.SysSrcFromContent("人工输入"));
            Assert.Equal("", CH4.ChatSession.SysSrcFromContent("前缀在中间 [systemauto]"));
            Assert.Equal("", CH4.ChatSession.SysSrcFromContent(null));
        }

        /// <summary>
        /// 停机态门禁——宿主重启中系统注入与人工输入同被拒（失败可见，不静默）。
        /// </summary>
        [Fact]
        public void PostSystemMessage_HostRestarting_Rejected()
        {
            PlainLlm llm = new PlainLlm();
            CH4.SessionViewStore viewStore;
            CH4.ChatSession session = CreateSession(llm, out viewStore);
            DataBox.Set<string>("global", "host_restart_state", "requested");
            try
            {
                Assert.StartsWith("ERR|HOST_RESTARTING", session.PostSystemMessage("systemauto", "内容"));
                Assert.False(session.PostUserMessage("人工一行"));
                Assert.Equal(0, llm.CallCount);
            }
            finally
            {
                DataBox.Set<string>("global", "host_restart_state", "");
            }
        }

        /// <summary>
        /// 桥入口目标解析——空目标 / 会话不存在各报 ERR；命中则经会话 sys 入口受理。
        /// </summary>
        [Fact]
        public void ChatBridge_PostSystemMessage_CatLookup()
        {
            CH4.ChatBridge bridge = new CH4.ChatBridge(null);
            Assert.StartsWith("ERR|BAD_ARGS", bridge.PostSystemMessage("", "systemauto", "x"));
            Assert.StartsWith("ERR|NO_CAT", bridge.PostSystemMessage("nope", "systemauto", "x"));
            PlainLlm llm = new PlainLlm();
            CH4.SessionViewStore viewStore;
            CH4.ChatSession session = CreateSession(llm, out viewStore);
            bridge.RegisterSession(session);
            Assert.Equal("OK", bridge.PostSystemMessage("test-session", "systemauto", "经桥注入"));
            PumpUntilIdle(session);
            List<string> users = UserContents(session);
            Assert.Single(users);
            Assert.StartsWith(CH4.ChatSession.SysInjectPrefix, users[0]);
            Assert.EndsWith("经桥注入", users[0]);
        }

        /// <summary>
        /// 前后文成对——注入同时落前文与视图（两面并列，各自持久化）。
        /// </summary>
        [Fact]
        public void SystemInject_LandsInBothFaces()
        {
            PlainLlm llm = new PlainLlm();
            CH4.SessionViewStore viewStore;
            CH4.ChatSession session = CreateSession(llm, out viewStore);
            Assert.Equal("OK", session.PostSystemMessage("systemauto", "双面落位"));
            PumpUntilIdle(session);
            Assert.Collection(UserContents(session),
                delegate (string c) { Assert.EndsWith("双面落位", c); });
            Assert.Single(Payloads(viewStore, "user"));
        }

        /// <summary>
        /// 延迟族来源——入前文同样带行头前缀，视图 src 同为 systemauto（口径与 sys 入口一致）。
        /// </summary>
        [Fact]
        public void DelaySource_GetsPrefixAndSrc()
        {
            PlainLlm llm = new PlainLlm();
            CH4.SessionViewStore viewStore;
            CH4.ChatSession session = CreateSession(llm, out viewStore);
            Assert.True(session.PostUserMessage("（定时唤醒 · 60 秒已到）", "sleep"));
            PumpUntilIdle(session);
            List<string> users = UserContents(session);
            Assert.Single(users);
            Assert.StartsWith(CH4.ChatSession.SysInjectPrefix, users[0]);
            using (JsonDocument doc = JsonDocument.Parse(LastPayload(viewStore, "user")))
            {
                Assert.Equal("systemauto", doc.RootElement.GetProperty("src").GetString());
            }
        }

        /// <summary>
        /// 前缀加工幂等——已带前缀不重复套（入口侧与落前文侧共用同一实现）。
        /// </summary>
        [Fact]
        public void BuildSysInjectText_Idempotent()
        {
            Assert.Equal("[systemauto]内容", CH4.ChatSession.BuildSysInjectText("内容"));
            Assert.Equal("[systemauto]内容", CH4.ChatSession.BuildSysInjectText("[systemauto]内容"));
            Assert.Equal("[systemauto]", CH4.ChatSession.BuildSysInjectText(null));
        }
    }
}

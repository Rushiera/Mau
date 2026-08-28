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
    /// ChatSession 核心测试——相位环/工具批收敛/Note 全链（S7 程序集拆分验收）。
    /// 覆盖：纯 LLM 回复相位环闭环 / 内置工具（Note）工具批路径 / Note 手动添加与启动 / 工具批收敛上限。
    /// </summary>
    public class ChatSessionTests
    {
        /// <summary>
        /// Mock LLM 运行时——按队列返回 tool_calls 或纯文本（每次 ChatStream 调用消费一个）。
        /// </summary>
        private sealed class MockLlm : ILlmRuntime
        {
            /// <summary>每轮 tool_calls JSON 队列——空=返回纯文本</summary>
            public Queue<string> ToolCallsQueue = new Queue<string>();

            /// <summary>纯文本回复内容</summary>
            public string ReplyText = "ok";

            /// <summary>E3——usage JSON（非空则在文本前产 Usage 事件；null=不产）</summary>
            public string UsageJson = "";

            /// <summary>ChatStream 调用次数——工具批收敛验证</summary>
            public int CallCount = 0;

            /// <summary>
            /// 流式对话——按队列返回工具调用或纯文本。
            /// </summary>
            /// <param name="messages">消息序列</param>
            /// <param name="tools">工具定义</param>
            /// <param name="userId">用户标识</param>
            /// <param name="ct">取消令牌</param>
            /// <returns>事件流</returns>
            public async IAsyncEnumerable<LlmStreamEvent> ChatStream(LlmMessage[] messages, ToolSpec[] tools, string userId = "", [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
            {
                CallCount = CallCount + 1;
                if (ToolCallsQueue.Count > 0)
                {
                    string tc = ToolCallsQueue.Dequeue();
                    yield return new LlmStreamEvent(LlmStreamKind.ToolCalls, tc);
                    await Task.Yield();
                    yield return new LlmStreamEvent(LlmStreamKind.Done, "");
                    yield break;
                }
                // E3——usage 事件（文本前产——模拟 usage-only 尾帧到达）
                if (UsageJson != null && UsageJson.Length > 0)
                {
                    yield return new LlmStreamEvent(LlmStreamKind.Usage, UsageJson);
                }
                yield return new LlmStreamEvent(LlmStreamKind.Text, ReplyText);
                await Task.Yield();
                yield return new LlmStreamEvent(LlmStreamKind.Done, "");
            }
        }

        /// <summary>
        /// Mock 宿主推送面——捕获 SSE 事件调用（E3 usage 转发断言）。
        /// </summary>
        private sealed class MockHost : IHostPush
        {
            /// <summary>捕获的 PushLlm 调用——kind → text 列表</summary>
            public Dictionary<string, List<string>> LlmEvents = new Dictionary<string, List<string>>();

            /// <summary>用户消息事件（不捕获）</summary>
            public void PushUserMessage(string text, string source) { }

            /// <summary>LLM 事件捕获——按 kind 累积</summary>
            /// <param name="kind">事件类型</param>
            /// <param name="text">载荷</param>
            public void PushLlm(string kind, string text)
            {
                List<string> list;
                if (!LlmEvents.TryGetValue(kind, out list))
                {
                    list = new List<string>();
                    LlmEvents[kind] = list;
                }
                list.Add(text);
            }

            /// <summary>工具结果事件（不捕获）</summary>
            public void PushToolResult(string name, string arguments, string result) { }

            /// <summary>会话完成事件（不捕获）</summary>
            public void PushChatDone(int count) { }

            /// <summary>Note 状态事件（不捕获）</summary>
            public void PushNoteState(string json) { }

            /// <summary>捕获的 PushView 调用——renderType → payload 列表（F4 视图事件）</summary>
            public Dictionary<string, List<string>> ViewEvents = new Dictionary<string, List<string>>();

            /// <summary>视图事件序号——递增分配（F4）</summary>
            private int _viewSeq;

            /// <summary>视图事件捕获——按 renderType 累积（F4）</summary>
            /// <param name="renderType">渲染类型</param>
            /// <param name="payload">载荷 JSON</param>
            /// <param name="replaceSeq">被替换序号（忽略）</param>
            /// <param name="seqHint">序号提示（忽略——测试独立分配）</param>
            /// <returns>分配序号</returns>
            public int PushView(string renderType, string payload, long replaceSeq, long seqHint)
            {
                List<string> list;
                if (!ViewEvents.TryGetValue(renderType, out list))
                {
                    list = new List<string>();
                    ViewEvents[renderType] = list;
                }
                list.Add(payload);
                _viewSeq = _viewSeq + 1;
                return _viewSeq;
            }
        }

        /// <summary>
        /// 构造测试会话——临时前文文件 + Mock LLM + 空工具表。
        /// </summary>
        /// <param name="llm">LLM 运行时</param>
        /// <returns>会话实体</returns>
        private static CH4.ChatSession CreateSession(ILlmRuntime llm)
        {
            ChatContext ctx = new ChatContext();
            string tmp = Path.Combine(Path.GetTempPath(), "cat4test_" + Guid.NewGuid().ToString("N") + ".json");
            SessionStore store = new SessionStore(tmp);
            OA oa = new OA(new ThreadGuard());
            // 测试工具声明面——含内置 Note（IsToolAllowed 声明面拦截需要；OA 工具测试走内置避免无消费者卡死）
            ToolSpec[] tools = new ToolSpec[]
            {
                new ToolSpec("Note", "Note 任务追踪", "{}")
            };
            CH4.SessionViewStore viewStore = new CH4.SessionViewStore(Path.Combine(Path.GetTempPath(), "cat4test_" + Guid.NewGuid().ToString("N") + ".view.json"));
            CH4.ChatSession session = new CH4.ChatSession("test-session", "test", ctx, store, llm, oa, tools, delegate(string name, string args) { return "ERR|NO_TOOL|" + name; }, viewStore);
            return session;
        }

        /// <summary>
        /// 泵会话直到 Idle——上限 200 帧（防死循环）。
        /// </summary>
        /// <param name="session">会话</param>
        private static void PumpUntilIdle(CH4.ChatSession session)
        {
            // 至少泵 3 帧消费 pending（初始 Idle 但 pending 有内容）；后台 LLM 消费需真实时间片（sleep 5ms）
            for (int i = 0; i < 500; i = i + 1)
            {
                session.Pump();
                Thread.Sleep(5);
                if (session.IsIdle && i >= 2)
                {
                    break;
                }
            }
        }

        /// <summary>
        /// 提取最近一条 assistant 文本——从会话上下文。
        /// </summary>
        /// <param name="session">会话</param>
        /// <returns>assistant 内容（无则空串）</returns>
        private static string GetLastAssistantText(CH4.ChatSession session)
        {
            LlmMessage[] all = session.Context.GetMessages();
            for (int i = all.Length - 1; i >= 0; i = i - 1)
            {
                if (all[i].Role == LlmRole.Assistant && all[i].Content != null && all[i].Content.Length > 0)
                {
                    return all[i].Content;
                }
            }
            return "";
        }

        /// <summary>
        /// 相位环闭环——纯 LLM 文本回复：PostUserMessage → Pump → 回复入上下文 → 回 Idle。
        /// </summary>
        [Fact]
        public void PhaseCycle_TextReply_ReturnsToIdle()
        {
            MockLlm llm = new MockLlm();
            llm.ReplyText = "你好，测试回复";
            CH4.ChatSession session = CreateSession(llm);
            session.PostUserMessage("测试一下");
            PumpUntilIdle(session);
            Assert.True(session.IsIdle);
            Assert.Contains("你好，测试回复", GetLastAssistantText(session));
            Assert.Equal(1, llm.CallCount);
        }

        /// <summary>
        /// Note 工具批路径——LLM 返回 Note set 工具调用 → 工具批执行 → 计划写入 → 第二轮文本回复。
        /// </summary>
        [Fact]
        public void Note_ExecuteTool_SetPlan_ThroughToolBatch()
        {
            MockLlm llm = new MockLlm();
            // 第一轮：Note 工具调用（set 计划）
            string tc = "[{\"id\":\"n1\",\"function\":{\"name\":\"Note\",\"arguments\":\"{\\\"action\\\":\\\"set\\\",\\\"content\\\":\\\"任务A\\\\n任务B\\\"}\"}}]";
            llm.ToolCallsQueue.Enqueue(tc);
            CH4.ChatSession session = CreateSession(llm);
            session.PostUserMessage("创建计划");
            PumpUntilIdle(session);
            Assert.True(session.IsIdle);
            // 计划已写入 Note 状态（JSON 中文为 \uXXXX 转义——解析后断言）
            string noteJson = session.BuildNoteJson();
            using (JsonDocument nd = JsonDocument.Parse(noteJson))
            {
                JsonElement ntasks;
                Assert.True(nd.RootElement.TryGetProperty("tasks", out ntasks));
                Assert.Equal(2, ntasks.GetArrayLength());
                Assert.Equal("任务A", ntasks[0].GetString());
                Assert.Equal("任务B", ntasks[1].GetString());
            }
        }

        /// <summary>
        /// Note 手动添加 + 启动——NoteAdd 入列 → NoteStart 触发 LLM 轮次。
        /// </summary>
        [Fact]
        public void Note_ManualAddAndStart()
        {
            MockLlm llm = new MockLlm();
            llm.ReplyText = "开始执行";
            CH4.ChatSession session = CreateSession(llm);
            session.NoteAdd("第一步");
            session.NoteAdd("第二步");
            string before = session.BuildNoteJson();
            using (JsonDocument bd = JsonDocument.Parse(before))
            {
                JsonElement btasks;
                Assert.True(bd.RootElement.TryGetProperty("tasks", out btasks));
                Assert.Equal(2, btasks.GetArrayLength());
                Assert.Equal("第一步", btasks[0].GetString());
                Assert.Equal("第二步", btasks[1].GetString());
            }
            // NoteStart → 以 user 名义推计划 → LLM 回复
            session.NoteStart();
            PumpUntilIdle(session);
            Assert.True(session.IsIdle);
            Assert.Contains("开始执行", GetLastAssistantText(session));
        }

        /// <summary>
        /// 工具批收敛——连续多轮工具调用后纯文本结束：轮次 ≤ 收敛上限且回 Idle。
        /// </summary>
        [Fact]
        public void ToolBatch_ConvergesAfterToolRounds()
        {
            MockLlm llm = new MockLlm();
            // 三轮内置 time 工具调用（不进 OA——会话内直执）+ 最后文本收尾
            llm.ToolCallsQueue.Enqueue("[{\"id\":\"t1\",\"function\":{\"name\":\"time\",\"arguments\":\"{}\"}}]");
            llm.ToolCallsQueue.Enqueue("[{\"id\":\"t2\",\"function\":{\"name\":\"time\",\"arguments\":\"{}\"}}]");
            llm.ToolCallsQueue.Enqueue("[{\"id\":\"t3\",\"function\":{\"name\":\"time\",\"arguments\":\"{}\"}}]");
            CH4.ChatSession session = CreateSession(llm);
            session.PostUserMessage("连续调用工具");
            PumpUntilIdle(session);
            Assert.True(session.IsIdle);
            // 4 次 LLM 调用（3 轮工具 + 1 轮收尾文本）——收敛
            Assert.Equal(4, llm.CallCount);
        }

        /// <summary>
        /// E3 usage 转发——LLM 流带 Usage 事件 → 宿主收到 PushLlm("usage") 且累计整轮（覆盖式）。
        /// </summary>
        [Fact]
        public void Usage_ForwardedToHost()
        {
            MockLlm llm = new MockLlm();
            llm.ReplyText = "token 统计";
            llm.UsageJson = "{\"prompt\":100,\"completion\":20,\"cacheHit\":30}";
            MockHost host = new MockHost();
            CH4.ChatSession session = CreateSession(llm);
            session.AttachHost(host);
            session.PostUserMessage("统计一下");
            PumpUntilIdle(session);
            Assert.True(session.IsIdle);
            List<string> usageEvents;
            Assert.True(host.ViewEvents.TryGetValue("control", out usageEvents));
            Assert.True(usageEvents.Count > 0);
            // 找 usage 控制块（可能混有 chatdone）
            string usageCtrl = null;
            for (int i = usageEvents.Count - 1; i >= 0; i--)
            {
                using (JsonDocument cd = JsonDocument.Parse(usageEvents[i]))
                {
                    if (cd.RootElement.TryGetProperty("type", out JsonElement t) && t.GetString() == "usage")
                    {
                        usageCtrl = usageEvents[i];
                        break;
                    }
                }
            }
            Assert.NotNull(usageCtrl);
            // 累计 JSON 含三字段（整轮累计值）
            using (JsonDocument ud = JsonDocument.Parse(usageCtrl))
            {
                JsonElement data = ud.RootElement.GetProperty("data");
                Assert.Equal(100, data.GetProperty("prompt").GetInt64());
                Assert.Equal(20, data.GetProperty("completion").GetInt64());
                Assert.Equal(30, data.GetProperty("cacheHit").GetInt64());
            }
        }
    }
}

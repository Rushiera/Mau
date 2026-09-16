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
    /// ChatSession 核心测试——相位环/工具批/Note 全链（S7 程序集拆分验收）。
    /// 覆盖：纯 LLM 回复相位环闭环 / 内置工具（Note）工具批路径 / Note 手动添加与启动 / 工具批无限续轮（2026-09-09 删收敛上限）。
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

            /// <summary>前 N 次调用返回错误——S2 重试/错误隔离验证（0=不注入错误）</summary>
            public int FailTimes = 0;

            /// <summary>注入的错误文本——FailTimes > 0 时使用</summary>
            public string FailText = "ERR|TEST|模拟失败";

            /// <summary>是否模拟重试——产 Retrying 事件后正常回复（S2 §8.4 前端可见性验证）</summary>
            public bool EmitRetry = false;

            /// <summary>是否模拟空回复——只产 Done 不产 Text（空回复续传验证）</summary>
            public bool EmptyReply = false;

            /// <summary>空回复次数——前 N 次调用产空回复（0=每次）</summary>
            public int EmptyReplyTimes = 0;

            /// <summary>是否模拟 STREAM_CLOSED——产 Error 不产 Done（续传验证）</summary>
            public bool EmitStreamClosed = false;

            /// <summary>STREAM_CLOSED 次数——前 N 次调用产 Error（0=每次）</summary>
            public int StreamClosedTimes = 0;
            /// <summary>是否模拟纯空格回复——只产空格 Text（Trim 判空续传验证）</summary>
            public bool WhitespaceReply = false;
            /// <summary>纯空格次数——前 N 次调用产空格（0=每次）</summary>
            public int WhitespaceReplyTimes = 0;

            /// <summary>P6 中止模拟——产 Text 后挂起等待此事件（Pause 时 cts.Cancel → WaitOne 抛 OCE；null=不挂起）</summary>
            public AutoResetEvent HoldStream;

            /// <summary>P6 中止模拟——产 Retrying 前挂起等待取消（Runtime 取消识别后路径：取消 → OCE 冒泡，不产 Retrying）</summary>
            public bool EmitRetryThenCancel;

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
                // S2 错误注入——前 N 次调用产出 Error 事件（对应 Runtime 重试耗尽前/后两态）
                if (CallCount <= FailTimes)
                {
                    yield return new LlmStreamEvent(LlmStreamKind.Error, FailText);
                    yield break;
                }
                // 空回复续传——STREAM_CLOSED 模拟：前 N 次产 Error（未以 [DONE] 结束）
                if (EmitStreamClosed && (StreamClosedTimes == 0 || CallCount <= StreamClosedTimes))
                {
                    yield return new LlmStreamEvent(LlmStreamKind.Error, "ERR|STREAM_CLOSED|SSE 流未以 [DONE] 结束");
                    yield break;
                }
                // 空回复续传——空回复模拟：只产 Done 不产 Text（有思考无回复语义；EmptyReplyTimes 控制前 N 次）
                if (EmptyReply && (EmptyReplyTimes == 0 || CallCount <= EmptyReplyTimes))
                {
                    yield return new LlmStreamEvent(LlmStreamKind.Reasoning, "思考中");
                    yield return new LlmStreamEvent(LlmStreamKind.Done, "");
                    yield break;
                }
                // 空回复续传——纯空格模拟：只产空格 Text（Trim 判空续传验证；WhitespaceReplyTimes 控制前 N 次）
                if (WhitespaceReply && (WhitespaceReplyTimes == 0 || CallCount <= WhitespaceReplyTimes))
                {
                    yield return new LlmStreamEvent(LlmStreamKind.Text, "   ");
                    yield return new LlmStreamEvent(LlmStreamKind.Done, "");
                    yield break;
                }
                // S2 §8.4——模拟 Runtime 重试：重试通知后正常产出（前端可见性验证）
                if (EmitRetry)
                {
                    yield return new LlmStreamEvent(LlmStreamKind.Retrying, "RETRY|1/3|ERR|TRANSPORT|模拟网络抖动");
                    // 运行态七态——退避结束、重发开始（真实 Runtime：退避后重发前产 RetryResume；2026-09-16）
                    yield return new LlmStreamEvent(LlmStreamKind.RetryResume, "");
                }
                // P6 中止——取消识别路径：产 Retrying 前挂起（Pause → cts.Cancel → OCE 冒泡——不产 retry 气泡；Runtime catch OCE throw 语义模拟）
                if (EmitRetryThenCancel)
                {
                    while (!HoldStream.WaitOne(50))
                    {
                        ct.ThrowIfCancellationRequested();
                    }
                }
                if (ToolCallsQueue.Count > 0)
                {
                    string tc = ToolCallsQueue.Dequeue();
                    // 运行态七态——工具决策流开始（真实 Runtime：首个 tool_calls 增量帧产一次；2026-09-16）
                    yield return new LlmStreamEvent(LlmStreamKind.ToolCallsStart, "");
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
                // P6 中止模拟——挂起流等待用户暂停（Pause → cts.Cancel → ThrowIfCancellationRequested 抛 OCE——Runtime 取消路径；AutoResetEvent 无 CancellationToken 重载——轮询）
                if (HoldStream != null)
                {
                    while (!HoldStream.WaitOne(50))
                    {
                        ct.ThrowIfCancellationRequested();
                    }
                }
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
            // 工具注册表初始化——内置判定（IsBuiltinTool）读 ToolRegistry 单一真相源；测试环境无宿主 Init，
            // 须显式灌入内置表（否则 Note 被判非内置 → 走 OA 工单 → 测试无消费者卡死；判例 2026-09-16）
            CH4.ToolRegistry.Init(new ToolSpec[]
            {
                new ToolSpec("Note", "Note 任务追踪", "{}"),
                new ToolSpec("time", "当前时间", "{}"),
                new ToolSpec("random", "随机整数", "{}"),
                new ToolSpec("info", "运行状态", "{}")
            }, null);
            ChatContext ctx = new ChatContext();
            string tmp = Path.Combine(Path.GetTempPath(), "cat4test_" + Guid.NewGuid().ToString("N") + ".jsonl");
            SessionStore store = new SessionStore(tmp);
            OA oa = new OA(new ThreadGuard());
            // 测试工具声明面——含内置 Note（IsToolAllowed 声明面拦截需要；OA 工具测试走内置避免无消费者卡死）
            ToolSpec[] tools = new ToolSpec[]
            {
                new ToolSpec("Note", "Note 任务追踪", "{}")
            };
            CH4.SessionViewStore viewStore = new CH4.SessionViewStore(Path.Combine(Path.GetTempPath(), "cat4test_" + Guid.NewGuid().ToString("N") + ".view.json"));
            CH4.ChatSession session = new CH4.ChatSession("test-session", "test", ctx, store, llm, oa, tools, delegate (string name, string args) { return "ERR|NO_TOOL|" + name; }, viewStore);
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
        /// 工具批并发编号——同批多工具 toolcard payload 携带 toolIndex/toolTotal（前端 [n/m] 前缀数据源）。
        /// </summary>
        [Fact]
        public void ToolBatch_ConcurrentIndexAndTotal()
        {
            MockLlm llm = new MockLlm();
            // 同批两个内置 time 调用（并发——不依赖 OA 消费者）
            string tc = "[{\"id\":\"t1\",\"function\":{\"name\":\"time\",\"arguments\":\"{}\"}},{\"id\":\"t2\",\"function\":{\"name\":\"time\",\"arguments\":\"{}\"}}]";
            llm.ToolCallsQueue.Enqueue(tc);
            MockHost host = new MockHost();
            CH4.ChatSession session = CreateSession(llm);
            session.AttachHost(host);
            session.PostUserMessage("并发调用工具");
            PumpUntilIdle(session);
            Assert.True(session.IsIdle);
            List<string> cards = host.ViewEvents["toolcard"];
            Assert.Equal(2, cards.Count);
            using (JsonDocument d1 = JsonDocument.Parse(cards[0]))
            {
                Assert.Equal(1, d1.RootElement.GetProperty("toolIndex").GetInt32());
                Assert.Equal(2, d1.RootElement.GetProperty("toolTotal").GetInt32());
            }
            using (JsonDocument d2 = JsonDocument.Parse(cards[1]))
            {
                Assert.Equal(2, d2.RootElement.GetProperty("toolIndex").GetInt32());
                Assert.Equal(2, d2.RootElement.GetProperty("toolTotal").GetInt32());
            }
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

        /// <summary>
        /// roundsum 轮末统计——CloseRound 推送（Token 消耗 + 工具次数 + 请求次数 + 总耗时 + 七态用时；chatdone stats 带 context 前文长度）。
        /// </summary>
        [Fact]
        public void RoundSum_PushedOnCloseRound()
        {
            MockLlm llm = new MockLlm();
            llm.ReplyText = "统计完成";
            llm.UsageJson = "{\"prompt\":1000,\"completion\":200,\"cacheHit\":800}";
            MockHost host = new MockHost();
            CH4.ChatSession session = CreateSession(llm);
            session.AttachHost(host);
            session.PostUserMessage("算一下");
            PumpUntilIdle(session);
            Assert.True(session.IsIdle);
            // roundsum 视图事件存在
            List<string> rsEvents;
            Assert.True(host.ViewEvents.TryGetValue("roundsum", out rsEvents));
            Assert.True(rsEvents.Count >= 1);
            // 载荷结构——type=roundsum + data 六字段 + phases 七态（2026-09-16 运行态七态）
            using (JsonDocument rd = JsonDocument.Parse(rsEvents[0]))
            {
                JsonElement root = rd.RootElement;
                Assert.Equal("roundsum", root.GetProperty("type").GetString());
                JsonElement data = root.GetProperty("data");
                Assert.Equal(1000, data.GetProperty("prompt").GetInt64());
                Assert.Equal(200, data.GetProperty("completion").GetInt64());
                Assert.Equal(800, data.GetProperty("cacheHit").GetInt64());
                Assert.Equal(200, data.GetProperty("miss").GetInt64());
                Assert.True(data.GetProperty("elapsedMs").GetInt64() >= 0);
                // 请求次数——纯文本轮 = 1 次 API 请求（link 段数）
                Assert.Equal(1, data.GetProperty("requests").GetInt64());
                JsonElement phases = data.GetProperty("phases");
                Assert.True(phases.GetProperty("idle").GetInt64() >= 0);
                Assert.True(phases.GetProperty("wait").GetInt64() >= 0);
                Assert.True(phases.GetProperty("link").GetInt64() >= 0);
                Assert.True(phases.GetProperty("think").GetInt64() >= 0);
                Assert.True(phases.GetProperty("tool").GetInt64() >= 0);
                Assert.True(phases.GetProperty("run").GetInt64() >= 0);
                Assert.True(phases.GetProperty("reply").GetInt64() >= 0);
            }
            // chatdone stats 带 context（单次前文长度——非累计）
            List<string> ctrlEvents;
            Assert.True(host.ViewEvents.TryGetValue("control", out ctrlEvents));
            string doneCtrl = null;
            for (int i = ctrlEvents.Count - 1; i >= 0; i--)
            {
                using (JsonDocument cd = JsonDocument.Parse(ctrlEvents[i]))
                {
                    if (cd.RootElement.TryGetProperty("type", out JsonElement t) && t.GetString() == "chatdone")
                    {
                        doneCtrl = ctrlEvents[i];
                        break;
                    }
                }
            }
            Assert.NotNull(doneCtrl);
            using (JsonDocument dd = JsonDocument.Parse(doneCtrl))
            {
                JsonElement stats = dd.RootElement.GetProperty("stats");
                Assert.Equal(1000, stats.GetProperty("context").GetInt64());
            }
        }

        /// <summary>
        /// S2 错误隔离——单次瞬态错误后会话就地修复：错误文本不入上下文，上下文保持断点（用户消息保留），相位回 Idle。
        /// 语义：错误可见（Error 事件已推前端），但不污染 ChatContext——断点续传的干净前提。
        /// 说明：Runtime 重试（TRANSPORT/5xx/429 最多 3 次）在 DeepSeekLlmRuntime 内部完成，对会话层透明；
        /// 本测试验证的是 Runtime 重试耗尽后最终 Error 的会话层处理——错误文本绝不入上下文。
        /// </summary>
        [Fact]
        public void S2_Error_NotPollutingContext_KeepsBreakpoint()
        {
            MockLlm llm = new MockLlm();
            llm.FailTimes = 1;
            CH4.ChatSession session = CreateSession(llm);
            session.PostUserMessage("断点测试");
            PumpUntilIdle(session);
            Assert.True(session.IsIdle);
            // 错误文本绝不入上下文（GetLastAssistantText 不含 ERR|）
            Assert.Equal("", GetLastAssistantText(session));
            // 上下文保持断点——用户消息保留（不回退到上一节点——CH2 语义淘汰）
            LlmMessage[] all = session.Context.GetMessages();
            bool hasUser = false;
            for (int i = 0; i < all.Length; i = i + 1)
            {
                if (all[i].Role == LlmRole.User && all[i].Content == "断点测试")
                {
                    hasUser = true;
                }
            }
            Assert.True(hasUser);
        }

        /// <summary>
        /// S2 断点续传——错误隔离后用户重发：上下文无错误残留，新一轮正常回复（断点续传的用户触发面）。
        /// </summary>
        [Fact]
        public void S2_Error_ThenResend_RecoversClean()
        {
            MockLlm llm = new MockLlm();
            llm.FailTimes = 1;
            llm.ReplyText = "重试成功回复";
            CH4.ChatSession session = CreateSession(llm);
            session.PostUserMessage("重试测试");
            PumpUntilIdle(session);
            Assert.True(session.IsIdle);
            // 第一次失败——错误隔离中止（无回复文本）
            Assert.Equal("", GetLastAssistantText(session));
            // 用户重发（断点续传）——第二次调用成功
            session.PostUserMessage("再试一次");
            PumpUntilIdle(session);
            Assert.True(session.IsIdle);
            Assert.Contains("重试成功回复", GetLastAssistantText(session));
            // 全程上下文无错误文本
            LlmMessage[] all = session.Context.GetMessages();
            for (int i = 0; i < all.Length; i = i + 1)
            {
                Assert.DoesNotContain("ERR|", all[i].Content == null ? "" : all[i].Content);
            }
        }

        /// <summary>
        /// A55 错误可见性——错误中止：视图层落盘 error 块（单块——统一出口 AbortRoundError；实时面不双推）。
        /// </summary>
        [Fact]
        public void A55_Error_PersistsSingleErrorBlock()
        {
            MockLlm llm = new MockLlm();
            llm.FailTimes = 1;
            CH4.ChatSession session = CreateSession(llm);
            session.PostUserMessage("错误落盘测试");
            PumpUntilIdle(session);
            Assert.True(session.IsIdle);
            CH4.ViewBlock[] blocks = session.ViewStore.GetBlocks();
            int errorCount = 0;
            string payload = "";
            for (int i = 0; i < blocks.Length; i = i + 1)
            {
                if (blocks[i].RenderType == "error")
                {
                    errorCount = errorCount + 1;
                    payload = blocks[i].Payload;
                }
            }
            Assert.Equal(1, errorCount);
            Assert.Contains("ERR|", payload);
        }

        /// <summary>
        /// A55 重试落盘——同一重试序列原位更新（一块不堆叠）；恢复后终态 resolved。
        /// </summary>
        [Fact]
        public void A55_Retry_PersistsSingleRetryBlock()
        {
            MockLlm llm = new MockLlm();
            llm.EmitRetry = true;
            CH4.ChatSession session = CreateSession(llm);
            session.PostUserMessage("重试落盘测试");
            PumpUntilIdle(session);
            Assert.True(session.IsIdle);
            CH4.ViewBlock[] blocks = session.ViewStore.GetBlocks();
            int retryCount = 0;
            string payload = "";
            for (int i = 0; i < blocks.Length; i = i + 1)
            {
                if (blocks[i].RenderType == "retry")
                {
                    retryCount = retryCount + 1;
                    payload = blocks[i].Payload;
                }
            }
            Assert.Equal(1, retryCount);
            Assert.Contains("resolved", payload);
        }

        /// <summary>
        /// S2 §8.4 重试前端可见性——MockLlm 产 Retrying 事件 → 会话层推 retry view 事件（retrying 态）
        /// → 首个 Text 到达 → 推 resolved 回填。断言 host 收到 retry 视图事件（retrying + resolved）。
        /// </summary>
        [Fact]
        public void S2_Retry_VisibleToFrontend()
        {
            MockLlm llm = new MockLlm();
            llm.EmitRetry = true;
            llm.ReplyText = "重试后正常回复";
            MockHost host = new MockHost();
            CH4.ChatSession session = CreateSession(llm);
            session.AttachHost(host);
            session.PostUserMessage("重试可见性");
            PumpUntilIdle(session);
            Assert.True(session.IsIdle);
            // 收到 retry view 事件（≥2：retrying + resolved）
            List<string> retryEvents;
            Assert.True(host.ViewEvents.TryGetValue("retry", out retryEvents));
            Assert.True(retryEvents.Count >= 2);
            // 首个 retrying 态——attempt/max 解析正确
            using (JsonDocument d1 = JsonDocument.Parse(retryEvents[0]))
            {
                Assert.Equal("retrying", d1.RootElement.GetProperty("state").GetString());
                Assert.Equal("1", d1.RootElement.GetProperty("attempt").GetString());
                Assert.Equal("3", d1.RootElement.GetProperty("max").GetString());
            }
            // 末个 resolved 态——重试成功回填
            using (JsonDocument dLast = JsonDocument.Parse(retryEvents[retryEvents.Count - 1]))
            {
                Assert.Equal("resolved", dLast.RootElement.GetProperty("state").GetString());
            }
            // 重试后正常回复
            Assert.Contains("重试后正常回复", GetLastAssistantText(session));
        }

        /// <summary>
        /// 空回复续传——MockLlm 模拟流正常结束但只产思考不产文本（CH2 [段2.3] 语义）：
        /// 第一次调用 EmptyReply=true（空回复）→ 续传（同上下文重发）→ 第二次调用正常回复。
        /// 断言：续传后正常完成 + 无空 assistant 消息入上下文 + 无前端 error（续传可恢复不推 error）。
        /// </summary>
        [Fact]
        public void EmptyReply_AutoContinueAndResolve()
        {
            MockLlm llm = new MockLlm();
            // 第 1 次调用空回复（只思考无文本）——后续正常
            llm.EmptyReply = true;
            llm.EmptyReplyTimes = 1;
            CH4.ChatSession session = CreateSession(llm);
            session.PostUserMessage("空回复续传测试");
            PumpUntilIdle(session);
            Assert.True(session.IsIdle);
            // 续传后正常完成——assistant 文本存在（第二次调用产出 "ok"）
            Assert.Contains("ok", GetLastAssistantText(session));
            // 无空 assistant 消息——上下文中不出现 content=="" 的 assistant 消息
            LlmMessage[] all = session.Context.GetMessages();
            for (int i = 0; i < all.Length; i = i + 1)
            {
                if (all[i].Role == LlmRole.Assistant)
                {
                    Assert.True(all[i].Content == null || all[i].Content.Length > 0);
                }
            }
            // LLM 调用次数 = 2（1 次空回复 + 1 次续传）
            Assert.Equal(2, llm.CallCount);
        }
        /// <summary>
        /// 纯空格回复续传——MockLlm 模拟流正常结束但只产空格文本（Trim 判空语义）：
        /// 第一次调用产空格 → 自动续传 → 第二次调用正常回复。
        /// 断言：续传后正常完成 + 无空格 assistant 消息入上下文 + 调用次数 = 2。
        /// </summary>
        [Fact]
        public void WhitespaceReply_AutoContinueAndResolve()
        {
            MockLlm llm = new MockLlm();
            llm.WhitespaceReply = true;
            llm.WhitespaceReplyTimes = 1;
            CH4.ChatSession session = CreateSession(llm);
            session.PostUserMessage("纯空格续传测试");
            PumpUntilIdle(session);
            Assert.True(session.IsIdle);
            // 续传后正常完成——assistant 文本存在（第二次调用产出 "ok"）
            Assert.Contains("ok", GetLastAssistantText(session));
            // 无空格 assistant 消息——上下文中不出现纯空格 assistant 消息
            LlmMessage[] all = session.Context.GetMessages();
            for (int i = 0; i < all.Length; i = i + 1)
            {
                if (all[i].Role == LlmRole.Assistant)
                {
                    Assert.True(all[i].Content == null || all[i].Content.Trim().Length > 0);
                }
            }

            // LLM 调用次数 = 2（1 次空格回复 + 1 次续传）
            Assert.Equal(2, llm.CallCount);
        }
        /// <summary>
        /// STREAM_CLOSED 续传——MockLlm 模拟 SSE 流未以 [DONE] 结束（ERR|STREAM_CLOSED）：
        /// 第一次调用产 STREAM_CLOSED 错误 → 续传（同上下文重发）→ 第二次调用正常回复。
        /// 断言：续传后正常完成 + 空回复续传计数工作（前 N 次 STREAM_CLOSED 由 StreamClosedTimes 控制）。
        /// </summary>
        [Fact]
        public void StreamClosed_AutoContinueAndResolve()
        {
            MockLlm llm = new MockLlm();
            llm.EmitStreamClosed = true;
            llm.StreamClosedTimes = 1; // 只有第 1 次产 STREAM_CLOSED——后续正常
            CH4.ChatSession session = CreateSession(llm);
            session.PostUserMessage("流中断续传测试");
            PumpUntilIdle(session);
            Assert.True(session.IsIdle);
            // 续传后正常完成
            Assert.Contains("ok", GetLastAssistantText(session));
            // LLM 调用次数 = 2（1 次 STREAM_CLOSED + 1 次续传）
            Assert.Equal(2, llm.CallCount);
        }
        /// <summary>
        /// P6 中止——LLM 流中暂停：后台流挂起 → Pause() 取消 → 上下文保留用户消息（半截文本不入上下文——不裁剪、不写半截）→ 复位 Idle + paused 事件（无 chatdone）。
        /// </summary>
        [Fact]
        public void Pause_WhileLlmStreaming_KeepsContextAndPushesPaused()
        {
            MockLlm llm = new MockLlm();
            llm.ReplyText = "半截回复";
            llm.HoldStream = new AutoResetEvent(false);
            MockHost host = new MockHost();
            CH4.ChatSession session = CreateSession(llm);
            session.AttachHost(host);
            session.PostUserMessage("测试暂停");
            // 泵帧直到进入活跃相位（后台流挂起在 HoldStream——保证 Pause 时 LlmRunning）
            for (int i = 0; i < 100 && session.IsIdle; i++)
            {
                session.Pump();
                Thread.Sleep(5);
            }
            Assert.False(session.IsIdle);
            // 中止——取消令牌 → 后台 WaitOne 抛 OCE → 主线程 Pump 消费收尾
            session.Pause();
            PumpUntilIdle(session);
            Assert.True(session.IsIdle);
            // 上下文保留用户消息——半截文本不入上下文（不裁剪、不写半截；测试会话无 system 提示词——仅 user）
            LlmMessage[] msgs = session.Context.GetMessages();
            Assert.Single(msgs); // user（半截丢弃）
            Assert.Equal(LlmRole.User, msgs[0].Role);
            // paused 控制事件推送 + 无 chatdone（中止非正常完成语义）
            List<string> ctrlEvents;
            Assert.True(host.ViewEvents.TryGetValue("control", out ctrlEvents));
            bool foundPaused = false;
            bool foundChatDone = false;
            for (int i = ctrlEvents.Count - 1; i >= 0; i--)
            {
                using (JsonDocument cd = JsonDocument.Parse(ctrlEvents[i]))
                {
                    if (cd.RootElement.TryGetProperty("type", out JsonElement t))
                    {
                        string tv = t.GetString();
                        if (tv == "paused")
                        {
                            foundPaused = true;
                        }
                        if (tv == "chatdone")
                        {
                            foundChatDone = true;
                        }
                    }
                }
            }
            Assert.True(foundPaused);
            Assert.False(foundChatDone);
        }
        /// <summary>
        /// P6 中止——取消识别：Runtime 取消路径不产 Retrying（挂起在 Retrying 前）→ Pause 后无 retry 气泡 + paused 气泡 + user 保留。
        /// </summary>
        [Fact]
        public void Pause_CancelBeforeRetry_NoRetryBubble()
        {
            MockLlm llm = new MockLlm();
            llm.EmitRetryThenCancel = true;
            llm.HoldStream = new AutoResetEvent(false);
            MockHost host = new MockHost();
            CH4.ChatSession session = CreateSession(llm);
            session.AttachHost(host);
            session.PostUserMessage("暂停重试测试");
            // 泵帧直到活跃（后台流挂起在 Retrying 前——等待取消）
            for (int i = 0; i < 100 && session.IsIdle; i++)
            {
                session.Pump();
                Thread.Sleep(5);
            }
            Assert.False(session.IsIdle);
            session.Pause();
            PumpUntilIdle(session);
            Assert.True(session.IsIdle);
            // 无 retry 视图气泡（取消不产重试）
            Assert.False(host.ViewEvents.ContainsKey("retry"));
            // paused 气泡存在
            List<string> ctrlEvents;
            Assert.True(host.ViewEvents.TryGetValue("control", out ctrlEvents));
            bool foundPaused = false;
            for (int i = ctrlEvents.Count - 1; i >= 0; i--)
            {
                using (JsonDocument cd = JsonDocument.Parse(ctrlEvents[i]))
                {
                    if (cd.RootElement.TryGetProperty("type", out JsonElement t) && t.GetString() == "paused")
                    {
                        foundPaused = true;
                        break;
                    }
                }
            }
            Assert.True(foundPaused);
            // 用户消息保留（不裁剪）
            Assert.Single(session.Context.GetMessages());
        }
        /// <summary>
        /// P10 roundsum 重建归并——多轮会话重建后 GetBlocks 顺序：roundsum 位于所属轮末块之后、下一轮块之前
        /// （时间戳排序——真实时序权威，跨重启稳定；修复"刷新后所有统计排末尾"）。
        /// </summary>
        [Fact]
        public void View_Rebuild_RoundSumMergedByTimestamp()
        {
            // 构造多轮真实前文——Sleep 保证 CreatedAt 毫秒递增（视图排序键）
            ChatContext ctx = new ChatContext();
            ctx.SetSystemPrompt("系统");
            ctx.AddUserMessage("第一轮问题");
            ctx.AddAssistantMessage("第一轮回复");
            Thread.Sleep(5);
            ctx.AddUserMessage("第二轮问题");
            ctx.AddAssistantMessage("第二轮回复");
            Thread.Sleep(5);
            ctx.AddUserMessage("第三轮问题");
            ctx.AddAssistantMessage("第三轮回复");
            LlmMessage[] msgs = ctx.GetMessages();
            // 增量写入视图（与真实运行一致：每轮 CloseRound 追加 roundsum——时间戳 = 轮末块之后）
            string tmp = Path.Combine(Path.GetTempPath(), "cat4test_" + Guid.NewGuid().ToString("N") + ".view.json");
            CH4.SessionViewStore viewStore = new CH4.SessionViewStore(tmp);
            for (int i = 0; i < msgs.Length; i++)
            {
                LlmMessage m = msgs[i];
                if (m.Role == LlmRole.System)
                {
                    continue;
                }
                if (m.Role == LlmRole.User)
                {
                    viewStore.OnUserMessage(m, m.CreatedAt, i);
                }
                else if (m.Role == LlmRole.Assistant)
                {
                    viewStore.OnAssistantText(m, m.CreatedAt, i);
                }
            }
            // 每轮 CloseRound——rs 时间戳略大于该轮末块（真实语义：CloseRound 在该轮结束后）
            viewStore.AppendRoundSummary("{\"type\":\"roundsum\",\"data\":{}}", msgs[2].CreatedAt + 1);
            viewStore.AppendRoundSummary("{\"type\":\"roundsum\",\"data\":{}}", msgs[4].CreatedAt + 1);
            viewStore.AppendRoundSummary("{\"type\":\"roundsum\",\"data\":{}}", msgs[6].CreatedAt + 1);
            viewStore.Save();
            // 重建——从真实前文（重启场景；rs 从 view.json 读回——LoadInjectReport）
            viewStore.Rebuild(msgs);
            viewStore.LoadInjectReport();
            // 归并顺序断言——每轮：user, text, roundsum（rs 紧跟轮末块，不在末尾堆叠）
            CH4.ViewBlock[] merged = viewStore.GetBlocks();
            Assert.Equal(9, merged.Length); // 6 blocks + 3 rs（无注入报告）
            string[] expected = { "user", "text", "roundsum", "user", "text", "roundsum", "user", "text", "roundsum" };
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.Equal(expected[i], merged[i].RenderType);
            }
        }

        /// <summary>
        /// P6b 回滚——从正式回复节点截断：上下文/落盘/视图/统计全部复位（保留 [0..msgIndex]）。
        /// 测试环境无 system 消息——两轮后结构：user1+reply1+user2+reply2 = 4 条。
        /// </summary>
        [Fact]
        public void Rollback_ToReply_TruncatesAndPersists()
        {
            MockLlm llm = new MockLlm();
            llm.ReplyText = "第一轮回复";
            CH4.ChatSession session = CreateSession(llm);
            // 两轮对话——user1+reply1+user2+reply2 = 4 条（测试环境无 system）
            session.PostUserMessage("第一轮问题");
            PumpUntilIdle(session);
            llm.ReplyText = "第二轮回复";
            session.PostUserMessage("第二轮问题");
            PumpUntilIdle(session);
            LlmMessage[] before = session.Context.GetMessages();
            Assert.Equal(4, before.Length);
            // 回滚到第一个正式回复（索引 1）
            string result = session.Rollback(1);
            Assert.StartsWith("rollback", result);
            LlmMessage[] after = session.Context.GetMessages();
            Assert.Equal(2, after.Length);
            Assert.Equal("第一轮问题", after[0].Content);
            Assert.Equal("第一轮回复", after[1].Content);
            // 落盘验证——从文件重载仍是截断后前文
            LlmMessage[] restored;
            SessionStats? stats;
            Assert.True(session.Store.TryLoad(out restored, out stats));
            Assert.Equal(2, restored.Length);
            // 视图验证——user + text 两块，MsgIndex 指向真实前文索引
            CH4.ViewBlock[] blocks = session.GetViewBlocks();
            Assert.Equal(2, blocks.Length);
            Assert.Equal("user", blocks[0].RenderType);
            Assert.Equal(0, blocks[0].MsgIndex);
            Assert.Equal("text", blocks[1].RenderType);
            Assert.Equal(1, blocks[1].MsgIndex);
            // 统计复位——新起点零统计
            Assert.Equal(0, session.LastStats.EntryCount);
        }

        /// <summary>
        /// P6b 回滚——非正式回复节点拒绝（user 消息/工具声明不可作切点——协议完整性）。
        /// </summary>
        [Fact]
        public void Rollback_NonReplyNode_Rejected()
        {
            MockLlm llm = new MockLlm();
            llm.ReplyText = "回复";
            CH4.ChatSession session = CreateSession(llm);
            session.PostUserMessage("问题");
            PumpUntilIdle(session);
            string result = session.Rollback(0); // user 消息
            Assert.StartsWith("ERR|ROLLBACK_NODE", result);
            Assert.Equal(2, session.Context.GetMessages().Length);
        }

        /// <summary>
        /// P6b 回滚——越界索引拒绝。
        /// </summary>
        [Fact]
        public void Rollback_OutOfRange_Rejected()
        {
            MockLlm llm = new MockLlm();
            llm.ReplyText = "回复";
            CH4.ChatSession session = CreateSession(llm);
            session.PostUserMessage("问题");
            PumpUntilIdle(session);
            string result = session.Rollback(99);
            Assert.StartsWith("ERR|ROLLBACK_INDEX", result);
            result = session.Rollback(-1);
            Assert.StartsWith("ERR|ROLLBACK_INDEX", result);
            Assert.Equal(2, session.Context.GetMessages().Length);
        }

        /// <summary>
        /// P6b 视图 MsgIndex——增量写入时携带真实前文索引（text 块指向正式回复；roundsum=-1）。
        /// </summary>
        [Fact]
        public void View_MsgIndex_CarriedOnAppend()
        {
            ChatContext ctx = new ChatContext();
            ctx.SetSystemPrompt("系统");
            ctx.AddUserMessage("问题");
            ctx.AddAssistantMessage("回复");
            LlmMessage[] msgs = ctx.GetMessages();
            string tmp = Path.Combine(Path.GetTempPath(), "cat4test_" + Guid.NewGuid().ToString("N") + ".view.json");
            CH4.SessionViewStore viewStore = new CH4.SessionViewStore(tmp);
            for (int i = 0; i < msgs.Length; i = i + 1)
            {
                LlmMessage m = msgs[i];
                if (m.Role == LlmRole.System)
                {
                    continue;
                }
                if (m.Role == LlmRole.User)
                {
                    viewStore.OnUserMessage(m, m.CreatedAt, i);
                }
                else if (m.Role == LlmRole.Assistant)
                {
                    viewStore.OnAssistantText(m, m.CreatedAt, i);
                }
            }
            viewStore.AppendRoundSummary("{\"type\":\"roundsum\",\"data\":{}}", msgs[2].CreatedAt + 1);
            CH4.ViewBlock[] blocks = viewStore.GetBlocks();
            Assert.Equal(3, blocks.Length);
            Assert.Equal(1, blocks[0].MsgIndex);
            Assert.Equal(2, blocks[1].MsgIndex);
            Assert.Equal(-1, blocks[2].MsgIndex);
        }

        /// <summary>
        /// Q2 对照——工具轮后空回复无限续传恢复为正常 reply：有 reply 且 Note 未完成 → Note 拉起（正常语义保持）。
        /// </summary>
        [Fact]
        public void ToolRound_ThenEmptyReply_ThenReply_PullsNote()
        {
            MockLlm llm = new MockLlm();
            // 第 1 轮：工具调用（time 内置——不进 OA）
            llm.ToolCallsQueue.Enqueue("[{\"id\":\"t1\",\"function\":{\"name\":\"time\",\"arguments\":\"{}\"}}]");
            // 第 2 轮：空回复（只思考无文本）→ 无限续传
            llm.EmptyReply = true;
            llm.EmptyReplyTimes = 1;
            // 第 3 轮：正常回复（MockLlm 默认 ReplyText="ok"）→ Note 剩余 2 条 → 自动拉起
            CH4.ChatSession session = CreateSession(llm);
            session.NoteAdd("任务A");
            session.NoteAdd("任务B");
            session.PostUserMessage("开始");
            // 泵至 [Note 未完成] 入上下文（上限 300 帧——拉起后 MockLlm 持续回复会重复拉起，非本测试范围）
            bool pulled = false;
            for (int i = 0; i < 300 && !pulled; i = i + 1)
            {
                session.Pump();
                Thread.Sleep(5);
                LlmMessage[] check = session.Context.GetMessages();
                for (int j = 0; j < check.Length; j = j + 1)
                {
                    if (check[j].Role == LlmRole.User && check[j].Content != null && check[j].Content.IndexOf("[Note 未完成]", StringComparison.Ordinal) >= 0)
                    {
                        pulled = true;
                    }
                }
            }
            // 有正常 reply + Note 未完成 → Note 拉起（user 消息入上下文）
            Assert.True(pulled);
        }

        /// <summary>
        /// Q 对照——剩余 1 条（最后一条）不自动拉起：LLM 完成后自然结束（工具提示"最后一条完成后可结束本轮"——不再强制拉起）。
        /// </summary>
        [Fact]
        public void ToolRound_LastTaskRemain_NoPull()
        {
            MockLlm llm = new MockLlm();
            llm.ToolCallsQueue.Enqueue("[{\"id\":\"t1\",\"function\":{\"name\":\"time\",\"arguments\":\"{}\"}}]");
            llm.EmptyReply = true;
            llm.EmptyReplyTimes = 1;
            CH4.ChatSession session = CreateSession(llm);
            session.NoteAdd("任务A");
            session.PostUserMessage("开始");
            bool pulled = false;
            for (int i = 0; i < 120 && !pulled; i = i + 1)
            {
                session.Pump();
                Thread.Sleep(5);
                LlmMessage[] check = session.Context.GetMessages();
                for (int j = 0; j < check.Length; j = j + 1)
                {
                    if (check[j].Role == LlmRole.User && check[j].Content != null && check[j].Content.IndexOf("[Note 未完成]", StringComparison.Ordinal) >= 0)
                    {
                        pulled = true;
                    }
                }
            }
            // 剩余 1 条不拉起——上下文无 [Note 未完成]（最后一条由 LLM 完成后自然结束）
            Assert.False(pulled);
        }
        /// <summary>
        /// 运行态七态——工具轮：requests=2（工具前后各一次 API 请求）；七态表可读；轮末当前态归 idle（2026-09-16 基建事实态）。
        /// </summary>
        [Fact]
        public void RunState_SevenPhases_ToolRound()
        {
            MockLlm llm = new MockLlm();
            llm.ReplyText = "完成";
            // 单任务 Note（不触发 Note 自动拉起——剩余 0 条）
            string tc = "[{\"id\":\"n1\",\"function\":{\"name\":\"Note\",\"arguments\":\"{\\\"action\\\":\\\"set\\\",\\\"content\\\":\\\"任务A\\\"}\"}}]";
            llm.ToolCallsQueue.Enqueue(tc);
            CH4.ChatSession session = CreateSession(llm);
            session.PostUserMessage("做任务");
            PumpUntilIdle(session);
            Assert.True(session.IsIdle);
            // 请求次数——工具轮 = 2 次 API 请求（工具前 + 工具后）
            Assert.Equal(2, llm.CallCount);
            // 运行态读取面——七态键齐 + 请求次数 + 轮末归 idle
            string stateName;
            int requests;
            Dictionary<string, long> ms = session.GetRunState(out stateName, out requests);
            Assert.Equal(2, requests);
            Assert.Equal("idle", stateName);
            Assert.True(ms.ContainsKey("idle"));
            Assert.True(ms.ContainsKey("wait"));
            Assert.True(ms.ContainsKey("link"));
            Assert.True(ms.ContainsKey("think"));
            Assert.True(ms.ContainsKey("tool"));
            Assert.True(ms.ContainsKey("run"));
            Assert.True(ms.ContainsKey("reply"));
            // 本地态与远端态均可累计（数值非负——真实时长归后端单源）
            Assert.True(ms["link"] >= 0);
            Assert.True(ms["reply"] >= 0);
            Assert.True(ms["run"] >= 0);
        }
    }
}
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Mau.Runtime;
using CatHome4.Contracts;
using Xunit;

namespace CatHome4.Core.Tests
{
    /// <summary>
    /// timeback 上下文作用域测试（design-ch4-timeback · 2026-09-28 语义重设 · 2026-10-09 拆两名）——
    /// 区间删除（锚点 = timeback-start 调用 / 结论 = timeback-back 返回值）/ 同批空区间 / 三工具锁定 /
    /// 视图不触 / 参数面 / 执行序两态。
    /// 拆分后：原单工具 timeback（action=start|back）→ 两个工具名（功能不变）。
    /// </summary>
    [Collection("GlobalToolState")]
    public class TimebackTests
    {
        /// <summary>
        /// Mock LLM——按队列返回 tool_calls 或纯文本（每次 ChatStream 消费一项）。
        /// </summary>
        private sealed class MockLlm : ILlmRuntime
        {
            /// <summary>tool_calls JSON 队列——空=返回纯文本</summary>
            public Queue<string> ToolCallsQueue = new Queue<string>();

            /// <summary>纯文本回复内容</summary>
            public string ReplyText = "ok";

            /// <summary>ChatStream 调用次数</summary>
            public int CallCount = 0;

            /// <summary>
            /// 流式对话——队列有内容则产 tool_calls（含决策流开始帧），否则产纯文本。
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
                    yield return new LlmStreamEvent(LlmStreamKind.ToolCallsStart, "");
                    yield return new LlmStreamEvent(LlmStreamKind.ToolCalls, tc);
                    await Task.Yield();
                    yield return new LlmStreamEvent(LlmStreamKind.Done, "");
                    yield break;
                }
                yield return new LlmStreamEvent(LlmStreamKind.Text, ReplyText);
                await Task.Yield();
                yield return new LlmStreamEvent(LlmStreamKind.Done, "");
            }
        }

        /// <summary>
        /// 构造测试会话——临时前文文件 + 唯一临时归档路径 + Mock LLM（Note / timeback-* / random 工具面）。
        /// </summary>
        /// <param name="llm">Mock LLM</param>
        /// <returns>会话实体</returns>
        private static CH4.ChatSession CreateSession(MockLlm llm)
        {
            return CreateSession(llm, new string[] { "Note", "timeback-start", "timeback-back", "random", "sleep", "timer" });
        }
        /// <summary>
        /// 构造测试会话（指定工具面）——临时前文文件 + 唯一临时归档路径 + Mock LLM。
        /// </summary>
        /// <param name="llm">Mock LLM</param>
        /// <param name="toolNames">工具名数组（注册表工具面）</param>
        /// <returns>会话实体</returns>
        private static CH4.ChatSession CreateSession(MockLlm llm, string[] toolNames)
        {
            ToolSpec[] tools = new ToolSpec[toolNames.Length];
            for (int i = 0; i < toolNames.Length; i = i + 1)
            {
                tools[i] = new ToolSpec(toolNames[i], toolNames[i], "{}");
            }
            CH4.ToolRegistry.Init(tools, null, null);
            CH4.ChatSession.AuthorizedToolNamesProvider = null;
            string archiveDir = Path.Combine(Path.GetTempPath(), "cat4tb_" + Guid.NewGuid().ToString("N"));
            CH4.ChatSession.TimebackArchiveDirProvider = delegate (string catKey)
            {
                return archiveDir;
            };
            CH4.ChatSession.TimebackInfoProvider = delegate ()
            {
                return "{\"cat\":\"tb-session\",\"note\":\"info 快照占位\"}";
            };
            ChatContext ctx = new ChatContext();
            string tmp = Path.Combine(Path.GetTempPath(), "cat4tb_" + Guid.NewGuid().ToString("N") + ".jsonl");
            SessionStore store = new SessionStore(tmp);
            OA oa = new OA(new ThreadGuard());
            CH4.SessionViewStore viewStore = new CH4.SessionViewStore(Path.Combine(Path.GetTempPath(), "cat4tb_" + Guid.NewGuid().ToString("N") + ".view.json"));
            CH4.ChatSession session = new CH4.ChatSession("tb-session", "tb", ctx, store, llm, oa, tools, delegate (string name, string args) { return "ERR|NO_TOOL|" + name; }, viewStore);
            session.SetCatKey("tb-session");
            return session;
        }

        /// <summary>
        /// 泵会话直到 Idle——上限 500 帧（防死循环）。
        /// </summary>
        /// <param name="session">会话</param>
        private static void PumpUntilIdle(CH4.ChatSession session)
        {
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
        /// 构造单条 tool_calls JSON（OpenAI wire 形态）。
        /// </summary>
        /// <param name="toolName">工具名</param>
        /// <param name="id">调用 id</param>
        /// <param name="argsJson">参数 JSON 字符串</param>
        /// <returns>tool_calls 数组 JSON</returns>
        private static string BuildToolCalls(string toolName, string id, string argsJson)
        {
            return "[{\"id\":\"" + id + "\",\"function\":{\"name\":\"" + toolName + "\",\"arguments\":" + JsonSerializer.Serialize(argsJson) + "}}]";
        }

        /// <summary>
        /// 拼接同批多条 tool_calls JSON（顺序即执行顺序）。
        /// </summary>
        /// <param name="toolNames">工具名数组</param>
        /// <param name="ids">调用 id 数组</param>
        /// <param name="argsJsons">参数 JSON 字符串数组</param>
        /// <returns>tool_calls 数组 JSON</returns>
        private static string BuildToolCallsBatch(string[] toolNames, string[] ids, string[] argsJsons)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("[");
            for (int i = 0; i < ids.Length; i = i + 1)
            {
                if (i > 0)
                {
                    sb.Append(",");
                }
                sb.Append("{\"id\":\"" + ids[i] + "\",\"function\":{\"name\":\"" + toolNames[i] + "\",\"arguments\":" + JsonSerializer.Serialize(argsJsons[i]) + "}}");
            }
            sb.Append("]");
            return sb.ToString();
        }

        /// <summary>
        /// 取前文第 k 条（0 起）tool 消息内容——工具结果断言用。
        /// </summary>
        /// <param name="session">会话</param>
        /// <param name="k">序号（0 起）</param>
        /// <returns>结果文本（不足=空串）</returns>
        private static string ToolResultText(CH4.ChatSession session, int k)
        {
            LlmMessage[] all = session.Context.GetMessages();
            int seen = 0;
            for (int i = 0; i < all.Length; i = i + 1)
            {
                if (all[i].Role == LlmRole.Tool)
                {
                    if (seen == k)
                    {
                        return all[i].Content ?? "";
                    }
                    seen = seen + 1;
                }
            }
            return "";
        }

        /// <summary>
        /// 前文是否含指定角色与子串的消息。
        /// </summary>
        /// <param name="session">会话</param>
        /// <param name="role">角色</param>
        /// <param name="fragment">子串</param>
        /// <returns>true=命中</returns>
        private static bool HasMessage(CH4.ChatSession session, LlmRole role, string fragment)
        {
            LlmMessage[] all = session.Context.GetMessages();
            for (int i = 0; i < all.Length; i = i + 1)
            {
                if (all[i].Role == role && all[i].Content != null && all[i].Content.Contains(fragment, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 视图块是否含指定子串（任意块型的 payload）。
        /// </summary>
        /// <param name="session">会话</param>
        /// <param name="fragment">子串</param>
        /// <returns>true=命中</returns>
        private static bool ViewHas(CH4.ChatSession session, string fragment)
        {
            CH4.ViewBlock[] blocks = session.GetViewBlocks();
            for (int i = 0; i < blocks.Length; i = i + 1)
            {
                string payload = blocks[i].Payload == null ? "" : blocks[i].Payload;
                if (payload.Contains(fragment, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 跨轮取证——start → 查证（random）→ back：区间删除后前文只留两次调用对与结论。
        /// </summary>
        [Fact]
        public void Back_DeletesScopeRange_KeepsBothCalls()
        {
            MockLlm llm = new MockLlm();
            CH4.ChatSession session = CreateSession(llm);
            llm.ToolCallsQueue.Enqueue(BuildToolCalls("timeback-start", "t1", "{\"purpose\":\"跨轮取证\"}"));
            llm.ToolCallsQueue.Enqueue(BuildToolCalls("random", "f1", "{\"min\":1,\"max\":10}"));
            llm.ToolCallsQueue.Enqueue(BuildToolCalls("timeback-back", "t2", "{\"findings\":\"结论：目录 85 个\"}"));
            session.PostUserMessage("开始取证");
            PumpUntilIdle(session);
            Assert.True(session.IsIdle, "msgCount=" + session.Context.GetMessageCount().ToString() + " phase=" + session.Phase.ToString() + " round=" + session.Round.ToString());
            // 前文：[user][start 声明][start 结果][back 声明][back 结果][结论轮回复] = 6 条
            Assert.Equal(6, session.Context.GetMessageCount());
            // 两次调用对保留——assistant 声明（ToolCallsJson）仍在
            LlmMessage[] all = session.Context.GetMessages();
            bool hasStartDecl = false;
            bool hasBackDecl = false;
            for (int i = 0; i < all.Length; i = i + 1)
            {
                string tcJson = all[i].ToolCallsJson == null ? "" : all[i].ToolCallsJson;
                if (all[i].Role != LlmRole.Assistant || !tcJson.Contains("timeback", StringComparison.Ordinal))
                {
                    continue;
                }
                if (tcJson.Contains("start", StringComparison.Ordinal))
                {
                    hasStartDecl = true;
                }
                if (tcJson.Contains("back", StringComparison.Ordinal))
                {
                    hasBackDecl = true;
                }
            }
            Assert.True(hasStartDecl);
            Assert.True(hasBackDecl);
            // 结论 = back 的工具返回值（tool 消息）
            Assert.True(HasMessage(session, LlmRole.Tool, "结论：目录 85 个"));
            // 返回值元数据——释放条数（back 时刻预算）+ 前文长度快照与净增（真实 usage 值；测试环境尚未请求 → 0）
            string backResult = ToolResultText(session, 1);
            Assert.Contains("\"released\":2", backResult);
            Assert.Contains("\"tokens\":0", backResult);
            Assert.Contains("\"grew\":0", backResult);
            // 查证过程（random 调用与结果）已删
            for (int i = 0; i < all.Length; i = i + 1)
            {
                Assert.DoesNotContain("random", all[i].Content == null ? "" : all[i].Content);
                Assert.DoesNotContain("random", all[i].ToolCallsJson == null ? "" : all[i].ToolCallsJson);
            }
            // 序列合法：末尾为结论轮的正式回复
            Assert.Equal(LlmRole.Assistant, all[all.Length - 1].Role);
            Assert.False(session.TimebackActive);
        }

        /// <summary>
        /// 回执自带用途（2026-10-01 · B 档）——start 回执正文含 purpose（第一个保留的工具对自解释）；
        /// back 结构化头含 purpose（回收卡自解释——不必回翻锚定声明）。
        /// </summary>
        [Fact]
        public void Back_MetaCarriesPurpose()
        {
            MockLlm llm = new MockLlm();
            CH4.ChatSession session = CreateSession(llm);
            llm.ToolCallsQueue.Enqueue(BuildToolCalls("timeback-start", "t1", "{\"purpose\":\"回执用途验证\"}"));
            llm.ToolCallsQueue.Enqueue(BuildToolCalls("random", "f1", "{\"min\":1,\"max\":10}"));
            llm.ToolCallsQueue.Enqueue(BuildToolCalls("timeback-back", "t2", "{\"findings\":\"结论：验证\\n事实：（无）\\n指针：（无）\"}"));
            session.PostUserMessage("开始取证");
            PumpUntilIdle(session);
            Assert.Contains("用途「回执用途验证」", ToolResultText(session, 0));
            Assert.Contains("\"purpose\":\"回执用途验证\"", ToolResultText(session, 1));
        }

        /// <summary>
        /// 同批 start + back——无可删区间（两次调用同一批），作用域照常关闭。
        /// </summary>
        [Fact]
        public void StartAndBackInSameBatch_DeletesNothing()
        {
            MockLlm llm = new MockLlm();
            CH4.ChatSession session = CreateSession(llm);
            string batch = BuildToolCallsBatch(
                new string[] { "timeback-start", "timeback-back" },
                new string[] { "t1", "t2" },
                new string[] { "{\"purpose\":\"同批\"}", "{\"findings\":\"结论：同批\"}" });
            llm.ToolCallsQueue.Enqueue(batch);
            session.PostUserMessage("同批开收");
            PumpUntilIdle(session);
            Assert.True(session.IsIdle);
            // user + 声明 + 两个结果 + 结论轮回复 = 5 条（无删除）
            Assert.Equal(5, session.Context.GetMessageCount());
            Assert.True(HasMessage(session, LlmRole.Tool, "结论：同批"));
            Assert.False(session.TimebackActive);
        }

        /// <summary>
        /// 同批多调用——start 与另一工具同批时，start 的完整结果块必须原样保留。
        /// （回归：曾按「声明 + 1」算保留边界，把同批其余结果误删 → 格式修复补「宿主中断」占位）
        /// </summary>
        [Fact]
        public void StartBatchWithSiblingTool_KeepsAllResults()
        {
            MockLlm llm = new MockLlm();
            CH4.ChatSession session = CreateSession(llm);
            string batch = BuildToolCallsBatch(
                new string[] { "timeback-start", "random" },
                new string[] { "t1", "r1" },
                new string[] { "{\"purpose\":\"同批多调用\"}", "{\"min\":1,\"max\":10}" });
            llm.ToolCallsQueue.Enqueue(batch);
            llm.ToolCallsQueue.Enqueue(BuildToolCalls("random", "r2", "{\"min\":1,\"max\":10}"));
            llm.ToolCallsQueue.Enqueue(BuildToolCalls("timeback-back", "t2", "{\"findings\":\"结论：多调用\"}"));
            session.PostUserMessage("同批多调用取证");
            PumpUntilIdle(session);
            Assert.True(session.IsIdle, "phase=" + session.Phase.ToString());
            // 前文：user + 声明(2 调用) + 结果×2 + back 声明 + back 结果 + 结论轮回复 = 7 条
            Assert.Equal(7, session.Context.GetMessageCount());
            // start 的真实结果保留（非「宿主中断」占位）
            Assert.True(HasMessage(session, LlmRole.Tool, "已锚定"));
            Assert.DoesNotContain("宿主中断", ToolResultText(session, 1));
            Assert.True(HasMessage(session, LlmRole.Tool, "结论：多调用"));
        }

        /// <summary>
        /// 作用域锁定——Note / sleep / timer 在活跃期一律拒绝（ERR|TIMEBACK_LOCKED）。
        /// </summary>
        [Fact]
        public void Scope_Locks_Note_Sleep_Timer()
        {
            MockLlm llm = new MockLlm();
            CH4.ChatSession session = CreateSession(llm);
            string batch = BuildToolCallsBatch(
                new string[] { "timeback-start", "Note", "sleep", "timer" },
                new string[] { "t1", "n1", "s1", "m1" },
                new string[] { "{\"purpose\":\"锁定验证\"}", "{\"action\":\"set\",\"content\":\"任务A\"}", "{\"seconds\":30}", "{\"content\":\"回来看看\",\"seconds\":60}" });
            llm.ToolCallsQueue.Enqueue(batch);
            session.PostUserMessage("锁定验证");
            PumpUntilIdle(session);
            Assert.Contains("TIMEBACK_LOCKED", ToolResultText(session, 1));
            Assert.Contains("TIMEBACK_LOCKED", ToolResultText(session, 2));
            Assert.Contains("TIMEBACK_LOCKED", ToolResultText(session, 3));
            // 作用域仍开着（锁定不影响 start 本身）
            Assert.True(session.TimebackActive);
        }
        /// <summary>暴毙风险黑名单（T2 扩展 · 2026-10-01 宽松化 · 2026-10-02 放行 mau-setup）——作用域存活期只拦「会让进程 / 作用域当场失效」的两件：restart-full / restart-incr / restart-host / host-reload 一律 ERR|TIMEBACK_LOCKED；其余（只读 / 仓库产物 / 一键链 prepare·sync-html / 配置写 / 管理指令）放行——作用域外同工具不受该判定拦截。</summary>
        [Fact]
        public void Scope_Locks_BodyRepairTools()
        {
            MockLlm llm = new MockLlm();
            CH4.ChatSession session = CreateSession(llm, new string[]
            {
                                "timeback-start", "timeback-back", "random", "info", "host-flows", "host-reload", "restart-full"
            });
            // 作用域外——host-reload 不被该判定拦（测试环境直执回落 ERR|NO_TOOL）
            llm.ToolCallsQueue.Enqueue(BuildToolCalls("host-reload", "h0", "{\"cat\":\"TextCat\"}"));
            // 作用域内——start 与「两件拦 + 三件放行」同批。
            // mau-setup 已于 2026-10-02 放行（deploy 从工具面退役后无暴毙风险），但它是 OA 工具——
            // 测试环境无工单消费者会挂起，故不在此批验证；黑名单清单本身由 §C5 规格 + 代码审查锁定。
            string batch = BuildToolCallsBatch(
                new string[] { "timeback-start", "host-reload", "restart-full", "host-flows", "info", "random" },
                new string[] { "t1", "h1", "r1", "f1", "i1", "x1" },
                new string[]
                {
                                    "{\"purpose\":\"黑名单验证\"}",
                                    "{\"cat\":\"TextCat\"}",
                                    "{}",
                                    "{}",
                                    "{}",
                                    "{\"min\":1,\"max\":10}"
                });
            llm.ToolCallsQueue.Enqueue(batch);
            session.PostUserMessage("黑名单验证");
            PumpUntilIdle(session);
            Assert.True(session.IsIdle, "phase=" + session.Phase.ToString());
            // 作用域外那一批未被锁定
            Assert.False(ToolResultText(session, 0).Contains("TIMEBACK_LOCKED", StringComparison.Ordinal));
            // 作用域内：start 回执保留 + 其后两件全拒（暴毙风险面）
            Assert.Contains("已锚定", ToolResultText(session, 1));
            Assert.Contains("TIMEBACK_LOCKED", ToolResultText(session, 2));
            Assert.Contains("TIMEBACK_LOCKED", ToolResultText(session, 3));
            // 放行面——只读 / 常驻内置工具不再被锁
            for (int i = 4; i <= 6; i = i + 1)
            {
                Assert.False(ToolResultText(session, i).Contains("TIMEBACK_LOCKED", StringComparison.Ordinal), "第 " + i.ToString() + " 件被误锁");
            }
            Assert.True(session.TimebackActive);
        }
        /// <summary>
        /// start 回执带 findings 骨架（2026-10-06 改 · 成果定位口径）——开锚即给格式，四段齐备（成果 / 未竟 / 卡点 / 失败）+ 位置硬约束句（只写位置不写结论）。
        /// </summary>
        [Fact]
        public void Start_ReceiptCarriesFindingsSkeleton()
        {
            MockLlm llm = new MockLlm();
            CH4.ChatSession session = CreateSession(llm);
            llm.ToolCallsQueue.Enqueue(BuildToolCalls("timeback-start", "s1", "{\"purpose\":\"骨架验证\"}"));
            session.PostUserMessage("骨架验证");
            PumpUntilIdle(session);
            string receipt = ToolResultText(session, 0);
            Assert.Contains("已锚定", receipt);
            Assert.Contains("成果：", receipt);
            Assert.Contains("未竟：", receipt);
            Assert.Contains("卡点：", receipt);
            Assert.Contains("失败：", receipt);
            Assert.Contains("只写「成果在哪」", receipt);
            Assert.Contains("位置必须是这趟真实读到", receipt);
        }
        /// <summary>
        /// 写操作台账——域内 order ≥ 1 的工具逐条登记（宿主记录），只读面不入账；back 回执附于 findings 之前。
        /// </summary>
        [Fact]
        public void Back_ReceiptCarriesWriteLog()
        {
            MockLlm llm = new MockLlm();
            CH4.ChatSession session = CreateSession(llm);
            llm.ToolCallsQueue.Enqueue(BuildToolCalls("timeback-start", "s1", "{\"purpose\":\"台账验证\"}"));
            session.PostUserMessage("开锚");
            PumpUntilIdle(session);
            llm.ToolCallsQueue.Enqueue(BuildToolCalls("time", "s2", "{}"));
            session.PostUserMessage("读时间");
            PumpUntilIdle(session);
            llm.ToolCallsQueue.Enqueue(BuildToolCalls("host-reload", "s3", "{\"cat\":\"TextCat\"}"));
            session.PostUserMessage("域内热重载");
            PumpUntilIdle(session);
            llm.ToolCallsQueue.Enqueue(BuildToolCalls("timeback-back", "s4", "{\"findings\":\"结论：台账验证\\n事实：（无）\\n指针：（无）\"}"));
            session.PostUserMessage("收域");
            PumpUntilIdle(session);
            string receipt = ToolResultText(session, 1);
            // 情报行——返回体正文自带裁剪读数（条数 / token / 写入；本次 1 处写入 → 带下表指引）
            Assert.Contains("📉 释放 ", receipt);
            Assert.Contains("写入操作 1 处（详见下表）", receipt);
            Assert.Contains("本域写操作台账", receipt);
            Assert.Contains("host-reload", receipt);
            Assert.Contains("FAIL", receipt);
            Assert.DoesNotContain("time · ", receipt);
        }

        /// <summary>
        /// 台账目标标识提取（2026-10-02）——路径类取末段 · 标识类原样 · powershell 取命令行原文（不截断）· 取不到写「—」。
        /// </summary>
        [Fact]
        public void TimebackWriteTarget_ShapesAndPowerShellCommand()
        {
            Assert.Equal("Tree.md", CH4.ChatSession.TimebackWriteTarget(@"{""path"":""ccbp:L1/Tree.md""}"));
            Assert.Equal("Foo", CH4.ChatSession.TimebackWriteTarget(@"{""class"":""Foo""}"));
            string psArg = @"{""command"":""dotnet test CatHome4.sln --nologo""}";
            Assert.Equal("dotnet test CatHome4.sln --nologo", CH4.ChatSession.TimebackWriteTarget(psArg));
            Assert.Equal("—", CH4.ChatSession.TimebackWriteTarget("{}"));
        }

        /// <summary>
        /// 被删区间的视图块转 gap——人可见面保住查证过程（且落在 Rebuild 不清的容器）。
        /// </summary>
        [Fact]
        public void DeletedRange_ViewConvertedToGap()
        {
            MockLlm llm = new MockLlm();
            CH4.ChatSession session = CreateSession(llm);
            llm.ToolCallsQueue.Enqueue(BuildToolCalls("timeback-start", "t1", "{\"purpose\":\"视图转 gap\"}"));
            llm.ToolCallsQueue.Enqueue(BuildToolCalls("random", "f1", "{\"min\":1,\"max\":10}"));
            llm.ToolCallsQueue.Enqueue(BuildToolCalls("timeback-back", "t2", "{\"findings\":\"结论：视图验证\"}"));
            session.PostUserMessage("开始取证");
            PumpUntilIdle(session);
            // 被删的 random 工具卡内容仍在视图层（转 gap 文本）
            Assert.True(ViewHas(session, "random"));
            Assert.True(ViewHas(session, "结论：视图验证"));
        }

        /// <summary>
        /// 归档落盘（A104）——一次回收一个文件：首行 meta + 次行 info 快照 + 其后被删前文消息；全局计数落 count.json。
        /// </summary>
        [Fact]
        public void Back_WritesScopeArchiveFile()
        {
            MockLlm llm = new MockLlm();
            string archiveDir = Path.Combine(Path.GetTempPath(), "cat4tb_scope_" + Guid.NewGuid().ToString("N"));
            CH4.ChatSession session = CreateSession(llm);
            CH4.ChatSession.TimebackArchiveDirProvider = delegate (string catKey)
            {
                return archiveDir;
            };
            llm.ToolCallsQueue.Enqueue(BuildToolCalls("timeback-start", "t1", "{\"purpose\":\"归档验证\"}"));
            llm.ToolCallsQueue.Enqueue(BuildToolCalls("random", "f1", "{\"min\":1,\"max\":10}"));
            llm.ToolCallsQueue.Enqueue(BuildToolCalls("timeback-back", "t2", "{\"findings\":\"结论：A\"}"));
            session.PostUserMessage("开始取证");
            PumpUntilIdle(session);
            // [断言1] 全局计数——count.json 落盘且为 1
            string countPath = Path.Combine(archiveDir, "count.json");
            Assert.True(File.Exists(countPath));
            Assert.Contains("\"count\":1", File.ReadAllText(countPath));
            // [断言2] 作用域文件——<编号>-<回收时刻>.jsonl（一次回收一个文件）
            string[] files = Directory.GetFiles(archiveDir, "*.jsonl");
            Assert.Single(files);
            Assert.StartsWith("1-", Path.GetFileName(files[0]));
            string[] lines = File.ReadAllLines(files[0]);
            // [断言3] 首行 meta（记录字段）+ 次行 info 快照 + 其后被删前文消息
            Assert.Contains("\"t\":\"timeback\"", lines[0]);
            Assert.Contains("归档验证", lines[0]);
            Assert.Contains("\"n\":2", lines[0]);
            Assert.Contains("\"findings\":\"结论：A\"", lines[0]);
            Assert.Contains("info 快照占位", lines[1]);
            Assert.True(lines.Length >= 4);
            Assert.Contains("\"t\":\"m\"", lines[2]);
            Assert.Contains("\"t\":\"m\"", lines[3]);
            Directory.Delete(archiveDir, true);
        }

        /// <summary>
        /// 参数面——未闭合时再次 start 被拒（ERR|TIMEBACK_NESTED）；无作用域 / 缺载荷的 back 被拒。
        /// </summary>
        [Fact]
        public void Args_RejectedAsNestedOrWithoutScope()
        {
            MockLlm llm = new MockLlm();
            CH4.ChatSession session = CreateSession(llm);
            // 无作用域 back
            llm.ToolCallsQueue.Enqueue(BuildToolCalls("timeback-back", "t0", "{\"findings\":\"结论：C\"}"));
            session.PostUserMessage("空 back");
            PumpUntilIdle(session);
            Assert.Contains("TIMEBACK_NO_SCOPE", ToolResultText(session, 0));
            // 批内重复 timeback-start——参数面拒绝（A106：批内各至多 × 1）
            string batch = BuildToolCallsBatch(
                new string[] { "timeback-start", "timeback-start" },
                new string[] { "t1", "t2" },
                new string[] { "{\"purpose\":\"甲\"}", "{\"purpose\":\"乙\"}" });
            llm.ToolCallsQueue.Enqueue(batch);
            session.PostUserMessage("同批连开两次");
            PumpUntilIdle(session);
            Assert.Contains("批内不允许多条", ToolResultText(session, 2));
            Assert.True(session.TimebackActive);
            // 跨批再次 start——嵌套拒绝（ERR|TIMEBACK_NESTED）
            llm.ToolCallsQueue.Enqueue(BuildToolCalls("timeback-start", "t4", "{\"purpose\":\"丙\"}"));
            session.PostUserMessage("再开一次");
            PumpUntilIdle(session);
            Assert.Contains("TIMEBACK_NESTED", ToolResultText(session, 3));
            // 缺 findings 的 back
            llm.ToolCallsQueue.Enqueue(BuildToolCalls("timeback-back", "t5", "{}"));
            session.PostUserMessage("缺载荷");
            PumpUntilIdle(session);
            Assert.Contains("TIMEBACK_ARGS", ToolResultText(session, 4));
        }

        /// <summary>
        /// 归档编号（A104）——count.json 全局递增：跨实例（等价跨重启）续接；不依赖归档文件内容。
        /// </summary>
        [Fact]
        public void Archive_NumberIncrementsAcrossInstances()
        {
            string dir = Path.Combine(Path.GetTempPath(), "cat4tb_count_" + Guid.NewGuid().ToString("N"));
            CH4.TimebackArchive first = new CH4.TimebackArchive(dir);
            Assert.Equal(1L, first.NextId());
            // 跨实例（等价跨重启）续接——读 count.json，不扫归档文件
            CH4.TimebackArchive second = new CH4.TimebackArchive(dir);
            Assert.Equal(2L, second.NextId());
            Assert.Equal(3L, second.NextId());
            Assert.Contains("\"count\":3", File.ReadAllText(Path.Combine(dir, "count.json")));
            CH4.TimebackArchive third = new CH4.TimebackArchive(dir);
            Assert.Equal(4L, third.NextId());
            Directory.Delete(dir, true);
        }
        /// <summary>
        /// 状态提示（T2）——作用域内累计满 25 个事件（think / 工具完成 / assistant 各计 1）时注入一条 user 系统提示。
        /// 角色 user（source=systemauto）——思考模式下 assistant 注入缺 reasoning 回传会致端点 400（判例 2026-09-28）。
        /// </summary>
        [Fact]
        public void Notice_InjectedAfterTwentyFiveEvents()
        {
            MockLlm llm = new MockLlm();
            CH4.ChatSession session = CreateSession(llm);
            llm.ToolCallsQueue.Enqueue(BuildToolCalls("timeback-start", "t1", "{\"purpose\":\"计数取证\"}"));
            // 计数从作用域开启后起算：start 批只计其结果 1（该批 assistant 产出于开锚之前）；
            // 其后每批 +2（assistant + 工具完成）——第 12 个 random 批后累计 25，越过 25 阈值触发一次自述
            for (int i = 0; i < 12; i = i + 1)
            {
                llm.ToolCallsQueue.Enqueue(BuildToolCalls("random", "r" + i.ToString(), "{\"min\":1,\"max\":10}"));
            }
            session.PostUserMessage("计数取证");
            PumpUntilIdle(session);
            Assert.True(session.IsIdle, "phase=" + session.Phase.ToString());
            Assert.True(session.TimebackActive);
            Assert.True(HasMessage(session, LlmRole.User, "你处在 timeback 中"));
            Assert.True(HasMessage(session, LlmRole.User, "已经历【25】条"));
        }
        /// <summary>
        /// 状态提示归作用域区间——回收时与查证过程一并删除（零残留）。
        /// </summary>
        [Fact]
        public void Notice_RemovedWithScopeOnBack()
        {
            MockLlm llm = new MockLlm();
            CH4.ChatSession session = CreateSession(llm);
            llm.ToolCallsQueue.Enqueue(BuildToolCalls("timeback-start", "t1", "{\"purpose\":\"计数取证\"}"));
            // 先凑满 25 阈值触发一次状态提示（1 + 2×12），再由 back 批验证提示随区间回收删除
            for (int i = 0; i < 12; i = i + 1)
            {
                llm.ToolCallsQueue.Enqueue(BuildToolCalls("random", "r" + i.ToString(), "{\"min\":1,\"max\":10}"));
            }
            llm.ToolCallsQueue.Enqueue(BuildToolCalls("timeback-back", "t2", "{\"findings\":\"结论：计数取证\"}"));
            session.PostUserMessage("计数取证");
            PumpUntilIdle(session);
            Assert.True(session.IsIdle, "phase=" + session.Phase.ToString());
            Assert.False(session.TimebackActive);
            // start 回执（保留）在，状态提示（区间内）已随回收删除
            Assert.True(HasMessage(session, LlmRole.Tool, "已锚定"));
            Assert.False(HasMessage(session, LlmRole.User, "你处在 timeback 中"));
            Assert.True(HasMessage(session, LlmRole.Tool, "结论：计数取证"));
            LlmMessage[] all = session.Context.GetMessages();
            for (int i = 0; i < all.Length; i = i + 1)
            {
                string content = all[i].Content == null ? "" : all[i].Content;
                Assert.DoesNotContain("random", content);
            }
        }
        /// <summary>
        /// 归档读面（A104）——只读各文件首行 meta、按编号取末尾 N 个；异名文件跳过。
        /// </summary>
        [Fact]
        public void Archive_ReadRecentMetaSkipsBadFiles()
        {
            string dir = Path.Combine(Path.GetTempPath(), "cat4tb_recent_" + Guid.NewGuid().ToString("N"));
            CH4.TimebackArchive archive = new CH4.TimebackArchive(dir);
            CH4.TimebackScopeRecord first = BuildScopeRecord(archive.NextId(), "用途甲", "结论甲", 1000);
            string path1 = "";
            // info 快照含缩进换行——入档须归一为单行（JSONL 每行独立）
            string multilineInfo = "{\n  \"cat\": \"cat\",\n  \"version\": \"1.0\"\n}";
            Assert.True(archive.WriteScopeFile(first, multilineInfo, new LlmMessage[0], out path1));
            Assert.Equal(2, File.ReadAllLines(path1).Length);
            CH4.TimebackScopeRecord second = BuildScopeRecord(archive.NextId(), "用途乙", "结论乙", 2000);
            string path2 = "";
            Assert.True(archive.WriteScopeFile(second, "", new LlmMessage[0], out path2));
            // 异名文件（编号前缀不可解析）——不参与读面
            File.WriteAllText(Path.Combine(dir, "readme.jsonl"), "{\"t\":\"timeback\",\"id\":99}");
            List<string> one = archive.ReadRecentMeta(1);
            Assert.Single(one);
            Assert.Contains("结论乙", one[0]);
            List<string> all = archive.ReadRecentMeta(5);
            Assert.Equal(2, all.Count);
            // 最旧在前（按编号升序）
            Assert.Contains("结论甲", all[0]);
            Assert.Contains("结论乙", all[1]);
            Assert.False(archive.LastWriteFailed);
            Directory.Delete(dir, true);
        }

        /// <summary>
        /// 构造归档记录——读面用例用（编号 / 用途 / findings / 回收时刻可变）。
        /// </summary>
        /// <param name="id">编号</param>
        /// <param name="purpose">用途标签</param>
        /// <param name="findings">带回载荷</param>
        /// <param name="backAt">回收时刻——Unix 毫秒</param>
        /// <returns>记录实例</returns>
        private static CH4.TimebackScopeRecord BuildScopeRecord(long id, string purpose, string findings, long backAt)
        {
            CH4.TimebackScopeRecord record = new CH4.TimebackScopeRecord();
            record.Id = id;
            record.CatKey = "cat";
            record.Purpose = purpose;
            record.Anchor = 3;
            record.StartAt = backAt - 1000;
            record.BackAt = backAt;
            record.Seconds = 1;
            record.N = 2;
            record.Tokens = 900;
            record.Grew = 100;
            record.Released = 2;
            record.Findings = findings;
            return record;
        }
        /// <summary>
        /// 归档写失败（A104 判据）——写面失败可见（返回 false + LastWriteFailed / LastWriteFailedAny 立起；info archive.ok 数据源）。
        /// </summary>
        [Fact]
        public void Archive_WriteFailureIsVisible()
        {
            // 目录路径指向一个已存在的文件——建目录必然失败（写面失败必须可见）
            string blockPath = Path.Combine(Path.GetTempPath(), "cat4tb_block_" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(blockPath, "x");
            CH4.TimebackArchive archive = new CH4.TimebackArchive(blockPath);
            string path = "";
            Assert.False(archive.WriteScopeFile(BuildScopeRecord(1, "用途", "结论", 1000), "", new LlmMessage[0], out path));
            Assert.True(archive.LastWriteFailed);
            Assert.True(CH4.TimebackArchive.LastWriteFailedAny);
            File.Delete(blockPath);
        }
        /// <summary>
        /// A106 批内次序——start 在数组后位也必须先执行：数组前位的 C5 黑名单工具（host-reload）按作用域活跃判定被拒。
        /// （回归：按数组序判定时，前位工具在作用域开启前逃逸——作用域内改本体的缺口。）
        /// </summary>
        [Fact]
        public void BatchOrder_StartStripsBeforeSiblings_LocksBodyRepairTools()
        {
            MockLlm llm = new MockLlm();
            CH4.ChatSession session = CreateSession(llm, new string[] { "timeback-start", "timeback-back", "host-reload" });
            string batch = BuildToolCallsBatch(
                new string[] { "host-reload", "timeback-start" },
                new string[] { "h1", "t1" },
                new string[] { "{\"cat\":\"TextCat\"}", "{\"purpose\":\"批内次序\"}" });
            llm.ToolCallsQueue.Enqueue(batch);
            session.PostUserMessage("批内次序");
            PumpUntilIdle(session);
            Assert.True(session.IsIdle, "phase=" + session.Phase.ToString());
            // 数组前位的 host-reload 被 C5 拦下；后位的 start 已锚定
            Assert.Contains("TIMEBACK_LOCKED", ToolResultText(session, 0));
            Assert.Contains("已锚定", ToolResultText(session, 1));
            Assert.True(session.TimebackActive);
        }
        /// <summary>
        /// A106 批内次序——start 失败（参数面拒绝）不阻断、不按假想作用域拦截：作用域未开时同批 host-reload 照常执行。
        /// </summary>
        [Fact]
        public void BatchOrder_StartFailed_NoScopeNoLocks()
        {
            MockLlm llm = new MockLlm();
            CH4.ChatSession session = CreateSession(llm, new string[] { "timeback-start", "timeback-back", "host-reload" });
            string batch = BuildToolCallsBatch(
                new string[] { "timeback-start", "host-reload" },
                new string[] { "t1", "h1" },
                new string[] { "{}", "{\"cat\":\"TextCat\"}" });
            llm.ToolCallsQueue.Enqueue(batch);
            session.PostUserMessage("start 失败");
            PumpUntilIdle(session);
            Assert.True(session.IsIdle, "phase=" + session.Phase.ToString());
            // start 缺 purpose → 参数面拒绝；作用域未开 → host-reload 不被本体修正黑名单拦
            Assert.Contains("TIMEBACK_ARGS", ToolResultText(session, 0));
            Assert.Contains("NO_TOOL", ToolResultText(session, 1));
            Assert.False(session.TimebackActive);
        }
        /// <summary>
        /// A106 批内次序——同一批内 back 在数组前位、start 在数组后位时规范化为「先开后收」：
        /// back 不再误报 NO_SCOPE，同批区间为空不删（批是原子时间点，批内序由宿主定）。
        /// </summary>
        [Fact]
        public void BatchOrder_BackStripsAfterSiblings_SameBatchOrderNormalized()
        {
            MockLlm llm = new MockLlm();
            CH4.ChatSession session = CreateSession(llm);
            string batch = BuildToolCallsBatch(
                new string[] { "timeback-back", "random", "timeback-start" },
                new string[] { "t2", "r1", "t1" },
                new string[] { "{\"findings\":\"结论：乱序开收\"}", "{\"min\":1,\"max\":10}", "{\"purpose\":\"乱序开收\"}" });
            llm.ToolCallsQueue.Enqueue(batch);
            session.PostUserMessage("乱序开收");
            PumpUntilIdle(session);
            Assert.True(session.IsIdle, "phase=" + session.Phase.ToString());
            // back 作为结论返回（非 NO_SCOPE）；同批无跨批区间 → 不删；作用域关闭
            Assert.Contains("结论：乱序开收", ToolResultText(session, 0));
            Assert.DoesNotContain("NO_SCOPE", ToolResultText(session, 0));
            Assert.False(session.TimebackActive);
            // user + 声明 + 三结果 + 结论回复 = 6 条
            Assert.Equal(6, session.Context.GetMessageCount());
        }
        /// <summary>
        /// A106 批内次序——back 与 sibling 工具同批（back 在数组前位）：回收区间仍以本批声明为上界，
        /// 同批 sibling 结果保留，释放条数与跨批查证区间一致。
        /// </summary>
        [Fact]
        public void BatchOrder_BackWithSiblingTool_ScopeRangeStillDeleted()
        {
            MockLlm llm = new MockLlm();
            CH4.ChatSession session = CreateSession(llm);
            llm.ToolCallsQueue.Enqueue(BuildToolCalls("timeback-start", "t1", "{\"purpose\":\"批内 sibling\"}"));
            llm.ToolCallsQueue.Enqueue(BuildToolCalls("random", "f1", "{\"min\":1,\"max\":10}"));
            llm.ToolCallsQueue.Enqueue(BuildToolCallsBatch(
                new string[] { "timeback-back", "random" },
                new string[] { "t2", "r2" },
                new string[] { "{\"findings\":\"结论：批内 sibling\"}", "{\"min\":1,\"max\":10}" }));
            session.PostUserMessage("批内 sibling 取证");
            PumpUntilIdle(session);
            Assert.True(session.IsIdle, "phase=" + session.Phase.ToString());
            // 跨批查证区间（random 声明 + 结果）删除 → released:2
            Assert.Contains("\"released\":2", ToolResultText(session, 1));
            // 同批 sibling 结果保留（back 后置执行 → 区间上界仍为本批声明）
            Assert.Contains("min", ToolResultText(session, 2));
            Assert.True(HasMessage(session, LlmRole.Tool, "结论：批内 sibling"));
            Assert.False(session.TimebackActive);
        }
        /// <summary>
        /// A106 批内次序——批内不允许多条同名 timeback 工具：同类第 2 条起判参数面拒绝（不静默丢弃）。
        /// </summary>
        [Fact]
        public void BatchOrder_DuplicateTimebackRejected()
        {
            MockLlm llm = new MockLlm();
            CH4.ChatSession session = CreateSession(llm);
            string batch = BuildToolCallsBatch(
                new string[] { "timeback-start", "timeback-start" },
                new string[] { "t1", "t2" },
                new string[] { "{\"purpose\":\"首条\"}", "{\"purpose\":\"重复\"}" });
            llm.ToolCallsQueue.Enqueue(batch);
            session.PostUserMessage("重复 timeback");
            PumpUntilIdle(session);
            Assert.True(session.IsIdle, "phase=" + session.Phase.ToString());
            Assert.Contains("已锚定", ToolResultText(session, 0));
            Assert.Contains("批内不允许多条", ToolResultText(session, 1));
            Assert.True(session.TimebackActive);
        }
        /// <summary>
        /// A165 契约 H——timeback 回收**不触碰视图层**：持久区只增不改，回收只作用于送入 LLM 的前文；
        /// 视图层不再产 `void` 块（移出语义整体退役）。
        /// </summary>
        [Fact]
        public void Back_DoesNotTouchViewBlocks()
        {
            MockLlm llm = new MockLlm();
            CH4.ChatSession session = CreateSession(llm);
            llm.ToolCallsQueue.Enqueue(BuildToolCalls("timeback-start", "t1", "{\"purpose\":\"视图不受影响验证\"}"));
            llm.ToolCallsQueue.Enqueue(BuildToolCalls("random", "f1", "{\"min\":1,\"max\":10}"));
            llm.ToolCallsQueue.Enqueue(BuildToolCalls("timeback-back", "t2", "{\"findings\":\"结论：视图不受影响\"}"));
            session.PostUserMessage("视图不受影响验证");
            PumpUntilIdle(session);
            Assert.True(session.IsIdle, "phase=" + session.Phase.ToString());
            CH4.ViewBlock[] blocks = session.GetViewBlocks();
            int voidCount = 0;
            for (int i = 0; i < blocks.Length; i = i + 1)
            {
                if (blocks[i].RenderType == "void")
                {
                    voidCount = voidCount + 1;
                }
            }
            Assert.Equal(0, voidCount);
            // 回收仍在前文生效——区间内容已从送入 LLM 的前文移除，结论保留
            Assert.True(HasMessage(session, LlmRole.Tool, "结论：视图不受影响"));
        }
        /// <summary>
        /// A165 契约 H——同批 sibling 结果卡**留在视图层**：视图层与真实前文并列，
        /// 前文侧回收只影响前文，不驱动视图（不再有「移出 / 废弃段」概念）。
        /// </summary>
        [Fact]
        public void Back_KeepsSiblingResultCardInView()
        {
            MockLlm llm = new MockLlm();
            CH4.ChatSession session = CreateSession(llm);
            string batch = BuildToolCallsBatch(
                new string[] { "timeback-start", "random" },
                new string[] { "t1", "r1" },
                new string[] { "{\"purpose\":\"sibling 保留\"}", "{\"min\":1,\"max\":10}" });
            llm.ToolCallsQueue.Enqueue(batch);
            llm.ToolCallsQueue.Enqueue(BuildToolCalls("timeback-back", "t2", "{\"findings\":\"结论：sibling\"}"));
            session.PostUserMessage("sibling 保留");
            PumpUntilIdle(session);
            Assert.True(session.IsIdle, "phase=" + session.Phase.ToString());
            CH4.ViewBlock[] blocks = session.GetViewBlocks();
            int voidCount = 0;
            bool anchorCardKept = false;
            bool siblingCardKept = false;
            for (int i = 0; i < blocks.Length; i = i + 1)
            {
                string candidatePayload = blocks[i].Payload == null ? "" : blocks[i].Payload;
                if (blocks[i].RenderType == "void")
                {
                    voidCount = voidCount + 1;
                }
                if (blocks[i].RenderType == "toolcard" && candidatePayload.Contains("\"timeback-start\"", StringComparison.Ordinal))
                {
                    anchorCardKept = true;
                }
                if (blocks[i].RenderType == "toolcard" && candidatePayload.Contains("\"random\"", StringComparison.Ordinal))
                {
                    siblingCardKept = true;
                }
            }
            Assert.Equal(0, voidCount);
            Assert.True(anchorCardKept);
            Assert.True(siblingCardKept);
        }
    }
}

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
    /// timeback 上下文作用域测试（design-ch4-timeback · 2026-09-28 语义重设）——
    /// 区间删除（锚点 = start 调用 / 结论 = back 返回值）/ 同批空区间 / 三工具锁定 / 视图转 gap / 参数面。
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
        /// 构造测试会话——临时前文文件 + 唯一临时归档路径 + Mock LLM（Note / timeback / random 工具面）。
        /// </summary>
        /// <param name="llm">Mock LLM</param>
        /// <returns>会话实体</returns>
        private static CH4.ChatSession CreateSession(MockLlm llm)
        {
            return CreateSession(llm, new string[] { "Note", "timeback", "random", "sleep", "timer" });
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
            string archivePath = Path.Combine(Path.GetTempPath(), "cat4tb_" + Guid.NewGuid().ToString("N") + ".jsonl");
            CH4.ChatSession.TimebackArchivePathProvider = delegate (string catKey)
            {
                return archivePath;
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
            llm.ToolCallsQueue.Enqueue(BuildToolCalls("timeback", "t1", "{\"action\":\"start\",\"purpose\":\"跨轮取证\"}"));
            llm.ToolCallsQueue.Enqueue(BuildToolCalls("random", "f1", "{\"min\":1,\"max\":10}"));
            llm.ToolCallsQueue.Enqueue(BuildToolCalls("timeback", "t2", "{\"action\":\"back\",\"findings\":\"结论：目录 85 个\"}"));
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
        /// 同批 start + back——无可删区间（两次调用同一批），作用域照常关闭。
        /// </summary>
        [Fact]
        public void StartAndBackInSameBatch_DeletesNothing()
        {
            MockLlm llm = new MockLlm();
            CH4.ChatSession session = CreateSession(llm);
            string batch = BuildToolCallsBatch(
                new string[] { "timeback", "timeback" },
                new string[] { "t1", "t2" },
                new string[] { "{\"action\":\"start\",\"purpose\":\"同批\"}", "{\"action\":\"back\",\"findings\":\"结论：同批\"}" });
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
                new string[] { "timeback", "random" },
                new string[] { "t1", "r1" },
                new string[] { "{\"action\":\"start\",\"purpose\":\"同批多调用\"}", "{\"min\":1,\"max\":10}" });
            llm.ToolCallsQueue.Enqueue(batch);
            llm.ToolCallsQueue.Enqueue(BuildToolCalls("random", "r2", "{\"min\":1,\"max\":10}"));
            llm.ToolCallsQueue.Enqueue(BuildToolCalls("timeback", "t2", "{\"action\":\"back\",\"findings\":\"结论：多调用\"}"));
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
                new string[] { "timeback", "Note", "sleep", "timer" },
                new string[] { "t1", "n1", "s1", "m1" },
                new string[] { "{\"action\":\"start\",\"purpose\":\"锁定验证\"}", "{\"action\":\"set\",\"content\":\"任务A\"}", "{\"seconds\":30}", "{\"content\":\"回来看看\",\"seconds\":60}" });
            llm.ToolCallsQueue.Enqueue(batch);
            session.PostUserMessage("锁定验证");
            PumpUntilIdle(session);
            Assert.Contains("TIMEBACK_LOCKED", ToolResultText(session, 1));
            Assert.Contains("TIMEBACK_LOCKED", ToolResultText(session, 2));
            Assert.Contains("TIMEBACK_LOCKED", ToolResultText(session, 3));
            // 作用域仍开着（锁定不影响 start 本身）
            Assert.True(session.TimebackActive);
        }
        /// <summary>
        /// 本体修正黑名单（T2 扩展）——作用域存活期禁止对 CH4 自身做修正：
        /// majordomo-restart / host-reload / mau-* / config-* / majordomo-cmd 一律 ERR|TIMEBACK_LOCKED；作用域外同工具不受该判定拦截。
        /// </summary>
        [Fact]
        public void Scope_Locks_BodyRepairTools()
        {
            MockLlm llm = new MockLlm();
            CH4.ChatSession session = CreateSession(llm, new string[]
            {
                        "timeback", "random", "host-reload", "mau-verify", "mau-gen", "mau-proj", "mau-setup",
                        "majordomo-restart", "config-set", "config-reset", "config-cat-set", "majordomo-cmd"
            });
            // 作用域外——host-reload 不被该判定拦（测试环境直执回落 ERR|NO_TOOL）
            llm.ToolCallsQueue.Enqueue(BuildToolCalls("host-reload", "h0", "{\"cat\":\"TextCat\"}"));
            // 作用域内——start 与全部黑名单工具同批
            string batch = BuildToolCallsBatch(
                new string[] { "timeback", "host-reload", "mau-verify", "mau-gen", "mau-proj", "mau-setup", "majordomo-restart", "config-set", "config-reset", "config-cat-set", "majordomo-cmd" },
                new string[] { "t1", "h1", "m1", "m2", "m3", "m4", "r1", "c1", "c2", "c3", "g1" },
                new string[]
                {
                            "{\"action\":\"start\",\"purpose\":\"黑名单验证\"}",
                            "{\"cat\":\"TextCat\"}",
                            "{\"file\":\"corpus/ch4/text_cat/text_cat.mau\"}",
                            "{\"proj\":\"corpus/ch4/text_cat/text_cat.mauproj\"}",
                            "{\"proj\":\"corpus/ch4/text_cat/text_cat.mauproj\",\"build\":true}",
                            "{\"mode\":\"prepare\"}",
                            "{}",
                            "{\"key\":\"ui.chat_font_size\",\"value\":\"15\"}",
                            "{\"key\":\"ui.chat_font_size\"}",
                            "{\"cat\":\"tb\",\"field\":\"displayName\",\"value\":\"x\"}",
                            "cat.list"
                });
            llm.ToolCallsQueue.Enqueue(batch);
            session.PostUserMessage("黑名单验证");
            PumpUntilIdle(session);
            Assert.True(session.IsIdle, "phase=" + session.Phase.ToString());
            // 作用域外那一批未被锁定
            Assert.False(ToolResultText(session, 0).Contains("TIMEBACK_LOCKED", StringComparison.Ordinal));
            // 作用域内：start 回执保留 + 其后 10 件全拒
            Assert.Contains("已锚定", ToolResultText(session, 1));
            for (int i = 2; i <= 11; i = i + 1)
            {
                Assert.Contains("TIMEBACK_LOCKED", ToolResultText(session, i));
            }
            Assert.True(session.TimebackActive);
        }

        /// <summary>
        /// 被删区间的视图块转 gap——人可见面保住查证过程（且落在 Rebuild 不清的容器）。
        /// </summary>
        [Fact]
        public void DeletedRange_ViewConvertedToGap()
        {
            MockLlm llm = new MockLlm();
            CH4.ChatSession session = CreateSession(llm);
            llm.ToolCallsQueue.Enqueue(BuildToolCalls("timeback", "t1", "{\"action\":\"start\",\"purpose\":\"视图转 gap\"}"));
            llm.ToolCallsQueue.Enqueue(BuildToolCalls("random", "f1", "{\"min\":1,\"max\":10}"));
            llm.ToolCallsQueue.Enqueue(BuildToolCalls("timeback", "t2", "{\"action\":\"back\",\"findings\":\"结论：视图验证\"}"));
            session.PostUserMessage("开始取证");
            PumpUntilIdle(session);
            // 被删的 random 工具卡内容仍在视图层（转 gap 文本）
            Assert.True(ViewHas(session, "random"));
            Assert.True(ViewHas(session, "结论：视图验证"));
        }

        /// <summary>
        /// 归档落盘——open + close 两行（本猫文件；close.n = 实际删除条数）。
        /// </summary>
        [Fact]
        public void Back_WritesArchiveOpenAndClose()
        {
            MockLlm llm = new MockLlm();
            CH4.ChatSession session = CreateSession(llm);
            string archivePath = Path.Combine(Path.GetTempPath(), "cat4tb_arch_" + Guid.NewGuid().ToString("N") + ".jsonl");
            CH4.ChatSession.TimebackArchivePathProvider = delegate (string catKey)
            {
                return archivePath;
            };
            llm.ToolCallsQueue.Enqueue(BuildToolCalls("timeback", "t1", "{\"action\":\"start\",\"purpose\":\"归档验证\"}"));
            llm.ToolCallsQueue.Enqueue(BuildToolCalls("random", "f1", "{\"min\":1,\"max\":10}"));
            llm.ToolCallsQueue.Enqueue(BuildToolCalls("timeback", "t2", "{\"action\":\"back\",\"findings\":\"结论：A\"}"));
            session.PostUserMessage("开始取证");
            PumpUntilIdle(session);
            Assert.True(File.Exists(archivePath));
            string[] lines = File.ReadAllLines(archivePath);
            Assert.Equal(2, lines.Length);
            Assert.Contains("\"t\":\"open\"", lines[0]);
            Assert.Contains("\"t\":\"close\"", lines[1]);
            Assert.Contains("归档验证", lines[0]);
            File.Delete(archivePath);
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
            llm.ToolCallsQueue.Enqueue(BuildToolCalls("timeback", "t0", "{\"action\":\"back\",\"findings\":\"结论：C\"}"));
            session.PostUserMessage("空 back");
            PumpUntilIdle(session);
            Assert.Contains("TIMEBACK_NO_SCOPE", ToolResultText(session, 0));
            // 嵌套 start（同批：第二个 start 被拒）
            string batch = BuildToolCallsBatch(
                new string[] { "timeback", "timeback" },
                new string[] { "t1", "t2" },
                new string[] { "{\"action\":\"start\",\"purpose\":\"甲\"}", "{\"action\":\"start\",\"purpose\":\"乙\"}" });
            llm.ToolCallsQueue.Enqueue(batch);
            session.PostUserMessage("连开两次");
            PumpUntilIdle(session);
            Assert.Contains("TIMEBACK_NESTED", ToolResultText(session, 2));
            Assert.True(session.TimebackActive);
            // 缺 findings 的 back
            llm.ToolCallsQueue.Enqueue(BuildToolCalls("timeback", "t3", "{\"action\":\"back\"}"));
            session.PostUserMessage("缺载荷");
            PumpUntilIdle(session);
            Assert.Contains("TIMEBACK_ARGS", ToolResultText(session, 3));
        }

        /// <summary>
        /// 归档编号——跨实例（等价跨重启）递增；末行残缺不影响取号。
        /// </summary>
        [Fact]
        public void Archive_NumberIncrementsAcrossInstances()
        {
            string path = Path.Combine(Path.GetTempPath(), "cat4tb_arch_" + Guid.NewGuid().ToString("N") + ".jsonl");
            CH4.TimebackArchive first = new CH4.TimebackArchive(path);
            Assert.Equal(1L, first.NextId());
            Assert.True(first.AppendOpen(1, "cat", 0, 3, 1000, "用途", 5));
            Assert.True(first.AppendClose(1, 2000, 4, 1, "结论"));
            CH4.TimebackArchive second = new CH4.TimebackArchive(path);
            Assert.Equal(2L, second.NextId());
            Assert.True(second.AppendOpen(2, "cat", 1, 3, 1000, "用途2", 5));
            File.AppendAllText(path, "{\"t\":\"open\",\"id\":9");
            CH4.TimebackArchive third = new CH4.TimebackArchive(path);
            Assert.Equal(3L, third.NextId());
            File.Delete(path);
        }
        /// <summary>
        /// 状态提示（T2）——作用域内累计满 10 个事件（think / 工具完成 / assistant 各计 1）时注入一条 user 系统提示。
        /// 角色 user（source=systemauto）——思考模式下 assistant 注入缺 reasoning 回传会致端点 400（判例 2026-09-28）。
        /// </summary>
        [Fact]
        public void Notice_InjectedAfterTenEvents()
        {
            MockLlm llm = new MockLlm();
            CH4.ChatSession session = CreateSession(llm);
            llm.ToolCallsQueue.Enqueue(BuildToolCalls("timeback", "t1", "{\"action\":\"start\",\"purpose\":\"计数取证\"}"));
            // 计数从作用域开启后起算：start 批只计其结果 1（该批 assistant 产出于开锚之前）；
            // 其后每批 +2（assistant + 工具完成）——第 5 个 random 批后累计 11，越过 10 阈值触发一次自述
            for (int i = 0; i < 5; i = i + 1)
            {
                llm.ToolCallsQueue.Enqueue(BuildToolCalls("random", "r" + i.ToString(), "{\"min\":1,\"max\":10}"));
            }
            session.PostUserMessage("计数取证");
            PumpUntilIdle(session);
            Assert.True(session.IsIdle, "phase=" + session.Phase.ToString());
            Assert.True(session.TimebackActive);
            Assert.True(HasMessage(session, LlmRole.User, "你处在 timeback 中"));
            Assert.True(HasMessage(session, LlmRole.User, "已经历【11】条"));
        }
        /// <summary>
        /// 状态提示归作用域区间——回收时与查证过程一并删除（零残留）。
        /// </summary>
        [Fact]
        public void Notice_RemovedWithScopeOnBack()
        {
            MockLlm llm = new MockLlm();
            CH4.ChatSession session = CreateSession(llm);
            llm.ToolCallsQueue.Enqueue(BuildToolCalls("timeback", "t1", "{\"action\":\"start\",\"purpose\":\"计数取证\"}"));
            for (int i = 0; i < 5; i = i + 1)
            {
                llm.ToolCallsQueue.Enqueue(BuildToolCalls("random", "r" + i.ToString(), "{\"min\":1,\"max\":10}"));
            }
            llm.ToolCallsQueue.Enqueue(BuildToolCalls("timeback", "t2", "{\"action\":\"back\",\"findings\":\"结论：计数取证\"}"));
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
        /// 归档读面（T2）——尾部读取跳坏行；open 行不含 seconds 字段（口径：存活时长归 close 行）。
        /// </summary>
        [Fact]
        public void Archive_ReadTailSkipsBadLinesAndOpenHasNoSeconds()
        {
            string path = Path.Combine(Path.GetTempPath(), "cat4tb_tail_" + Guid.NewGuid().ToString("N") + ".jsonl");
            CH4.TimebackArchive archive = new CH4.TimebackArchive(path);
            long id = archive.NextId();
            Assert.True(archive.AppendOpen(id, "cat", 0, 3, 1000, "用途", 5));
            File.AppendAllText(path, "{\"t\":\"open\",\"id\":9\n");
            Assert.True(archive.AppendClose(id, 2000, 4, 7, "结论"));
            List<string> tail = archive.ReadTailJson(5);
            Assert.Equal(2, tail.Count);
            Assert.DoesNotContain("seconds", tail[0]);
            Assert.Contains("\"seconds\":7", tail[1]);
            Assert.False(archive.LastWriteFailed);
            File.Delete(path);
        }
        /// <summary>
        /// 归档写失败（T2 判据 8）——写面失败可见（返回 false + LastWriteFailed 立起；info archive.ok 数据源）。
        /// </summary>
        [Fact]
        public void Archive_WriteFailureIsVisible()
        {
            CH4.TimebackArchive archive = new CH4.TimebackArchive(Path.GetTempPath());
            Assert.False(archive.AppendClose(1, 2000, 4, 7, "结论"));
            Assert.True(archive.LastWriteFailed);
        }
    }
}

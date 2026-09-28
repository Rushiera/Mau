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
    /// timeback 上下文作用域测试（A101 / design-ch4-timeback §七）——
    /// 回卷语义与切点安全 / Note 豁免 / 归档编号递增 / 视图不缩水 / 参数面拒绝。
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
        /// 构造测试会话——临时前文文件 + 唯一临时归档路径 + Mock LLM（Note / timeback 工具面）。
        /// </summary>
        /// <param name="llm">Mock LLM</param>
        /// <returns>会话实体</returns>
        private static CH4.ChatSession CreateSession(MockLlm llm)
        {
            ToolSpec[] tools = new ToolSpec[]
            {
                new ToolSpec("Note", "Note 任务追踪", "{}"),
                new ToolSpec("timeback", "上下文作用域", "{}")
            };
            // 内置判定读注册面名单（timeback 已在名单内）；授权面回落会话声明面快照（provider 清空——防跨测试静态污染）
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
        /// 构造函数单条 tool_calls JSON（OpenAI wire 形态）。
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
        /// 前文是否含指定角色与子串的消息——结论注入 / Note 拉起断言用。
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
        /// 同批 start + back——回卷到锚点（上一轮正式回复）+ 结论注入 + 视图不缩水。
        /// </summary>
        [Fact]
        public void Back_TruncatesToAnchorAndInjectsConclusion()
        {
            MockLlm llm = new MockLlm();
            CH4.ChatSession session = CreateSession(llm);
            // 第一轮：纯文本正式回复——成为锚点
            session.PostUserMessage("先聊一句");
            PumpUntilIdle(session);
            int anchorCount = session.Context.GetMessageCount();
            Assert.Equal(2, anchorCount);
            // 第二轮：同批 start + back（取证型链）
            llm.ToolCallsQueue.Enqueue(BuildToolCalls("timeback", "t1", "{\"action\":\"start\",\"purpose\":\"测试取证\"}"));
            llm.ToolCallsQueue.Enqueue(BuildToolCalls("timeback", "t2", "{\"action\":\"back\",\"findings\":\"结论：文件在 X 路径\"}"));
            session.PostUserMessage("开始取证");
            PumpUntilIdle(session);
            Assert.True(session.IsIdle);
            // 回卷：锚点（含）保留 + 结论（user）+ 结论轮回复 = anchorCount + 3
            // （B 方案 2026-09-28：锚点取更晚的边界——本轮 user 请求，而非上一轮 assistant 回复）
            Assert.Equal(anchorCount + 3, session.Context.GetMessageCount());
            Assert.True(HasMessage(session, LlmRole.User, "结论：文件在 X 路径"));
            // 末条为结论轮的正式回复（无孤立 tool 消息——切点未劈开工具对）
            LlmMessage[] all = session.Context.GetMessages();
            Assert.Equal(LlmRole.Assistant, all[all.Length - 1].Role);
            // 视图层零重建——被卷掉的取证过程仍在人可见面（块数 > 当前前文条数）
            Assert.True(session.GetViewBlocks().Length > session.Context.GetMessageCount());
            // 活跃期已结束
            Assert.False(session.TimebackActive);
        }

        /// <summary>
        /// 首轮 user 边界（B 方案 2026-09-28）——新会话首轮无正式回复节点时，
        /// 锚点回落到最后一条 user（不再 ERR|TIMEBACK_ANCHOR）：保留该 user + 结论 + 结论轮回复 = 3 条。
        /// </summary>
        [Fact]
        public void Start_FirstRoundWithoutReply_UsesUserAnchor()
        {
            MockLlm llm = new MockLlm();
            CH4.ChatSession session = CreateSession(llm);
            // 首轮即取证：前文只有 user 请求（无 assistant 正式回复）
            llm.ToolCallsQueue.Enqueue(BuildToolCalls("timeback", "t1", "{\"action\":\"start\",\"purpose\":\"首轮取证\"}"));
            llm.ToolCallsQueue.Enqueue(BuildToolCalls("timeback", "t2", "{\"action\":\"back\",\"findings\":\"结论：首轮\"}"));
            session.PostUserMessage("直接取证");
            PumpUntilIdle(session);
            Assert.True(session.IsIdle);
            // 锚点 = user 请求（index 0）→ 保留 1 条 + 结论 + 结论轮回复 = 3 条
            Assert.Equal(3, session.Context.GetMessageCount());
            Assert.True(HasMessage(session, LlmRole.User, "结论：首轮"));
            Assert.False(session.TimebackActive);
        }

        /// <summary>
        /// 归档落盘——open + close 两行（本猫文件）。
        /// </summary>
        [Fact]
        public void Back_WritesArchiveOpenAndClose()
        {
            MockLlm llm = new MockLlm();
            CH4.ChatSession session = CreateSession(llm);
            // 用例专属归档路径（会话首次使用归档时构造实例——此前可覆盖 provider）
            string archivePath = Path.Combine(Path.GetTempPath(), "cat4tb_arch_" + Guid.NewGuid().ToString("N") + ".jsonl");
            CH4.ChatSession.TimebackArchivePathProvider = delegate (string catKey)
            {
                return archivePath;
            };
            session.PostUserMessage("先聊一句");
            PumpUntilIdle(session);
            llm.ToolCallsQueue.Enqueue(BuildToolCalls("timeback", "t1", "{\"action\":\"start\",\"purpose\":\"归档验证\"}"));
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
        /// Note 豁免（C3）——回卷不清 Note：同批 Note 计划 + start + back 后，
        /// 轮末 Note 拉起仍发生（计划存活的最强证据——被清则拉起不发生）。
        /// </summary>
        [Fact]
        public void Back_KeepsNotePlan()
        {
            MockLlm llm = new MockLlm();
            CH4.ChatSession session = CreateSession(llm);
            // 第一轮：纯文本正式回复——成为锚点
            session.PostUserMessage("先聊一句");
            PumpUntilIdle(session);
            // 第二轮：同批 Note 计划（2 条——未完成时会拉起）+ timeback start + back
            string batch = BuildToolCallsBatch(
                new string[] { "Note", "timeback", "timeback" },
                new string[] { "n1", "t1", "t2" },
                new string[] { "{\"action\":\"set\",\"content\":\"任务A\\n任务B\"}", "{\"action\":\"start\",\"purpose\":\"取证\"}", "{\"action\":\"back\",\"findings\":\"结论：B\"}" });
            llm.ToolCallsQueue.Enqueue(batch);
            session.PostUserMessage("建计划并取证");
            // 定长泵（Note 拉起会续轮——不依赖 Idle 收敛）
            for (int i = 0; i < 200; i = i + 1)
            {
                session.Pump();
                Thread.Sleep(5);
            }
            // C3——Note 计划存活：轮末拉起发生（人工回滚路径才清 Note）
            Assert.True(HasMessage(session, LlmRole.User, "[Note 未完成]"));
        }

        /// <summary>
        /// 参数面——未闭合时再次 start 被拒（ERR|TIMEBACK_NESTED）。
        /// </summary>
        [Fact]
        public void Start_Twice_RejectedAsNested()
        {
            MockLlm llm = new MockLlm();
            CH4.ChatSession session = CreateSession(llm);
            session.PostUserMessage("先聊一句");
            PumpUntilIdle(session);
            llm.ToolCallsQueue.Enqueue(BuildToolCalls("timeback", "t1", "{\"action\":\"start\",\"purpose\":\"甲\"}"));
            llm.ToolCallsQueue.Enqueue(BuildToolCalls("timeback", "t2", "{\"action\":\"start\",\"purpose\":\"乙\"}"));
            session.PostUserMessage("连开两次");
            PumpUntilIdle(session);
            Assert.Contains("TIMEBACK_NESTED", ToolResultText(session, 1));
            Assert.True(session.TimebackActive);
        }

        /// <summary>
        /// 参数面——无作用域 back 被拒（ERR|TIMEBACK_NO_SCOPE）与缺 findings 被拒（ERR|TIMEBACK_ARGS）。
        /// </summary>
        [Fact]
        public void Back_RejectedWithoutScopeOrFindings()
        {
            MockLlm llm = new MockLlm();
            CH4.ChatSession session = CreateSession(llm);
            session.PostUserMessage("先聊一句");
            PumpUntilIdle(session);
            llm.ToolCallsQueue.Enqueue(BuildToolCalls("timeback", "t1", "{\"action\":\"back\",\"findings\":\"结论：C\"}"));
            session.PostUserMessage("空 back");
            PumpUntilIdle(session);
            Assert.Contains("TIMEBACK_NO_SCOPE", ToolResultText(session, 0));
            // 缺 findings——先正常 start，再发一次不带 findings 的 back（同批：结果序号 1=start / 2=back）
            string batch = BuildToolCallsBatch(
                new string[] { "timeback", "timeback" },
                new string[] { "t2", "t3" },
                new string[] { "{\"action\":\"start\",\"purpose\":\"取证\"}", "{\"action\":\"back\"}" });
            llm.ToolCallsQueue.Enqueue(batch);
            session.PostUserMessage("缺载荷");
            PumpUntilIdle(session);
            Assert.Contains("TIMEBACK_ARGS", ToolResultText(session, 2));
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
            Assert.True(first.AppendOpen(1, "cat", 0, 3, 1000, "用途", 5, 0));
            Assert.True(first.AppendClose(1, 2000, 4, 1, "结论"));
            // 新实例（同路径）——编号从已落盘最大值续接
            CH4.TimebackArchive second = new CH4.TimebackArchive(path);
            Assert.Equal(2L, second.NextId());
            Assert.True(second.AppendOpen(2, "cat", 1, 3, 1000, "用途2", 5, 0));
            // 末行残缺——不影响取号（以最大可解析 id 为准）
            File.AppendAllText(path, "{\"t\":\"open\",\"id\":9");
            CH4.TimebackArchive third = new CH4.TimebackArchive(path);
            Assert.Equal(3L, third.NextId());
            File.Delete(path);
        }
    }
}

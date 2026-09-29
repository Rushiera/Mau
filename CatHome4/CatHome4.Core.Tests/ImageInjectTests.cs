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
    /// 图片注入测试（design-ch4-chat-images §八 · A105）——
    /// 域外门禁（ERR|TIMEBACK_REQUIRED）/ 批后段注入合并为一条 user 附件消息 / 失败与非目标工具零注入。
    /// 说明：注入动作经内部面直调（dogs 参数）；OA 闭环（VisionCat 认领）与真图展开由部署实测覆盖。
    /// </summary>
    [Collection("GlobalToolState")]
    public sealed class ImageInjectTests
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

            /// <summary>
            /// 流式对话——队列有内容则产 tool_calls，否则产纯文本。
            /// </summary>
            /// <param name="messages">消息序列</param>
            /// <param name="tools">工具定义</param>
            /// <param name="userId">用户标识</param>
            /// <param name="ct">取消令牌</param>
            /// <returns>事件流</returns>
            public async IAsyncEnumerable<LlmStreamEvent> ChatStream(LlmMessage[] messages, ToolSpec[] tools, string userId = "", [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
            {
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
        /// 构造测试会话——临时前文文件 + 唯一临时归档路径 + Mock LLM（image-inject 工具面）。
        /// </summary>
        /// <param name="llm">Mock LLM</param>
        /// <returns>会话实体</returns>
        private static CH4.ChatSession CreateSession(MockLlm llm)
        {
            string[] toolNames = new string[] { "timeback", "image-inject", "random" };
            ToolSpec[] tools = new ToolSpec[toolNames.Length];
            for (int i = 0; i < toolNames.Length; i = i + 1)
            {
                tools[i] = new ToolSpec(toolNames[i], toolNames[i], "{}");
            }
            CH4.ToolRegistry.Init(tools, null, null);
            CH4.ChatSession.AuthorizedToolNamesProvider = null;
            string archivePath = Path.Combine(Path.GetTempPath(), "cat4ii_" + Guid.NewGuid().ToString("N") + ".jsonl");
            CH4.ChatSession.TimebackArchivePathProvider = delegate (string catKey)
            {
                return archivePath;
            };
            ChatContext ctx = new ChatContext();
            string tmp = Path.Combine(Path.GetTempPath(), "cat4ii_" + Guid.NewGuid().ToString("N") + ".jsonl");
            SessionStore store = new SessionStore(tmp);
            OA oa = new OA(new ThreadGuard());
            CH4.SessionViewStore viewStore = new CH4.SessionViewStore(Path.Combine(Path.GetTempPath(), "cat4ii_" + Guid.NewGuid().ToString("N") + ".view.json"));
            CH4.ChatSession session = new CH4.ChatSession("ii-session", "ii", ctx, store, llm, oa, tools, delegate (string name, string args) { return "ERR|NO_TOOL|" + name; }, viewStore);
            session.SetCatKey("ii-session");
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
        /// 统计前文带附件引用的 user 消息条数。
        /// </summary>
        /// <param name="session">会话</param>
        /// <returns>条数</returns>
        private static int CountInjectedMessages(CH4.ChatSession session)
        {
            int count = 0;
            LlmMessage[] all = session.Context.GetMessages();
            for (int i = 0; i < all.Length; i = i + 1)
            {
                if (all[i].Role == LlmRole.User && all[i].ImagesJson != null && all[i].ImagesJson.Length > 0)
                {
                    count = count + 1;
                }
            }
            return count;
        }

        /// <summary>
        /// 域外门禁——无 timeback 作用域时调用 image-inject → ERR|TIMEBACK_REQUIRED，且不产生注入消息。
        /// </summary>
        [Fact]
        public void ImageInject_OutsideScope_Rejected()
        {
            MockLlm llm = new MockLlm();
            CH4.ChatSession session = CreateSession(llm);
            llm.ToolCallsQueue.Enqueue(BuildToolCalls("image-inject", "i1", "{\"path\":\"C:/tmp/a.png\"}"));
            session.PostUserMessage("域外调用");
            PumpUntilIdle(session);
            Assert.True(session.IsIdle, "phase=" + session.Phase.ToString());
            Assert.Contains("TIMEBACK_REQUIRED", ToolResultText(session, 0));
            Assert.Equal(0, CountInjectedMessages(session));
        }

        /// <summary>
        /// 注入合并——两个成功工单 → 一条 user 附件消息（引用数组含两路径）。
        /// </summary>
        [Fact]
        public void FlushImageInjections_MergesIntoSingleUserMessage()
        {
            MockLlm llm = new MockLlm();
            CH4.ChatSession session = CreateSession(llm);
            List<CH4.ToolOrderDog> dogs = new List<CH4.ToolOrderDog>();
            CH4.ToolOrderDog d1 = new CH4.ToolOrderDog("i1", "image-inject", "{\"path\":\"C:/tmp/a.png\"}");
            d1.Result = "{\"path\":\"C:/tmp/a.png\"}";
            CH4.ToolOrderDog d2 = new CH4.ToolOrderDog("i2", "image-inject", "{\"path\":\"C:/tmp/b.png\"}");
            d2.Result = "{\"path\":\"C:/tmp/b.png\"}";
            dogs.Add(d1);
            dogs.Add(d2);
            session.FlushImageInjections(dogs);
            Assert.Equal(1, CountInjectedMessages(session));
            LlmMessage[] all = session.Context.GetMessages();
            string imagesJson = "";
            for (int i = 0; i < all.Length; i = i + 1)
            {
                if (all[i].Role == LlmRole.User && all[i].ImagesJson != null && all[i].ImagesJson.Length > 0)
                {
                    imagesJson = all[i].ImagesJson;
                }
            }
            Assert.Contains("C:/tmp/a.png", imagesJson, StringComparison.Ordinal);
            Assert.Contains("C:/tmp/b.png", imagesJson, StringComparison.Ordinal);
        }

        /// <summary>
        /// 失败工单零注入——ERR 回执的工具不产生附件消息。
        /// </summary>
        [Fact]
        public void FlushImageInjections_SkipsFailedDogs()
        {
            MockLlm llm = new MockLlm();
            CH4.ChatSession session = CreateSession(llm);
            List<CH4.ToolOrderDog> dogs = new List<CH4.ToolOrderDog>();
            CH4.ToolOrderDog d1 = new CH4.ToolOrderDog("i1", "image-inject", "{\"path\":\"C:/tmp/a.png\"}");
            d1.Result = "ERR|OA_TIMEOUT|工单超时无人认领: image-inject";
            dogs.Add(d1);
            session.FlushImageInjections(dogs);
            Assert.Equal(0, CountInjectedMessages(session));
        }

        /// <summary>
        /// 非目标工具零注入——批内没有 image-inject 时零动作（零回归）。
        /// </summary>
        [Fact]
        public void FlushImageInjections_NoTargetTool_NoMessage()
        {
            MockLlm llm = new MockLlm();
            CH4.ChatSession session = CreateSession(llm);
            List<CH4.ToolOrderDog> dogs = new List<CH4.ToolOrderDog>();
            CH4.ToolOrderDog d1 = new CH4.ToolOrderDog("r1", "random", "{\"min\":1,\"max\":10}");
            d1.Result = "7";
            dogs.Add(d1);
            session.FlushImageInjections(dogs);
            Assert.Equal(0, CountInjectedMessages(session));
        }
        /// <summary>
        /// A108——image-inject 的根寻址形态在注入时规范化为绝对路径（复用 FileSystemService.Resolve）：
        /// 引用数组落绝对路径，请求构造面的展开不再依赖根表。
        /// </summary>
        [Fact]
        public void RootAddress_NormalizedToAbsolutePathOnInject()
        {
            MockLlm llm = new MockLlm();
            CH4.ChatSession session = CreateSession(llm);
            string rootDir = Path.Combine(Path.GetTempPath(), "cat4ii_root_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(rootDir);
            CH4.ToolCatContext.UpdateCatFileSystem("ii-session", new WorkspaceConfig.RootEntry[]
            {
                        new WorkspaceConfig.RootEntry { Id = "imgs", Path = rootDir, Writable = true, Note = "测试根" }
            }, Path.Combine(rootDir, "_recycle"));
            try
            {
                List<CH4.ToolOrderDog> dogs = new List<CH4.ToolOrderDog>();
                CH4.ToolOrderDog d1 = new CH4.ToolOrderDog("i1", "image-inject", "{\"path\":\"imgs:a.png\"}");
                d1.Result = "{\"path\":\"imgs:a.png\"}";
                dogs.Add(d1);
                session.FlushImageInjections(dogs);
                Assert.Equal(1, CountInjectedMessages(session));
                string imagesJson = LastImagesJson(session);
                // 引用已解析为绝对路径（根寻址形态不再出现在引用数组里）
                Assert.Contains(rootDir.Replace("\\", "\\\\"), imagesJson, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("imgs:", imagesJson, StringComparison.Ordinal);
            }
            finally
            {
                CH4.ToolCatContext.RemoveCatFileSystem("ii-session");
                try
                {
                    Directory.Delete(rootDir, true);
                }
                catch (Exception)
                {
                    // 清理失败不影响断言
                }
            }
        }
        /// <summary>
        /// A108 回归——非根寻址形态不被根解析吞掉：外部 URL 原样透传；盘符绝对路径照常入引用数组
        /// （盘符路径若落在某受控根内会被 Resolve 规范化，故只断言文件名存活，不锁定路径形态）。
        /// </summary>
        [Fact]
        public void NonRootAddress_PassesThroughUnchanged()
        {
            MockLlm llm = new MockLlm();
            CH4.ChatSession session = CreateSession(llm);
            List<CH4.ToolOrderDog> dogs = new List<CH4.ToolOrderDog>();
            CH4.ToolOrderDog d1 = new CH4.ToolOrderDog("i1", "image-inject", "{\"path\":\"https://example.com/a.png\"}");
            d1.Result = "{\"path\":\"https://example.com/a.png\"}";
            CH4.ToolOrderDog d2 = new CH4.ToolOrderDog("i2", "image-inject", "{\"path\":\"C:/tmp/b.png\"}");
            d2.Result = "{\"path\":\"C:/tmp/b.png\"}";
            dogs.Add(d1);
            dogs.Add(d2);
            session.FlushImageInjections(dogs);
            string imagesJson = LastImagesJson(session);
            Assert.Contains("https://example.com/a.png", imagesJson, StringComparison.Ordinal);
            Assert.Contains("b.png", imagesJson, StringComparison.Ordinal);
        }
        /// <summary>
        /// 取最后一条带附件引用的 user 消息的引用数组——注入断言用。
        /// </summary>
        /// <param name="session">会话</param>
        /// <returns>ImagesJson（无=空串）</returns>
        private static string LastImagesJson(CH4.ChatSession session)
        {
            string imagesJson = "";
            LlmMessage[] all = session.Context.GetMessages();
            for (int i = 0; i < all.Length; i = i + 1)
            {
                if (all[i].Role == LlmRole.User && all[i].ImagesJson != null && all[i].ImagesJson.Length > 0)
                {
                    imagesJson = all[i].ImagesJson;
                }
            }
            return imagesJson;
        }
    }
}

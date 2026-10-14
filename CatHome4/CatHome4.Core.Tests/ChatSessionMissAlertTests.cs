using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Mau.Runtime;
using CatHome4.Contracts;
using Xunit;

namespace CatHome4.Core.Tests
{
    /// <summary>
    /// 请求 miss 超阈预警测试——单次请求 miss 超阈值投 error 气泡 / 不超不投 / 同请求多帧去重。
    /// </summary>
    [Collection("GlobalToolState")]
    public class ChatSessionMissAlertTests
    {
        /// <summary>
        /// 脚本化假 LLM 运行时——每次流式调用先产配置的 usage 帧，再产文本与结束帧。
        /// </summary>
        private sealed class ScriptedLlm : ILlmRuntime
        {
            /// <summary>本流要产出的 usage 事件文本序列（按序产出）。</summary>
            public string[] UsageFrames = new string[0];

            /// <summary>
            /// 流式对话——usage 帧 + 文本 + 结束帧。
            /// </summary>
            /// <param name="messages">消息序列</param>
            /// <param name="tools">工具定义</param>
            /// <param name="userId">用户标识</param>
            /// <param name="ct">取消令牌</param>
            /// <returns>事件流</returns>
            public async IAsyncEnumerable<LlmStreamEvent> ChatStream(LlmMessage[] messages, ToolSpec[] tools, string userId = "", [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
            {
                for (int i = 0; i < UsageFrames.Length; i = i + 1)
                {
                    yield return new LlmStreamEvent(LlmStreamKind.Usage, UsageFrames[i]);
                }
                yield return new LlmStreamEvent(LlmStreamKind.Text, "回复");
                yield return new LlmStreamEvent(LlmStreamKind.Done, "");
                await Task.Yield();
            }
        }

        /// <summary>
        /// 构造测试会话——临时前文文件 + 视图存储。
        /// </summary>
        /// <param name="llm">LLM 运行时</param>
        /// <returns>会话实体</returns>
        private static CH4.ChatSession CreateSession(ILlmRuntime llm)
        {
            CH4.ToolRegistry.Init(new ToolSpec[0], null, new Dictionary<string, bool>());
            ChatContext ctx = new ChatContext();
            string tmp = Path.Combine(Path.GetTempPath(), "cat4miss_" + Guid.NewGuid().ToString("N") + ".jsonl");
            SessionStore store = new SessionStore(tmp);
            OA oa = new OA(new ThreadGuard());
            CH4.SessionViewStore viewStore = new CH4.SessionViewStore(Path.Combine(Path.GetTempPath(), "cat4miss_" + Guid.NewGuid().ToString("N") + ".view.jsonl"));
            return new CH4.ChatSession("test-miss", "miss", ctx, store, llm, oa, new ToolSpec[0], delegate (string name, string args) { return "ERR|NO_TOOL|" + name; }, viewStore);
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
        /// 提取视图块中的 error 条目（按创建序）。
        /// </summary>
        /// <param name="session">会话</param>
        /// <returns>error 块数组</returns>
        private static CH4.ViewBlock[] ErrorBlocks(CH4.ChatSession session)
        {
            List<CH4.ViewBlock> result = new List<CH4.ViewBlock>();
            CH4.ViewBlock[] blocks = session.GetViewBlocks();
            for (int i = 0; i < blocks.Length; i = i + 1)
            {
                if (blocks[i].RenderType == "error")
                {
                    result.Add(blocks[i]);
                }
            }
            return result.ToArray();
        }

        /// <summary>
        /// 单次请求 miss 超阈值——投一个 error 气泡，载荷含 miss 值。
        /// </summary>
        [Fact]
        public void MissAlert_OverThreshold_PushesErrorBlock()
        {
            ScriptedLlm llm = new ScriptedLlm();
            llm.UsageFrames = new string[] { "{\"prompt\":200000,\"completion\":5,\"cacheHit\":50000}" };
            CH4.ChatSession session = CreateSession(llm);
            session.PostUserMessage("测试一下");
            PumpUntilIdle(session);
            CH4.ViewBlock[] errors = ErrorBlocks(session);
            Assert.Single(errors);
            Assert.Contains("150000", errors[0].Payload);
        }

        /// <summary>
        /// 单次请求 miss 未超阈值——不投 error 气泡。
        /// </summary>
        [Fact]
        public void MissAlert_UnderThreshold_NoErrorBlock()
        {
            ScriptedLlm llm = new ScriptedLlm();
            llm.UsageFrames = new string[] { "{\"prompt\":200000,\"completion\":5,\"cacheHit\":190000}" };
            CH4.ChatSession session = CreateSession(llm);
            session.PostUserMessage("测试一下");
            PumpUntilIdle(session);
            Assert.Empty(ErrorBlocks(session));
        }

        /// <summary>
        /// 同一请求多 usage 帧——只投一个 error 气泡（请求级去重）。
        /// </summary>
        [Fact]
        public void MissAlert_MultipleFramesSameRequest_PushesOnce()
        {
            ScriptedLlm llm = new ScriptedLlm();
            llm.UsageFrames = new string[]
            {
                "{\"prompt\":200000,\"completion\":5,\"cacheHit\":50000}",
                "{\"prompt\":200000,\"completion\":5,\"cacheHit\":50000}"
            };
            CH4.ChatSession session = CreateSession(llm);
            session.PostUserMessage("测试一下");
            PumpUntilIdle(session);
            Assert.Single(ErrorBlocks(session));
        }
    }
}

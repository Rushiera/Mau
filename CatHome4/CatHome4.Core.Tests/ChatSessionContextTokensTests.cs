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
    /// 已知最新前文长度（ContextTokensKnown）测试——请求级实时优先 / 未发起请求回落落盘快照（A202：最后一次请求边界态）。
    /// </summary>
    [Collection("GlobalToolState")]
    public class ChatSessionContextTokensTests
    {
        /// <summary>空 LLM 运行时——本组测试不发起请求。</summary>
        private sealed class NullLlm : ILlmRuntime
        {
            /// <summary>
            /// 流式对话——产单条结束事件（本组测试不触发）。
            /// </summary>
            /// <param name="messages">消息序列</param>
            /// <param name="tools">工具定义</param>
            /// <param name="userId">用户标识</param>
            /// <param name="ct">取消令牌</param>
            /// <returns>事件流</returns>
            public async IAsyncEnumerable<LlmStreamEvent> ChatStream(LlmMessage[] messages, ToolSpec[] tools, string userId = "", [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
            {
                yield return new LlmStreamEvent(LlmStreamKind.Done, "");
                await Task.Yield();
            }
        }

        /// <summary>
        /// 构造最小会话——临时前文文件 + 空工具面。
        /// </summary>
        /// <returns>会话实体</returns>
        private static CH4.ChatSession CreateSession()
        {
            CH4.ToolRegistry.Init(new ToolSpec[0], null, new Dictionary<string, bool>());
            ChatContext ctx = new ChatContext();
            string tmp = Path.Combine(Path.GetTempPath(), "cat4ctx_" + Guid.NewGuid().ToString("N") + ".jsonl");
            SessionStore store = new SessionStore(tmp);
            OA oa = new OA(new ThreadGuard());
            CH4.SessionViewStore viewStore = new CH4.SessionViewStore(Path.Combine(Path.GetTempPath(), "cat4ctx_" + Guid.NewGuid().ToString("N") + ".view.json"));
            return new CH4.ChatSession(DateTime.Now.Ticks.ToString(), "ctx-test", ctx, store, new NullLlm(), oa, new ToolSpec[0], delegate (string name, string args) { return "ERR|NO_TOOL|" + name; }, viewStore);
        }

        /// <summary>
        /// 无落盘快照（新会话 / 未落盘）——回落值为 0（不估算、不造值）。
        /// </summary>
        [Fact]
        public void ContextTokensKnown_NoPersistedMeta_ReturnsZero()
        {
            CH4.ChatSession session = CreateSession();
            Assert.Equal(0L, session.ContextTokensKnown);
        }

        /// <summary>
        /// 恢复已落盘快照（宿主重启后未发起请求）——回落值为 session.json 的 ContextTokens
        /// （A202：最后一次请求边界态；「看别猫」场景有值）。
        /// </summary>
        [Fact]
        public void ContextTokensKnown_WithPersistedMeta_ReturnsSnapshot()
        {
            string catId = "ctx-persist-" + Guid.NewGuid().ToString("N");
            string metaPath = Path.Combine(Path.GetTempPath(), "cat4ctx_" + Guid.NewGuid().ToString("N") + ".session.json");
            CH4.SessionMeta meta = new CH4.SessionMeta();
            meta.SessionId = "instance-ctx";
            meta.CatId = catId;
            meta.ContextTokens = 224497;
            new CH4.SessionMetaStore(metaPath).Save(meta);
            Func<string, string> prev = CH4.ChatSession.SessionMetaPathProvider;
            try
            {
                CH4.ChatSession.SessionMetaPathProvider = delegate (string key) { return metaPath; };
                CH4.ChatSession session = CreateSession();
                session.LoadMeta();
                Assert.Equal(224497L, session.ContextTokensKnown);
            }
            finally
            {
                CH4.ChatSession.SessionMetaPathProvider = prev;
            }
        }
    }
}

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
    /// 会话元数据持久化测试（A201 · design-ch4-protocol §十三）——独立落盘面
    /// sessions/&lt;id&gt;/&lt;id&gt;.session.json：两级 token 累计 + 派生命中率 + 时间戳与实例 ID 跨重启恢复。
    /// </summary>
    [Collection("GlobalToolState")]
    public class ChatSessionMetaTests
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

        /// <summary>临时文件路径——唯一化（测试隔离）。</summary>
        /// <param name="tag">标签</param>
        /// <param name="ext">扩展名</param>
        /// <returns>临时绝对路径</returns>
        private static string TempPath(string tag, string ext)
        {
            return Path.Combine(Path.GetTempPath(), "cat4meta_" + tag + "_" + Guid.NewGuid().ToString("N") + ext);
        }

        /// <summary>
        /// 构造最小会话——临时前文 / 视图 / 元数据文件 + 空工具面。
        /// </summary>
        /// <param name="catId">猫 key</param>
        /// <param name="displayName">显示名</param>
        /// <returns>会话实体</returns>
        private static CH4.ChatSession CreateSession(string catId, string displayName)
        {
            CH4.ToolRegistry.Init(new ToolSpec[0], null, new Dictionary<string, bool>());
            ChatContext ctx = new ChatContext();
            SessionStore store = new SessionStore(TempPath(catId, ".jsonl"));
            OA oa = new OA(new ThreadGuard());
            CH4.SessionViewStore viewStore = new CH4.SessionViewStore(TempPath(catId, ".view.json"));
            return new CH4.ChatSession(catId, displayName, ctx, store, new NullLlm(), oa, new ToolSpec[0], delegate (string name, string args) { return "ERR|NO_TOOL|" + name; }, viewStore);
        }

        /// <summary>派生值——未命中 = prompt − cacheHit；命中率 = cacheHit ÷ prompt。</summary>
        [Fact]
        public void SessionTokens_DerivedValues()
        {
            CH4.SessionTokens t = new CH4.SessionTokens();
            t.Prompt = 1000;
            t.CacheHit = 750;
            t.Completion = 100;
            Assert.Equal(250L, t.Miss);
            Assert.Equal(0.75, t.Rate, 4);
        }

        /// <summary>派生值边界——分母 0 命中率为 0；cacheHit 超 prompt 时未命中不下穿 0。</summary>
        [Fact]
        public void SessionTokens_DerivedValues_EdgeCases()
        {
            CH4.SessionTokens zero = new CH4.SessionTokens();
            Assert.Equal(0L, zero.Miss);
            Assert.Equal(0.0, zero.Rate, 4);
            CH4.SessionTokens over = new CH4.SessionTokens();
            over.Prompt = 100;
            over.CacheHit = 130;
            Assert.Equal(0L, over.Miss);
        }

        /// <summary>落盘往返——元数据写入后可原样读回（含两级 token 与时间戳）。</summary>
        [Fact]
        public void MetaStore_SaveLoad_RoundTrip()
        {
            string path = TempPath("store", ".session.json");
            CH4.SessionMetaStore store = new CH4.SessionMetaStore(path);
            CH4.SessionMeta meta = new CH4.SessionMeta();
            meta.SessionId = "639258885922650133";
            meta.CatId = "meta-test";
            meta.DisplayName = "测试猫";
            meta.CreatedAt = 1790951243483;
            meta.LastActiveAt = 1790951300000;
            meta.ContextCount = 12;
            meta.ContextChars = 3456;
            meta.ContextTokens = 789;
            CH4.SessionTokens st = new CH4.SessionTokens();
            st.Prompt = 5000;
            st.CacheHit = 4000;
            st.Completion = 300;
            meta.SessionTokens = st;
            CH4.SessionTokens rt = new CH4.SessionTokens();
            rt.Prompt = 700;
            rt.CacheHit = 600;
            rt.Completion = 50;
            meta.RoundTokens = rt;
            store.Save(meta);
            Assert.True(File.Exists(path));
            // 派生值不落盘（design-ch4-protocol §13.2——文件只承载真实值）
            string raw = File.ReadAllText(path);
            Assert.DoesNotContain("\"Miss\"", raw);
            Assert.DoesNotContain("\"Rate\"", raw);
            CH4.SessionMeta loaded = store.Load();
            Assert.NotNull(loaded);
            Assert.Equal("639258885922650133", loaded.SessionId);
            Assert.Equal("测试猫", loaded.DisplayName);
            Assert.Equal(1790951243483L, loaded.CreatedAt);
            Assert.Equal(12L, loaded.ContextCount);
            Assert.Equal(3456L, loaded.ContextChars);
            Assert.Equal(5000L, loaded.SessionTokens.Prompt);
            Assert.Equal(4000L, loaded.SessionTokens.CacheHit);
            Assert.Equal(300L, loaded.SessionTokens.Completion);
            Assert.Equal(1000L, loaded.SessionTokens.Miss);
            Assert.Equal(700L, loaded.RoundTokens.Prompt);
        }

        /// <summary>落盘缺失——Load 返回 null（调用方走首建，不抛异常）。</summary>
        [Fact]
        public void MetaStore_Load_MissingFile_ReturnsNull()
        {
            CH4.SessionMetaStore store = new CH4.SessionMetaStore(TempPath("missing", ".session.json"));
            Assert.Null(store.Load());
        }

        /// <summary>
        /// 启动恢复——LoadMeta 读回落盘的两级累计，并经 state 段 tokens 面暴露（跨重启不丢）。
        /// </summary>
        [Fact]
        public void LoadMeta_RestoresSessionTokens_IntoStateJson()
        {
            string catId = "meta-restore-" + Guid.NewGuid().ToString("N");
            string path = TempPath(catId, ".session.json");
            CH4.SessionMeta meta = new CH4.SessionMeta();
            meta.SessionId = "instance-1";
            meta.CatId = catId;
            meta.DisplayName = "恢复猫";
            meta.CreatedAt = 1790951243483;
            meta.ContextChars = 999;
            CH4.SessionTokens st = new CH4.SessionTokens();
            st.Prompt = 1000;
            st.CacheHit = 800;
            st.Completion = 120;
            meta.SessionTokens = st;
            new CH4.SessionMetaStore(path).Save(meta);
            Func<string, string> prev = CH4.ChatSession.SessionMetaPathProvider;
            try
            {
                CH4.ChatSession.SessionMetaPathProvider = delegate (string key) { return path; };
                CH4.ChatSession session = CreateSession(catId, "恢复猫");
                session.LoadMeta();
                Assert.Equal("instance-1", session.SessionInstanceId);
                Assert.Equal(1790951243483L, session.SessionCreatedAt);
                string json = session.BuildStateJson();
                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    JsonElement tokens = doc.RootElement.GetProperty("tokens");
                    Assert.Equal(1000L, tokens.GetProperty("sessionPrompt").GetInt64());
                    Assert.Equal(800L, tokens.GetProperty("sessionCacheHit").GetInt64());
                    Assert.Equal(120L, tokens.GetProperty("sessionCompletion").GetInt64());
                    Assert.Equal(200L, tokens.GetProperty("sessionMiss").GetInt64());
                    Assert.Equal(0.8, tokens.GetProperty("sessionRate").GetDouble(), 4);
                    JsonElement metaElement = doc.RootElement.GetProperty("meta");
                    Assert.Equal("恢复猫", metaElement.GetProperty("displayName").GetString());
                    Assert.Equal(999L, metaElement.GetProperty("contextChars").GetInt64());
                }
            }
            finally
            {
                CH4.ChatSession.SessionMetaPathProvider = prev;
            }
        }

        /// <summary>首次启动（文件缺失）——LoadMeta 走首建：生成实例 ID 与创建时刻。</summary>
        [Fact]
        public void LoadMeta_MissingFile_BootstrapsIdentity()
        {
            string catId = "meta-fresh-" + Guid.NewGuid().ToString("N");
            string path = TempPath(catId, ".session.json");
            Func<string, string> prev = CH4.ChatSession.SessionMetaPathProvider;
            try
            {
                CH4.ChatSession.SessionMetaPathProvider = delegate (string key) { return path; };
                CH4.ChatSession session = CreateSession(catId, "新猫");
                session.LoadMeta();
                Assert.True(session.SessionInstanceId.Length > 0);
                Assert.True(session.SessionCreatedAt > 0);
            }
            finally
            {
                CH4.ChatSession.SessionMetaPathProvider = prev;
            }
        }

        /// <summary>
        /// session.new——ResetStats 换发新实例 ID + 会话级累计归零 + 落盘元数据（新会话初始态）。
        /// </summary>
        [Fact]
        public void ResetStats_NewInstanceId_And_PersistsMeta()
        {
            string catId = "meta-reset-" + Guid.NewGuid().ToString("N");
            string path = TempPath(catId, ".session.json");
            Func<string, string> prev = CH4.ChatSession.SessionMetaPathProvider;
            try
            {
                CH4.ChatSession.SessionMetaPathProvider = delegate (string key) { return path; };
                CH4.ChatSession session = CreateSession(catId, "重置猫");
                session.LoadMeta();
                string first = session.SessionInstanceId;
                session.ResetStats();
                Assert.NotEqual(first, session.SessionInstanceId);
                Assert.True(session.SessionInstanceId.Length > 0);
                CH4.SessionMeta saved = new CH4.SessionMetaStore(path).Load();
                Assert.NotNull(saved);
                Assert.Equal(session.SessionInstanceId, saved.SessionId);
                Assert.Equal(catId, saved.CatId);
                Assert.Equal("重置猫", saved.DisplayName);
                Assert.Equal(0L, saved.SessionTokens.Prompt);
            }
            finally
            {
                CH4.ChatSession.SessionMetaPathProvider = prev;
            }
        }

        /// <summary>显示名刷新——SetDisplayName 空值不覆盖（session.new 无配置时保持原值）。</summary>
        [Fact]
        public void SetDisplayName_EmptyKeepsOriginal()
        {
            CH4.ChatSession session = CreateSession("meta-name-" + Guid.NewGuid().ToString("N"), "原名");
            session.SetDisplayName("");
            Assert.Equal("原名", session.DisplayName);
            session.SetDisplayName("新名");
            Assert.Equal("新名", session.DisplayName);
        }
    }
}

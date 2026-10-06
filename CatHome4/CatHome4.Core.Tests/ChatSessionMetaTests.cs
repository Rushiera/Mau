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
            // A202——请求边界态四件 + Note
            CH4.SessionPhases ph = new CH4.SessionPhases();
            ph.Link = 5800;
            ph.Think = 5100;
            ph.Tool = 1500;
            ph.Run = 1800;
            ph.Reply = 1800;
            meta.RoundPhases = ph;
            meta.RoundRequests = 7;
            meta.RoundTools = 15;
            meta.RoundElapsedMs = 15900;
            CH4.SessionNote note = new CH4.SessionNote();
            note.Tasks = new string[] { "甲", "乙" };
            note.Current = 1;
            note.Done = 0;
            meta.Note = note;
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
            // A202——请求边界态 + Note 往返
            Assert.Equal(5800L, loaded.RoundPhases.Link);
            Assert.Equal(5100L, loaded.RoundPhases.Think);
            Assert.Equal(1500L, loaded.RoundPhases.Tool);
            Assert.Equal(1800L, loaded.RoundPhases.Run);
            Assert.Equal(1800L, loaded.RoundPhases.Reply);
            Assert.Equal(0L, loaded.RoundPhases.Wait);
            Assert.Equal(7L, loaded.RoundRequests);
            Assert.Equal(15L, loaded.RoundTools);
            Assert.Equal(15900L, loaded.RoundElapsedMs);
            Assert.NotNull(loaded.Note.Tasks);
            Assert.Equal(2, loaded.Note.Tasks.Length);
            Assert.Equal("乙", loaded.Note.Tasks[1]);
            Assert.Equal(1L, loaded.Note.Current);
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
                    // 易用性修复（A201 前端轮）：contextChars 改为推送时实时刷新——state 段给前文实况
                    // （LoadMeta 恢复的文件值由落盘面承载；本用例前文为空，故实况为 0）
                    Assert.Equal(0L, metaElement.GetProperty("contextChars").GetInt64());
                }
            }
            finally
            {
                CH4.ChatSession.SessionMetaPathProvider = prev;
            }
        }

        /// <summary>
        /// 启动恢复——A202 请求边界态回落：六态累计 / 请求次数 / Note 经 state 段暴露；
        /// 相位不恢复（恢复后一律 idle——瞬时态不落盘）。
        /// </summary>
        [Fact]
        public void LoadMeta_RestoresRoundSnapshot_IntoStateJson()
        {
            string catId = "meta-round-" + Guid.NewGuid().ToString("N");
            string path = TempPath(catId, ".session.json");
            CH4.SessionMeta meta = new CH4.SessionMeta();
            meta.SessionId = "instance-round";
            meta.CatId = catId;
            meta.ContextTokens = 12345;
            CH4.SessionPhases ph = new CH4.SessionPhases();
            ph.Link = 5800;
            ph.Reply = 1800;
            meta.RoundPhases = ph;
            meta.RoundRequests = 7;
            CH4.SessionNote note = new CH4.SessionNote();
            note.Tasks = new string[] { "甲", "乙", "丙" };
            note.Current = 1;
            note.Done = 1;
            meta.Note = note;
            new CH4.SessionMetaStore(path).Save(meta);
            Func<string, string> prev = CH4.ChatSession.SessionMetaPathProvider;
            try
            {
                CH4.ChatSession.SessionMetaPathProvider = delegate (string key) { return path; };
                CH4.ChatSession session = CreateSession(catId, "边界猫");
                session.LoadMeta();
                Assert.Equal(12345L, session.ContextTokensKnown);
                string json = session.BuildStateJson();
                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    JsonElement runMs = doc.RootElement.GetProperty("runMs");
                    Assert.Equal(5800L, runMs.GetProperty("link").GetInt64());
                    Assert.Equal(1800L, runMs.GetProperty("reply").GetInt64());
                    Assert.Equal(0L, runMs.GetProperty("think").GetInt64());
                    // 相位不恢复——恢复后一律 idle
                    Assert.Equal("idle", doc.RootElement.GetProperty("runState").GetString());
                    Assert.Equal(7, doc.RootElement.GetProperty("requests").GetInt32());
                    JsonElement noteElement = doc.RootElement.GetProperty("note");
                    Assert.Equal(3, noteElement.GetProperty("tasks").GetArrayLength());
                    Assert.Equal(1, noteElement.GetProperty("current").GetInt32());
                    Assert.Equal(1, noteElement.GetProperty("done").GetInt32());
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

        /// <summary>state 段 contextChars——推送时实时刷新（前文实况；A201 易用性修复：原实现只在轮末刷新，轮内滞后）。</summary>
        [Fact]
        public void BuildStateJson_ContextChars_Realtime()
        {
            string catId = "meta-chars-" + Guid.NewGuid().ToString("N");
            CH4.ToolRegistry.Init(new ToolSpec[0], null, new Dictionary<string, bool>());
            ChatContext ctx = new ChatContext();
            ctx.AddUserMessage("12345");
            SessionStore store = new SessionStore(TempPath(catId, ".jsonl"));
            OA oa = new OA(new ThreadGuard());
            CH4.SessionViewStore viewStore = new CH4.SessionViewStore(TempPath(catId, ".view.json"));
            CH4.ChatSession session = new CH4.ChatSession(catId, "字符猫", ctx, store, new NullLlm(), oa, new ToolSpec[0], delegate (string name, string args) { return "ERR|NO_TOOL|" + name; }, viewStore);
            string json = session.BuildStateJson();
            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                JsonElement metaElement = doc.RootElement.GetProperty("meta");
                // 前文 = 1 条用户消息（5 字符）——实时字符数与条数同源
                Assert.Equal(5L, metaElement.GetProperty("contextChars").GetInt64());
                Assert.Equal(1L, metaElement.GetProperty("contextCount").GetInt64());
            }
        }

        /// <summary>state 段 lastActiveAt——前文从未变动时回落创建时刻（A201 易用性修复：原直取 LastChangeAt，恢复导入后为 0）。</summary>
        [Fact]
        public void BuildStateJson_LastActiveAt_FallsBackToCreatedAt()
        {
            string catId = "meta-active-" + Guid.NewGuid().ToString("N");
            CH4.ChatSession session = CreateSession(catId, "回落猫");
            session.LoadMeta();
            string json = session.BuildStateJson();
            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                JsonElement metaElement = doc.RootElement.GetProperty("meta");
                long lastActive = metaElement.GetProperty("lastActiveAt").GetInt64();
                Assert.True(lastActive > 0);
                Assert.Equal(session.SessionCreatedAt, lastActive);
            }
        }
    }
}

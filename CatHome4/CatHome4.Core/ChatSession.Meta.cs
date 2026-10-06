using System;
using Mau.Runtime;

namespace CH4
{
    /// <summary>
    /// 宿主会话实体——会话元数据持久化段（A201 · design-ch4-protocol §十三）。
    /// 会话自身参数（实例 ID / displayName / 时间戳 / 前文三项 / 两级 token 累计）独立落盘，
    /// 与 cat.cfg（配置面）· jsonl（真实前文）· view.json（视图前文）并列——重启可恢复。
    /// </summary>
    internal sealed partial class ChatSession
    {
        /// <summary>会话元数据落点解析——猫 key → 元数据文件绝对路径（入口壳注入；空=不持久化——测试环境）</summary>
        public static Func<string, string> SessionMetaPathProvider;

        /// <summary>显示名解析——猫 key → cat.cfg 现值（入口壳注入；空=session.new 不刷新显示名）</summary>
        public static Func<string, string> DisplayNameProvider;

        /// <summary>会话实例 ID——session.new 生成（显示用；不参与 LLM 请求身份与网关缓存命名空间）</summary>
        private string _sessionInstanceId = "";

        /// <summary>会话创建时刻——Unix 毫秒（session.new 重置；启动无文件时按首建生成）</summary>
        private long _sessionCreatedAt;

        /// <summary>最近一轮 token 三项快照——结算时捕获（读面空闲回落源 + 元数据落盘数据源）</summary>
        private SessionTokens _lastRoundTokens;

        /// <summary>前文总字符数——轮末 / 恢复时刷新（state 段读此缓存，不逐帧重算）</summary>
        private long _contextChars;

        /// <summary>元数据落盘器——按 Provider 路径构造（空 Provider = 不持久化）</summary>
        private SessionMetaStore _metaStore;

        /// <summary>元数据落盘器解析标志——区分「未解析」与「解析后为空」</summary>
        private bool _metaStoreResolved;

        /// <summary>会话实例 ID——显示用（不参与 LLM 请求身份）</summary>
        public string SessionInstanceId
        {
            get
            {
                return _sessionInstanceId;
            }
        }

        /// <summary>会话创建时刻——Unix 毫秒</summary>
        public long SessionCreatedAt
        {
            get
            {
                return _sessionCreatedAt;
            }
        }

        /// <summary>
        /// 设置会话显示名——session.new 时按当前 cat.cfg 刷新（A201：displayName 随新会话取最新）。
        /// </summary>
        /// <param name="displayName">显示名（空 = 保持原值）</param>
        public void SetDisplayName(string displayName)
        {
            if (displayName == null || displayName.Length == 0)
            {
                return;
            }
            _displayName = displayName;
        }

        /// <summary>
        /// 载入会话元数据——启动恢复（装配层构造后调用）。
        /// 文件缺失 = 首建（生成实例 ID + 创建时刻）；损坏 = 按首建处理（落盘器已出声）。
        /// displayName 不取文件值——构造注入的 cat.cfg 现值更权威。
        /// </summary>
        public void LoadMeta()
        {
            SessionMetaStore store = ResolveMetaStore();
            SessionMeta meta = store == null ? null : store.Load();
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (meta == null)
            {
                _sessionInstanceId = SessionStore.NewSessionId();
                _sessionCreatedAt = now;
                return;
            }
            _sessionInstanceId = meta.SessionId == null ? "" : meta.SessionId;
            if (_sessionInstanceId.Length == 0)
            {
                _sessionInstanceId = SessionStore.NewSessionId();
            }
            _sessionCreatedAt = meta.CreatedAt;
            _sessionPrompt = meta.SessionTokens.Prompt;
            _sessionCacheHit = meta.SessionTokens.CacheHit;
            _sessionCompletion = meta.SessionTokens.Completion;
            _lastRoundTokens = meta.RoundTokens;
            _contextChars = meta.ContextChars;
        }

        /// <summary>
        /// 落盘会话元数据——轮末 / session.new / 中断收尾（覆盖式原子写；失败出声不阻断）。
        /// </summary>
        private void SaveMeta()
        {
            SessionMetaStore store = ResolveMetaStore();
            if (store == null)
            {
                return;
            }
            _contextChars = ComputeContextChars();
            SessionMeta meta = new SessionMeta();
            meta.SessionId = _sessionInstanceId;
            meta.CatId = _id;
            meta.DisplayName = _displayName;
            meta.CreatedAt = _sessionCreatedAt;
            long active = _context.LastChangeAt;
            meta.LastActiveAt = active > 0 ? active : _sessionCreatedAt;
            meta.ContextCount = _context.GetMessageCount();
            meta.ContextChars = _contextChars;
            meta.ContextTokens = ContextTokensKnown;
            SessionTokens st = new SessionTokens();
            st.Prompt = _sessionPrompt;
            st.CacheHit = _sessionCacheHit;
            st.Completion = _sessionCompletion;
            meta.SessionTokens = st;
            meta.RoundTokens = _lastRoundTokens;
            store.Save(meta);
        }

        /// <summary>
        /// 捕获本轮 token 三项快照——结算时调用（此后内存值即清零；读面空闲回落此快照）。
        /// </summary>
        /// <returns>本轮三项快照</returns>
        private SessionTokens CaptureRoundTokens()
        {
            SessionTokens t = new SessionTokens();
            t.Prompt = _usagePrompt;
            t.CacheHit = _usageCacheHit;
            t.Completion = _usageCompletion;
            return t;
        }

        /// <summary>
        /// 前文总字符数——全消息 Content 长度之和（轮末 / 恢复时按需计算，不逐帧）。
        /// </summary>
        /// <returns>字符总数</returns>
        private long ComputeContextChars()
        {
            LlmMessage[] all = _context.GetMessages();
            long total = 0;
            for (int i = 0; i < all.Length; i = i + 1)
            {
                string content = all[i].Content;
                if (content != null)
                {
                    total = total + content.Length;
                }
            }
            return total;
        }

        /// <summary>
        /// 元数据落盘器解析——按 Provider 路径构造（一次解析缓存；Provider 空 = 不持久化）。
        /// </summary>
        /// <returns>落盘器；未接线 = null</returns>
        private SessionMetaStore ResolveMetaStore()
        {
            if (_metaStoreResolved)
            {
                return _metaStore;
            }
            _metaStoreResolved = true;
            if (SessionMetaPathProvider == null || _id == null || _id.Length == 0)
            {
                return null;
            }
            string path = SessionMetaPathProvider(_id);
            if (path == null || path.Length == 0)
            {
                return null;
            }
            _metaStore = new SessionMetaStore(path);
            return _metaStore;
        }
    }
}

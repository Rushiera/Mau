using System.Collections.Generic;
using CatHome4.Contracts;
using Mau.Runtime;

namespace CH4
{
    /// <summary>
    /// 会话视图出口——状态推送出口（A162 批次二，取代 A158 期三的事件流出口）。
    /// ① 状态表（_blocks）：持有当前视图的全部块（流式 + 持久）——"当前状态"的唯一内存快照，
    ///    供连接建立时取全量（BuildFull）。
    /// ② 变更集（_dirty / _removed）：记录自上次取走以来的块级增删改——宿主帧轮取走即清空，
    ///    供增量推送（TryTakeDelta）。
    /// ③ 瞬时事件（control）：usage / chatdone / paused / note / session_reset——非块，仍即时推送。
    /// 推送时机移交宿主：连接建立取 BuildFull（连接私有首帧），此后每帧轮取 TryTakeDelta 广播。
    /// 本类不再直接推送块（除 control）——「何时推、推给谁」归 HttpHost，「当前状态是什么」归本类。
    /// 块键即身份：同键后续写入覆盖（换手 = 持久块覆盖同键流式块，不产生移除）。
    /// 宿主推送面未附加时全部入口静默跳过（与拆分前会话侧 _httpHost 判空语义一致）。
    /// </summary>
    internal sealed class ViewBus
    {
        /// <summary>宿主推送面——Bootstrap 段6 宿主 HTTP 启动后 Attach 赋值（构造时宿主 HTTP 未启动）</summary>
        private IHostPush _host;

        /// <summary>流式区块表——块键 → 块（当前正在进行的块；出区即移除）</summary>
        private readonly Dictionary<string, ViewBlock> _live = new Dictionary<string, ViewBlock>();

        /// <summary>状态表——块键 → 块（流式 + 持久全表：全量推送的数据源）</summary>
        private readonly Dictionary<string, ViewBlock> _blocks = new Dictionary<string, ViewBlock>();

        /// <summary>变更集·增改——块键 → 块（自上次取走以来新增或变化的块）</summary>
        private readonly Dictionary<string, ViewBlock> _dirty = new Dictionary<string, ViewBlock>();

        /// <summary>变更集·移除——自上次取走以来被移除的块键</summary>
        private readonly HashSet<string> _removed = new HashSet<string>();

        /// <summary>持久区已记键集合——同一块键至多记一次（幂等在后端，前端零规则）</summary>
        private readonly HashSet<string> _persisted = new HashSet<string>();

        /// <summary>实时块键序号——流式容器键分配（持久面键由块自身承载，实时面自持计数）</summary>
        private long _realtimeKeySeq;

        /// <summary>流式文本容器键——首增量分配后复用（容器块的实时身份）</summary>
        private string _textStreamKey;

        /// <summary>流式文本容器时刻——首增量确定，后续增量复用（同块同 ts）</summary>
        private long _textStreamTs;

        /// <summary>流式思考容器键——首增量分配后复用</summary>
        private string _reasonStreamKey;

        /// <summary>流式思考容器时刻——首次增量确定，后续增量复用</summary>
        private long _reasonStreamTs;

        /// <summary>取流式区当前块快照——history 载荷随带（区内块按时间戳升序）</summary>
        /// <returns>区内块数组（按时间戳升序）</returns>
        public ViewBlock[] GetLiveBlocks()
        {
            // 主线程调用——与其余入口同域（会话轮内），无锁
            List<ViewBlock> list = new List<ViewBlock>();
            foreach (KeyValuePair<string, ViewBlock> kv in _live)
            {
                list.Add(kv.Value);
            }
            list.Sort(delegate (ViewBlock a, ViewBlock b)
            {
                return a.Timestamp.CompareTo(b.Timestamp);
            });
            return list.ToArray();
        }

        /// <summary>推送面就绪标志——宿主外观层已附加</summary>
        public bool Ready
        {
            get
            {
                return _host != null;
            }
        }

        /// <summary>
        /// 附加宿主推送面——Bootstrap 段6 宿主 HTTP 启动后调用（SSE 转发面就位）。
        /// </summary>
        /// <param name="host">HTTP 外观层实例</param>
        public void Attach(IHostPush host)
        {
            _host = host;
        }

        /// <summary>
        /// 全量载荷——当前状态全部块（连接建立时取一次，前端整体重建）+ 状态栏前文三段数据（A97）。
        /// 前文条数与长度由会话侧传入（本类不持有前文——只持视图块）。
        /// </summary>
        /// <param name="sessionId">会话 ID（前端状态栏归属）</param>
        /// <param name="ctxCount">前文条数（送入 LLM 的消息数——与 history 端点同源）</param>
        /// <param name="ctxTokens">前文长度（已知最新——请求级实时优先 + 轮末回落）</param>
        /// <returns>载荷 JSON（{"op":"full","sessionId":…,"ctxCount":…,"ctxTokens":…,"blocks":[…] }）</returns>
        public string BuildFull(string sessionId, int ctxCount, long ctxTokens)
        {
            List<ViewBlock> list = new List<ViewBlock>();
            foreach (KeyValuePair<string, ViewBlock> kv in _blocks)
            {
                list.Add(kv.Value);
            }
            list.Sort(delegate (ViewBlock a, ViewBlock b)
            {
                return a.Timestamp.CompareTo(b.Timestamp);
            });
            List<string> fragments = new List<string>();
            for (int i = 0; i < list.Count; i = i + 1)
            {
                fragments.Add(BlockJson(list[i]));
            }
            return JsonUtil.Object(
                ("op", "full"),
                ("sessionId", sessionId == null ? "" : sessionId),
                ("ctxCount", ctxCount),
                ("ctxTokens", ctxTokens),
                ("blocks", JsonUtil.RawArray(fragments.ToArray())));
        }

        /// <summary>
        /// 取增量载荷——变更集为空时返回 false（宿主帧轮据此零推送）。
        /// 取走即清空变更集（每块每次变化至多推一次）。
        /// </summary>
        /// <param name="json">载荷 JSON（{"op":"delta","blocks":[…],"remove":[…]}）；无变化时为 null</param>
        /// <returns>true = 有变化且 json 已产出</returns>
        public bool TryTakeDelta(out string json)
        {
            json = null;
            if (_dirty.Count == 0 && _removed.Count == 0)
            {
                return false;
            }
            List<ViewBlock> list = new List<ViewBlock>();
            foreach (KeyValuePair<string, ViewBlock> kv in _dirty)
            {
                list.Add(kv.Value);
            }
            list.Sort(delegate (ViewBlock a, ViewBlock b)
            {
                return a.Timestamp.CompareTo(b.Timestamp);
            });
            List<string> fragments = new List<string>();
            for (int i = 0; i < list.Count; i = i + 1)
            {
                fragments.Add(BlockJson(list[i]));
            }
            List<string> removedKeys = new List<string>();
            foreach (string key in _removed)
            {
                removedKeys.Add(key);
            }
            json = JsonUtil.Object(
                ("op", "delta"),
                ("blocks", JsonUtil.RawArray(fragments.ToArray())),
                ("remove", JsonUtil.Raw(KeysJson(removedKeys))));
            _dirty.Clear();
            _removed.Clear();
            return true;
        }

        /// <summary>流式文本增量——首个增量入流式区，后续增量更新区内块</summary>
        /// <param name="json">载荷 JSON（增量片段）</param>
        public void PushTextStream(string json)
        {
            if (_host == null)
            {
                return;
            }
            if (_textStreamKey == null)
            {
                _textStreamKey = NextKey("stream:text");
                _textStreamTs = NowMs();
                LiveAdd(_textStreamKey, "stream", json, _textStreamTs);
                return;
            }
            LiveUpdate(_textStreamKey, json, -1, ViewBlock.StatePending);
        }

        /// <summary>流式思考增量——reasoning 独立容器（首个增量入区，后续更新）</summary>
        /// <param name="json">载荷 JSON（增量片段）</param>
        public void PushReasonStream(string json)
        {
            if (_host == null)
            {
                return;
            }
            if (_reasonStreamKey == null)
            {
                _reasonStreamKey = NextKey("stream:reason");
                _reasonStreamTs = NowMs();
                LiveAdd(_reasonStreamKey, "stream", json, _reasonStreamTs);
                return;
            }
            LiveUpdate(_reasonStreamKey, json, -1, ViewBlock.StatePending);
        }

        /// <summary>流式文本容器移除——整块到达 / 轮终（容器不在途时零动作）</summary>
        public void ResetTextStream()
        {
            if (_textStreamKey != null)
            {
                LiveRemove(_textStreamKey);
            }
            _textStreamKey = null;
            _textStreamTs = 0;
        }

        /// <summary>流式思考容器移除——思考段终结 / 轮终（容器不在途时零动作）</summary>
        public void ResetReasonStream()
        {
            if (_reasonStreamKey != null)
            {
                LiveRemove(_reasonStreamKey);
            }
            _reasonStreamKey = null;
            _reasonStreamTs = 0;
        }

        /// <summary>工具卡是否有在途先行卡——批派发时判断该工具是否已入过「进行中」卡</summary>
        /// <param name="key">块键（tool:&lt;toolCallId&gt;）</param>
        /// <returns>true = 有在途先行卡</returns>
        public bool IsToolCardPending(string key)
        {
            if (key == null)
            {
                return false;
            }
            return _live.ContainsKey(key);
        }

        /// <summary>工具卡是否已定稿——区内块状态为 final（中断补推的幂等判据：终态卡出区前不得重复补推）</summary>
        /// <param name="key">块键（tool:&lt;toolCallId&gt;）</param>
        /// <returns>true = 区内块已定稿</returns>
        public bool IsToolCardFinaled(string key)
        {
            if (key == null)
            {
                return false;
            }
            ViewBlock block;
            if (!_live.TryGetValue(key, out block))
            {
                return false;
            }
            return block.State == ViewBlock.StateFinal;
        }

        /// <summary>
        /// 工具卡先行卡——建 pending 块入流式区（同键重复推送为区内块变化，不堆叠）。
        /// 块键 = tool:&lt;toolCallId&gt;（与持久块同键——持久块写入时按同键换手覆盖）。
        /// </summary>
        /// <param name="key">块键（tool:&lt;toolCallId&gt;）</param>
        /// <param name="json">载荷 JSON</param>
        /// <param name="ts">块时间戳（Unix 毫秒——工具调用声明时刻）</param>
        public void PushToolCardPending(string key, string json, long ts)
        {
            if (_host == null || key == null || key.Length == 0)
            {
                return;
            }
            if (_live.ContainsKey(key))
            {
                LiveUpdate(key, json, -1, ViewBlock.StatePending);
                return;
            }
            LiveAdd(key, "toolcard", json, ts);
        }

        /// <summary>
        /// 工具卡终态——流式区内原位更新（完成由 FlushToolCard 逐条回填 / 中断由收尾补推）。
        /// 块留在流式区：持久块写入时按同键换手覆盖；无在途先行卡（被拦工具 / 直执路径）先入区再定稿。
        /// </summary>
        /// <param name="key">块键（tool:&lt;toolCallId&gt;）</param>
        /// <param name="json">载荷 JSON</param>
        /// <param name="durMs">运行时长（毫秒；-1 = 未记录）</param>
        public void PushToolCardDone(string key, string json, long durMs)
        {
            if (_host == null || key == null || key.Length == 0)
            {
                return;
            }
            if (!_live.ContainsKey(key))
            {
                LiveAdd(key, "toolcard", json, NowMs());
            }
            LiveUpdate(key, json, durMs, ViewBlock.StateFinal);
        }

        /// <summary>
        /// 持久块记入——持久区建块即记（由 SessionViewStore 建块事件驱动，本类不建持久块）。
        /// 幂等：同一块键至多记一次；记入后同键流式块出区（换手 = 持久块覆盖，不产生移除指令）。
        /// </summary>
        /// <param name="block">已定稿的持久块</param>
        public void PushPersist(ViewBlock block)
        {
            if (block == null || _host == null)
            {
                return;
            }
            string key = block.Key == null ? "" : block.Key;
            if (key.Length > 0 && !_persisted.Add(key))
            {
                return;
            }
            if (key.Length == 0)
            {
                return;
            }
            _blocks[key] = block;
            _dirty[key] = block;
            _removed.Remove(key);
            _live.Remove(key);
        }
        /// <summary>
        /// 状态表恢复——宿主重启后把已落盘的持久块灌回状态表（A96：首连全量帧含完整历史）。
        /// 只填状态表与幂等集，不记增改（历史块不该以增量重推——前端已由全量帧持有）。
        /// </summary>
        /// <param name="blocks">已落盘的持久块（按生成序）</param>
        public void Seed(ViewBlock[] blocks)
        {
            if (blocks == null)
            {
                return;
            }
            for (int i = 0; i < blocks.Length; i = i + 1)
            {
                ViewBlock block = blocks[i];
                if (block == null || block.Key == null || block.Key.Length == 0)
                {
                    continue;
                }
                _blocks[block.Key] = block;
                _persisted.Add(block.Key);
            }
        }

        /// <summary>瞬时事件推送——usage / chatdone / paused / note / session_reset（非块：前端按事件处理，不渲气泡）</summary>
        /// <param name="json">载荷 JSON</param>
        public void PushControl(string json)
        {
            if (_host == null)
            {
                return;
            }
            string meta = JsonUtil.Object(
                ("key", NextKey("rt:control")),
                ("renderType", "control"),
                ("ts", NowMs()));
            _host.PushView("control", json, meta);
        }

        /// <summary>状态清空——轮终 / 新会话 / 清空前文：流式块逐个记移除，状态表全清（后续按需重建）</summary>
        public void ResetLive()
        {
            string[] keys = new string[_live.Count];
            _live.Keys.CopyTo(keys, 0);
            for (int i = 0; i < keys.Length; i = i + 1)
            {
                LiveRemove(keys[i]);
            }
            _blocks.Clear();
            _persisted.Clear();
            _textStreamKey = null;
            _textStreamTs = 0;
            _reasonStreamKey = null;
            _reasonStreamTs = 0;
        }

        /// <summary>块序列化——九字段契约（与 history 块面同构）</summary>
        /// <param name="block">视图块</param>
        /// <returns>块 JSON</returns>
        private static string BlockJson(ViewBlock block)
        {
            string originJson = block.Origin == null
                ? "null"
                : JsonUtil.Object(("msgIndex", block.Origin.MsgIndex), ("hash", block.Origin.Hash == null ? "" : block.Origin.Hash));
            string payload = block.Payload == null || block.Payload.Length == 0 ? "null" : block.Payload;
            return JsonUtil.Object(
                ("key", block.Key == null ? "" : block.Key),
                ("id", block.Id == null ? "" : block.Id),
                ("ts", block.Timestamp),
                ("renderType", block.RenderType == null ? "" : block.RenderType),
                ("payload", JsonUtil.Raw(payload)),
                ("origin", JsonUtil.Raw(originJson)),
                ("src", block.Src == null ? "" : block.Src),
                ("durMs", block.DurMs),
                ("state", block.State == null ? "" : block.State));
        }

        /// <summary>字符串数组序列化——元素走统一入口转义（骨架式拼接）</summary>
        /// <param name="values">字符串元素</param>
        /// <returns>JSON 数组文本</returns>
        private static string KeysJson(List<string> values)
        {
            List<string> fragments = new List<string>();
            for (int i = 0; i < values.Count; i = i + 1)
            {
                fragments.Add(JsonUtil.Str(values[i]));
            }
            return "[" + string.Join(",", fragments) + "]";
        }

        /// <summary>流式区入块——建 pending 块记入流式区与状态表 + 记增改</summary>
        /// <param name="key">块键</param>
        /// <param name="renderType">渲染类型</param>
        /// <param name="json">载荷 JSON</param>
        /// <param name="ts">块时间戳（Unix 毫秒）</param>
        private void LiveAdd(string key, string renderType, string json, long ts)
        {
            if (_host == null)
            {
                return;
            }
            ViewBlock block = ViewBlock.BuildPending(key, renderType, json, ts, null, "independent");
            _live[key] = block;
            _blocks[key] = block;
            _dirty[key] = block;
            _removed.Remove(key);
        }

        /// <summary>流式区内容变化——区内块更新（不在区则零动作）+ 记增改</summary>
        /// <param name="key">块键</param>
        /// <param name="json">载荷 JSON</param>
        /// <param name="durMs">运行时长（毫秒；-1 = 不适用 / 未记录）</param>
        /// <param name="state">生命周期状态（ViewBlock.State*）</param>
        private void LiveUpdate(string key, string json, long durMs, string state)
        {
            if (_host == null)
            {
                return;
            }
            ViewBlock block;
            if (!_live.TryGetValue(key, out block))
            {
                return;
            }
            block.Payload = json;
            block.State = state;
            if (durMs >= 0)
            {
                block.DurMs = durMs;
            }
            _dirty[key] = block;
        }

        /// <summary>流式区出块——表内移除 + 状态表摘除 + 记移除（不在区则零动作）</summary>
        /// <param name="key">块键</param>
        private void LiveRemove(string key)
        {
            if (!_live.Remove(key))
            {
                return;
            }
            if (_host == null)
            {
                return;
            }
            _blocks.Remove(key);
            _dirty.Remove(key);
            _removed.Add(key);
        }

        /// <summary>生成时刻——流式容器的时间戳（Unix 毫秒；前文派生块由调用方传消息 CreatedAt）</summary>
        /// <returns>当前墙钟毫秒</returns>
        private static long NowMs()
        {
            return System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        /// <summary>实时块键分配——自持计数，与持久面键相互独立</summary>
        /// <param name="prefix">键前缀</param>
        /// <returns>实时块键</returns>
        private string NextKey(string prefix)
        {
            _realtimeKeySeq = _realtimeKeySeq + 1;
            return prefix + ":" + _realtimeKeySeq.ToString();
        }
    }
}

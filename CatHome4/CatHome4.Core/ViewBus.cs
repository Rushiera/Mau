using System.Collections.Generic;
using CatHome4.Contracts;
using Mau.Runtime;

namespace CH4
{
    /// <summary>
    /// 会话视图出口——两区镜像出口（A158 期三）。
    /// ① 流式区（live）：持有「当前正在进行的块」——流式文本 / 思考容器、工具卡先行卡；
    ///    变更以三 op 推送（live.add / live.update / live.remove），前端忠实镜像（不配对、不判断）。
    /// ② 持久区（persist）：持久块建块即推 persist.append（由 SessionViewStore 建块事件驱动）；
    ///    本类按块键幂等（同一键至多推一次），推入时同键流式块出区（工具卡换手收口于本方法）。
    /// ③ 瞬时事件（control）：usage / chatdone / paused / note / session_reset——非块，前端按事件处理。
    /// A158 计数退役：流式三序号 / retry 序号 / 工具卡槽位与定稿双标志并入本类流式表与块键。
    /// 时间戳口径：前文派生块由调用方传消息 CreatedAt；流式容器取生成时刻（块契约 §3.1）。
    /// 宿主推送面未附加时全部推送静默跳过（与拆分前会话侧 _httpHost 判空语义一致）。
    /// </summary>
    internal sealed class ViewBus
    {
        /// <summary>宿主推送面——Bootstrap 段6 宿主 HTTP 启动后 Attach 赋值（构造时宿主 HTTP 未启动）</summary>
        private IHostPush _host;

        /// <summary>流式区块表——块键 → 块（当前正在进行的块；出区即移除）</summary>
        private readonly Dictionary<string, ViewBlock> _live = new Dictionary<string, ViewBlock>();

        /// <summary>持久区已推键集合——同一块键至多推一次 persist.append（幂等在后端，前端零规则）</summary>
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

        /// <summary>取流式区当前块快照——history 全量载荷随带（重连 / 刷新后前端忠实重建流式区）</summary>
        /// <returns>区内块数组（按时间戳升序）</returns>
        public ViewBlock[] GetLiveBlocks()
        {
            // 主线程调用——与其余推送同域（会话轮内），无锁
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

        /// <summary>流式文本增量——首个增量入流式区（live.add），后续增量区内容变化（live.update）</summary>
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

        /// <summary>流式思考增量——reasoning 独立容器（首个增量 live.add，后续 live.update）</summary>
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

        /// <summary>流式文本容器移除——整块到达 / 轮终（live.remove；容器不在途时零动作）</summary>
        public void ResetTextStream()
        {
            if (_textStreamKey != null)
            {
                LiveRemove(_textStreamKey);
            }
            _textStreamKey = null;
            _textStreamTs = 0;
        }

        /// <summary>流式思考容器移除——思考段终结 / 轮终（live.remove；容器不在途时零动作）</summary>
        public void ResetReasonStream()
        {
            if (_reasonStreamKey != null)
            {
                LiveRemove(_reasonStreamKey);
            }
            _reasonStreamKey = null;
            _reasonStreamTs = 0;
        }

        /// <summary>工具卡是否有在途先行卡——批派发时判断该工具是否已推过「进行中」卡</summary>
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
        /// 工具卡先行卡——建 pending 块入流式区（同键重复推送为区内容变化，不堆叠）。
        /// 块键 = tool:&lt;toolCallId&gt;（与持久块同键——持久块建立时按同键换手移除）。
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
        /// 块留在流式区：持久块建立时按同键换手移除；无在途先行卡（被拦工具 / 直执路径）先入区再定稿。
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
        /// 持久块推送——持久区建块即推（由 SessionViewStore 建块事件驱动，本类不建持久块）。
        /// 幂等：同一块键至多推一次（后端承担，前端只追加）；推入后同键流式块出区（换手收口于本方法）。
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
            string originJson = block.Origin == null
                ? "null"
                : JsonUtil.Object(("msgIndex", block.Origin.MsgIndex), ("hash", block.Origin.Hash == null ? "" : block.Origin.Hash));
            string meta = JsonUtil.Object(
                ("key", key),
                ("renderType", block.RenderType == null ? "" : block.RenderType),
                ("ts", block.Timestamp),
                ("durMs", block.DurMs),
                ("state", block.State == null ? "" : block.State),
                ("id", block.Id == null ? "" : block.Id),
                ("src", block.Src == null ? "" : block.Src),
                ("origin", JsonUtil.Raw(originJson)));
            _host.PushView("persist.append", block.Payload, meta);
            if (key.Length > 0 && _live.ContainsKey(key))
            {
                LiveRemove(key);
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

        /// <summary>流式区清空——轮终 / 新会话 / 清空前文（逐块 live.remove；持久区记录一并作废）</summary>
        public void ResetLive()
        {
            string[] keys = new string[_live.Count];
            _live.Keys.CopyTo(keys, 0);
            for (int i = 0; i < keys.Length; i = i + 1)
            {
                LiveRemove(keys[i]);
            }
            _textStreamKey = null;
            _textStreamTs = 0;
            _reasonStreamKey = null;
            _reasonStreamTs = 0;
            _persisted.Clear();
        }

        /// <summary>流式区入块——建 pending 块入表 + live.add</summary>
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
            string meta = JsonUtil.Object(
                ("key", key),
                ("renderType", renderType),
                ("ts", ts),
                ("durMs", -1),
                ("state", ViewBlock.StatePending));
            _host.PushView("live.add", json, meta);
        }

        /// <summary>流式区内容变化——区内块更新（不在区则零动作）+ live.update</summary>
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
            string meta = JsonUtil.Object(
                ("key", key),
                ("renderType", block.RenderType == null ? "" : block.RenderType),
                ("ts", block.Timestamp),
                ("durMs", durMs),
                ("state", state == null ? "" : state));
            _host.PushView("live.update", json, meta);
        }

        /// <summary>流式区出块——表内移除 + live.remove（不在区则零动作）</summary>
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
            string meta = JsonUtil.Object(("key", key));
            _host.PushView("live.remove", "", meta);
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

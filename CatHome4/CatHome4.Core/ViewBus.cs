using System.Collections.Generic;
using CatHome4.Contracts;
using Mau.Runtime;

namespace CH4
{
    /// <summary>
    /// 会话视图出口——实时区（A155 步二 2a：视图层双区化的结构收口）。
    /// 职责：全部视图 SSE 直推 + 实时区状态（流式容器 / retry 气泡 / 工具卡先行卡）。
    /// 分区：固化块区（SessionViewStore）管落盘持久块；本类管不落盘的过程块出口与状态。
    /// A157：推送事件随带块元数据（key / ts / durMs / state）；工具卡生命周期（pending → final）
    /// 收口于本类槽位表——取代工具单 Dog 上的 CardSeq / CardFlushed 双标志（定稿幂等由键集合承担）。
    /// 时间戳口径：前文派生块（user / text / reason）由调用方传消息 CreatedAt；
    /// 独立块与流式容器取<b>生成时刻</b>（本类 NowMs——块契约 §3.1：独立块取生成时刻）。
    /// 宿主推送面未附加时全部推送静默跳过（与拆分前会话侧 _httpHost 判空语义一致）。
    /// </summary>
    internal sealed class ViewBus
    {
        /// <summary>宿主推送面——Bootstrap 段6 宿主 HTTP 启动后 Attach 赋值（构造时宿主 HTTP 未启动）</summary>
        private IHostPush _host;

        /// <summary>流式文本块序号——流式增量容器标识（整块到达时 replace 定位）</summary>
        private long _textStreamSeq;

        /// <summary>流式思考块序号——流式增量容器标识（reason 整块 replace；纯文本轮无整块）</summary>
        private long _reasonStreamSeq;

        /// <summary>retry 气泡视图序号——多次重试复用同一气泡（replaceSeq 替换不堆叠）</summary>
        private long _retrySeq;

        /// <summary>实时块键序号——流式容器与独立块的实时键分配（A157：持久面按容器计数，实时面自持计数）</summary>
        private long _realtimeKeySeq;

        /// <summary>流式文本容器键——首增量分配后复用（容器块的实时身份）</summary>
        private string _textStreamKey;

        /// <summary>流式文本容器时刻——首次增量确定，后续增量复用（同块同 ts）</summary>
        private long _textStreamTs;

        /// <summary>流式思考容器键——首增量分配后复用</summary>
        private string _reasonStreamKey;

        /// <summary>流式思考容器时刻——首次增量确定，后续增量复用</summary>
        private long _reasonStreamTs;

        /// <summary>retry 气泡容器键——新建时分配后复用（原位更新沿用同键）</summary>
        private string _retryKey;

        /// <summary>retry 气泡时刻——新建时确定，原位更新复用</summary>
        private long _retryTs;

        /// <summary>工具卡实时槽位——块键 → 槽位（块 + SSE 序号）；pending → final 生命周期由本表承担</summary>
        private readonly Dictionary<string, ToolCardSlot> _toolCards = new Dictionary<string, ToolCardSlot>();

        /// <summary>工具卡已定稿键集合——同一工具单终态卡至多一次（A157：定稿幂等）</summary>
        private readonly HashSet<string> _toolCardFinaled = new HashSet<string>();

        /// <summary>工具卡实时槽位——先行卡的块对象与推送序号</summary>
        private sealed class ToolCardSlot
        {
            /// <summary>先行卡块（pending 态——定稿时补 ID 与运行时长）</summary>
            public ViewBlock Block;

            /// <summary>先行卡 SSE 序号（终态卡原位替换定位）</summary>
            public long Seq;
        }

        /// <summary>推送面就绪标志——宿主外观层已附加</summary>
        public bool Ready
        {
            get
            {
                return _host != null;
            }
        }

        /// <summary>retry 气泡在途标志——本轮是否推过 retry 视图（错误中止措辞分档判据）</summary>
        public bool RetryActive
        {
            get
            {
                return _retrySeq != 0;
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

        /// <summary>流式文本增量——首个增量分配容器键与序号，后续复用（整块 text 到达时以该序号替换）</summary>
        /// <param name="json">载荷 JSON</param>
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
            }
            _textStreamSeq = PushView("stream", json, -1, _textStreamSeq, _textStreamKey, _textStreamTs, -1, ViewBlock.StatePending);
        }

        /// <summary>流式思考增量——reasoning 独立容器（reason 整块到达时替换）</summary>
        /// <param name="json">载荷 JSON</param>
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
            }
            _reasonStreamSeq = PushView("stream", json, -1, _reasonStreamSeq, _reasonStreamKey, _reasonStreamTs, -1, ViewBlock.StatePending);
        }

        /// <summary>流式文本序号复位——整块到达 / 轮终统一调用（容器键与时刻同时作废）</summary>
        public void ResetTextStream()
        {
            _textStreamSeq = 0;
            _textStreamKey = null;
            _textStreamTs = 0;
        }

        /// <summary>流式思考序号复位——整块收口 / 轮终统一调用（容器键与时刻同时作废）</summary>
        public void ResetReasonStream()
        {
            _reasonStreamSeq = 0;
            _reasonStreamKey = null;
            _reasonStreamTs = 0;
        }

        /// <summary>取流式思考序号并复位——思考段终结取值（容器键保留至整块推送，供整块沿用同一块键）</summary>
        /// <returns>在途思考容器序号（0 = 无在途流式块）</returns>
        public long TakeReasonStreamSeq()
        {
            long seq = _reasonStreamSeq;
            _reasonStreamSeq = 0;
            return seq;
        }

        /// <summary>文本整块——replaceSeq 指向流式容器（无容器时前端新建气泡）</summary>
        /// <param name="json">载荷 JSON</param>
        /// <param name="key">块键（空 = 沿用流式容器键；调用方按块来源给 msg:&lt;index&gt;:text / gap:&lt;n&gt;）</param>
        /// <param name="ts">块时间戳（Unix 毫秒——消息 CreatedAt；独立块取生成时刻）</param>
        public void PushTextBlock(string json, string key, long ts)
        {
            if (_host == null)
            {
                return;
            }
            string useKey = FallbackKey(key, _textStreamKey, "rt:text");
            PushView("text", json, _textStreamSeq, 0, useKey, ts, -1, ViewBlock.StateFinal);
        }

        /// <summary>思考整块——replaceSeq 指向流式容器序号（思考段终结唯一出口传入）</summary>
        /// <param name="json">载荷 JSON</param>
        /// <param name="streamSeq">流式容器序号（0 = 无在途流式块）</param>
        /// <param name="key">块键（空 = 沿用流式容器键；调用方按块来源给 msg:&lt;index&gt;:reason）</param>
        /// <param name="ts">块时间戳（Unix 毫秒）</param>
        public void PushReasonBlock(string json, long streamSeq, string key, long ts)
        {
            if (_host == null)
            {
                return;
            }
            string useKey = FallbackKey(key, _reasonStreamKey, "rt:reason");
            PushView("reason", json, streamSeq, 0, useKey, ts, -1, ViewBlock.StateFinal);
        }

        /// <summary>用户消息块——所有进内核消息的统一出口（前端气泡唯一来源）</summary>
        /// <param name="json">载荷 JSON</param>
        /// <param name="key">块键（空 = 实时自动键；调用方按块来源给 msg:&lt;index&gt;:user）</param>
        /// <param name="ts">块时间戳（Unix 毫秒——消息 CreatedAt）</param>
        public void PushUser(string json, string key, long ts)
        {
            if (_host == null)
            {
                return;
            }
            PushView("user", json, -1, 0, FallbackKey(key, null, "rt:user"), ts, -1, ViewBlock.StateFinal);
        }

        /// <summary>控制块——usage / chatdone / paused / note / session_reset（前端阶段控制唯一入口）</summary>
        /// <param name="json">载荷 JSON</param>
        public void PushControl(string json)
        {
            if (_host == null)
            {
                return;
            }
            PushView("control", json, -1, 0, NextKey("rt:control"), NowMs(), -1, ViewBlock.StateFinal);
        }

        /// <summary>错误气泡块——LLM 错误 / 发送失败（独立渲染面）</summary>
        /// <param name="json">载荷 JSON</param>
        /// <param name="key">块键（空 = 实时自动键；调用方按块来源给 error:&lt;n&gt;）</param>
        /// <param name="ts">块时间戳（Unix 毫秒——独立块取生成时刻）</param>
        public void PushError(string json, string key, long ts)
        {
            if (_host == null)
            {
                return;
            }
            PushView("error", json, -1, 0, FallbackKey(key, null, "rt:error"), ts, -1, ViewBlock.StateFinal);
        }

        /// <summary>轮末统计块——本轮 Token 消耗 + 工具次数 + 四态用时 + 总耗时</summary>
        /// <param name="json">载荷 JSON</param>
        /// <param name="key">块键（空 = 实时自动键；调用方按块来源给 roundsum:&lt;n&gt;）</param>
        /// <param name="ts">块时间戳（Unix 毫秒——独立块取生成时刻）</param>
        public void PushRoundSum(string json, string key, long ts)
        {
            if (_host == null)
            {
                return;
            }
            PushView("roundsum", json, -1, 0, FallbackKey(key, null, "rt:roundsum"), ts, -1, ViewBlock.StateFinal);
        }

        /// <summary>retry 气泡新建——分配 / 复用气泡序号（retrying 态）</summary>
        /// <param name="json">载荷 JSON</param>
        /// <param name="ts">块时间戳（Unix 毫秒——独立块取生成时刻）</param>
        public void PushRetryNew(string json, long ts)
        {
            if (_host == null)
            {
                return;
            }
            if (_retryKey == null)
            {
                _retryKey = NextKey("rt:retry");
                _retryTs = ts;
            }
            _retrySeq = PushView("retry", json, -1, _retrySeq, _retryKey, _retryTs, -1, ViewBlock.StatePending);
        }

        /// <summary>retry 气泡原位更新——replaceSeq = 既有气泡序号（resolved / failed 态）</summary>
        /// <param name="json">载荷 JSON</param>
        /// <param name="ts">块时间戳（Unix 毫秒——无在途气泡时用作新块时刻）</param>
        public void PushRetryUpdate(string json, long ts)
        {
            if (_host == null)
            {
                return;
            }
            string useKey = _retryKey == null ? NextKey("rt:retry") : _retryKey;
            long useTs = _retryKey == null ? ts : _retryTs;
            PushView("retry", json, _retrySeq, 0, useKey, useTs, -1, ViewBlock.StateFinal);
        }

        /// <summary>retry 气泡序号复位——轮终（正常 / 中断 / 错误中止）统一调用，防跨轮残留</summary>
        public void ResetRetry()
        {
            _retrySeq = 0;
            _retryKey = null;
            _retryTs = 0;
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
            return _toolCards.ContainsKey(key);
        }

        /// <summary>工具卡是否已定稿——终态卡幂等判据（同一工具单至多一次）</summary>
        /// <param name="key">块键（tool:&lt;toolCallId&gt;）</param>
        /// <returns>true = 已推过终态卡</returns>
        public bool IsToolCardFinaled(string key)
        {
            if (key == null)
            {
                return false;
            }
            return _toolCardFinaled.Contains(key);
        }

        /// <summary>
        /// 工具卡先行卡——建 pending 块入槽位（同一键重复推送原位替换，不堆叠）。
        /// A157：块键 = tool:&lt;toolCallId&gt;（与持久块同键）；事件随带 key / ts / durMs=-1 / state=pending。
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
            ViewBlock block = ViewBlock.BuildPending(key, "toolcard", json, ts, null, "independent");
            ToolCardSlot slot = new ToolCardSlot();
            slot.Block = block;
            slot.Seq = PushView("toolcard", json, -1, 0, key, ts, -1, block.State);
            _toolCards[key] = slot;
        }

        /// <summary>
        /// 工具卡终态——有槽位则原位替换先行卡（replaceSeq = 槽位序号），无槽位则新建推送（被拦工具 / 直执路径）。
        /// 定稿幂等：同一键第二次调用直接忽略（等价原 CardFlushed 语义，且不依赖调用方判据）。
        /// </summary>
        /// <param name="key">块键（tool:&lt;toolCallId&gt;）</param>
        /// <param name="json">载荷 JSON</param>
        /// <param name="durMs">运行时长（毫秒；-1 = 未记录）</param>
        /// <param name="ts">块时间戳（Unix 毫秒——无槽位时用作块时间戳）</param>
        public void PushToolCardFinal(string key, string json, long durMs, long ts)
        {
            if (_host == null || key == null || key.Length == 0)
            {
                return;
            }
            if (_toolCardFinaled.Contains(key))
            {
                return;
            }
            ToolCardSlot slot;
            if (_toolCards.TryGetValue(key, out slot))
            {
                slot.Block.Payload = json;
                slot.Block.Finalize(durMs);
                PushView("toolcard", json, slot.Seq, 0, key, slot.Block.Timestamp, slot.Block.DurMs, slot.Block.State);
                _toolCards.Remove(key);
                _toolCardFinaled.Add(key);
                return;
            }
            ViewBlock block = ViewBlock.BuildPending(key, "toolcard", json, ts, null, "independent");
            block.Finalize(durMs);
            PushView("toolcard", json, -1, 0, key, block.Timestamp, block.DurMs, block.State);
            _toolCardFinaled.Add(key);
        }

        /// <summary>工具卡槽位与定稿记录复位——新会话 / 清空前文时调用（防跨会话残留）</summary>
        public void ResetToolCards()
        {
            _toolCards.Clear();
            _toolCardFinaled.Clear();
        }

        /// <summary>生成时刻——独立块与流式容器的时间戳（Unix 毫秒；前文派生块由调用方传消息 CreatedAt）</summary>
        /// <returns>当前墙钟毫秒</returns>
        private static long NowMs()
        {
            return System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        /// <summary>实时块键分配——自持计数，与持久面容器计数相互独立</summary>
        /// <param name="prefix">键前缀</param>
        /// <returns>实时块键</returns>
        private string NextKey(string prefix)
        {
            _realtimeKeySeq = _realtimeKeySeq + 1;
            return prefix + ":" + _realtimeKeySeq.ToString();
        }

        /// <summary>块键取舍——调用方给键优先，其次流式容器键，最后实时自动键</summary>
        /// <param name="key">调用方给的块键（可空）</param>
        /// <param name="streamKey">流式容器键（可空）</param>
        /// <param name="autoPrefix">自动键前缀</param>
        /// <returns>实际使用的块键</returns>
        private string FallbackKey(string key, string streamKey, string autoPrefix)
        {
            if (key != null && key.Length > 0)
            {
                return key;
            }
            if (streamKey != null && streamKey.Length > 0)
            {
                return streamKey;
            }
            return NextKey(autoPrefix);
        }

        /// <summary>视图事件推送——附加块元数据（key / ts / durMs / state；A157 块契约随事件下发）</summary>
        /// <param name="renderType">渲染类型</param>
        /// <param name="json">载荷 JSON</param>
        /// <param name="replaceSeq">被替换块序号（-1 = 无）</param>
        /// <param name="seqHint">流式增量已分配序号（0 = 新分配）</param>
        /// <param name="key">块键</param>
        /// <param name="ts">块时间戳（Unix 毫秒）</param>
        /// <param name="durMs">运行时长（毫秒；-1 = 不适用 / 未定稿）</param>
        /// <param name="state">块生命周期状态（ViewBlock.State*）</param>
        /// <returns>事件序号</returns>
        private int PushView(string renderType, string json, long replaceSeq, long seqHint, string key, long ts, long durMs, string state)
        {
            if (_host == null)
            {
                return 0;
            }
            Dictionary<string, object> meta = new Dictionary<string, object>();
            meta["key"] = key == null ? "" : key;
            meta["ts"] = ts;
            meta["durMs"] = durMs;
            meta["state"] = state == null ? "" : state;
            return _host.PushView(renderType, json, replaceSeq, seqHint, JsonUtil.Serialize(meta));
        }
    }
}

using System;
using System.Collections.Generic;
using System.Text;
using CatHome4.Contracts;
using Mau.Runtime;

namespace CH4
{
    /// <summary>
    /// 会话视图出口——chat 快照协议 v2 出口（A165 阶段 1；取代 A162 状态推送出口）。
    /// 三段结构：state（前端状态 · 后端权威业务态）/ persist（持久对话区 · 只增）/ live（临时区 · 全量镜像）。
    /// 三种传输模式：全量（连接建立与重置后重发）· 追加（新持久条目）· 变化增量（状态与临时区变化）。
    /// 同步标识面（块键 / 块 ID / 生命周期态 / 移除指令 / 两区换手）全部退役——前端零键算术、零配对、零排序；
    /// 后端内部槽位自持（流式容器与工具卡定位），槽位键不进协议。
    /// 「何时推、推给谁」归 HttpHost，「当前状态是什么」归本类；宿主推送面未附加时全部入口静默跳过。
    /// </summary>
    internal sealed class ViewBus
    {
        /// <summary>宿主推送面——Bootstrap 段6 宿主 HTTP 启动后 Attach 赋值（构造时宿主 HTTP 未启动）</summary>
        private IHostPush _host;

        /// <summary>持久区全表——只增序列（**降级路径**：全量数据源未注入时使用；无移除面）</summary>
        private readonly List<ViewBlock> _persist = new List<ViewBlock>();

        /// <summary>
        /// 全量数据源——会话侧注入（视图存储的合成出口 GetBlocks）。
        /// 🔴 单一真相源：全量帧每次**现取**，不依赖任何「启动时同步」——视图存储是唯一权威，
        /// 本类不持副本（副本必然与权威分叉：载入 / 清空 / 回滚三处都是分叉点）。
        /// 未注入时回落 `_persist`（降级，仅供测试与未接线场景）。
        /// </summary>
        private Func<ViewBlock[]> _fullSource;

        /// <summary>待推追加——自上次取帧以来的新增持久条目（取走即清）</summary>
        private readonly List<ViewBlock> _pendingAppend = new List<ViewBlock>();

        /// <summary>全量待发标记——连接建立之外的重置（新会话 / 清空前文）置位，帧轮广播全量帧</summary>
        private bool _fullPending;

        /// <summary>临时区表——内部槽位键 → 块（全量镜像数据源；槽位键不进协议）</summary>
        private readonly Dictionary<string, ViewBlock> _live = new Dictionary<string, ViewBlock>();

        /// <summary>临时区顺序——槽位键插入序（前端零排序：推送序即渲染序）</summary>
        private readonly List<string> _liveOrder = new List<string>();

        /// <summary>临时区变化标记——自上次取帧以来有增删改</summary>
        private bool _liveDirty;

        /// <summary>已定稿槽位集合——工具卡终态（中断补推的幂等判据）</summary>
        private readonly HashSet<string> _finaledSlots = new HashSet<string>();

        /// <summary>工具卡交接表——完成时刻 → 槽位键（持久卡以同时间戳落地即清临时面板项，面板 → 对话区无空窗）</summary>
        private readonly Dictionary<long, string> _handoverSlots = new Dictionary<long, string>();

        /// <summary>流式文本累积缓冲——契约 G：临时区全量镜像（每帧推累计全文）</summary>
        private readonly StringBuilder _textBuffer = new StringBuilder();

        /// <summary>流式思考累积缓冲——临时区全量镜像</summary>
        private readonly StringBuilder _reasonBuffer = new StringBuilder();

        /// <summary>流式文本槽位键——首增量分配后复用（空 = 不在途）</summary>
        private string _textSlot;

        /// <summary>流式思考槽位键——首增量分配后复用（空 = 不在途）</summary>
        private string _reasonSlot;

        /// <summary>槽位序号——内部槽位键分配计数</summary>
        private long _slotSeq;

        /// <summary>状态段 JSON——会话侧 SetState 供（后端权威业务态整段：轮阶段 / token / 前文长度与条数 / Note / 按钮可用性）</summary>
        private string _stateJson = "{}";

        /// <summary>状态段变化标记——整段比对，变化才推</summary>
        private bool _stateDirty;

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

        /// <summary>状态段更新——后端权威业务态整段（会话侧组装；与上帧相同则零动作）</summary>
        /// <param name="json">状态段 JSON（空 = 空对象）</param>
        public void SetState(string json)
        {
            string value = json == null || json.Length == 0 ? "{}" : json;
            if (value == _stateJson)
            {
                return;
            }
            _stateJson = value;
            _stateDirty = true;
        }

        /// <summary>全量帧——当前状态整段快照（连接建立首帧取一次；重置后由帧轮广播）</summary>
        /// <returns>载荷 JSON（{"v":2,"state":{…},"persist":{"mode":"full","items":[…]},"live":{"items":[…]}}）</returns>
        public string BuildFull()
        {
            string persistJson = JsonUtil.Object(
                ("mode", "full"),
                ("items", JsonUtil.RawArray(PersistItems())));
            string liveJson = JsonUtil.Object(
                ("items", JsonUtil.RawArray(LiveItems())));
            string stateJson = _stateJson == null || _stateJson.Length == 0 ? "{}" : _stateJson;
            _pendingAppend.Clear();
            _liveDirty = false;
            _stateDirty = false;
            _fullPending = false;
            return JsonUtil.Object(
                ("v", 2),
                ("state", JsonUtil.Raw(stateJson)),
                ("persist", JsonUtil.Raw(persistJson)),
                ("live", JsonUtil.Raw(liveJson)));
        }

        /// <summary>
        /// 取帧——帧轮唯一出口：无变化返回 false（零字节），有变化按内容组装（追加 / 状态 / 临时区可同帧承载）。
        /// 取走即清变更标记；全量待发优先（重置后整体重绘）。
        /// </summary>
        /// <param name="json">帧 JSON；无变化时为 null</param>
        /// <returns>true = 有帧可推</returns>
        public bool TryTakeFrame(out string json)
        {
            json = null;
            if (_host == null)
            {
                return false;
            }
            if (_fullPending)
            {
                _fullPending = false;
                json = BuildFull();
                return true;
            }
            if (_pendingAppend.Count == 0 && !_stateDirty && !_liveDirty)
            {
                return false;
            }
            List<string> parts = new List<string>();
            if (_pendingAppend.Count > 0)
            {
                ViewBlock[] appended = _pendingAppend.ToArray();
                _pendingAppend.Clear();
                string persistJson = JsonUtil.Object(
                    ("mode", "append"),
                    ("items", JsonUtil.RawArray(PersistItems(appended))));
                parts.Add("\"persist\":" + persistJson);
            }
            if (_stateDirty)
            {
                _stateDirty = false;
                string stateJson = _stateJson == null || _stateJson.Length == 0 ? "{}" : _stateJson;
                parts.Add("\"state\":" + stateJson);
            }
            if (_liveDirty)
            {
                _liveDirty = false;
                parts.Add("\"live\":" + JsonUtil.Object(("items", JsonUtil.RawArray(LiveItems()))));
            }
            json = "{" + string.Join(",", parts) + "}";
            return true;
        }

        /// <summary>
        /// 持久块记入——持久区建块即记（由 SessionViewStore 建块事件驱动，本类不建持久块）：只增，
        /// 记入追加队列（帧轮按「追加」模式推送）。
        /// 工具卡交接：持久工具卡以同时间戳落地即清对应临时面板项（同一次调用两态不在两区并存）。
        /// </summary>
        /// <param name="block">已定稿的持久块</param>
        public void PushPersist(ViewBlock block)
        {
            if (block == null || _host == null)
            {
                return;
            }
            _persist.Add(block);
            _pendingAppend.Add(block);
            if (block.RenderType == "toolcard")
            {
                string slot;
                if (_handoverSlots.TryGetValue(block.Timestamp, out slot))
                {
                    _handoverSlots.Remove(block.Timestamp);
                    RemoveLive(slot);
                }
            }
        }

        /// <summary>
        /// 注入全量数据源——会话侧接线（ChatSession.AttachHost）时调用一次。
        /// 全量帧（连接建立 / 重连 / 刷新）每次经此**现取**——不缓存、不同步、不分叉。
        /// </summary>
        /// <param name="source">全量块取数委托（视图存储合成出口）</param>
        public void AttachSource(Func<ViewBlock[]> source)
        {
            _fullSource = source;
        }

        /// <summary>流式文本推送——累计全文入临时区（契约 G：全量镜像，非增量；首推建容器，后续原位覆盖载荷）</summary>
        /// <param name="chunk">本帧增量片段（可为空——空片段仅刷新镜像）</param>
        public void PushTextStream(string chunk)
        {
            if (_host == null)
            {
                return;
            }
            if (chunk != null && chunk.Length > 0)
            {
                _textBuffer.Append(chunk);
            }
            string payload = JsonUtil.Object(("text", _textBuffer.ToString()));
            if (_textSlot == null)
            {
                _textSlot = NextSlot("text");
                AddLive(_textSlot, "stream.text", payload, NowMs());
                return;
            }
            UpdateLive(_textSlot, payload);
        }

        /// <summary>流式思考推送——累计全文入临时区（与流式文本同构，独立容器）</summary>
        /// <param name="chunk">本帧增量片段（可为空）</param>
        public void PushReasonStream(string chunk)
        {
            if (_host == null)
            {
                return;
            }
            if (chunk != null && chunk.Length > 0)
            {
                _reasonBuffer.Append(chunk);
            }
            string payload = JsonUtil.Object(("text", _reasonBuffer.ToString()));
            if (_reasonSlot == null)
            {
                _reasonSlot = NextSlot("reason");
                AddLive(_reasonSlot, "stream.reason", payload, NowMs());
                return;
            }
            UpdateLive(_reasonSlot, payload);
        }

        /// <summary>流式文本容器出区——整块到达 / 轮终（容器不在途时零动作）</summary>
        public void ResetTextStream()
        {
            if (_textSlot != null)
            {
                RemoveLive(_textSlot);
            }
            _textSlot = null;
            _textBuffer.Clear();
        }

        /// <summary>流式思考容器出区——思考段终结 / 轮终（容器不在途时零动作）</summary>
        public void ResetReasonStream()
        {
            if (_reasonSlot != null)
            {
                RemoveLive(_reasonSlot);
            }
            _reasonSlot = null;
            _reasonBuffer.Clear();
        }

        /// <summary>工具卡是否有在途临时卡——派发时判断该工具是否已入过「进行中」卡</summary>
        /// <param name="slotKey">内部槽位键（tool:&lt;toolCallId&gt;）</param>
        /// <returns>true = 有在途临时卡</returns>
        public bool IsToolCardPending(string slotKey)
        {
            if (slotKey == null)
            {
                return false;
            }
            return _live.ContainsKey(slotKey);
        }

        /// <summary>工具卡是否已定稿——中断补推与逐条回填的幂等判据（终态至多一次）</summary>
        /// <param name="slotKey">内部槽位键（tool:&lt;toolCallId&gt;）</param>
        /// <returns>true = 已定稿</returns>
        public bool IsToolCardFinaled(string slotKey)
        {
            if (slotKey == null)
            {
                return false;
            }
            return _finaledSlots.Contains(slotKey);
        }

        /// <summary>
        /// 工具卡「进行中」——建临时卡入面板区（同槽位重复推送为原位覆盖，不堆叠）。
        /// 槽位键 = tool:&lt;toolCallId&gt;（仅后端内部定位；协议面不带键）。
        /// </summary>
        /// <param name="slotKey">内部槽位键（tool:&lt;toolCallId&gt;）</param>
        /// <param name="json">载荷 JSON（name / arguments / toolIndex / toolTotal）</param>
        /// <param name="ts">块时间戳（Unix 毫秒——工具调用声明时刻，与持久卡同基点）</param>
        public void PushToolCardPending(string slotKey, string json, long ts)
        {
            if (_host == null || slotKey == null || slotKey.Length == 0)
            {
                return;
            }
            if (_live.ContainsKey(slotKey))
            {
                UpdateLive(slotKey, json);
                return;
            }
            AddLive(slotKey, "toolcard.pending", json, ts);
        }

        /// <summary>
        /// 工具卡终态——面板区原位覆盖（完成由 FlushToolCard 逐条回填 / 中断由收尾补推）；
        /// 记入已定稿集合（幂等）并登记交接时刻——持久卡以同时间戳落地时清面板项。
        /// </summary>
        /// <param name="slotKey">内部槽位键（tool:&lt;toolCallId&gt;）</param>
        /// <param name="json">载荷 JSON（含 result 与 durMs）</param>
        public void PushToolCardDone(string slotKey, string json)
        {
            if (_host == null || slotKey == null || slotKey.Length == 0)
            {
                return;
            }
            if (!_live.ContainsKey(slotKey))
            {
                AddLive(slotKey, "toolcard.pending", json, NowMs());
            }
            UpdateLive(slotKey, json);
            _finaledSlots.Add(slotKey);
            ViewBlock block;
            if (_live.TryGetValue(slotKey, out block))
            {
                _handoverSlots[block.Timestamp] = slotKey;
            }
        }

        /// <summary>临时区快照——当前面板区全部块（按插入序；QQ 转发与自查观测面取用）</summary>
        /// <returns>临时区块数组（按插入序）</returns>
        public ViewBlock[] GetLiveBlocks()
        {
            List<ViewBlock> list = new List<ViewBlock>();
            for (int i = 0; i < _liveOrder.Count; i = i + 1)
            {
                ViewBlock block;
                if (_live.TryGetValue(_liveOrder[i], out block))
                {
                    list.Add(block);
                }
            }
            return list.ToArray();
        }

        /// <summary>
        /// 状态清空——新会话 / 清空前文：持久区与临时区全清并置全量待发标记（帧轮广播全量帧，前端整体重绘空态）。
        /// 持久区只增的例外仅此一处：会话重置即新会话，旧条目不再属于本会话。
        /// </summary>
        public void ResetAll()
        {
            _persist.Clear();
            _pendingAppend.Clear();
            _live.Clear();
            _liveOrder.Clear();
            _finaledSlots.Clear();
            _handoverSlots.Clear();
            _textSlot = null;
            _reasonSlot = null;
            _textBuffer.Clear();
            _reasonBuffer.Clear();
            _fullPending = true;
        }

        /// <summary>
        /// 全量块集——**单一真相源优先**：注入的全量数据源（视图存储合成出口）→ 未注入时回落内部累积（降级）。
        /// 排序在此完成（前端零排序——推送序即渲染序）。
        /// </summary>
        /// <returns>全量持久块（时间戳升序）</returns>
        private ViewBlock[] FullBlocks()
        {
            ViewBlock[] all = null;
            if (_fullSource != null)
            {
                all = _fullSource();
            }
            if (all == null)
            {
                all = _persist.ToArray();
            }
            List<ViewBlock> list = new List<ViewBlock>(all);
            list.Sort(delegate (ViewBlock a, ViewBlock b)
            {
                return a.Timestamp.CompareTo(b.Timestamp);
            });
            return list.ToArray();
        }

        /// <summary>持久条目片段数组——全量数据源（单一真相源取数 + 时间戳升序）</summary>
        /// <returns>条目 JSON 片段数组</returns>
        private string[] PersistItems()
        {
            return PersistItems(FullBlocks());
        }

        /// <summary>持久条目片段数组——指定块集（追加帧数据源，保持入队序）</summary>
        /// <param name="blocks">块数组</param>
        /// <returns>条目 JSON 片段数组</returns>
        private static string[] PersistItems(ViewBlock[] blocks)
        {
            string[] result = new string[blocks.Length];
            for (int i = 0; i < blocks.Length; i = i + 1)
            {
                result[i] = ItemJson(blocks[i]);
            }
            return result;
        }

        /// <summary>临时条目片段数组——全量镜像（按插入序）</summary>
        /// <returns>条目 JSON 片段数组</returns>
        private string[] LiveItems()
        {
            List<string> result = new List<string>();
            for (int i = 0; i < _liveOrder.Count; i = i + 1)
            {
                ViewBlock block;
                if (_live.TryGetValue(_liveOrder[i], out block))
                {
                    result.Add(LiveItemJson(block));
                }
            }
            return result.ToArray();
        }

        /// <summary>持久条目序列化——{type, ts, msgIndex, round, payload}（无同步标识面）</summary>
        /// <param name="block">持久块</param>
        /// <returns>条目 JSON</returns>
        private static string ItemJson(ViewBlock block)
        {
            string payload = block.Payload == null || block.Payload.Length == 0 ? "null" : block.Payload;
            return JsonUtil.Object(
                ("type", block.RenderType == null ? "" : block.RenderType),
                ("ts", block.Timestamp),
                ("msgIndex", block.MsgIndex),
                ("round", block.Round),
                ("payload", JsonUtil.Raw(payload)));
        }

        /// <summary>临时条目序列化——{type, payload}（只带渲染所需；无定位字段）</summary>
        /// <param name="block">临时块</param>
        /// <returns>条目 JSON</returns>
        private static string LiveItemJson(ViewBlock block)
        {
            string payload = block.Payload == null || block.Payload.Length == 0 ? "null" : block.Payload;
            return JsonUtil.Object(
                ("type", block.RenderType == null ? "" : block.RenderType),
                ("payload", JsonUtil.Raw(payload)));
        }

        /// <summary>临时区入块——建块记入表尾 + 置变化标记</summary>
        /// <param name="slot">内部槽位键</param>
        /// <param name="renderType">渲染类型（临时族）</param>
        /// <param name="payload">载荷 JSON</param>
        /// <param name="ts">块时间戳（Unix 毫秒）</param>
        private void AddLive(string slot, string renderType, string payload, long ts)
        {
            ViewBlock block = new ViewBlock();
            block.RenderType = renderType;
            block.Payload = payload;
            block.Timestamp = ts;
            block.MsgIndex = -1;
            block.Round = 0;
            _live[slot] = block;
            _liveOrder.Add(slot);
            _liveDirty = true;
        }

        /// <summary>临时区内容变化——表内块原位覆盖载荷（不在表则零动作）+ 置变化标记</summary>
        /// <param name="slot">内部槽位键</param>
        /// <param name="payload">载荷 JSON</param>
        private void UpdateLive(string slot, string payload)
        {
            ViewBlock block;
            if (!_live.TryGetValue(slot, out block))
            {
                return;
            }
            block.Payload = payload;
            _liveDirty = true;
        }

        /// <summary>临时区出块——表内移除 + 摘除定稿记录 + 置变化标记（不在表则零动作）</summary>
        /// <param name="slot">内部槽位键</param>
        private void RemoveLive(string slot)
        {
            if (!_live.Remove(slot))
            {
                return;
            }
            _liveOrder.Remove(slot);
            _finaledSlots.Remove(slot);
            _liveDirty = true;
        }

        /// <summary>生成时刻——临时容器时间戳（Unix 毫秒；持久块时刻由调用方按事件时刻传入）</summary>
        /// <returns>当前墙钟毫秒</returns>
        private static long NowMs()
        {
            return System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        /// <summary>内部槽位键分配——自持计数（不导出协议）</summary>
        /// <param name="prefix">键前缀（text / reason）</param>
        /// <returns>槽位键</returns>
        private string NextSlot(string prefix)
        {
            _slotSeq = _slotSeq + 1;
            return prefix + ":" + _slotSeq.ToString();
        }
    }
}

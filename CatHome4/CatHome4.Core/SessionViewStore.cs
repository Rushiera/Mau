using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Mau.Runtime;
using CatHome4.Contracts;

namespace CH4
{
    /// <summary>
    /// 会话视图存储——视图层持久区（A156：与真实前文并列，载入即权威）。
    /// 块在事件发生点一次性定稿落盘（写入序即权威序）；载入读回持久块与独立块，不再从前文重建。
    /// 残留重建面只余两处：顶尾补差（AppendTailMissing）与对账哨兵（AuditOrigins），且不得改写已有块。
    /// </summary>
    internal sealed class SessionViewStore
    {
        /// <summary>视图文件路径——sessions/&lt;id&gt;/&lt;id&gt;.view.json</summary>
        private readonly string _path;

        /// <summary>内存视图块——按生成序（真实前文 append 序）</summary>
        private readonly List<ViewBlock> _blocks = new List<ViewBlock>();

        /// <summary>待配对工具——assistant 工具调用登记，tool 结果到达时生成工具卡块</summary>
        private readonly List<PendingTool> _pendingTools = new List<PendingTool>();
        /// <summary>
        /// 注入报告——session.new 时生成（独立字段：非真实前文派生，Rebuild 不清；Save 落盘）
        /// </summary>
        private string _injectReport = "";
        /// <summary>
        /// 轮末统计块——roundsum（每轮 CloseRound 生成：Token 消耗 + 四态用时；非真实前文派生，Rebuild 不清；Save 落盘，GetBlocks 按帧合并）
        /// </summary>
        private readonly List<ViewBlock> _roundSums = new List<ViewBlock>();
        /// <summary>间隙文本块——工具轮 seal 文本（非真实前文派生，Rebuild 不清；Save 落盘；QQBot 转发/前端历史数据源）</summary>
        private readonly List<ViewBlock> _gapTexts = new List<ViewBlock>();
        /// <summary>错误块——LLM 错误气泡（非真实前文派生，Rebuild 不清；写入即落盘——刷新可回看）</summary>
        private readonly List<ViewBlock> _errors = new List<ViewBlock>();
        /// <summary>重试过程块——retry 气泡（非真实前文派生；同一重试序列原位更新不堆叠；写入即落盘）</summary>
        private readonly List<ViewBlock> _retries = new List<ViewBlock>();
        /// <summary>废弃块——timeback 回收区间的合并归档块（非真实前文派生，Rebuild 不清；Save 落盘；前端「已废弃」气泡）</summary>
        private readonly List<ViewBlock> _voids = new List<ViewBlock>();

        /// <summary>最近块时间戳——单调补差基准（载入后按既有块最大值初始化；A156 I5）</summary>
        private long _lastTs;

        /// <summary>
        /// 块序代际号——任何块序变更（清除 / 轮统计清理 / 区间转废弃 / 截断）递增（A142）：
        /// 前端重连时带 gen + 已持有块数请求增量历史；gen 不匹配 = 前缀失效 → 回落全量重建。
        /// </summary>
        private int _blockGen;

        /// <summary>
        /// 块序变更通知——视图层唯一出声点（清除 / 轮统计清理 / 区间转废弃 / 截断四处变更点统一调用）：
        /// 转发面（QQ）订阅后按变更区间 / 内容锚点校正块游标（A111）。空=无消费方（无动作）。
        /// </summary>
        public Action<ViewOrderChange> OnBlocksReordered;

        /// <summary>
        /// 建块通知——块定稿入容器后调用（A158 期三：持久区「建块即推」的单一出口）。
        /// 消费方 = 会话侧接 ViewBus.PushPersist；前端语义「持久区多一块就渲一块」（只增不改、零配对）。
        /// 空 = 无消费方（无动作；视图层照常落盘）。
        /// </summary>
        public Action<ViewBlock> OnBlockAppended;

        /// <summary>
        /// 块序代际号——读（HTTP 增量历史口比对前缀有效性用）
        /// </summary>
        /// <returns>当前代际号（0 = 从未变更）</returns>
        public int GetBlockGen() { return _blockGen; }
        /// <summary>
        /// 注入报告 JSON——写（HandleSessionNew 生成后调用；空=无注入报告）
        /// </summary>
        /// <param name="json">注入报告 JSON（file/status/…）</param>
        public void SetInjectReport(string json) { _injectReport = json ?? ""; }
        /// <summary>
        /// 注入报告 JSON——读（前端渲染/历史重建数据源；空串=无注入报告）
        /// </summary>
        /// <returns>注入报告 JSON</returns>
        public string GetInjectReport() { return _injectReport; }

        /// <summary>待配对工具条目</summary>
        private sealed class PendingTool
        {
            /// <summary>工具调用 ID——与 tool 消息配对锚点</summary>
            public string ToolCallId;

            /// <summary>工具名</summary>
            public string Name;

            /// <summary>参数摘要（≤200）</summary>
            public string Arguments;

            /// <summary>并发序号（1-based——tool_calls 数组顺序）</summary>
            public int Index;

            /// <summary>并发总数（同批 tool_calls 数组长度）</summary>
            public int Total;

            /// <summary>声明时刻（Unix 毫秒）——工具卡块时间戳基准（A157：取工具调用声明消息 CreatedAt，与实时先行卡同基点）</summary>
            public long Ts;
        }

        /// <summary>视图文件数据——JSON 形态（blocks 按到达序）</summary>
        private sealed class ViewFileData
        {
            /// <summary>协议版本</summary>
            public int Version { get; set; }

            /// <summary>会话 ID</summary>
            public string SessionId { get; set; }

            /// <summary>视图块数组（写入序——事件直落，载入即权威；A156）</summary>
            public ViewBlock[] Blocks { get; set; }

            /// <summary>注入报告 JSON——会话元数据（非真实前文派生；Rebuild 不清，Save 落盘）</summary>
            public string InjectReport { get; set; }

            /// <summary>间隙文本块数组——gap text（非真实前文派生；Rebuild 不清，Save 落盘，Load 恢复）</summary>
            public ViewBlock[] GapTexts { get; set; }

            /// <summary>轮末统计块数组——roundsum（非真实前文派生；Rebuild 不清，Save 落盘，Load 恢复）</summary>
            public ViewBlock[] RoundSums { get; set; }

            /// <summary>错误块数组——error（非真实前文派生；Rebuild 不清，Save 落盘，Load 恢复）</summary>
            public ViewBlock[] Errors { get; set; }

            /// <summary>重试过程块数组——retry（非真实前文派生；Rebuild 不清，Save 落盘，Load 恢复）</summary>
            public ViewBlock[] Retries { get; set; }

            /// <summary>废弃块数组——void（timeback 回收区间合并归档；非真实前文派生；Rebuild 不清，Save 落盘，Load 恢复）</summary>
            public ViewBlock[] Voids { get; set; }
        }

        /// <summary>
        /// 建立会话视图存储
        /// </summary>
        /// <param name="path">视图文件路径</param>
        public SessionViewStore(string path)
        {
            _path = path;
        }

        /// <summary>
        /// 块序变更通知——变更前快照与当前块序取最小差异（公共前缀 / 公共后缀之外即变更区间）后发出（A111：校正入口单一——四处变更点共用）。
        /// 无消费方（未接线）或块序未变（差异为零）→ 零动作。
        /// </summary>
        /// <param name="before">变更前的合并视图块数组（调用方在变更前取快照）</param>
        public void NotifyBlocksReordered(ViewBlock[] before)
        {
            // A142——代际号无条件递增（与消费方接线无关；块序未变的空转也计一次，保守不出错）
            _blockGen = _blockGen + 1;
            Action<ViewOrderChange> handler = OnBlocksReordered;
            if (handler == null || before == null)
            {
                return;
            }
            ViewBlock[] after = GetBlocks();
            // [段1] 公共前缀——头部保留的未变块
            int prefix = 0;
            while (prefix < before.Length && prefix < after.Length && SameBlock(before[prefix], after[prefix]))
            {
                prefix = prefix + 1;
            }
            // [段2] 公共后缀——尾部保留的未变块（前缀区之后才参与）
            int suffix = 0;
            while (suffix < before.Length - prefix && suffix < after.Length - prefix
                && SameBlock(before[before.Length - 1 - suffix], after[after.Length - 1 - suffix]))
            {
                suffix = suffix + 1;
            }
            int removed = before.Length - prefix - suffix;
            int added = after.Length - prefix - suffix;
            if (removed == 0 && added == 0)
            {
                return;
            }
            ViewOrderChange change = new ViewOrderChange();
            change.From = prefix;
            change.RemovedCount = removed;
            change.AddedCount = added;
            handler(change);
        }

        /// <summary>块同一判定——哈希 + 时间戳 + 渲染类型三者相同才算同一块（变更比对口径；载荷不参与——注入报告内容变化不影响块序）</summary>
        private static bool SameBlock(ViewBlock a, ViewBlock b)
        {
            if (a == null || b == null)
            {
                return false;
            }
            string hashA = a.Origin == null ? "" : a.Origin.Hash;
            string hashB = b.Origin == null ? "" : b.Origin.Hash;
            if (hashA != hashB || a.Timestamp != b.Timestamp || a.RenderType != b.RenderType)
            {
                return false;
            }
            return true;
        }

        /// <summary>内存视图块——按生成序（history 数据源）</summary>
        public ViewBlock[] GetBlocks()
        {
            // 合并面——真实前文块 + 间隙文本块 + roundsum 轮末统计块 + error 错误块 + retry 重试块 + void 废弃块（按时间戳升序——同一坐标系：消息 CreatedAt / CloseRound 时刻）
            List<ViewBlock>[] sources = new List<ViewBlock>[] { _blocks, _gapTexts, _roundSums, _errors, _retries, _voids };
            int total = 0;
            for (int si = 0; si < sources.Length; si = si + 1)
            {
                total = total + sources[si].Count;
            }
            ViewBlock[] merged = new ViewBlock[total];
            int[] cursors = new int[sources.Length];
            for (int mi = 0; mi < total; mi = mi + 1)
            {
                int best = -1;
                long bestTs = long.MaxValue;
                for (int si = 0; si < sources.Length; si = si + 1)
                {
                    if (cursors[si] >= sources[si].Count)
                    {
                        continue;
                    }
                    long ts = sources[si][cursors[si]].Timestamp;
                    if (best < 0 || ts < bestTs)
                    {
                        best = si;
                        bestTs = ts;
                    }
                }
                merged[mi] = sources[best][cursors[best]];
                cursors[best] = cursors[best] + 1;
            }
            // 注入报告合成首块——会话元数据（非真实前文派生；前端首块渲染前文加载明细）
            if (_injectReport.Length > 0)
            {
                ViewBlock[] withReport = new ViewBlock[merged.Length + 1];
                ViewBlock report = NewBlock("inject_report", "inject_report", _injectReport, 0, null, "independent", -1, false);
                withReport[0] = report;
                for (int i = 0; i < merged.Length; i = i + 1)
                {
                    withReport[i + 1] = merged[i];
                }
                return withReport;
            }
            return merged;
        }        /// <summary>
                 /// 真实前文 append 钩子——用户消息 → user 块
                 /// </summary>
                 /// <param name="m">真实前文消息</param>
                 /// <param name="msgIndex">真实前文消息索引（块来源关系字段）</param>
        public void OnUserMessage(LlmMessage m, int msgIndex)
        {
            Dictionary<string, object> payload = new Dictionary<string, object>();
            payload["content"] = m.Content ?? "";
            Append(m, "user", payload, msgIndex);
        }

        /// <summary>
        /// 真实前文 append 钩子——assistant 纯文本回复 → reason 块（有思考时，先落）+ text 块
        /// </summary>
        /// <param name="m">真实前文消息</param>
        /// <param name="msgIndex">真实前文消息索引（块来源关系字段）</param>
        public void OnAssistantText(LlmMessage m, int msgIndex)
        {
            // A158 期三——纯文本轮的思考段落块（工具轮走 OnAssistantToolCalls；缺此路径思考内容在持久区丢失）
            string reasoning = m.ReasoningContent ?? "";
            if (reasoning.Length > 0)
            {
                Dictionary<string, object> reasonPayload = new Dictionary<string, object>();
                reasonPayload["content"] = reasoning;
                Append(m, "reason", reasonPayload, msgIndex);
            }
            Dictionary<string, object> payload = new Dictionary<string, object>();
            payload["content"] = m.Content ?? "";
            Append(m, "text", payload, msgIndex);
        }

        /// <summary>
        /// 真实前文 append 钩子——assistant 工具调用声明 → reason 块（有思考时）+ 登记待配对工具
        /// </summary>
        /// <param name="m">真实前文消息</param>
        /// <param name="msgIndex">真实前文消息索引（块来源关系字段）</param>
        public void OnAssistantToolCalls(LlmMessage m, int msgIndex)
        {
            string reasoning = m.ReasoningContent ?? "";
            if (reasoning.Length > 0)
            {
                Dictionary<string, object> payload = new Dictionary<string, object>();
                payload["content"] = reasoning;
                Append(m, "reason", payload, msgIndex);
            }
            RegisterPendingTools(m.ToolCallsJson ?? "", m.CreatedAt);
        }

        /// <summary>
        /// 真实前文 append 钩子——tool 结果 → 配对生成工具卡块（孤立 tool 丢弃——视图容错）。
        /// A157：块时间戳取工具调用声明时刻（target.Ts），与实时区先行卡同基点。
        /// </summary>
        /// <param name="m">真实前文消息</param>
        /// <param name="msgIndex">真实前文消息索引（块来源关系字段）</param>
        /// <param name="durMs">运行时长（毫秒；-1 = 未记录——由会话侧按工具单派发时刻结算）</param>
        public void OnToolResult(LlmMessage m, int msgIndex, long durMs)
        {
            string toolCallId = m.ToolCallId ?? "";
            PendingTool target = null;
            for (int i = _pendingTools.Count - 1; i >= 0; i = i - 1)
            {
                if (_pendingTools[i].ToolCallId == toolCallId)
                {
                    target = _pendingTools[i];
                    _pendingTools.RemoveAt(i);
                    break;
                }
            }
            if (target == null)
            {
                return; // 孤立 tool 丢弃
            }
            string name = target.Name.Length > 0 ? target.Name : (m.ToolName ?? "");
            // A69 视图层报错中文注释——真实前文保持原文，仅视图块追加中文注释
            string viewResult = ErrorNote.Apply(m.Content ?? "");
            Dictionary<string, object> payload = ViewCardPayload.BuildToolCard(name, target.Arguments, viewResult, target.Index, target.Total);
            Append(m, "toolcard", payload, msgIndex, durMs, target.Ts);
        }

        /// <summary>
        /// 区间块转废弃块（timeback 回收）——区间内全部视图块**合并为一个**废弃块，从对话流移出进独立容器（非前文派生，Rebuild 不清：跨重启仍可回看）。
        /// 语义（莎 2026-09-29）：这些内容已被主干排除（前文面不再含），前端只是「能看」——故不进对话流、不参与 QQ 转发（renderType≠text），独立块型 `void`。
        /// 移出面由调用方按语义给界（timeback 取「锚定声明之后」——比前文删除区间更宽：锚定批的 sibling 结果卡一并移出）；
        /// `keepToolName` 指定工具名的工具卡**保留在对话流**（timeback 自己的锚定卡——三段式「锚定 → 废弃段 → 回收」的锚）。
        /// 内容按块型组装（user / text / reason 取正文；toolcard 取「工具名 + 参数 + 结果」）；块时间戳取移出块中最早者（归并排序位不变）。
        /// </summary>
        /// <param name="fromMsgIndex">区间下界（真实前文消息索引，含）</param>
        /// <param name="toMsgIndex">区间上界（含）</param>
        /// <param name="keepToolName">区间内保留不动的工具卡名（空=不保留）</param>
        /// <returns>移出的视图块数（无命中 = 0，零动作）</returns>
        public int ConvertRangeToVoid(int fromMsgIndex, int toMsgIndex, string keepToolName)
        {
            // A111——块序变更快照（区间块移出 + 合并归档块插入：转发面游标按区间平移 / 锚点重定位）
            ViewBlock[] before = GetBlocks();
            List<ViewBlock> removed = new List<ViewBlock>();
            for (int i = _blocks.Count - 1; i >= 0; i = i - 1)
            {
                ViewBlock b = _blocks[i];
                if (b.Origin == null || b.Origin.MsgIndex < fromMsgIndex || b.Origin.MsgIndex > toMsgIndex)
                {
                    continue;
                }
                // 保留面——锚定/回收卡不走废弃段（三段式的锚；判据 = 工具卡名）
                if (keepToolName != null && keepToolName.Length > 0 && IsToolCardOf(b, keepToolName))
                {
                    continue;
                }
                removed.Insert(0, b);   // 逆序扫描——插回头部保持原生成序
                _blocks.RemoveAt(i);
            }
            int moved = removed.Count;
            if (moved == 0)
            {
                return 0;
            }
            StringBuilder body = new StringBuilder();
            for (int i = 0; i < removed.Count; i = i + 1)
            {
                string text = BuildGapContent(removed[i]);
                if (text.Length == 0)
                {
                    continue;
                }
                if (body.Length > 0)
                {
                    body.Append("\n\n");
                }
                body.Append(text);
            }
            List<string> movedHashes = new List<string>();
            for (int i = 0; i < removed.Count; i = i + 1)
            {
                string movedHash = removed[i].Origin == null ? "" : removed[i].Origin.Hash;
                movedHashes.Add(movedHash == null ? "" : movedHash);
            }
            string voidPayload = JsonUtil.Object(
                ("count", moved),
                ("text", body.ToString()),
                ("hashes", JsonUtil.Raw(JsonUtil.Array(movedHashes.ToArray()))));
            ViewBlock block = NewBlock("void:" + _voids.Count.ToString(), "void", voidPayload, removed[0].Timestamp, null, "independent", -1, false);
            _voids.Add(block);
            Save();
            EmitBlock(block);
            NotifyBlocksReordered(before);
            return moved;
        }
        // A156：PurgeVoidedBlocks 已退役——视图层不再从前文重建（载入即权威），
        // 被移出的块不会在前文重放时复活，无需按废弃段哈希过滤。

        // A156：ExtractVoidHashes 随 PurgeVoidedBlocks 一并退役（void 块 hashes 载荷字段保留作留档信息）。

        /// <summary>
        /// 组装 gap 文本——按块型取正文（toolcard 走名称 + 参数 + 结果；其余取 payload.content；解析失败回落原文）。
        /// </summary>
        /// <param name="b">源视图块</param>
        /// <returns>gap 文本（空串=无正文可用）</returns>
        private static string BuildGapContent(ViewBlock b)
        {
            string type = b.RenderType == null ? "" : b.RenderType;
            string payloadJson = b.Payload == null ? "" : b.Payload;
            if (payloadJson.Length == 0)
            {
                return "";
            }
            try
            {
                using (JsonDocument doc = JsonUtil.ParseStrict(payloadJson))
                {
                    JsonElement root = doc.RootElement;
                    if (type == "toolcard")
                    {
                        JsonElement nameEl;
                        JsonElement argsEl;
                        JsonElement resultEl;
                        string name = root.TryGetProperty("name", out nameEl) && nameEl.ValueKind == JsonValueKind.String ? nameEl.GetString() ?? "" : "工具";
                        string args = root.TryGetProperty("arguments", out argsEl) && argsEl.ValueKind == JsonValueKind.String ? argsEl.GetString() ?? "" : "";
                        string result = root.TryGetProperty("result", out resultEl) && resultEl.ValueKind == JsonValueKind.String ? resultEl.GetString() ?? "" : "";
                        return "【工具 · " + name + "】\n参数：" + args + "\n结果：" + result;
                    }
                    JsonElement contentEl;
                    if (root.TryGetProperty("content", out contentEl) && contentEl.ValueKind == JsonValueKind.String)
                    {
                        return contentEl.GetString() ?? "";
                    }
                }
                return "";
            }
            catch (Exception)
            {
                return payloadJson;
            }
        }

        /// <summary>
        /// 顶尾补差——从最后一块的前文来源之后重放缺失消息（A156：视图层写失败 / 中断留下的缺口修复）。
        /// 判据：只补尾部缺口——不重置已有块、不追改历史（无既有块时不做全量重放：旧数据不迁移）。
        /// </summary>
        /// <param name="messages">真实前文消息数组</param>
        /// <returns>补入的块数（0 = 无缺口 / 无锚不可补）</returns>
        public int AppendTailMissing(LlmMessage[] messages)
        {
            int from = LastOriginMsgIndex() + 1;
            if (from <= 0)
            {
                return 0;
            }
            int added = 0;
            for (int i = from; i < messages.Length; i++)
            {
                LlmMessage m = messages[i];
                if (m.Role == LlmRole.System)
                {
                    continue;
                }
                if (m.Role == LlmRole.User)
                {
                    OnUserMessage(m, i);
                    added = added + 1;
                    continue;
                }
                if (m.Role == LlmRole.Assistant)
                {
                    string toolCalls = m.ToolCallsJson ?? "";
                    if (toolCalls.Length > 0)
                    {
                        OnAssistantToolCalls(m, i);
                    }
                    else
                    {
                        OnAssistantText(m, i);
                    }
                    added = added + 1;
                    continue;
                }
                if (m.Role == LlmRole.Tool)
                {
                    OnToolResult(m, i, -1);
                    added = added + 1;
                }
            }
            NotifyBlocksReordered(null);
            return added;
        }

        /// <summary>
        /// 最后一块的前文来源消息索引——补差锚（-1 = 无前文派生块）。
        /// </summary>
        /// <returns>消息索引（-1 = 无）</returns>
        private int LastOriginMsgIndex()
        {
            int best = -1;
            for (int i = 0; i < _blocks.Count; i = i + 1)
            {
                ViewBlock b = _blocks[i];
                if (b.Origin != null && b.Origin.MsgIndex > best)
                {
                    best = b.Origin.MsgIndex;
                }
            }
            return best;
        }

        /// <summary>
        /// 视图文件落盘——全量覆写（CloseRound 与真实前文同批；流式中间态不含）
        /// </summary>
        public void Save()
        {
            try
            {
                string dir = Path.GetDirectoryName(_path);
                if (dir != null && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                ViewFileData data = new ViewFileData();
                data.Version = 2;
                data.SessionId = "";
                data.Blocks = _blocks.ToArray();
                data.InjectReport = _injectReport;
                data.GapTexts = _gapTexts.ToArray();
                data.RoundSums = _roundSums.ToArray();
                data.Errors = _errors.ToArray();
                data.Retries = _retries.ToArray();
                data.Voids = _voids.ToArray();
                JsonSerializerOptions options = new JsonSerializerOptions();
                options.IncludeFields = true;
                options.Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping;   // 中文直出（默认 \uXXXX 转义人读不便——2026-09-08 全局统一）
                string json = JsonSerializer.Serialize(data, options);
                File.WriteAllText(_path, json);
            }
            catch (Exception ex)
            {
                // A156：视图层为权威（不再可从前文重建）——保存失败必须可见 + 落待补标记（下次载入顶尾补差）
                LogStore.Add("SessionViewStore", 3, "视图保存失败（已落待补标记，下次载入顶尾补差）: " + ex.Message, "SYS");
                MarkPendingGap(ex.Message);
            }
        }

        /// <summary>
        /// 落待补标记——视图保存失败留痕（A156：视图层为权威，静默失败不可接受）。
        /// 标记文件 = view 文件同址 + ".pending"；载入时检出即顶尾补差并清除。
        /// </summary>
        /// <param name="reason">失败原因</param>
        private void MarkPendingGap(string reason)
        {
            try
            {
                File.WriteAllText(_path + ".pending", reason == null ? "" : reason);
            }
            catch (Exception ex)
            {
                LogStore.Add("SessionViewStore", 2, "待补标记写入失败: " + ex.Message, "SYS");
            }
        }

        /// <summary>
        /// 待补标记路径——保存失败留痕文件（载入时据此触发顶尾补差）。
        /// </summary>
        /// <returns>标记文件路径</returns>
        public string PendingGapPath()
        {
            return _path + ".pending";
        }        /// <summary>追加轮末统计块——roundsum（Token 消耗 + 工具次数 + 总耗时 + 四态用时）。非真实前文派生（Rebuild 不清）；写入即落盘——宿主中断不丢。</summary>
                 /// <param name="payloadJson">roundsum 载荷 JSON（{"type":"roundsum","data":{...}}）</param>
                 /// <param name="timestamp">创建时间戳（Unix 毫秒——与消息块同坐标系，归并排序键）</param>
                 /// <returns>块键（A157：实时推送沿用同一键）</returns>
        public string AppendRoundSummary(string payloadJson, long timestamp)
        {
            string key = "roundsum:" + _roundSums.Count.ToString();
            ViewBlock block = NewBlock(key, "roundsum", payloadJson, timestamp, null, "independent", -1);
            _roundSums.Add(block);
            Save();
            EmitBlock(block);
            return key;
        }
        /// <summary>追加间隙文本块——工具轮 seal 文本（模型调用工具前说的话）。非真实前文派生（Rebuild 不清）；写入即落盘——工具轮中途中断不丢。视图层 = 全部外观真源——前端历史/QQBot 转发统一消费此块。</summary>
        /// <param name="content">间隙文本</param>
        /// <param name="timestamp">创建时间戳（Unix 毫秒——与消息块同坐标系）</param>
        /// <returns>块键（A157：实时推送沿用同一键；空内容 = 空串）</returns>
        public string AppendGapText(string content, long timestamp)
        {
            if (content == null || content.Length == 0)
            {
                return "";
            }
            Dictionary<string, object> payload = new Dictionary<string, object>();
            payload["content"] = content;
            string key = "gap:" + _gapTexts.Count.ToString();
            ViewBlock block = NewBlock(key, "text", JsonUtil.Serialize(payload), timestamp, null, "independent", -1);
            _gapTexts.Add(block);
            Save();
            EmitBlock(block);
            return key;
        }

        /// <summary>
        /// 追加错误块——LLM 错误气泡（非真实前文派生；写入即落盘——刷新可回看）。
        /// </summary>
        /// <param name="text">错误文本</param>
        /// <param name="timestamp">创建时间戳（Unix 毫秒——与消息块同坐标系）</param>
        /// <returns>块键（A157：实时推送沿用同一键；空文本 = 空串）</returns>
        public string AppendError(string text, long timestamp)
        {
            if (text == null || text.Length == 0)
            {
                return "";
            }
            Dictionary<string, object> payload = new Dictionary<string, object>();
            payload["type"] = "error";
            payload["text"] = text;
            string key = "error:" + _errors.Count.ToString();
            ViewBlock block = NewBlock(key, "error", JsonUtil.Serialize(payload), timestamp, null, "independent", -1);
            _errors.Add(block);
            Save();
            EmitBlock(block);
            return key;
        }

        /// <summary>
        /// 追加重试块——retry 气泡（A158 期三：只增不改——「重试中 / 成功 / 失败」各推一块，不做原位更新）。
        /// </summary>
        /// <param name="payloadJson">retry 载荷 JSON（state/attempt/max/text）</param>
        /// <param name="timestamp">创建时间戳（Unix 毫秒）</param>
        /// <returns>块键</returns>
        public string AppendRetry(string payloadJson, long timestamp)
        {
            string key = "retry:" + _retries.Count.ToString();
            ViewBlock block = NewBlock(key, "retry", payloadJson, timestamp, null, "independent", -1);
            _retries.Add(block);
            Save();
            EmitBlock(block);
            return key;
        }
        /// <summary>文件名清洗——Windows 非法文件名字符替换为下划线并去首尾空白（显示名可含中文与空格）</summary>
        /// <param name="name">原始名（猫显示名 / 猫 key）</param>
        /// <returns>可作文件名前缀的串（空=原名缺失）</returns>
        private static string SanitizeFileName(string name)
        {
            if (name == null)
            {
                return "";
            }
            char[] invalid = Path.GetInvalidFileNameChars();
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < name.Length; i = i + 1)
            {
                char c = name[i];
                bool bad = false;
                for (int j = 0; j < invalid.Length; j = j + 1)
                {
                    if (invalid[j] == c)
                    {
                        bad = true;
                        break;
                    }
                }
                if (bad)
                {
                    sb.Append('_');
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString().Trim();
        }

        /// <summary>
        /// 旧会话留档——session.new 清空前导出（A87：user 消息 / 正式回复 / 注入报告 / 每轮结算 → sessions_old 下 MD 文件）。
        /// 只留四部分（思考块 / 工具卡 / 间隙文本 / 错误 / 重试一律不进档）；四部分全空也照常出档（档案面留痕优先于体积）。
        /// </summary>
        /// <param name="catKey">猫 key——文件名前缀（与会话目录名同源）</param>
        /// <param name="displayName">会话显示名——写入文件头</param>
        /// <returns>落盘文件绝对路径（空串=未生成；失败已记 ERR 日志）</returns>
        public string ArchiveLegacy(string catKey, string displayName)
        {
            try
            {
                // [段1] 落点——sessions_old 与 sessions 同级（视图路径形态 <data>/sessions/<id>/<id>.view.json）
                string sessionDir = Path.GetDirectoryName(_path);
                string sessionsRoot = null;
                if (sessionDir != null && sessionDir.Length > 0)
                {
                    sessionsRoot = Path.GetDirectoryName(sessionDir);
                }
                string dataDir = null;
                if (sessionsRoot != null && sessionsRoot.Length > 0 && Path.GetFileName(sessionsRoot) == "sessions")
                {
                    dataDir = Path.GetDirectoryName(sessionsRoot);
                }
                if (dataDir == null || dataDir.Length == 0)
                {
                    LogStore.Add("CatHome4", 3, "旧会话留档跳过：视图路径形态不符 " + _path, "CHAT");
                    return "";
                }
                string archiveDir = Path.Combine(dataDir, "sessions_old");
                if (!Directory.Exists(archiveDir))
                {
                    Directory.CreateDirectory(archiveDir);
                }
                // [段2] 文件名——猫显示名 + 归档时间戳（非法字符清洗；显示名缺省回落猫 key；同秒重复触发加序号，不覆盖既有档案）
                string prefix = SanitizeFileName(displayName);
                if (prefix.Length == 0)
                {
                    prefix = SanitizeFileName(catKey);
                }
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string file = Path.Combine(archiveDir, prefix + "_" + stamp + ".md");
                int suffix = 1;
                while (File.Exists(file))
                {
                    suffix = suffix + 1;
                    file = Path.Combine(archiveDir, prefix + "_" + stamp + "_" + suffix.ToString() + ".md");
                }
                // [段3] 生成 + 落盘（.md 契约：UTF-8 BOM；换行由写侧保真）
                string text = BuildLegacyMarkdown(catKey, displayName, stamp);
                File.WriteAllText(file, text, new UTF8Encoding(true));
                return file;
            }
            catch (Exception ex)
            {
                // 留档失败不阻断新会话（失败可见——ERR 日志 + 空返回）
                LogStore.Add("CatHome4", 3, "旧会话留档失败: " + ex.Message, "CHAT");
                return "";
            }
        }

        /// <summary>
        /// 生成留档 Markdown——文件头 + 新会话加载报告 + 对话时序（user / 正式回复 / 轮结算三类块，按时间戳归并）。
        /// </summary>
        /// <param name="catKey">猫 key</param>
        /// <param name="displayName">会话显示名</param>
        /// <param name="stamp">归档时间戳（文件名同源）</param>
        /// <returns>Markdown 全文</returns>
        private string BuildLegacyMarkdown(string catKey, string displayName, string stamp)
        {
            // [段1] 对话段——两源各自按时间戳升序，手工归并（_blocks 只取 user/text，roundsum 全取）
            StringBuilder body = new StringBuilder();
            long firstTs = 0;
            long lastTs = 0;
            bool hasTs = false;
            int userCount = 0;
            int replyCount = 0;
            int blockCursor = 0;
            int sumCursor = 0;
            while (blockCursor < _blocks.Count || sumCursor < _roundSums.Count)
            {
                while (blockCursor < _blocks.Count && !IsLegacyBlock(_blocks[blockCursor].RenderType))
                {
                    blockCursor = blockCursor + 1;
                }
                bool takeBlock = false;
                if (blockCursor >= _blocks.Count)
                {
                    takeBlock = false;
                }
                else if (sumCursor >= _roundSums.Count)
                {
                    takeBlock = true;
                }
                else if (_blocks[blockCursor].Timestamp <= _roundSums[sumCursor].Timestamp)
                {
                    takeBlock = true;
                }
                ViewBlock current;
                if (takeBlock)
                {
                    current = _blocks[blockCursor];
                    blockCursor = blockCursor + 1;
                }
                else
                {
                    current = _roundSums[sumCursor];
                    sumCursor = sumCursor + 1;
                }
                if (current.RenderType == "user")
                {
                    userCount = userCount + 1;
                }
                else if (current.RenderType == "text")
                {
                    replyCount = replyCount + 1;
                }
                if (!hasTs || current.Timestamp < firstTs)
                {
                    firstTs = current.Timestamp;
                }
                hasTs = true;
                if (current.Timestamp > lastTs)
                {
                    lastTs = current.Timestamp;
                }
                AppendLegacyBlock(body, current);
            }
            // [段2] 头部——猫 / 归档时刻 / 区间 / 块数（无时间块时区间记「—」）
            string range = "—";
            if (hasTs)
            {
                range = FormatLegacyTime(firstTs) + " ~ " + FormatLegacyTime(lastTs);
            }
            StringBuilder sb = new StringBuilder();
            sb.Append("# 会话留档 — " + displayName + "\n\n");
            sb.Append("- 猫：`" + catKey + "`（" + displayName + "）\n");
            sb.Append("- 归档：" + stamp + "\n");
            sb.Append("- 区间：" + range + "\n");
            sb.Append("- 块数：user " + userCount.ToString() + " · 回复 " + replyCount.ToString() + " · 轮结算 " + _roundSums.Count.ToString() + "\n\n");
            sb.Append("---\n\n");
            // [段3] 加载报告段 + 对话段
            AppendInjectReportSection(sb, _injectReport);
            sb.Append("---\n\n");
            sb.Append("## 对话\n\n");
            sb.Append(body.ToString());
            return sb.ToString();
        }

        /// <summary>留档入选判定——user 消息与正式回复（text）进档，其余块型一律丢弃</summary>
        /// <param name="renderType">块渲染类型</param>
        /// <returns>true=进档</returns>
        private static bool IsLegacyBlock(string renderType)
        {
            if (renderType == null)
            {
                return false;
            }
            return renderType == "user" || renderType == "text";
        }

        /// <summary>留档块渲染——按块型分派（user/text 取 content；roundsum 转简洁统计行）</summary>
        /// <param name="sb">目标缓冲</param>
        /// <param name="b">待渲染块</param>
        private static void AppendLegacyBlock(StringBuilder sb, ViewBlock b)
        {
            string type = b.RenderType;
            if (type == null)
            {
                type = "";
            }
            string title = "用户";
            if (type == "text")
            {
                title = "回复";
            }
            else if (type == "roundsum")
            {
                title = "轮结算";
            }
            sb.Append("### " + FormatLegacyTime(b.Timestamp) + " · " + title + "\n\n");
            if (type == "roundsum")
            {
                sb.Append(BuildRoundSumLine(b.Payload) + "\n\n");
                return;
            }
            string content = "";
            try
            {
                using (JsonDocument doc = JsonUtil.ParseStrict(b.Payload))
                {
                    content = GetStringProp(doc.RootElement, "content");
                }
            }
            catch (Exception ex)
            {
                // 单块解析失败不拖垮整份留档（局部降级可见——不静默丢块）
                LogStore.Add("CatHome4", 2, "旧会话留档块解析失败: " + ex.Message, "CHAT");
            }
            sb.Append(content + "\n\n");
        }

        /// <summary>轮结算行渲染——roundsum 载荷转人读一行（Token / 缓存 / 工具次数 / 请求次数 / 用时）</summary>
        /// <param name="payloadJson">roundsum 载荷 JSON</param>
        /// <returns>单行文本</returns>
        private static string BuildRoundSumLine(string payloadJson)
        {
            try
            {
                using (JsonDocument doc = JsonUtil.ParseStrict(payloadJson))
                {
                    JsonElement root = doc.RootElement;
                    JsonElement data;
                    if (!root.TryGetProperty("data", out data))
                    {
                        return "（载荷缺 data 字段）";
                    }
                    long prompt = ReadLongProp(data, "prompt");
                    long completion = ReadLongProp(data, "completion");
                    long cacheHit = ReadLongProp(data, "cacheHit");
                    long toolCount = ReadLongProp(data, "toolCount");
                    long requests = ReadLongProp(data, "requests");
                    long elapsedMs = ReadLongProp(data, "elapsedMs");
                    double seconds = elapsedMs / 1000.0;
                    return "Token 上 " + prompt.ToString() + " / 下 " + completion.ToString()
                        + "（缓存 " + cacheHit.ToString() + "）· 工具 " + toolCount.ToString() + " 次"
                        + " · 请求 " + requests.ToString() + " 次 · 用时 " + seconds.ToString("0.0") + " 秒";
                }
            }
            catch (Exception ex)
            {
                // 解析失败降级为原文（不静默丢统计）
                LogStore.Add("CatHome4", 2, "旧会话留档结算解析失败: " + ex.Message, "CHAT");
                return "（解析失败）" + payloadJson;
            }
        }

        /// <summary>加载报告段渲染——旧会话注入报告（逐文件清单 + 工具组清单）；无报告记一行，解析失败降级为原文</summary>
        /// <param name="sb">目标缓冲</param>
        /// <param name="injectReport">注入报告 JSON（空=无）</param>
        private static void AppendInjectReportSection(StringBuilder sb, string injectReport)
        {
            sb.Append("## 新会话加载报告\n\n");
            if (injectReport == null || injectReport.Length == 0)
            {
                sb.Append("- 无（旧会话未生成注入报告）\n\n");
                return;
            }
            try
            {
                using (JsonDocument doc = JsonUtil.ParseStrict(injectReport))
                {
                    JsonElement root = doc.RootElement;
                    long total = ReadLongProp(root, "total");
                    long ok = ReadLongProp(root, "ok");
                    long missing = ReadLongProp(root, "missing");
                    long failed = ReadLongProp(root, "failed");
                    sb.Append("- 注入清单：" + total.ToString() + " 个文件（ok " + ok.ToString()
                        + " / missing " + missing.ToString() + " / 失败 " + failed.ToString() + "）\n");
                    JsonElement files;
                    if (root.TryGetProperty("files", out files) && files.ValueKind == JsonValueKind.Array)
                    {
                        sb.Append("\n");
                        foreach (JsonElement f in files.EnumerateArray())
                        {
                            sb.Append("- `" + GetStringProp(f, "file") + "` — " + GetStringProp(f, "status")
                                + "（" + ReadLongProp(f, "chars").ToString() + " 字符）\n");
                        }
                    }
                    JsonElement groups;
                    if (root.TryGetProperty("toolGroups", out groups) && groups.ValueKind == JsonValueKind.Array)
                    {
                        sb.Append("\n");
                        foreach (JsonElement g in groups.EnumerateArray())
                        {
                            sb.Append("- 工具组 `" + GetStringProp(g, "group") + "`：");
                            JsonElement tools;
                            bool first = true;
                            if (g.TryGetProperty("tools", out tools) && tools.ValueKind == JsonValueKind.Array)
                            {
                                foreach (JsonElement t in tools.EnumerateArray())
                                {
                                    if (!first)
                                    {
                                        sb.Append("、");
                                    }
                                    first = false;
                                    sb.Append(GetStringProp(t, "name"));
                                }
                            }
                            sb.Append("\n");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // 报告解析失败降级为原文（可辨识——不静默丢内容）
                LogStore.Add("CatHome4", 2, "旧会话留档报告解析失败: " + ex.Message, "CHAT");
                sb.Append("- 解析失败，原文：\n\n```json\n" + injectReport + "\n```\n");
            }
            sb.Append("\n");
        }

        /// <summary>留档时间戳格式化——Unix 毫秒 → 本地「MM-dd HH:mm:ss」（非正值记「—」）</summary>
        /// <param name="ts">Unix 毫秒时间戳</param>
        /// <returns>格式化文本</returns>
        private static string FormatLegacyTime(long ts)
        {
            if (ts <= 0)
            {
                return "—";
            }
            return DateTimeOffset.FromUnixTimeMilliseconds(ts).ToLocalTime().ToString("MM-dd HH:mm:ss");
        }

        /// <summary>读取 JSON 数值属性——防御式（缺字段 / 非数值返回 0）</summary>
        /// <param name="data">JSON 对象</param>
        /// <param name="prop">属性名</param>
        /// <returns>数值（缺省 0）</returns>
        private static long ReadLongProp(JsonElement data, string prop)
        {
            JsonElement value;
            if (!data.TryGetProperty(prop, out value))
            {
                return 0;
            }
            if (value.ValueKind != JsonValueKind.Number)
            {
                return 0;
            }
            return value.GetInt64();
        }

        /// <summary>清空视图层——session.new 清前文时同步（真实前文 Clear 后视图随生命周期清理）</summary>
        public void Clear()
        {
            // A111——块序变更快照（清空 = 全量移除：转发面游标随之归位）
            ViewBlock[] before = GetBlocks();
            _blocks.Clear();
            _pendingTools.Clear();
            // 注入报告随视图层清理——session.new 后 HandleSessionNew 重新 Set + Save
            _injectReport = "";
            // 间隙文本随视图层清理——新会话不保留旧 gap 块
            _gapTexts.Clear();
            // 轮末统计随视图层清理——新会话不保留旧轮统计
            _roundSums.Clear();
            // 错误块随视图层清理——新会话不保留旧错误气泡
            _errors.Clear();
            // 重试过程块随视图层清理——新会话不保留旧重试记录
            _retries.Clear();
            // 废弃块随视图层清理——新会话不保留旧回收归档
            _voids.Clear();
            NotifyBlocksReordered(before);
        }
        /// <summary>
        /// 清空轮末统计块——回滚裁剪后调用（roundsum 非真实前文派生，Rebuild 不清——裁剪后残留旧统计）
        /// </summary>
        public void ClearRoundSums()
        {
            // A111——块序变更快照（轮统计块移出：转发面游标随之前移）
            ViewBlock[] before = GetBlocks();
            _roundSums.Clear();
            NotifyBlocksReordered(before);
        }
        /// <summary>
        /// 生成前文派生视图块——块键 + 来源关系 + 自哈希 ID（A156）；时间戳取消息 CreatedAt。
        /// A157：工具卡可指定时间戳覆盖——取工具调用声明时刻，与实时区先行卡同基点。
        /// </summary>
        /// <param name="m">真实前文消息</param>
        /// <param name="renderType">渲染类型</param>
        /// <param name="payload">渲染载荷（字典）</param>
        /// <param name="msgIndex">真实前文消息索引（块来源关系字段）</param>
        /// <param name="durMs">运行时长（毫秒；-1 = 不适用）</param>
        /// <param name="timestampOverride">时间戳覆盖（>0 生效；0 = 取消息 CreatedAt——工具卡取声明时刻）</param>
        private void Append(LlmMessage m, string renderType, Dictionary<string, object> payload, int msgIndex, long durMs = -1, long timestampOverride = 0)
        {
            string payloadJson = JsonUtil.Serialize(payload);
            ViewOrigin origin = new ViewOrigin();
            origin.MsgIndex = msgIndex;
            origin.Hash = ComputeHash(m);
            string key = "msg:" + msgIndex.ToString() + ":" + renderType;
            if (renderType == "toolcard")
            {
                key = "tool:" + (m.ToolCallId ?? "");
            }
            long timestamp = timestampOverride > 0 ? timestampOverride : m.CreatedAt;
            ViewBlock block = NewBlock(key, renderType, payloadJson, timestamp, origin, "front", durMs);
            _blocks.Add(block);
            EmitBlock(block);
        }

        /// <summary>
        /// 截断视图层——移除前文来源索引 ≥ fromMsgIndex 的块（回滚 / 前文裁剪的显式同步；A156：两面不互派生）。
        /// 独立块（roundsum / error / retry 等）不受影响——由调用方另行清理。
        /// </summary>
        /// <param name="fromMsgIndex">起点消息索引（含——该索引及其后的前文派生块全部移除）</param>
        /// <returns>移除的块数（0 = 无需截断）</returns>
        public int TruncateFrom(int fromMsgIndex)
        {
            ViewBlock[] before = GetBlocks();
            int removed = 0;
            for (int i = _blocks.Count - 1; i >= 0; i = i - 1)
            {
                ViewBlock b = _blocks[i];
                if (b.Origin != null && b.Origin.MsgIndex >= fromMsgIndex)
                {
                    _blocks.RemoveAt(i);
                    removed = removed + 1;
                }
            }
            if (removed > 0)
            {
                NotifyBlocksReordered(before);
            }
            return removed;
        }

        /// <summary>
        /// 建块单点——块键 / 时间戳单调化 / 自哈希 ID / 来源关系（A157：构造内核对齐 ViewBlock，两区共用）。
        /// </summary>
        /// <param name="key">块键（建块即定的稳定句柄——msg:&lt;index&gt; / tool:&lt;toolCallId&gt; / 容器:&lt;序号&gt;）</param>
        /// <param name="renderType">渲染类型</param>
        /// <param name="payloadJson">渲染载荷 JSON</param>
        /// <param name="timestamp">创建时间戳（Unix 毫秒——前文派生取消息 CreatedAt）</param>
        /// <param name="origin">前文来源（null = 独立块）</param>
        /// <param name="src">来源类别（front / independent）</param>
        /// <param name="durMs">运行时长（毫秒；-1 = 不适用）</param>
        /// <param name="monotonic">是否参与时间戳单调化（默认 true；归档类块取历史时间戳时传 false）</param>
        /// <returns>已定稿的视图块</returns>
        private ViewBlock NewBlock(string key, string renderType, string payloadJson, long timestamp, ViewOrigin origin, string src, long durMs, bool monotonic = true)
        {
            long ts = monotonic ? MonotonicTs(timestamp) : timestamp;
            ViewBlock block = ViewBlock.BuildPending(key, renderType, payloadJson, ts, origin, src);
            block.Finalize(durMs);
            return block;
        }

        /// <summary>
        /// 建块通知（A158 期三）——块定稿入容器后调用，供会话侧接持久区推送出口（ViewBus.PushPersist）。
        /// </summary>
        /// <param name="block">已入容器的定稿块</param>
        private void EmitBlock(ViewBlock block)
        {
            Action<ViewBlock> handler = OnBlockAppended;
            if (handler != null)
            {
                handler(block);
            }
        }

        /// <summary>
        /// 时间戳单调化——同刻或回退由写入侧 +1 保证全序（A156 I5：前端可无脑按 ts 冒泡插入）。
        /// </summary>
        /// <param name="timestamp">原始时间戳（Unix 毫秒）</param>
        /// <returns>单调递增后的时间戳</returns>
        private long MonotonicTs(long timestamp)
        {
            if (timestamp <= _lastTs)
            {
                timestamp = _lastTs + 1;
            }
            _lastTs = timestamp;
            return timestamp;
        }

        /// <summary>
        /// 登记待配对工具——解析 tool_calls JSON 数组（{id,function:{name,arguments}}；解析失败空登记——容错）
        /// </summary>
        /// <param name="toolCallsJson">tool_calls JSON</param>
        /// <param name="declaredTs">声明时刻（Unix 毫秒——工具卡块时间戳基准，A157）</param>
        private void RegisterPendingTools(string toolCallsJson, long declaredTs)
        {
            try
            {
                using (JsonDocument doc = JsonUtil.ParseStrict(toolCallsJson))
                {
                    JsonElement root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Array)
                    {
                        return;
                    }
                    for (int i = 0; i < root.GetArrayLength(); i++)
                    {
                        JsonElement call = root[i];
                        string id = GetStringProp(call, "id");
                        string name = "";
                        string arguments = "";
                        JsonElement funcEl;
                        if (call.TryGetProperty("function", out funcEl))
                        {
                            name = GetStringProp(funcEl, "name");
                            arguments = GetStringProp(funcEl, "arguments");
                        }
                        PendingTool pt = new PendingTool();
                        pt.ToolCallId = id;
                        pt.Name = name;
                        pt.Arguments = arguments;
                        pt.Index = i + 1;
                        pt.Total = root.GetArrayLength();
                        pt.Ts = declaredTs;
                        _pendingTools.Add(pt);
                    }
                }
            }
            catch (Exception ex)
            {
                // 解析失败——空登记（容错）
                LogStore.Add("SessionViewStore", 2, "工具卡登记解析失败，按空登记: " + ex.Message, "SYS");
            }
        }

        /// <summary>
        /// 读取 JSON 对象字符串属性——防御式（缺字段返回空串）
        /// </summary>
        /// <param name="obj">JSON 对象</param>
        /// <param name="prop">属性名</param>
        /// <returns>属性值</returns>
        private static string GetStringProp(JsonElement obj, string prop)
        {
            JsonElement value;
            if (obj.TryGetProperty(prop, out value) && value.ValueKind == JsonValueKind.String)
            {
                string got = value.GetString();
                if (got != null)
                {
                    return got;
                }
            }
            return "";
        }

        /// <summary>
        /// 前文消息内容哈希——单块完整字段 SHA256 十六进制（块来源关系字段 origin.hash 的取值）。
        /// </summary>
        /// <param name="m">真实前文消息</param>
        /// <returns>哈希十六进制串</returns>
        private static string ComputeHash(LlmMessage m)
        {
            string raw = m.Role.ToString()
                + "\u0001" + (m.Content ?? "")
                + "\u0001" + (m.ToolCallId ?? "")
                + "\u0001" + (m.ToolName ?? "")
                + "\u0001" + (m.ToolCallsJson ?? "")
                + "\u0001" + (m.ReasoningContent ?? "");
            return ViewBlock.Sha256Hex(raw);
        }
        /// <summary>
        /// 载入视图层——读回持久块 + 五个独立数组 + 注入报告（A156：载入即权威，不再从前文重建）。
        /// 失败出声——视图层为权威，空视图继续 = 前端历史整体缺口，静默不可接受。
        /// </summary>
        public void Load()
        {
            try
            {
                if (!System.IO.File.Exists(_path))
                {
                    return;
                }

                string json = System.IO.File.ReadAllText(_path);
                JsonSerializerOptions options = new JsonSerializerOptions();
                options.IncludeFields = true;
                ViewFileData data = JsonSerializer.Deserialize<ViewFileData>(json, options);
                if (data == null)
                {
                    LogStore.Add("SessionViewStore", 3, "视图载入失败——文件内容为空（视图层为权威：本会话历史将为空白）", "SYS");
                    return;
                }
                _blocks.Clear();
                if (data.Blocks != null)
                {
                    _blocks.AddRange(data.Blocks);
                }
                if (data.InjectReport != null)
                {
                    _injectReport = data.InjectReport;
                }
                _gapTexts.Clear();
                if (data.GapTexts != null)
                {
                    _gapTexts.AddRange(data.GapTexts);
                }
                _roundSums.Clear();
                if (data.RoundSums != null)
                {
                    _roundSums.AddRange(data.RoundSums);
                }
                _errors.Clear();
                if (data.Errors != null)
                {
                    _errors.AddRange(data.Errors);
                }
                _retries.Clear();
                if (data.Retries != null)
                {
                    _retries.AddRange(data.Retries);
                }
                _voids.Clear();
                if (data.Voids != null)
                {
                    _voids.AddRange(data.Voids);
                }
                _lastTs = MaxTimestamp();
            }
            catch (Exception ex)
            {
                // A156：视图层为权威——载入失败出声（空视图继续 = 前端历史整体缺口）
                LogStore.Add("SessionViewStore", 3, "视图载入失败（按空视图继续——本会话历史将空白）: " + ex.Message, "SYS");
            }
        }

        /// <summary>
        /// 六源块最大时间戳——单调补差基准初始化（载入后调用）。
        /// </summary>
        /// <returns>最大时间戳（无块 = 0）</returns>
        private long MaxTimestamp()
        {
            long max = 0;
            ViewBlock[] all = GetBlocks();
            for (int i = 0; i < all.Length; i = i + 1)
            {
                if (all[i].Timestamp > max)
                {
                    max = all[i].Timestamp;
                }
            }
            return max;
        }

        /// <summary>
        /// 对账哨兵——抽样比对块的前文来源哈希与真实前文对应消息（A156：替代常驻重建作正确性保证）。
        /// 不一致出声（L3）；不修改任何块。
        /// </summary>
        /// <param name="messages">真实前文消息数组</param>
        /// <param name="sample">抽样块数（0 或负 = 全量比对）</param>
        /// <returns>不一致块数（0 = 全对）</returns>
        public int AuditOrigins(LlmMessage[] messages, int sample)
        {
            int checkedCount = 0;
            int bad = 0;
            for (int i = 0; i < _blocks.Count; i = i + 1)
            {
                ViewBlock b = _blocks[i];
                if (b.Origin == null || b.Origin.MsgIndex < 0 || b.Origin.MsgIndex >= messages.Length)
                {
                    continue;
                }
                if (sample > 0 && checkedCount >= sample)
                {
                    break;
                }
                checkedCount = checkedCount + 1;
                string expect = ComputeHash(messages[b.Origin.MsgIndex]);
                if (expect != b.Origin.Hash)
                {
                    bad = bad + 1;
                    LogStore.Add("SessionViewStore", 3, "对账哨兵：块来源哈希与前文不一致（块 " + (b.Key == null ? "" : b.Key) + " / 消息 " + b.Origin.MsgIndex.ToString() + "）", "SYS");
                }
            }
            return bad;
        }
        /// <summary>
        /// 工具卡归属判定——该块是否为指定工具名的工具卡（仅 toolcard 块型；载荷解析失败 = false，进废弃块）。
        /// 用途：`ConvertRangeToVoid` 的保留面（timeback 自己的锚定卡保留在对话流——三段式的锚）。
        /// </summary>
        /// <param name="b">视图块</param>
        /// <param name="toolName">工具名</param>
        /// <returns>true=命中（保留在对话流）</returns>
        private static bool IsToolCardOf(ViewBlock b, string toolName)
        {
            if (b.RenderType != "toolcard" || b.Payload == null || b.Payload.Length == 0)
            {
                return false;
            }
            try
            {
                using (JsonDocument doc = JsonUtil.ParseStrict(b.Payload))
                {
                    JsonElement nameEl;
                    if (doc.RootElement.TryGetProperty("name", out nameEl) && nameEl.ValueKind == JsonValueKind.String)
                    {
                        string got = nameEl.GetString();
                        return got != null && string.Equals(got, toolName, StringComparison.Ordinal);
                    }
                }
            }
            catch (Exception ex)
            {
                // 载荷解析失败——不保留（走废弃块）
                LogStore.Add("SessionViewStore", 2, "工具卡载荷解析失败，不进保留面: " + ex.Message, "SYS");
            }
            return false;
        }
    }
}

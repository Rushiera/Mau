using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mau.Runtime;
using CatHome4.Contracts;

namespace CH4
{
    /// <summary>
    /// 会话视图存储——F4 视图持久化（内存整块列表 + 文件落盘 + 从真实前文重建）。
    /// 只存整块（流式中间态/占位卡不落盘）；重建 = 真实前文绝对可用 → 完全重置视图层。
    /// 数据源语义：内存真源（运行时增量构建）；文件 = 落盘面（CloseRound 同步写）。
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

        /// <summary>
        /// 块序代际号——任何块序变更（清除 / 重建 / 轮统计清理 / 区间转废弃）递增（A142）：
        /// 前端重连时带 gen + 已持有块数请求增量历史；gen 不匹配 = 前缀失效 → 回落全量重建。
        /// </summary>
        private int _blockGen;

        /// <summary>
        /// 块序变更通知——视图层唯一出声点（清除 / 重建 / 轮统计清理 / 区间转废弃四处变更点统一调用）：
        /// 转发面（QQ）订阅后按变更区间 / 内容锚点校正块游标（A111）。空=无消费方（无动作）。
        /// </summary>
        public Action<ViewOrderChange> OnBlocksReordered;

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
        }

        /// <summary>视图文件数据——JSON 形态（blocks 按到达序）</summary>
        private sealed class ViewFileData
        {
            /// <summary>协议版本</summary>
            public int Version { get; set; }

            /// <summary>会话 ID</summary>
            public string SessionId { get; set; }

            /// <summary>视图块数组（按到达序——重建后重新生成）</summary>
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
            if (a.Hash != b.Hash || a.Timestamp != b.Timestamp || a.RenderType != b.RenderType)
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
                ViewBlock report = new ViewBlock();
                report.Timestamp = 0;
                report.Hash = "inject_report";
                report.MsgIndex = -1;
                report.RenderType = "inject_report";
                report.Payload = _injectReport;
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
                 /// <param name="timestamp">创建时间戳（Unix 毫秒）</param>
                 /// <param name="msgIndex">真实前文消息索引（节点定位锚）</param>
        public void OnUserMessage(LlmMessage m, long timestamp, int msgIndex)
        {
            Dictionary<string, object> payload = new Dictionary<string, object>();
            payload["content"] = m.Content ?? "";
            Append(m, "user", payload, timestamp, msgIndex);
        }

        /// <summary>
        /// 真实前文 append 钩子——assistant 纯文本回复 → text 块
        /// </summary>
        /// <param name="m">真实前文消息</param>
        /// <param name="timestamp">创建时间戳（Unix 毫秒）</param>
        /// <param name="msgIndex">真实前文消息索引（节点定位锚）</param>
        public void OnAssistantText(LlmMessage m, long timestamp, int msgIndex)
        {
            Dictionary<string, object> payload = new Dictionary<string, object>();
            payload["content"] = m.Content ?? "";
            Append(m, "text", payload, timestamp, msgIndex);
        }

        /// <summary>
        /// 真实前文 append 钩子——assistant 工具调用声明 → reason 块（有思考时）+ 登记待配对工具
        /// </summary>
        /// <param name="m">真实前文消息</param>
        /// <param name="timestamp">创建时间戳（Unix 毫秒）</param>
        /// <param name="msgIndex">真实前文消息索引（节点定位锚）</param>
        public void OnAssistantToolCalls(LlmMessage m, long timestamp, int msgIndex)
        {
            string reasoning = m.ReasoningContent ?? "";
            if (reasoning.Length > 0)
            {
                Dictionary<string, object> payload = new Dictionary<string, object>();
                payload["content"] = reasoning;
                Append(m, "reason", payload, timestamp, msgIndex);
            }
            RegisterPendingTools(m.ToolCallsJson ?? "");
        }

        /// <summary>
        /// 真实前文 append 钩子——tool 结果 → 配对生成工具卡块（孤立 tool 丢弃——视图容错）
        /// </summary>
        /// <param name="m">真实前文消息</param>
        /// <param name="timestamp">创建时间戳（Unix 毫秒）</param>
        /// <param name="msgIndex">真实前文消息索引（节点定位锚）</param>
        public void OnToolResult(LlmMessage m, long timestamp, int msgIndex)
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
            Append(m, "toolcard", payload, timestamp, msgIndex);
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
                if (b.MsgIndex < fromMsgIndex || b.MsgIndex > toMsgIndex)
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
            ViewBlock block = new ViewBlock();
            block.Timestamp = removed[0].Timestamp;
            block.Hash = "void_" + _voids.Count.ToString();
            block.MsgIndex = -1;
            block.RenderType = "void";
            // 留档被移出块的内容哈希——Rebuild 据此过滤（视图层 = f(真实前文, 视图层留档)）；
            // 判据取内容哈希不取索引：timeback 删前文后索引漂移，哈希跨删稳定。
            // 骨架式构造——Dictionary + Serialize 不识别 JsonFragment（片段会落成转义对象），须走 Object + Raw
            List<string> movedHashes = new List<string>();
            for (int i = 0; i < removed.Count; i = i + 1)
            {
                movedHashes.Add(removed[i].Hash == null ? "" : removed[i].Hash);
            }
            block.Payload = JsonUtil.Object(
                ("count", moved),
                ("text", body.ToString()),
                ("hashes", JsonUtil.Raw(JsonUtil.Array(movedHashes.ToArray()))));
            _voids.Add(block);
            Save();
            NotifyBlocksReordered(before);
            return moved;
        }
        /// <summary>
        /// 重建后置过滤——按废弃段留档的内容哈希移除已移出块（视图层 = f(真实前文, 视图层留档)）。
        /// 为什么需要：timeback 的 sibling 结果卡「前文里删不得」（assistant 声明的 tool_calls 必须与结果配对），
        /// 而重建只认前文 → 每次启动恢复都会把已入废弃段的块复活，与永久留档的废弃段重复。
        /// 判据取内容哈希（前文单块 SHA256）不取索引——timeback 删前文后索引漂移，哈希跨删稳定。
        /// 旧档（无 hashes 字段的废弃段）不追溯——仅在本次变更后新产生的废弃段生效。
        /// </summary>
        private void PurgeVoidedBlocks()
        {
            HashSet<string> voided = new HashSet<string>(StringComparer.Ordinal);
            for (int v = 0; v < _voids.Count; v = v + 1)
            {
                string[] hashes = ExtractVoidHashes(_voids[v]);
                for (int h = 0; h < hashes.Length; h = h + 1)
                {
                    if (hashes[h].Length > 0)
                    {
                        voided.Add(hashes[h]);
                    }
                }
            }
            if (voided.Count == 0)
            {
                return;
            }
            for (int i = _blocks.Count - 1; i >= 0; i = i - 1)
            {
                string hash = _blocks[i].Hash;
                if (hash != null && voided.Contains(hash))
                {
                    _blocks.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// 取废弃段留档的被移出块哈希集合——载荷字段 hashes（解析失败 / 旧档无该字段 = 空数组）。
        /// </summary>
        /// <param name="voidBlock">废弃块</param>
        /// <returns>哈希数组（空 = 无留档）</returns>
        private static string[] ExtractVoidHashes(ViewBlock voidBlock)
        {
            string payloadJson = voidBlock.Payload == null ? "" : voidBlock.Payload;
            if (payloadJson.Length == 0)
            {
                return new string[0];
            }
            try
            {
                using (JsonDocument doc = JsonUtil.ParseStrict(payloadJson))
                {
                    JsonElement root = doc.RootElement;
                    JsonElement hashesEl;
                    if (!root.TryGetProperty("hashes", out hashesEl) || hashesEl.ValueKind != JsonValueKind.Array)
                    {
                        return new string[0];
                    }
                    int count = hashesEl.GetArrayLength();
                    string[] result = new string[count];
                    for (int i = 0; i < count; i = i + 1)
                    {
                        JsonElement item = hashesEl[i];
                        result[i] = item.ValueKind == JsonValueKind.String ? (item.GetString() ?? "") : "";
                    }
                    return result;
                }
            }
            catch (Exception)
            {
                return new string[0];
            }
        }

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
        /// 从真实前文重建视图层——完全重置（真实前文绝对可用；启动恢复/视图文件缺失时调用）。
        /// 块时间戳取消息 CreatedAt——真实时序权威（跨重启稳定；旧消息 CreatedAt=0 按 List 顺序稳定排前——重建是兜底场景，不做兼容维护）。
        /// </summary>
        /// <param name="messages">真实前文消息数组</param>
        public void Rebuild(LlmMessage[] messages)
        {
            // A111——块序变更快照（重建 = 完全重置：转发面游标按锚点重定位，不以块数比对追发历史）
            ViewBlock[] before = GetBlocks();
            _blocks.Clear();
            _pendingTools.Clear();
            for (int i = 0; i < messages.Length; i++)
            {
                LlmMessage m = messages[i];
                if (m.Role == LlmRole.System)
                {
                    continue;
                }
                if (m.Role == LlmRole.User)
                {
                    OnUserMessage(m, m.CreatedAt, i);
                    continue;
                }
                if (m.Role == LlmRole.Assistant)
                {
                    string toolCalls = m.ToolCallsJson ?? "";
                    if (toolCalls.Length > 0)
                    {
                        OnAssistantToolCalls(m, m.CreatedAt, i);
                    }
                    else
                    {
                        OnAssistantText(m, m.CreatedAt, i);
                    }
                    continue;
                }
                if (m.Role == LlmRole.Tool)
                {
                    OnToolResult(m, m.CreatedAt, i);
                }
            }
            // 视图层留档重放——前文不含「已移出」信息，须由废弃段哈希补齐（否则 sibling 卡每次重启复活）
            PurgeVoidedBlocks();
            NotifyBlocksReordered(before);
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
                data.Version = 1;
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
                // 保存失败不阻断会话（下次收工再试）——视图是派生态，真实前文可重建
                LogStore.Add("SessionViewStore", 2, "视图保存失败: " + ex.Message, "SYS");
            }
        }        /// <summary>追加轮末统计块——roundsum（Token 消耗 + 工具次数 + 总耗时 + 四态用时）。非真实前文派生（Rebuild 不清）；写入即落盘——宿主中断不丢。</summary>
/// <param name="payloadJson">roundsum 载荷 JSON（{"type":"roundsum","data":{...}}）</param>
/// <param name="timestamp">创建时间戳（Unix 毫秒——与消息块同坐标系，归并排序键）</param>
        public void AppendRoundSummary(string payloadJson, long timestamp)
        {
            ViewBlock block = new ViewBlock();
            block.Timestamp = timestamp;
            block.Hash = "roundsum_" + _roundSums.Count.ToString();
            block.MsgIndex = -1;
            block.RenderType = "roundsum";
            block.Payload = payloadJson;
            _roundSums.Add(block);
            Save();
        }
        /// <summary>追加间隙文本块——工具轮 seal 文本（模型调用工具前说的话）。非真实前文派生（Rebuild 不清）；写入即落盘——工具轮中途中断不丢。视图层 = 全部外观真源——前端历史/QQBot 转发统一消费此块。</summary>
        /// <param name="content">间隙文本</param>
        /// <param name="timestamp">创建时间戳（Unix 毫秒——与消息块同坐标系）</param>
        public void AppendGapText(string content, long timestamp)
        {
            if (content == null || content.Length == 0)
            {
                return;
            }
            ViewBlock block = new ViewBlock();
            block.Timestamp = timestamp;
            block.Hash = "gap_" + _gapTexts.Count.ToString();
            block.MsgIndex = -1;
            block.RenderType = "text";
            Dictionary<string, object> payload = new Dictionary<string, object>();
            payload["content"] = content;
            block.Payload = JsonUtil.Serialize(payload);
            _gapTexts.Add(block);
            Save();
        }

        /// <summary>
        /// 追加错误块——LLM 错误气泡（非真实前文派生；写入即落盘——刷新可回看）。
        /// </summary>
        /// <param name="text">错误文本</param>
        /// <param name="timestamp">创建时间戳（Unix 毫秒——与消息块同坐标系）</param>
        public void AppendError(string text, long timestamp)
        {
            if (text == null || text.Length == 0)
            {
                return;
            }
            ViewBlock block = new ViewBlock();
            block.Timestamp = timestamp;
            block.Hash = "error_" + _errors.Count.ToString();
            block.MsgIndex = -1;
            block.RenderType = "error";
            Dictionary<string, object> payload = new Dictionary<string, object>();
            payload["type"] = "error";
            payload["text"] = text;
            block.Payload = JsonUtil.Serialize(payload);
            _errors.Add(block);
            Save();
        }

        /// <summary>
        /// 写入重试块——retry 气泡（同一重试序列原位更新不堆叠；写入即落盘）。index 越界或为负 → 新建块。
        /// </summary>
        /// <param name="payloadJson">retry 载荷 JSON（state/attempt/max/text）</param>
        /// <param name="timestamp">创建时间戳（Unix 毫秒——新建块时使用）</param>
        /// <param name="index">既有块索引（-1=新建）</param>
        /// <returns>块索引（后续更新回传）</returns>
        public int UpsertRetry(string payloadJson, long timestamp, int index)
        {
            if (index >= 0 && index < _retries.Count)
            {
                _retries[index].Payload = payloadJson;
                Save();
                return index;
            }
            ViewBlock block = new ViewBlock();
            block.Timestamp = timestamp;
            block.Hash = "retry_" + _retries.Count.ToString();
            block.MsgIndex = -1;
            block.RenderType = "retry";
            block.Payload = payloadJson;
            _retries.Add(block);
            Save();
            return _retries.Count - 1;
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
        /// 生成视图块——内容哈希 = 真实前文单块完整字段 SHA256（裁决：前文块哈希作唯一标识）
        /// </summary>
        /// <param name="m">真实前文消息</param>
        /// <param name="renderType">渲染类型</param>
        /// <param name="payload">渲染载荷（字典）</param>
        /// <param name="timestamp">创建时间戳（Unix 毫秒）</param>
        /// <param name="msgIndex">真实前文消息索引（节点定位锚）</param>
        private void Append(LlmMessage m, string renderType, Dictionary<string, object> payload, long timestamp, int msgIndex)
        {
            ViewBlock block = new ViewBlock();
            block.Timestamp = timestamp;
            block.Hash = ComputeHash(m);
            block.MsgIndex = msgIndex;
            block.RenderType = renderType;
            block.Payload = JsonUtil.Serialize(payload);
            _blocks.Add(block);
        }

        /// <summary>
        /// 登记待配对工具——解析 tool_calls JSON 数组（{id,function:{name,arguments}}；解析失败空登记——容错）
        /// </summary>
        /// <param name="toolCallsJson">tool_calls JSON</param>
        private void RegisterPendingTools(string toolCallsJson)
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
        /// 真实前文块内容哈希——单块完整字段 SHA256 十六进制（时空双索引的"空间"维）
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
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < bytes.Length; i = i + 1)
                {
                    sb.Append(bytes[i].ToString("x2"));
                }
                return sb.ToString();
            }
        }
        /// <summary>
        /// 加载注入报告 + 轮末统计——启动恢复时调用（Rebuild 后读回；view.json 缺失/损坏静默空报告）
        /// </summary>
        public void LoadInjectReport()
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
                if (data != null && data.InjectReport != null)
                {
                    _injectReport = data.InjectReport;
                }
                if (data != null && data.GapTexts != null)
                {
                    _gapTexts.Clear();
                    _gapTexts.AddRange(data.GapTexts);
                }
                if (data != null && data.RoundSums != null)
                {
                    _roundSums.Clear();
                    _roundSums.AddRange(data.RoundSums);
                }
                if (data != null && data.Errors != null)
                {
                    _errors.Clear();
                    _errors.AddRange(data.Errors);
                }
                if (data != null && data.Retries != null)
                {
                    _retries.Clear();
                    _retries.AddRange(data.Retries);
                }
                if (data != null && data.Voids != null)
                {
                    _voids.Clear();
                    _voids.AddRange(data.Voids);
                }
            }
            catch (Exception ex)
            {
                // 加载失败静默——注入报告缺失不阻断（视图可重建）
                LogStore.Add("SessionViewStore", 2, "视图加载失败，按空视图继续: " + ex.Message, "SYS");
            }
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

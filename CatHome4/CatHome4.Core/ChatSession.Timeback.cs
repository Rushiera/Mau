using System;
using System.Collections.Generic;
using System.Text.Json;
using Mau.Runtime;

namespace CH4
{
    /// <summary>
    /// 宿主会话实体——timeback 上下文作用域分部（design-ch4-timeback §2 / §三 · 2026-09-28 语义重设）。
    /// 语义（莎定）：**锚点 = start 的 tool_calls 声明本身；回收 = back 的返回值**。
    ///   前文形态：[start 声明][start 结果] [查证过程…] [back 声明][back 结果 = findings]
    ///   回收动作：删除「start 结果之后、back 声明之前」的全部消息——两次调用对与结论原样保留。
    /// 由此收益：历史里不新增注入消息（无连续 user）、无绕点搜索、无轮末特判、无「劈开工具对」风险；
    /// 回卷后序列天然合法，本轮照常续跑（LLM 直接从 back 的工具返回继续）。
    /// 锁定：作用域存活期间 Note / sleep / timer 不可用（ERR|TIMEBACK_LOCKED——与区间删除语义冲突）。
    /// 视图层：删除区间对应的前文派生块合并为一个废弃块（Rebuild 不清——跨宿主重启仍可回看）。
    /// 归档（A104）：全局计数（Data/runtime/timeback/count.json）+ 每次回收一个作用域文件（&lt;编号&gt;-&lt;时间戳&gt;.jsonl）。
    /// </summary>
    internal sealed partial class ChatSession
    {
        /// <summary>归档目录提供者——catKey → Data/runtime/timeback 目录（宿主启动期由组合根注入；空=归档不可用）。</summary>
        internal static Func<string, string> TimebackArchiveDirProvider;

        /// <summary>info 快照提供者——归档文件第二行的现场记录（宿主启动期由组合根注入；空=该行不写）。</summary>
        internal static Func<string> TimebackInfoProvider;

        /// <summary>timeback 作用域——内存单对象（v1 不嵌套；宿主重启即失效，兜底走 session.rollback）。</summary>
        private sealed class TimebackScope
        {
            /// <summary>作用域编号——本猫内永久递增（归档 open/close 以 id 关联）。</summary>
            public long Id;

            /// <summary>start 的 tool_calls 声明消息索引——锚点（回收时保留，含其后续的 start 结果）。</summary>
            public int StartDeclIndex;

            /// <summary>back 的 tool_calls 声明消息索引——回收区间上界（回收时保留，含其后续的 back 结果）。</summary>
            public int BackDeclIndex;

            /// <summary>开锚时刻——Unix 毫秒。</summary>
            public long StartAtMs;

            /// <summary>用途标签——归档与事后判读原料。</summary>
            public string Purpose = "";

            /// <summary>待执行回收的带回载荷——back 已作为工具返回值送出，批后段据此执行区间删除。</summary>
            public string PendingFindings;

            /// <summary>作用域内事件累计——think / 工具完成 / assistant 产出各计 1（状态自述阈值数据源）。</summary>
            public long EventCount;

            /// <summary>上次状态自述时的计数水位——距其再满 25 个事件注入下一条（25 / 50 / 75…）。</summary>
            public long LastNotifyCount;

            /// <summary>开锚时的已知前文长度快照——回收时对比算净增（请求级真实 usage 值，零估算）。</summary>
            public long TokensAtOpen;

            /// <summary>本次回收的释放条数——back 时刻预算（区间上下界已定，精确可算）；随返回值送出，并与批后实际删除数对账。</summary>
            public int PendingReleased;
        }

        /// <summary>当前未闭合作用域——null=无作用域。</summary>
        private TimebackScope _timebackScope;

        /// <summary>归档实例——懒建（首次使用时按本猫路径构造）。</summary>
        private TimebackArchive _timebackArchive;

        /// <summary>
        /// timeback 活跃态——start 之后至 back 回收完成。
        /// 消费方：QQ 转发豁免（不消费来源 / 不推进游标）· 工具锁定（Note / sleep / timer）· 观测面。
        /// </summary>
        public bool TimebackActive
        {
            get
            {
                return _timebackScope != null;
            }
        }

        /// <summary>
        /// timeback 执行体——参数面（action 必填：start 需 purpose / back 需 findings；未知参数拒绝）。
        /// start：锚点 = 本刻前文末条（即本次 start 的 tool_calls 声明）+ 归档 open 行。
        /// back：结论即本次调用的返回值（findings 全文）——区间删除在工具批后段执行。
        /// </summary>
        /// <param name="argsJson">参数 JSON（action / purpose / findings）</param>
        /// <returns>结构化结果（元数据头 + 正文；失败 ERR| 前缀）</returns>
        private string ExecuteTimeback(string argsJson)
        {
            string action = "";
            string purpose = "";
            string findings = "";
            // [段0] 参数面——action 必填 + 未知参数拒绝（零容忍）
            if (argsJson != null && argsJson.Length > 0 && argsJson.StartsWith("{"))
            {
                try
                {
                    using (JsonDocument doc = JsonDocument.Parse(argsJson))
                    {
                        JsonElement root = doc.RootElement;
                        foreach (JsonProperty property in root.EnumerateObject())
                        {
                            if (property.Name == "catId")
                            {
                                continue;
                            }
                            if (property.Name == "action")
                            {
                                if (property.Value.ValueKind != JsonValueKind.String)
                                {
                                    return "ERR|TIMEBACK_ARGS|action 需为字符串";
                                }
                                action = property.Value.GetString() ?? "";
                                continue;
                            }
                            if (property.Name == "purpose")
                            {
                                if (property.Value.ValueKind != JsonValueKind.String)
                                {
                                    return "ERR|TIMEBACK_ARGS|purpose 需为字符串";
                                }
                                purpose = property.Value.GetString() ?? "";
                                continue;
                            }
                            if (property.Name == "findings")
                            {
                                if (property.Value.ValueKind != JsonValueKind.String)
                                {
                                    return "ERR|TIMEBACK_ARGS|findings 需为字符串";
                                }
                                findings = property.Value.GetString() ?? "";
                                continue;
                            }
                            return "ERR|TIMEBACK_ARGS|未知参数: " + property.Name + "（支持 action / purpose / findings）";
                        }
                    }
                }
                catch (Exception ex)
                {
                    return "ERR|TIMEBACK_ARGS|timeback 参数解析失败: " + ex.Message;
                }
            }
            if (action == "start")
            {
                return TimebackStart(purpose);
            }
            if (action == "back")
            {
                return TimebackBack(findings);
            }
            return "ERR|TIMEBACK_ARGS|action 需为 start 或 back";
        }

        /// <summary>
        /// start——登记作用域：锚点 = 本刻前文末条（本次 start 的 tool_calls 声明，工具批执行点已在盘上）。
        /// v1 未闭合前禁止再次 start。
        /// </summary>
        /// <param name="purpose">用途标签</param>
        /// <returns>回执文本</returns>
        private string TimebackStart(string purpose)
        {
            if (_timebackScope != null)
            {
                return "ERR|TIMEBACK_NESTED|已有未闭合作用域 #" + _timebackScope.Id.ToString() + "——先 back 再 start";
            }
            if (purpose.Length == 0)
            {
                return "ERR|TIMEBACK_ARGS|start 需要 purpose（用途标签）";
            }
            int declIndex = _context.GetMessageCount() - 1;
            if (declIndex < 0)
            {
                return "ERR|TIMEBACK_ANCHOR|前文为空——无锚点可记";
            }
            TimebackScope scope = new TimebackScope();
            scope.StartDeclIndex = declIndex;
            scope.StartAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            scope.Purpose = purpose.Length > 48 ? purpose.Substring(0, 48) : purpose;
            // 开锚快照——回收时据此算作用域净增 token（真实 usage 值，不估算）
            scope.TokensAtOpen = ContextTokensKnown;
            // A104 新机制——取号 = 全局计数落盘（count.json；跨猫 / 跨重启递增）；不再写 open 行
            // （回收时一次成档：首行 meta + info 快照 + 被删前文消息）；开了不回 = 号已耗而无文件
            TimebackArchive archive = ResolveTimebackArchive();
            if (archive != null)
            {
                scope.Id = archive.NextId();
            }
            _timebackScope = scope;
            Dictionary<string, object> fields = new Dictionary<string, object>();
            fields["id"] = scope.Id;
            fields["anchor"] = declIndex;
            string body = "timeback #" + scope.Id.ToString() + " 已锚定（锚点 = 本次调用声明 · 节点 " + declIndex.ToString() + "）——查证过程留在作用域内；"
                + "回收时用 back 带回 findings（作为该调用的返回值），两次调用之间的内容一并删除。"
                + "作用域内 Note / sleep / timer 已锁定。";
            LogStore.Add("CatHome4", 1, "timeback #" + scope.Id.ToString() + " 开锚（锚点 " + declIndex.ToString() + " / 用途 " + scope.Purpose + "）", "TIMEBACK");
            return ToolMetaHead.With("timeback", true, fields, body);
        }

        /// <summary>
        /// back——校验后返回结论（findings 即本次工具调用的返回值）；区间删除在工具批后段执行。
        /// </summary>
        /// <param name="findings">带回载荷（事实 + 指针）</param>
        /// <returns>回执 + findings 全文（工具返回值）</returns>
        private string TimebackBack(string findings)
        {
            if (_timebackScope == null)
            {
                return "ERR|TIMEBACK_NO_SCOPE|无未闭合作用域——back 无对象";
            }
            if (findings.Length == 0)
            {
                return "ERR|TIMEBACK_ARGS|back 需要 findings（带回载荷）";
            }
            _timebackScope.PendingFindings = findings;
            _timebackScope.BackDeclIndex = _context.GetMessageCount() - 1;
            // 释放条数预算——区间上下界此刻已定（[start 结果之后 .. back 声明之前]），精确可算；
            // 该值随返回值送出，并与批后实际删除数对账（归档记实际）
            _timebackScope.PendingReleased = CountTimebackReleased(_timebackScope);
            long tokensNow = ContextTokensKnown;
            Dictionary<string, object> fields = new Dictionary<string, object>();
            fields["id"] = _timebackScope.Id;
            fields["anchor"] = _timebackScope.StartDeclIndex;
            fields["released"] = _timebackScope.PendingReleased;
            fields["tokens"] = tokensNow;
            fields["grew"] = tokensNow - _timebackScope.TokensAtOpen;
            return ToolMetaHead.With("timeback", true, fields, findings);
        }

        /// <summary>
        /// 释放条数预算——按当前前文算「[start 结果之后 .. back 声明之前]」的消息条数。
        /// back 时刻区间上下界已定，故返回值给出的条数与批后实际删除数同源可对账（归档记实际，不一致记 L2）。
        /// </summary>
        /// <param name="scope">作用域</param>
        /// <returns>释放条数（无可删区间 = 0）</returns>
        private int CountTimebackReleased(TimebackScope scope)
        {
            LlmMessage[] all = _context.GetMessages();
            int keepEnd = scope.StartDeclIndex;
            while (keepEnd + 1 < all.Length && all[keepEnd + 1].Role == LlmRole.Tool)
            {
                keepEnd = keepEnd + 1;
            }
            int from = keepEnd + 1;
            int to = scope.BackDeclIndex - 1;
            if (from > to || from >= all.Length)
            {
                return 0;
            }
            if (to >= all.Length)
            {
                to = all.Length - 1;
            }
            return to - from + 1;
        }

        /// <summary>
        /// 批后回收执行——工具批结果全部回填后调用（design §12.2）：
        /// ① 前文删除「start 结果之后、back 声明之前」的全部消息（两次调用对与结论保留）
        /// ② 视图层移出「**锚定声明之后**、back 声明（含）」的块 → 合并为一个废弃块（`void`；timeback 自己的卡保留在对话流）
        /// ③ 归档一次成档（A104：首行 meta + 该猫 info 快照 + 被删前文消息）→ 关闭作用域。本轮照常续跑（不置工具主动 done、不注入消息）。
        /// 前文无可删区间（同批 start+back / 索引异常）时跳过 ①，② 仍执行（锚定批的 sibling 结果卡照归废弃段）。
        /// </summary>
        private void ApplyTimebackBack()
        {
            TimebackScope scope = _timebackScope;
            if (scope == null || scope.PendingFindings == null)
            {
                return;
            }
            LlmMessage[] all = _context.GetMessages();
            // 保留终点 = start 声明 + 其完整结果块（同批多调用时结果不止一条——逐条数到非 tool 为止）
            int keepEnd = scope.StartDeclIndex;
            while (keepEnd + 1 < all.Length && all[keepEnd + 1].Role == LlmRole.Tool)
            {
                keepEnd = keepEnd + 1;
            }
            int from = keepEnd + 1;
            int to = scope.BackDeclIndex - 1;
            int removed = 0;
            List<LlmMessage> removedMessages = new List<LlmMessage>();
            // [段0] 现场采集——info 快照取删除之前（归档第二行记录回收动作发生时的状态）
            string infoJson = CollectTimebackInfo();
            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long seconds = (nowMs - scope.StartAtMs) / 1000;
            if (seconds < 0)
            {
                seconds = 0;
            }
            // [段1] 前文删除——保留 [0..保留终点] + [back 声明..尾]（同批 start+back 时区间为空）
            if (from <= to && from < all.Length)
            {
                if (to >= all.Length)
                {
                    to = all.Length - 1;
                }
                int secondStart = scope.BackDeclIndex;
                if (secondStart < keepEnd + 1)
                {
                    secondStart = keepEnd + 1;
                }
                List<LlmMessage> keep = new List<LlmMessage>();
                for (int i = 0; i <= keepEnd && i < all.Length; i = i + 1)
                {
                    keep.Add(all[i]);
                }
                for (int i = secondStart; i < all.Length; i = i + 1)
                {
                    keep.Add(all[i]);
                }
                // 被删前文快照——归档文件的正文（删除前按原序取值）
                for (int i = from; i <= to && i < all.Length; i = i + 1)
                {
                    removedMessages.Add(all[i]);
                }
                removed = all.Length - keep.Count;
                // [段2] 前文落盘——区间删除重写（append-only 的合法例外）
                _context.ReplaceMessages(keep.ToArray());
                LlmMessage[] toSave = _context.GetMessages();
                _lastStats.EntryCount = toSave.Length;
                _store.Rewrite(toSave, _lastStats);
            }
            else if (from <= to)
            {
                // 索引超界（前文被外部改动）——不删，仅出声
                LogStore.Add("CatHome4", 2, "timeback 回收区间越界（from " + from.ToString() + " / to " + to.ToString() + " / len " + all.Length.ToString() + "）——本次未删", "TIMEBACK");
            }
            // [段3] 视图层移出——与前文删除面**解耦**：从**锚定声明之后**起算（比前文删除面宽一段——锚定批的 sibling 结果卡一并归入废弃段；
            // 前文里它们删不得：assistant 声明的 tool_calls 必须与结果配对）。前文无删除区间时同样执行——sibling 卡仍应归段。
            // 上界**含 back 声明**（back 声明之前的删除面不含它）——发起回收那条声明消息的 reason 块同属回收决策过程，一并归段
            // （判例 2026-09-29 莎定：「think 后使用工具，那么这个 think 不被锚回收吗」——三段式须干净为 锚定卡 → 废弃段 → 回收卡，中间不留 think）；
            // timeback 自己的工具卡（锚定 / 回收）保留在对话流（keepToolName 保留面；实时面不重建，屏幕上是正常块）。
            _viewStore.ConvertRangeToVoid(scope.StartDeclIndex + 1, scope.BackDeclIndex, "timeback");
            // [段4] 归档——一次回收一个文件（A104）：首行 meta + 该猫 info 快照 + 被删前文消息
            bool archived = WriteTimebackArchiveFile(scope, removed, removedMessages, infoJson, nowMs, seconds);
            // [段5] 释放条数对账——back 返回值给出的预算 vs 批后实际删除数（归档记实际；不一致必须出声）
            if (removed != scope.PendingReleased)
            {
                LogStore.Add("CatHome4", 2, "timeback #" + scope.Id.ToString() + " 释放条数对账不一致（预算 " + scope.PendingReleased.ToString() + " / 实际 " + removed.ToString() + "）", "TIMEBACK");
            }
            // [段6] 作用域关闭——活跃期结束（TimebackActive 回 false；Note / sleep / timer 解锁）
            _timebackScope = null;
            LogStore.Add("CatHome4", 1, "timeback #" + scope.Id.ToString() + " 已回收 " + removed.ToString() + " 条（" + seconds.ToString() + " 秒 / 锚点 " + scope.StartDeclIndex.ToString() + "）", "TIMEBACK");
            if (!archived)
            {
                LogStore.Add("CatHome4", 2, "timeback #" + scope.Id.ToString() + " 归档落档失败（运行不受影响）", "TIMEBACK");
            }
            // [段7] 浏览器实例关闭——域级生命周期（A110）：浏览器是非主干信息的载体，域收即关（不跨域残留）
            Mau.Runtime.IBrowserLifecycle browserLifecycle;
            Mau.Runtime.DataBox.TryResolve<Mau.Runtime.IBrowserLifecycle>(out browserLifecycle);
            if (browserLifecycle != null)
            {
                browserLifecycle.CloseCat(_catKey);
            }
        }

        /// <summary>
        /// 归档实例解析——懒建（首次使用时按归档目录构造）；provider 未接线或目录为空 → null（归档不可用，功能不阻断）。
        /// </summary>
        /// <returns>归档实例（null=不可用）</returns>
        private TimebackArchive ResolveTimebackArchive()
        {
            if (_timebackArchive != null)
            {
                return _timebackArchive;
            }
            if (TimebackArchiveDirProvider == null)
            {
                LogStore.Add("CatHome4", 2, "timeback 归档目录提供者未接线——本次不落档", "TIMEBACK");
                return null;
            }
            string dir = TimebackArchiveDirProvider(_catKey);
            if (dir == null || dir.Length == 0)
            {
                LogStore.Add("CatHome4", 2, "timeback 归档目录解析为空——本次不落档", "TIMEBACK");
                return null;
            }
            _timebackArchive = new TimebackArchive(dir);
            return _timebackArchive;
        }
        /// <summary>
        /// 归档写出一一一次回收一个文件（A104）：首行 meta + 该猫 info 快照 + 被删前文消息。
        /// 文件名 &lt;编号&gt;-&lt;回收时刻&gt;.jsonl；失败不阻断回收（记 L3 + 写面标志）。
        /// </summary>
        /// <param name="scope">作用域（记录字段数据源）</param>
        /// <param name="removed">实际删除条数</param>
        /// <param name="removedMessages">被删前文消息（原序）</param>
        /// <param name="infoJson">info 快照 JSON（空=不写该行）</param>
        /// <param name="nowMs">回收时刻——Unix 毫秒</param>
        /// <param name="seconds">存活时长（秒）</param>
        /// <returns>true=已落盘</returns>
        private bool WriteTimebackArchiveFile(TimebackScope scope, int removed, List<LlmMessage> removedMessages, string infoJson, long nowMs, long seconds)
        {
            TimebackArchive archive = ResolveTimebackArchive();
            if (archive == null)
            {
                return false;
            }
            TimebackScopeRecord record = new TimebackScopeRecord();
            record.Id = scope.Id;
            record.CatKey = _catKey;
            record.Purpose = scope.Purpose;
            record.Anchor = scope.StartDeclIndex;
            record.StartAt = scope.StartAtMs;
            record.BackAt = nowMs;
            record.Seconds = seconds;
            record.N = removed;
            record.Tokens = ContextTokensKnown;
            record.Grew = ContextTokensKnown - scope.TokensAtOpen;
            record.Released = scope.PendingReleased;
            record.Findings = scope.PendingFindings;
            string path = "";
            bool ok = archive.WriteScopeFile(record, infoJson, removedMessages.ToArray(), out path);
            if (ok)
            {
                LogStore.Add("CatHome4", 1, "timeback #" + scope.Id.ToString() + " 归档落档: " + path, "TIMEBACK");
            }
            return ok;
        }
        /// <summary>
        /// 采集本猫 info 快照——归档文件第二行的现场记录（provider 未接线 / 采集失败 = 空串，不阻断回收）。
        /// </summary>
        /// <returns>info JSON（单行；不可用=空串）</returns>
        private static string CollectTimebackInfo()
        {
            if (TimebackInfoProvider == null)
            {
                return "";
            }
            try
            {
                string info = TimebackInfoProvider();
                if (info == null)
                {
                    return "";
                }
                return info;
            }
            catch (Exception ex)
            {
                LogStore.Add("CatHome4", 2, "timeback info 快照采集失败（归档缺该行）: " + ex.Message, "TIMEBACK");
                return "";
            }
        }
        /// <summary>状态自述步长——作用域内每累计 25 个事件注入一条 user 系统提示（莎 2026-09-30 定：10 → 25）。</summary>
        private const long TimebackNoticeStep = 25;
        /// <summary>
        /// 作用域内事件计数——think / 工具完成 / assistant 产出各计 1（莎 2026-09-28 定）。
        /// 只累加，不触碰前文——注入时机归批后段 FlushTimebackNotice（批中注入会打乱 tool_call 配对）。
        /// </summary>
        private void NoteTimebackEvent()
        {
            TimebackScope scope = _timebackScope;
            if (scope == null)
            {
                return;
            }
            scope.EventCount = scope.EventCount + 1;
        }
        /// <summary>状态提示注入——批后段调用：距上次提示累计满 25 个事件则追加一条角色 user 的系统提示（source=systemauto）。
        /// C1 纪律：只追加新块、绝不回改历史块（前缀一字未动 → 缓存仍命中）。
        /// 🔴 角色是 user 不是 assistant（判例 2026-09-28 · 1.03.049 运行态）：思考模式下注入的 assistant 无 reasoning_content 回传 → 端点 400（`The reasoning_content in the thinking mode must be passed back to the API`）→ 本轮中止；user 注入走既有系统通道，零协议风险。
        /// 提示块落在作用域区间内——回收时与查证过程一并删除（零残留）；本轮照常续跑，不置工具主动 done。</summary>
        private void FlushTimebackNotice()
        {
            TimebackScope scope = _timebackScope;
            if (scope == null)
            {
                return;
            }
            if (scope.EventCount - scope.LastNotifyCount < TimebackNoticeStep)
            {
                return;
            }
            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long seconds = (nowMs - scope.StartAtMs) / 1000;
            if (seconds < 0)
            {
                seconds = 0;
            }
            scope.LastNotifyCount = scope.EventCount;
            string text = "（系统自动 · timeback #" + scope.Id.ToString() + "）你处在 timeback 中，已经历【" + scope.EventCount.ToString()
                + "】条前文条目（已用 " + seconds.ToString() + " 秒）——回收时用 back 带回 findings。";
            AppendMessage(_context.AddUserMessage(text));
            _viewStore.OnUserMessage(LastMessage(), ViewTimestamp(), _context.GetMessageCount() - 1);
            if (_httpHost != null)
            {
                string userJson = "{\"content\":" + JsonUtil.Serialize(text) + ",\"source\":\"systemauto\"}";
                _httpHost.PushView("user", userJson, -1, 0);
            }
            LogStore.Add("CatHome4", 1, "timeback #" + scope.Id.ToString() + " 状态提示注入（累计 " + scope.EventCount.ToString()
                + " 事件 / 已用 " + seconds.ToString() + " 秒）", "TIMEBACK");
        }
        /// <summary>
        /// 活跃作用域快照——info timeback.active 数据源（无作用域 = null）。
        /// </summary>
        /// <returns>字段字典（id / purpose / anchor / startAt / events；无作用域 = null）</returns>
        public Dictionary<string, object> TimebackActiveSnapshot()
        {
            TimebackScope scope = _timebackScope;
            if (scope == null)
            {
                return null;
            }
            Dictionary<string, object> map = new Dictionary<string, object>();
            map["id"] = scope.Id;
            map["purpose"] = scope.Purpose;
            map["anchor"] = scope.StartDeclIndex;
            map["startAt"] = scope.StartAtMs;
            map["events"] = scope.EventCount;
            return map;
        }
        /// <summary>
        /// 提取 timeback 参数中的 action——批内剥离判定专用（start / back 各取首条；A106 批内次序）。
        /// 解析失败 / 缺失 / 非对象 → 空串（该条不剥离，留主体段由执行体按参数面拒绝）。
        /// </summary>
        /// <param name="argsJson">参数 JSON</param>
        /// <returns>action 值（空=无法识别）</returns>
        private static string ExtractTimebackAction(string argsJson)
        {
            if (argsJson == null || argsJson.Length == 0 || !argsJson.StartsWith("{"))
            {
                return "";
            }
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(argsJson))
                {
                    JsonElement root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object)
                    {
                        return "";
                    }
                    JsonElement actionEl;
                    if (!root.TryGetProperty("action", out actionEl) || actionEl.ValueKind != JsonValueKind.String)
                    {
                        return "";
                    }
                    string action = actionEl.GetString();
                    if (action == null)
                    {
                        return "";
                    }
                    return action.Trim();
                }
            }
            catch (Exception)
            {
                return "";
            }
        }

        /// <summary>
        /// 批内后置执行——剥离出的 back 在本批其余工具（含 host-* 延迟直执）全部完成之后、结果回填之前执行
        /// （A106 批内次序 · design-ch4-timeback §2.4）：前文末条仍为本批 assistant 声明，
        /// 故回收区间上界与释放条数预算同源可对账。
        /// 失败（NO_SCOPE / ARGS）照常作为工具返回值送出——批后段因无待回收载荷自然跳过。
        /// </summary>
        private void RunDeferredTimebackBack()
        {
            ToolOrderDog dog = _timebackBackDog;
            if (dog == null)
            {
                return;
            }
            // 消费即清 + 已有结果不覆盖——置空后本字段不再持有该 Dog（下一批由 EnterToolBatch 重建），
            // 同一 Dog 若被重复调起则直接跳过并出声 L2（防回执被覆盖导致观测面失真）
            _timebackBackDog = null;
            if (dog.Result != null && dog.Result.Length > 0)
            {
                LogStore.Add("CatHome4", 2, "timeback 后置执行重入（已有回执——跳过，防覆盖）", "TIMEBACK");
                return;
            }
            dog.Result = ExecuteBuiltin(dog.Name, dog.ArgsJson);
            if (dog.Result == null || dog.Result.Length == 0)
            {
                dog.Result = "ERR|EMPTY_RESULT|工具执行无结果";
            }
            LogStore.Add("CatHome4", 1, "timeback 批内后置执行（back）", "TIMEBACK");
        }

        /// <summary>
        /// timeback 本体修正黑名单（莎 2026-09-28 定）——作用域存活期禁止对 CH4 自身做修正：
        /// 宿主重启 / 热重载 / 自举链（mau-*）/ 全局与每猫配置写入 / 管理指令族。
        /// 判据 = 工具名精确匹配（黑名单式，不用前缀通配——只读面如 host-flows / config-get / config-cat-get / mau-verify 与文本工具不受影响）。
        /// 与 C3 的 Note / sleep / timer 锁定并列：前者防「区间删除语义冲突」，本项防「作用域内改本体」。
        /// </summary>
        /// <param name="name">工具名</param>
        /// <returns>true=作用域内禁用</returns>
        private static bool IsTimebackBodyLocked(string name)
        {
            return name == "majordomo-restart"
                || name == "host-reload"
                || name == "mau-verify"
                || name == "mau-gen"
                || name == "mau-proj"
                || name == "mau-setup"
                || name == "config-set"
                || name == "config-reset"
                || name == "config-cat-set"
                || name == "majordomo-cmd";
        }

        /// <summary>
        /// timeback 域限定工具判定（A110）——仅作用域内可用（域外 ERR|TIMEBACK_REQUIRED）：
        /// image-inject（图片不必常驻主干）· browser-*（网页内容属非主干信息——用完回收）。
        /// 与本体修正黑名单方向相反：黑名单拦「域内调用」，本项拦「域外调用」。
        /// </summary>
        /// <param name="name">工具名</param>
        /// <returns>true=需要活跃作用域</returns>
        private static bool IsTimebackScopedTool(string name)
        {
            if (name == "image-inject")
            {
                return true;
            }
            return name.StartsWith("browser-", StringComparison.Ordinal);
        }
    }
}

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
    /// 用途（2026-10-03 莎裁）：仅两个域限定工具解锁——主干识图（image-inject）· 浏览网页（browser-*）；域内不做工作、不写计划。
    /// findings 口径（A205 · 2026-10-06 莎定）：**只写「成果在哪」，不写「结论是什么」**——域内不下判断，主干按位置回读、以回读到的真实内容为准（治域内复述致幻觉）。
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
            /// <summary>
            /// 本域写操作台账——宿主记录（order ≥ 1 的工具逐条登记：工具名 · 目标标识 · 成败）。
            /// 客观事实面：从实际执行流水提取（不由 LLM 自述）；back 回执附于 findings 之前，主干据此抽样核对。
            /// </summary>
            public List<string> WriteLog = new List<string>();
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
        /// back：载荷即本次调用的返回值（findings 全文——成果定位：位置 + 简短描述，不下结论）——区间删除在工具批后段执行。
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
                    using (JsonDocument doc = JsonUtil.ParseStrict(argsJson))
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
            // [段6] 浏览器实例预热（A123）——域 = 浏览器进程容器：开锚即起进程，锚关即关（CloseCat）；
            // 锚内 browser-* 工具只操作该进程（多页签 = 同一进程内的多个 target），不涉及孤儿与交接。
            // 失败不阻断开锚（浏览器起不来不该让取证任务开不了局）——锚内工具调用会重试并如实返回 ERR
            Mau.Runtime.IBrowserLifecycle browserLifecycle;
            Mau.Runtime.DataBox.TryResolve<Mau.Runtime.IBrowserLifecycle>(out browserLifecycle);
            if (browserLifecycle != null)
            {
                browserLifecycle.PrepareCat(_catKey);
            }
            Dictionary<string, object> fields = new Dictionary<string, object>();
            fields["id"] = scope.Id;
            fields["anchor"] = declIndex;
            fields["purpose"] = scope.Purpose;
            string body = "timeback #" + scope.Id.ToString() + " 已锚定（锚点 = 本次调用声明 · 节点 " + declIndex.ToString() + " · 用途「" + scope.Purpose + "」）——过程留在作用域内；"
                + "回收时用 back 带回 findings（作为该调用的返回值），两次调用之间的内容一并删除。"
                + "作用域内 Note / sleep / timer 已锁定。\n"
                + "findings 只写「成果在哪」，不写「结论是什么」——域内不下判断；主干按位置回读，以回读到的真实内容为准（段内无内容写「（无）」）：\n"
                + "  成果：<逐条：位置（文件:行区间 / URL / 截图路径）+ 一句话说那里是什么——主干据此回读>\n"
                + "  未竟：<没查完 / 没覆盖的>\n"
                + "  卡点：<被什么挡住 / 读不通的地方>\n"
                + "  失败：<失败原因>\n"
                + "每条 ≤1 行 · 每段 ≤5 条；位置必须是这趟真实读到 / 打开过的——没读到的、凭印象复述的一律不写。\n"
                + "例：成果：mau:CatHome4/Program.Tools.cs 的 timeback 描述块——findings 参数说明所在行（行号以你实读到的为准），请回读确认";
            LogStore.Add("CatHome4", 1, "timeback #" + scope.Id.ToString() + " 开锚（锚点 " + declIndex.ToString() + " / 用途 " + scope.Purpose + "）", "TIMEBACK");
            return ToolMetaHead.With("timeback", true, fields, body);
        }

        /// <summary>
        /// back——校验后返回载荷（findings 即本次工具调用的返回值）；区间删除在工具批后段执行。
        /// </summary>
        /// <param name="findings">带回载荷（成果定位：位置 + 简短描述，不下结论）</param>
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
            fields["purpose"] = _timebackScope.Purpose;
            fields["released"] = _timebackScope.PendingReleased;
            fields["tokens"] = tokensNow;
            fields["grew"] = tokensNow - _timebackScope.TokensAtOpen;
            fields["writes"] = _timebackScope.WriteLog.Count;
            // 台账附于 findings 之前——头 = 宿主事实（从执行流水提取，不可编），体 = LLM 自述；主干据此抽样核对
            return ToolMetaHead.With("timeback", true, fields, BuildTimebackWrites() + findings);
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
                _store.Rewrite(toSave);
            }
            else if (from <= to)
            {
                // 索引超界（前文被外部改动）——不删，仅出声
                LogStore.Add("CatHome4", 2, "timeback 回收区间越界（from " + from.ToString() + " / to " + to.ToString() + " / len " + all.Length.ToString() + "）——本次未删", "TIMEBACK");
            }
            // [段3] 视图层——不动（A165 v2 契约：持久即持久，移出 / 废弃面退役；回收只作用于送入 LLM 的前文）
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
            record.Writes = scope.WriteLog;
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
        /// <summary>
        /// 台账回执内联上限——超出后回执只列前 N 条 + 归档指针，全文进 jsonl（不静默截断）。
        /// </summary>
        private const int TimebackWriteLogInlineLimit = 20;
        /// <summary>
        /// 作用域内写操作台账——工具结果回填时登记（order ≥ 1：写入 / 构建执行）。
        /// 判据复用 ToolOrderTable.Resolve（与工具分批调度同源）；只记事实，不做语义判断。
        /// </summary>
        /// <param name="name">工具名</param>
        /// <param name="argsJson">工具参数 JSON</param>
        /// <param name="result">工具结果文本</param>
        private void NoteTimebackWrite(string name, string argsJson, string result)
        {
            TimebackScope scope = _timebackScope;
            if (scope == null)
            {
                return;
            }
            if (ToolOrderTable.Resolve(name, argsJson) < 1)
            {
                return;
            }
            bool ok = true;
            if (result != null && result.Length > 0)
            {
                ok = !result.StartsWith("ERR|", StringComparison.Ordinal) && !result.StartsWith("ROLLED_BACK", StringComparison.Ordinal);
            }
            scope.WriteLog.Add(name + " · " + TimebackWriteTarget(argsJson) + " · " + (ok ? "OK" : "FAIL"));
        }
        /// <summary>台账目标标识——从参数约定键取首个非空值（路径 / 目录类取文件名，标识类原样）；powershell 取命令行原文（外部通道 · 单行指令一次一条，不截断）。</summary>
        /// <param name="argsJson">工具参数 JSON</param>
        /// <returns>目标标识（取不到写「—」）</returns>
        internal static string TimebackWriteTarget(string argsJson)
        {
            if (argsJson == null || argsJson.Length == 0)
            {
                return "—";
            }
            JsonDocument doc = JsonUtil.Parse(argsJson, "timeback.write");
            if (doc == null)
            {
                return "—";
            }
            try
            {
                string[] keys = new string[] { "path", "dest", "src", "file", "proj", "dir", "class", "member" };
                for (int i = 0; i < keys.Length; i = i + 1)
                {
                    string value = JsonUtil.GetStr(doc.RootElement, keys[i], "");
                    if (value.Length > 0)
                    {
                        return ShortenTimebackTarget(value);
                    }
                }
                // powershell——命令行本身即标识（外部通道 · 单行指令一次一条），取原文不截断（莎 2026-10-02 定）
                string cmd = JsonUtil.GetStr(doc.RootElement, "command", "");
                if (cmd.Length > 0)
                {
                    return cmd;
                }
            }
            finally
            {
                doc.Dispose();
            }
            return "—";
        }
        /// <summary>
        /// 目标标识截短——取路径最后一段；超 48 字符截断。
        /// </summary>
        /// <param name="value">原始值</param>
        /// <returns>短标识</returns>
        private static string ShortenTimebackTarget(string value)
        {
            string text = value;
            int cut = text.LastIndexOf('/');
            int cut2 = text.LastIndexOf('\\');
            if (cut2 > cut)
            {
                cut = cut2;
            }
            if (cut >= 0 && cut + 1 < text.Length)
            {
                text = text.Substring(cut + 1);
            }
            if (text.Length > 48)
            {
                text = text.Substring(0, 48);
            }
            return text;
        }
        /// <summary>
        /// 本域写操作台账文本——附于 findings 之前（头 = 宿主事实，体 = LLM 自述）。
        /// 超上限给「计数 + 首 N 条 + 归档指针」，全文进归档 jsonl——不静默截断。
        /// </summary>
        /// <returns>台账段（无写操作返回空串）</returns>
        private string BuildTimebackWrites()
        {
            TimebackScope scope = _timebackScope;
            if (scope == null || scope.WriteLog.Count == 0)
            {
                return "";
            }
            string text = "[本域写操作台账 · 宿主记录 · " + scope.WriteLog.Count.ToString() + " 条]\n";
            int shown = scope.WriteLog.Count;
            if (shown > TimebackWriteLogInlineLimit)
            {
                shown = TimebackWriteLogInlineLimit;
            }
            for (int i = 0; i < shown; i = i + 1)
            {
                text = text + (i + 1).ToString() + ". " + scope.WriteLog[i] + "\n";
            }
            if (shown < scope.WriteLog.Count)
            {
                text = text + "…（余 " + (scope.WriteLog.Count - shown).ToString() + " 条见归档 jsonl）\n";
            }
            return text + "\n";
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
                + "】条前文条目（已用 " + seconds.ToString() + " 秒）——回收时用 back 带回 findings（只写成果位置，不写结论）。";
            AppendMessage(_context.AddUserMessage(text));
            _viewStore.OnUserMessage(LastMessage(), _context.GetMessageCount() - 1);
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
                using (JsonDocument doc = JsonUtil.ParseStrict(argsJson))
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

        /// <summary>timeback 暴毙风险黑名单（莎 2026-09-28 定 · 2026-10-01 宽松化 · 2026-10-02 放行 mau-setup）——只隔离「会让 Mau 框架 / 宿主进程自身当场失效」的行为，判据 = 该动作是否可能立刻中断进程或让作用域上下文失效（重启 / 程序集句柄替换）。拦两类：restart-*（重启族——本轮中断，作用域随内存丢失）· host-reload（换程序集句柄 + 重建工具池，无后悔药）。其余一律放行——只读（mau-verify / host-flows / config-get / cat.list 类指令）、写仓库产物区（mau-gen / mau-proj）、一键链（prepare / sync-html——只写仓库与静态资源，不换程序集）、配置写（config-*）、管理指令（majordomo-cmd）。判据 = 工具名精确匹配（黑名单式，不用前缀通配）。与 C3 的 Note / sleep / timer 锁定并列：前者防「区间删除语义冲突」，本项防「作用域内把本体搞崩」。🔴 本判据是工具边界要求（机制层），不是使用授权——放行不等于该在域内用，开域时机归规范层（环节判据，见工具描述与设计 §三）。</summary>
        /// <param name="name">工具名</param>
        /// <returns>true=作用域内禁用</returns>
        private static bool IsTimebackBodyLocked(string name)
        {
            return IsRestartTool(name)
                || name == "host-reload";
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

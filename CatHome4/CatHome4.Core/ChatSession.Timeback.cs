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
    /// 视图层：删除区间对应的前文派生块转 gap 块（Rebuild 不清——跨宿主重启仍可回看）。
    /// </summary>
    internal sealed partial class ChatSession
    {
        /// <summary>归档路径提供者——catKey → timeback.jsonl 路径（宿主启动期由组合根注入；空=归档不可用）。</summary>
        internal static Func<string, string> TimebackArchivePathProvider;

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
            TimebackArchive archive = ResolveTimebackArchive();
            if (archive != null)
            {
                scope.Id = archive.NextId();
                archive.AppendOpen(scope.Id, _catKey, _round, declIndex, scope.StartAtMs, scope.Purpose, _context.GetMessageCount(), 0);
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
            Dictionary<string, object> fields = new Dictionary<string, object>();
            fields["id"] = _timebackScope.Id;
            fields["anchor"] = _timebackScope.StartDeclIndex;
            return ToolMetaHead.With("timeback", true, fields, findings);
        }

        /// <summary>
        /// 批后回收执行——工具批结果全部回填后调用（design §12.2）：
        /// 删除「start 结果之后、back 声明之前」的全部消息（两次调用对与结论保留）→ 被删区间视图块转 gap
        /// → 归档 close → 关闭作用域。本轮照常续跑（不置工具主动 done、不注入消息）。
        /// 无可删区间（同批 start+back / 索引异常）时仅关闭作用域。
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
            // [段1] 区间删除——保留 [0..保留终点] + [back 声明..尾]（同批 start+back 时区间为空）
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
                removed = all.Length - keep.Count;
                // [段2] 视图层——被删区间的块转 gap（非前文派生，Rebuild 不清：跨重启可回看）
                _viewStore.ConvertRangeToGap(from, to);
                // [段3] 前文落盘——区间删除重写（append-only 的合法例外）
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
            // [段4] 归档 close——回收条数 + 存活秒数 + findings
            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long seconds = (nowMs - scope.StartAtMs) / 1000;
            if (seconds < 0)
            {
                seconds = 0;
            }
            bool archived = true;
            TimebackArchive archive = ResolveTimebackArchive();
            if (archive != null)
            {
                archived = archive.AppendClose(scope.Id, nowMs, removed, seconds, scope.PendingFindings);
            }
            // [段5] 作用域关闭——活跃期结束（TimebackActive 回 false；Note / sleep / timer 解锁）
            _timebackScope = null;
            LogStore.Add("CatHome4", 1, "timeback #" + scope.Id.ToString() + " 已回收 " + removed.ToString() + " 条（" + seconds.ToString() + " 秒 / 锚点 " + scope.StartDeclIndex.ToString() + "）", "TIMEBACK");
            if (!archived)
            {
                LogStore.Add("CatHome4", 2, "timeback #" + scope.Id.ToString() + " 归档落档失败（运行不受影响）", "TIMEBACK");
            }
        }

        /// <summary>
        /// 归档实例解析——懒建（首次使用时按本猫路径构造）；provider 未接线或路径为空 → null（归档不可用，功能不阻断）。
        /// </summary>
        /// <returns>归档实例（null=不可用）</returns>
        private TimebackArchive ResolveTimebackArchive()
        {
            if (_timebackArchive != null)
            {
                return _timebackArchive;
            }
            if (TimebackArchivePathProvider == null)
            {
                LogStore.Add("CatHome4", 2, "timeback 归档路径提供者未接线——本次不落档", "TIMEBACK");
                return null;
            }
            string path = TimebackArchivePathProvider(_catKey);
            if (path == null || path.Length == 0)
            {
                LogStore.Add("CatHome4", 2, "timeback 归档路径解析为空——本次不落档", "TIMEBACK");
                return null;
            }
            _timebackArchive = new TimebackArchive(path);
            return _timebackArchive;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Text.Json;
using Mau.Runtime;

namespace CH4
{
    /// <summary>
    /// 宿主会话实体——timeback 上下文作用域分部（design-ch4-timeback §2 / §三 / §12）。
    /// 语义：取证型任务把膨胀过程关进作用域——start 开锚 → 查证膨胀 → back 回卷（前文截断到锚点 + 一条 findings 结论注入）。
    /// 执行时机：实际回卷在**工具批后段**（PumpToolBatch 尾段调用 ApplyTimebackBack）——工具结果进前文之后才截断，
    /// 避免 tool 消息失去配对声明；执行体（ExecuteTimeback）只做参数校验与登记。
    /// 同轮继续：回卷后置 _toolDone，不新起收尾路径，直接走既有 _round++ → LaunchLlm（莎 2026-09-28 定案）。
    /// 视图层零动作：不重建 / 不清空 / 不推 session_reset（保层——人可见的取证过程原地保留；design §四）。
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

            /// <summary>锚点——不晚于 start 调用时刻的最近安全边界（assistant 正式回复节点）。</summary>
            public int Anchor;

            /// <summary>开锚时刻——Unix 毫秒（状态标记「已用 T 秒」数据源）。</summary>
            public long StartAtMs;

            /// <summary>用途标签——归档与事后判读原料。</summary>
            public string Purpose = "";
        }

        /// <summary>当前未闭合作用域——null=无作用域。</summary>
        private TimebackScope _timebackScope;

        /// <summary>待执行回卷的带回载荷——back 调用暂存，工具批后段消费（null=本批未请求回卷）。</summary>
        private string _timebackPendingFindings;

        /// <summary>归档实例——懒建（首次使用时按本猫路径构造）。</summary>
        private TimebackArchive _timebackArchive;

        /// <summary>
        /// timeback 活跃态——start 之后至 back 回卷完成。
        /// 消费方（活跃期内对所有消费方可见）：QQ 转发豁免（不消费来源 / 不推进游标）、观测面。
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
        /// start：登记作用域（锚点 = 不晚于本刻的最近安全边界）+ 归档 open 行。
        /// back：暂存 findings——实际回卷在工具批后段（ApplyTimebackBack）。
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
        /// start——登记作用域（v1 未闭合前禁止再次 start）。
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
            int anchor = ResolveTimebackAnchor();
            if (anchor < 0)
            {
                return "ERR|TIMEBACK_ANCHOR|前文无可用安全边界（无正式回复节点）";
            }
            TimebackScope scope = new TimebackScope();
            scope.Anchor = anchor;
            scope.StartAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            scope.Purpose = purpose.Length > 48 ? purpose.Substring(0, 48) : purpose;
            TimebackArchive archive = ResolveTimebackArchive();
            if (archive != null)
            {
                scope.Id = archive.NextId();
                archive.AppendOpen(scope.Id, _catKey, _round, anchor, scope.StartAtMs, scope.Purpose, _context.GetMessageCount(), 0);
            }
            _timebackScope = scope;
            Dictionary<string, object> fields = new Dictionary<string, object>();
            fields["id"] = scope.Id;
            fields["anchor"] = anchor;
            string body = "timeback #" + scope.Id.ToString() + " 已锚定（起点=节点 " + anchor.ToString() + "）——查证过程留在作用域内，回收时用 back 带回 findings。";
            LogStore.Add("CatHome4", 1, "timeback #" + scope.Id.ToString() + " 开锚（锚点 " + anchor.ToString() + " / 用途 " + scope.Purpose + "）", "TIMEBACK");
            return ToolMetaHead.With("timeback", true, fields, body);
        }

        /// <summary>
        /// back——校验（须有未闭合作用域 + findings 非空）后暂存载荷；实际回卷在工具批后段执行。
        /// </summary>
        /// <param name="findings">带回载荷（事实 + 指针）</param>
        /// <returns>回执文本</returns>
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
            _timebackPendingFindings = findings;
            Dictionary<string, object> fields = new Dictionary<string, object>();
            fields["id"] = _timebackScope.Id;
            fields["anchor"] = _timebackScope.Anchor;
            string body = "timeback #" + _timebackScope.Id.ToString() + " 已登记回收——本轮结束前回卷到锚点 " + _timebackScope.Anchor.ToString() + "，findings 随下一请求带回。";
            return ToolMetaHead.With("timeback", true, fields, body);
        }

        /// <summary>
        /// 批后回卷执行——工具批结果全部回填后调用（design §12.2）：
        /// 截断前文到锚点 → 归档 close → 注入 findings 结论（user 角色）→ 置工具主动 done；
        /// 视图层零动作（不重建 / 不清空 / 不推 session_reset——保层）。
        /// 无待执行回卷时静默返回（本批未调用 timeback back）。
        /// </summary>
        private void ApplyTimebackBack()
        {
            if (_timebackPendingFindings == null)
            {
                return;
            }
            string findings = _timebackPendingFindings;
            _timebackPendingFindings = null;
            TimebackScope scope = _timebackScope;
            if (scope == null)
            {
                // 防御分支——执行体已校验；到这里即状态不一致（失败必须可见）
                LogStore.Add("CatHome4", 2, "timeback 回卷请求无作用域——已忽略（防御分支）", "TIMEBACK");
                return;
            }
            // [段1] 回收条数——截断前取（锚点之后的前文条数，含本批工具调用与结果）
            int before = _context.GetMessageCount();
            int removed = before - (scope.Anchor + 1);
            if (removed < 0)
            {
                removed = 0;
            }
            TruncateMessages(scope.Anchor + 1);
            // [段2] 归档 close——回收条数 + 存活秒数 + findings
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
                archived = archive.AppendClose(scope.Id, nowMs, removed, seconds, findings);
            }
            // [段3] 结论注入——user 角色（tool 角色无配对声明即协议非法；伪造 assistant 混淆模型历史）
            string conclusion = "（timeback #" + scope.Id.ToString() + " 已回收 " + removed.ToString() + " 条 / " + seconds.ToString() + " 秒）\n" + findings;
            if (!archived)
            {
                conclusion = conclusion + "\n（提示：本次归档落档失败——明细见 runs/err_all.txt）";
            }
            AppendMessage(_context.AddUserMessage(conclusion));
            _viewStore.OnUserMessage(LastMessage(), ViewTimestamp(), _context.GetMessageCount() - 1);
            if (_httpHost != null)
            {
                string userJson = "{\"content\":" + JsonUtil.Serialize(conclusion) + ",\"source\":\"systemauto\"}";
                _httpHost.PushView("user", userJson, -1, 0);
            }
            // [段4] 本轮结束语义 = 工具主动 done（同轮继续——不新起收尾路径；QQ 面据此续约来源）
            _toolDone = true;
            // [段5] 作用域关闭——活跃期结束（TimebackActive 回 false）
            _timebackScope = null;
            LogStore.Add("CatHome4", 1, "timeback #" + scope.Id.ToString() + " 已回收 " + removed.ToString() + " 条（" + seconds.ToString() + " 秒 / 锚点 " + scope.Anchor.ToString() + "）", "TIMEBACK");
        }

        /// <summary>
        /// 锚点解析——不晚于当前时刻的最近安全边界（最晚者优先）：assistant 正式回复（Content>0）或 user 消息。
        /// 硬约束只有一条（C2）：不劈开「工具调用—结果」对——两类节点都是合法交替边界，取更晚的那条（少卷）。
        /// 说明：回卷后结论与下一条 user 相邻（连续 user），端点容忍度确认可行（莎 2026-09-28 批准 B 方案）。
        /// </summary>
        /// <returns>消息索引（-1=无可用边界——前文除 system 外为空）</returns>
        private int ResolveTimebackAnchor()
        {
            LlmMessage[] all = _context.GetMessages();
            for (int i = all.Length - 1; i >= 0; i = i - 1)
            {
                LlmMessage m = all[i];
                if (m.Role == LlmRole.User)
                {
                    return i;
                }
                if (m.Role == LlmRole.Assistant && m.Content != null && m.Content.Length > 0)
                {
                    return i;
                }
            }
            return -1;
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

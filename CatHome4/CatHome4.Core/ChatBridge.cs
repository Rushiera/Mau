using System;
using System.Collections.Generic;
using System.Text.Json;
using Mau.Runtime;

namespace CH4
{
    /// <summary>
    /// 会话协调桥——会话注册表 + 默认猫配置 + 轮转泵 + 会话指令（S1 程序集拆分：Program.Chat 静态面 → Core 实例类）。
    /// 归属：agent 循环基建（Core 层）——LLM 会话调度去中心化；注入提示词构建经委托注入（入口壳 CatCfg 域持有）。
    /// 访问性：internal——仅宿主程序集（CatHome4）经 InternalsVisibleTo 消费；ChatSession 同 internal 契约。
    /// </summary>
    internal sealed class ChatBridge
    {
        // [段1] 默认猫配置面——Bootstrap 从 majordomo cat.cfg 读（M2/M2b/M2c/M2d/R2.3）
        /// <summary>默认会话——P9.1 全部会话指令（Chat/session.new/session clear/count）路由目标；会话寻址扩展在 P9.3</summary>
        private ChatSession _defaultSession;

        /// <summary>默认猫角色段——空=无角色段（M2b）</summary>
        private string _defaultPersona = "";

        /// <summary>默认猫注入清单——空=不注入（M2d）</summary>
        private string[] _defaultInjectList = new string[0];

        /// <summary>默认猫工具声明面——Bootstrap 按 toolNames 裁剪（M2c；session.new 重注入复用）</summary>
        private ToolSpec[] _defaultToolSpecs;

        /// <summary>默认猫 qqbot 配置身份——Guid.Empty=未绑定（R2.3）</summary>
        private Guid _defaultQqBotId = Guid.Empty;

        /// <summary>默认猫 qqbot 启用标志——false=不注入不转发（R2.3）</summary>
        private bool _defaultQqBotEnable = false;

        // [段2] 会话注册表与泵
        /// <summary>会话注册表——PumpSessions 轮转推进（P9.1 含默认会话一席）</summary>
        private readonly List<ChatSession> _sessions = new List<ChatSession>();

        /// <summary>会话新开请求标志——HTTP 线程置位/主线程泵消费（P8.5 session.new——ThreadGuard 契约同 Chat）</summary>
        private volatile bool _sessionNewRequested;

        // [段3] 注入依赖
        /// <summary>注入提示词构建委托——入口壳提供（LoadCatDefaultCfg/FallbackBaseRole——CatCfg 域持有）</summary>
        private readonly Func<WorkspaceConfig, ToolSpec[], string, string[], string> _buildInjectPrompt;

        /// <summary>
        /// 建立会话协调桥——注入提示词构建委托（入口壳 CatCfg 域提供——角色段/注入知识/工具声明拼装）。
        /// </summary>
        /// <param name="buildInjectPrompt">注入提示词构建：workspace/specs/persona/injectList → 系统提示词</param>
        public ChatBridge(Func<WorkspaceConfig, ToolSpec[], string, string[], string> buildInjectPrompt)
        {
            _buildInjectPrompt = buildInjectPrompt;
        }

        /// <summary>默认会话——P9.1 全部会话指令路由目标（Bootstrap 构造后赋值）</summary>
        public ChatSession DefaultSession
        {
            get { return _defaultSession; }
            set { _defaultSession = value; }
        }

        /// <summary>默认猫角色段——空=无角色段（M2b）</summary>
        public string DefaultPersona
        {
            get { return _defaultPersona; }
            set { _defaultPersona = value; }
        }

        /// <summary>默认猫注入清单——空=不注入（M2d）</summary>
        public string[] DefaultInjectList
        {
            get { return _defaultInjectList; }
            set { _defaultInjectList = value; }
        }

        /// <summary>默认猫工具声明面——按 toolNames 裁剪（M2c）</summary>
        public ToolSpec[] DefaultToolSpecs
        {
            get { return _defaultToolSpecs; }
            set { _defaultToolSpecs = value; }
        }

        /// <summary>默认猫 qqbot 配置身份——Guid.Empty=未绑定（R2.3）</summary>
        public Guid DefaultQqBotId
        {
            get { return _defaultQqBotId; }
            set { _defaultQqBotId = value; }
        }

        /// <summary>默认猫 qqbot 启用标志——false=不注入不转发（R2.3）</summary>
        public bool DefaultQqBotEnable
        {
            get { return _defaultQqBotEnable; }
            set { _defaultQqBotEnable = value; }
        }

        /// <summary>会话注册表——编排轮转面（快照/空闲判定遍历共享）</summary>
        public List<ChatSession> Sessions
        {
            get { return _sessions; }
        }

        /// <summary>会话新开请求标志——HTTP 线程置位/主线程泵消费（P8.5 session.new）</summary>
        public bool SessionNewRequested
        {
            get { return _sessionNewRequested; }
            set { _sessionNewRequested = value; }
        }

        /// <summary>
        /// 注册会话进编排轮转表（P9.1 Bootstrap 建立默认会话时调用；注册序 = 推进序）。
        /// </summary>
        /// <param name="session">会话实体</param>
        public void RegisterSession(ChatSession session)
        {
            _sessions.Add(session);
        }

        /// <summary>
        /// 从编排轮转表移除会话（P9.3b cat.delete——会话销毁面）。
        /// </summary>
        /// <param name="session">会话实体</param>
        public void RemoveSession(ChatSession session)
        {
            _sessions.Remove(session);
        }

        /// <summary>
        /// 会话轮转泵——主循环每帧调用：活跃会话各推进一步。LLM 后台流式与工具批等待期间主线程自由泵其他会话——状态机化核心。
        /// </summary>
        public void PumpSessions()
        {
            for (int i = 0; i < _sessions.Count; i++)
            {
                _sessions[i].Pump();
            }
        }

        /// <summary>
        /// 任意会话工具批执行中——reload 忙时拒绝面（原 _toolBatchActive 全局标志语义保全）。
        /// </summary>
        /// <returns>true=有会话在工具批执行期</returns>
        public bool IsAnyToolBatchActive()
        {
            for (int i = 0; i < _sessions.Count; i++)
            {
                if (_sessions[i].ToolBatchActive)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 显式新会话处理——session.new 指令执行体（M2 参数化：按会话 persona/injectList/声明面重注入 + 清前文 + 落盘 + 审计）。
        /// </summary>
        /// <param name="session">目标会话</param>
        /// <param name="persona">角色段（空=仅基础角色）</param>
        /// <param name="injectList">注入清单（空=不注入）</param>
        /// <param name="specs">该会话工具声明面（裁剪后）</param>
        /// <param name="pushChatDone">该会话外观层推送（chatdone 事件；null=不推）</param>
        public void HandleSessionNew(ChatSession session, string persona, string[] injectList, ToolSpec[] specs, Action<int> pushChatDone)
        {
            WorkspaceConfig ws = null;
            DataBox.TryResolve<WorkspaceConfig>(out ws);
            // M3 新会话生效——拦截面同步最新声明面（改 toolNames 后 session.new 才拉取生效）
            session.SetToolSpecs(specs);
            string injectPrompt = _buildInjectPrompt(ws, specs, persona, injectList);
            session.Context.SetSystemPrompt(injectPrompt);
            session.Context.Clear();
            session.Store.Save(session.Context.GetMessages());
            DataBox.Set<string>("global", "chat_state", "idle");
            int injectCount = 0;
            if (injectList != null)
            {
                injectCount = injectList.Length;
            }
            string summary = "session.new | 注入 " + injectCount.ToString() + " 文件 | 前文已清";
            LogStore.Add("CatHome4", 1, summary, "CHAT");
            if (pushChatDone != null)
            {
                pushChatDone(session.Context.GetMessages().Length);
            }
            Console.WriteLine("[CatHome4] " + summary);
        }

        /// <summary>
        /// 构建注入系统提示词——转发注入委托（入口壳 CatCfg 域实现——角色 + 注入知识 + 工具语义声明）。
        /// </summary>
        /// <param name="workspace">工作区配置（roots + inject 清单）</param>
        /// <param name="specs">工具声明表</param>
        /// <param name="persona">角色段</param>
        /// <param name="injectList">注入清单</param>
        /// <returns>系统提示词</returns>
        public string BuildPrompt(WorkspaceConfig workspace, ToolSpec[] specs, string persona, string[] injectList)
        {
            return _buildInjectPrompt(workspace, specs, persona, injectList);
        }

        /// <summary>
        /// 会话调试指令执行——session clear（清空保留 system）/ session count（消息数）/ note.start / note.add；仅主线程调用（泵消费/CLI 直执）。
        /// </summary>
        /// <param name="session">目标会话</param>
        /// <param name="cmd">指令文本</param>
        public void HandleSessionCmd(ChatSession session, string cmd)
        {
            if (cmd == "note.start")
            {
                // M4c Note 启动——主线程执行（泵消费/CLI 直执）
                session.NoteStart();
                return;
            }
            if (cmd.StartsWith("note.add ", StringComparison.Ordinal))
            {
                // M4c Note 手动新增——主线程执行（泵消费/CLI 直执）
                session.NoteAdd(cmd.Substring(9).Trim());
                return;
            }
            if (cmd == "session clear")
            {
                session.Context.Clear();
                session.Store.Save(session.Context.GetMessages());
                Console.WriteLine("[CatHome4] 会话已清空（保留系统提示词）");
                return;
            }
            if (cmd == "session count")
            {
                Console.WriteLine("[CatHome4] 会话消息数: " + session.Context.GetMessageCount());
                return;
            }
        }

        /// <summary>
        /// 构建会话历史视图 JSON——B4 对话区（GET /api/v1/history 回调）。
        /// 会话视图转换：system 跳过；user/assistant 文本直出；assistant tool_calls 与后续 tool 结果配对合入工具卡片（参数 ≤200/结果 ≤300）；
        /// 孤立 tool 丢弃；保留尾部 max 条视图消息；seq 1-based 渲染锚点。
        /// </summary>
        /// <param name="session">目标会话（P9.3 按猫参数化——每猫闭包传各自会话）</param>
        /// <param name="max">视图消息条数上限（1-2000）</param>
        /// <returns>会话视图 JSON</returns>
        public string BuildHistoryView(ChatSession session, int max)
        {
            List<object> view = new List<object>();
            List<Dictionary<string, object>> pendingTools = new List<Dictionary<string, object>>();
            long seq = 0;
            LlmMessage[] all = session.Context.GetMessages();
            for (int i = 0; i < all.Length; i++)
            {
                LlmMessage m = all[i];
                if (m.Role == LlmRole.System)
                {
                    continue;
                }
                if (m.Role == LlmRole.User)
                {
                    pendingTools.Clear();
                    seq = seq + 1;
                    Dictionary<string, object> entry = new Dictionary<string, object>();
                    entry["role"] = "user";
                    entry["content"] = m.Content ?? "";
                    entry["seq"] = seq;
                    view.Add(entry);
                    continue;
                }
                if (m.Role == LlmRole.Assistant)
                {
                    seq = seq + 1;
                    List<object> tools = new List<object>();
                    if (m.ToolCallsJson != null && m.ToolCallsJson.Length > 0)
                    {
                        tools = ParseToolCalls(m.ToolCallsJson);
                    }
                    pendingTools.Clear();
                    for (int t = 0; t < tools.Count; t++)
                    {
                        pendingTools.Add((Dictionary<string, object>)tools[t]);
                    }
                    Dictionary<string, object> aentry = new Dictionary<string, object>();
                    aentry["role"] = "assistant";
                    aentry["content"] = m.Content ?? "";
                    aentry["reasoning"] = m.ReasoningContent ?? "";
                    aentry["tools"] = tools;
                    aentry["seq"] = seq;
                    view.Add(aentry);
                    continue;
                }
                if (m.Role == LlmRole.Tool)
                {
                    // 配对——按 ToolCallId 找待定工具卡（最后一条含该 id 的）
                    Dictionary<string, object> target = null;
                    for (int t = pendingTools.Count - 1; t >= 0; t = t - 1)
                    {
                        Dictionary<string, object> entry = pendingTools[t];
                        string id = "";
                        if (entry.ContainsKey("id") && entry["id"] != null)
                        {
                            id = (string)entry["id"];
                        }
                        if (id.Length > 0 && id == m.ToolCallId)
                        {
                            target = entry;
                            break;
                        }
                    }
                    if (target == null)
                    {
                        continue; // 孤立 tool 丢弃（视图容错）
                    }
                    if (!target.ContainsKey("result"))
                    {
                        target["result"] = TruncateText(m.Content ?? "", 300);
                    }
                }
            }
            // 尾部 max 条——视图消息裁剪（旧消息丢弃）
            if (max > 0 && view.Count > max)
            {
                view.RemoveRange(0, view.Count - max);
            }
            Dictionary<string, object> resp = new Dictionary<string, object>();
            resp["version"] = 1;
            resp["sessionId"] = session.Id;
            // count = 原始消息数（含 system/tool——与快照 sessions 段 msgCount 同源一致；视图裁剪只影响 messages 不缩计数）
            resp["count"] = all.Length;
            resp["messages"] = view;
            return JsonSerializer.Serialize(resp);
        }

        /// <summary>
        /// 解析 OpenAI tool_calls JSON 数组——视图工具卡 {id,name,arguments 截断}
        /// </summary>
        /// <param name="json">tool_calls JSON</param>
        /// <returns>工具卡列表（解析失败空列表——容错）</returns>
        private List<object> ParseToolCalls(string json)
        {
            List<object> list = new List<object>();
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    JsonElement root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Array)
                    {
                        return list;
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
                        Dictionary<string, object> entry = new Dictionary<string, object>();
                        entry["id"] = id;
                        entry["name"] = name;
                        entry["arguments"] = TruncateText(arguments, 200);
                        list.Add(entry);
                    }
                }
            }
            catch (Exception)
            {
                // 解析失败——空卡片列表（容错）
            }
            return list;
        }

        /// <summary>
        /// 读取 JSON 对象字符串属性——防御式（缺字段返回空串）
        /// </summary>
        /// <param name="obj">JSON 对象</param>
        /// <param name="prop">属性名</param>
        /// <returns>属性值</returns>
        private string GetStringProp(JsonElement obj, string prop)
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
        /// 文本截断——超长保留头部 + 截断提示（视图/落盘共用）
        /// </summary>
        /// <param name="text">原文</param>
        /// <param name="max">上限字符数</param>
        /// <returns>截断文本</returns>
        private string TruncateText(string text, int max)
        {
            if (text == null)
            {
                return "";
            }
            if (text.Length <= max)
            {
                return text;
            }
            return text.Substring(0, max) + "…[截断:原" + text.Length.ToString() + "字符]";
        }
    }
}

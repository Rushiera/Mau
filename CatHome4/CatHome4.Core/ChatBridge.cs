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
        private readonly Func<WorkspaceConfig, ToolSpec[], string, string[], InjectPromptResult> _buildInjectPrompt;

        /// <summary>
        /// 建立会话协调桥——注入提示词构建委托（入口壳 CatCfg 域提供——角色段/注入知识/工具声明拼装）。
        /// </summary>
        /// <param name="buildInjectPrompt">注入提示词构建：workspace/specs/persona/injectList → 提示词 + 逐文件结果</param>
        public ChatBridge(Func<WorkspaceConfig, ToolSpec[], string, string[], InjectPromptResult> buildInjectPrompt)
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
            InjectPromptResult injectResult = _buildInjectPrompt(ws, specs, persona, injectList);
            session.Context.SetSystemPrompt(injectResult.Prompt);
            session.Context.Clear();
            // E3 真实 usage 统计——新会话零统计起算
            session.ResetStats();
            session.Store.Save(session.Context.GetMessages(), session.LastStats);
            // F4 视图——session.new 清前文 → 视图随生命周期清空
            session.ClearView();
            // 注入报告——逐文件结果持久化进视图（独立字段：Rebuild 不清，Save 落盘；前端 history 首块渲染）
            session.SetInjectReport(BuildInjectReportJson(injectResult.Files, injectList));
            // 问题一修复——会话重置显式事件（前端收到后清空气泡再拉 history——消除清空竞态）
            session.PushSessionReset();
            DataBox.Set<string>("global", "chat_state", "idle");
            int injectCount = 0;
            if (injectList != null)
            {
                injectCount = injectList.Length;
            }
            string summary = "会话已重建：注入 " + injectCount.ToString() + " 个知识文件，前文已清空";
            LogStore.Add("CatHome4", 1, summary, "CHAT");
            if (pushChatDone != null)
            {
                pushChatDone(session.Context.GetMessages().Length);
            }
            
        }

        /// <summary>
        /// 构建注入报告 JSON——逐文件结果（前端 history 首块渲染；ok/missing/error 三态 + 字符数）
        /// </summary>
        /// <param name="files">逐文件结果（委托产物）</param>
        /// <param name="injectList">注入清单（null=空）</param>
        /// <returns>注入报告 JSON 字符串</returns>
        private static string BuildInjectReportJson(List<InjectFileResult> files, string[] injectList)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append("{\"files\":[");
            int total = 0;
            int ok = 0;
            int missing = 0;
            int failed = 0;
            if (files != null)
            {
                for (int i = 0; i < files.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(",");
                    }
                    InjectFileResult f = files[i];
                    string status = f.Status;
                    if (status == "ok") { ok = ok + 1; }
                    else if (status == "missing") { missing = missing + 1; }
                    else { failed = failed + 1; }
                    sb.Append("{\"file\":");
                    sb.Append(JsonSerializer.Serialize(f.File));
                    sb.Append(",\"status\":");
                    sb.Append(JsonSerializer.Serialize(status));
                    sb.Append(",\"message\":");
                    sb.Append(JsonSerializer.Serialize(f.Message ?? ""));
                    sb.Append(",\"chars\":");
                    sb.Append(f.Chars.ToString());
                    sb.Append("}");
                    total = total + 1;
                }
            }
            sb.Append("],\"total\":");
            sb.Append(total.ToString());
            sb.Append(",\"ok\":");
            sb.Append(ok.ToString());
            sb.Append(",\"missing\":");
            sb.Append(missing.ToString());
            sb.Append(",\"failed\":");
            sb.Append(failed.ToString());
            sb.Append(",\"injectCount\":");
            sb.Append(injectList != null ? injectList.Length.ToString() : "0");
            sb.Append("}");
            return sb.ToString();
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
            return _buildInjectPrompt(workspace, specs, persona, injectList).Prompt;
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
                // 问题一附带——session clear 同步清视图（视图随生命周期清理；注入报告保留——非会话轮次产物）
                session.ClearView();
                LogStore.Add("CatHome4", 1, "会话已清空（保留系统提示词）", "CMD");
                return;
            }
            if (cmd == "session count")
            {
                LogStore.Add("CatHome4", 1, "会话消息数: " + session.Context.GetMessageCount().ToString(), "CMD");
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
            ViewBlock[] blocks = session.GetViewBlocks();
            // 尾部 max 块——视图块裁剪（旧块丢弃；前端固定拉尾部 100）
            int start = 0;
            if (max > 0 && blocks.Length > max)
            {
                start = blocks.Length - max;
            }
            List<object> view = new List<object>();
            long seq = 0;
            for (int i = start; i < blocks.Length; i++)
            {
                seq = seq + 1;
                ViewBlock b = blocks[i];
                Dictionary<string, object> entry = new Dictionary<string, object>();
                entry["seq"] = seq;
                entry["id"] = b.Id;
                entry["renderType"] = b.RenderType;
                entry["payload"] = ParseViewPayload(b.Payload);
                view.Add(entry);
            }
            Dictionary<string, object> resp = new Dictionary<string, object>();
            resp["version"] = 1;
            resp["sessionId"] = session.Id;
            resp["count"] = blocks.Length;
            resp["blocks"] = view;
            // E3 真实 usage 统计——history 载荷携带（前端状态栏显示；零估算）
            SessionStats st = session.LastStats;
            Dictionary<string, object> stats = new Dictionary<string, object>();
            stats["entryCount"] = st.EntryCount;
            stats["prompt"] = st.LastPromptTokens;
            stats["cacheHit"] = st.LastCacheHitTokens;
            stats["completion"] = st.LastCompletionTokens;
            stats["context"] = st.LastContextTokens;
            resp["stats"] = stats;
            return JsonSerializer.Serialize(resp);
        }
/// <summary>
/// 视图块载荷 JSON 字符串 → JSON 元素（history 响应内嵌对象；解析失败回退字符串）
/// </summary>
/// <param name = "json">载荷 JSON 字符串</param>
/// <returns>JSON 元素或原字符串</returns>
private object ParseViewPayload(string json)
{
    try
    {
        using (JsonDocument doc = JsonDocument.Parse(json))
        {
            return doc.RootElement.Clone();
        }
    }
    catch (Exception)
    {
        return json;
    }
}    }
}

    /// <summary>
    /// 注入提示词构建结果——提示词 + 逐文件结果（问题二：前文加载明细可见性）。
    /// 只承载结果——不再裸拼字符串（入口壳 BuildInjectPrompt 产物）。
    /// </summary>
    internal sealed class InjectPromptResult
    {
        /// <summary>提示词——角色 + 注入知识 + 工具语义声明（原文拼装产物）</summary>
        public string Prompt;

        /// <summary>逐文件结果——按注入清单序（ok/missing/error）</summary>
        public List<InjectFileResult> Files;
    }

    /// <summary>
    /// 注入文件结果——单文件加载明细（前端注入报告视图块渲染单元）。
    /// </summary>
    internal sealed class InjectFileResult
    {
        /// <summary>文件路径——注入清单原文（受控根 id: 或绝对路径）</summary>
        public string File;

        /// <summary>状态——ok=成功 / missing=缺失跳过 / error=读取异常</summary>
        public string Status;

        /// <summary>补充信息——error 时错误摘要；ok 时可为空</summary>
        public string Message;

        /// <summary>成功时字符数——注入内容长度（0=失败）</summary>
        public int Chars;
    }

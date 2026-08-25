using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using Mau.Runtime;

namespace CH4
{
    /// <summary>
    /// Program 的 ChatBridge 分部——会话中枢工具协调（P5：MajorDomoCat 工具循环 / P8 B1：工具表驱动直执）。
    /// 归属：agent 循环基建（CH4 宿主侧）——LLM 调用 + tool_calls 分发 + 结果回传 + 前文落盘。
    /// 工具声明表与执行器在 Program.Tools.cs（P8 一期：宿主直执；二期 OA 工单化）。
    /// </summary>
    public static partial class Program
    {
        /// <summary>
        /// 宿主主线程 ID——Main 开头记录（HTTP 线程分流判断：Chat 指令跨线程 Tick 违规——Inbox 泵）
        /// </summary>
        private static int _mainThreadId;

        /// <summary>
        /// LLM 运行时——ChatStream 调度（Bootstrap 注入）
        /// </summary>
        private static ILlmRuntime _llmRuntime;

        /// <summary>
        /// 工具定义——P8 B1 表驱动 7 件（BuildToolSpecs——Program.Tools.cs）
        /// </summary>
        private static ToolSpec[] _tools;

        /// <summary>
        /// 默认会话——P9.1 全部会话指令（Chat/session.new/session clear/count）路由目标；会话寻址扩展在 P9.3
        /// </summary>
        private static ChatSession _defaultSession;

        /// <summary>默认猫角色段——Bootstrap 从 majordomo cat.cfg 读（M2b；空=无角色段）</summary>
        private static string _defaultPersona = "";

        /// <summary>默认猫注入清单——Bootstrap 从 majordomo cat.cfg 读（M2d；空=不注入）</summary>
        private static string[] _defaultInjectList = new string[0];

        /// <summary>默认猫工具声明面——Bootstrap 按 toolNames 裁剪（M2c；session.new 重注入复用）</summary>
        private static ToolSpec[] _defaultToolSpecs;

        /// <summary>
        /// 会话注册表——PumpSessions 轮转推进（P9.1 含默认会话一席）
        /// </summary>
        private static readonly List<ChatSession> _sessions = new List<ChatSession>();

        /// <summary>
        /// 会话新开请求标志——HTTP 线程置位/主线程泵消费（P8.5 session.new——ThreadGuard 契约同 Chat）
        /// </summary>
        private static volatile bool _sessionNewRequested;

        /// <summary>
        /// 工具单 Dog owner ID——宿主 Dog 域（OA 未开存活校验——任意 ID 可 Post；多 Dog 未来可扩展独立 ID）
        /// </summary>
        private const long ToolOwnerId = 1;

        /// <summary>
        /// 显式新会话处理——session.new 指令执行体（M2 参数化：按会话 persona/injectList/声明面重注入 + 清前文 + 落盘 + 审计）。
        /// </summary>
        /// <param name="session">目标会话</param>
        /// <param name="persona">角色段（空=仅基础角色）</param>
        /// <param name="injectList">注入清单（空=不注入）</param>
        /// <param name="specs">该会话工具声明面（裁剪后）</param>
        /// <param name="host">该会话外观层（PushChatDone；默认猫主端口）</param>
        private static void HandleSessionNew(ChatSession session, string persona, string[] injectList, ToolSpec[] specs, HttpHost host)
        {
            WorkspaceConfig ws = null;
            DataBox.TryResolve<WorkspaceConfig>(out ws);
            // M3 新会话生效——拦截面同步最新声明面（改 toolNames 后 session.new 才拉取生效）
            session.SetToolSpecs(specs);
            string injectPrompt = BuildInjectPrompt(ws, specs, persona, injectList);
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
            LogStore.Add("CH4.Entry", 1, summary, "CHAT");
            if (host != null)
            {
                host.PushChatDone();
            }
            Console.WriteLine("[CH4.Entry] " + summary);
        }

        /// <summary>
        /// 构建注入系统提示词——P8.5 会话配置化：角色 + 注入知识（workspace.json inject 清单按序读取，来源标注显式）+ 工具语义声明。
        /// 注入是宿主侧静态动作——新会话/显式 session.new 时调用一次，不随每轮携带。
        /// </summary>
        /// <param name="workspace">工作区配置（roots + inject 清单）</param>
        /// <param name="specs">工具声明表</param>
        /// <returns>系统提示词</returns>
        private static string BuildInjectPrompt(WorkspaceConfig workspace, ToolSpec[] specs, string persona, string[] injectList)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            // [段0] 基础角色段——全局模板 baseRole（空=无基础角色行；模板缺失回退内置文案——行为不倒退）
            CatDefaultCfgData tpl = LoadCatDefaultCfg();
            string baseRole = FallbackBaseRole;
            if (tpl != null && tpl.BaseRole != null)
            {
                baseRole = tpl.BaseRole.Trim();
            }
            if (baseRole.Length > 0)
            {
                sb.Append(baseRole);
            }
            // [段0] 角色段——cat.cfg persona 非空追加（M2b：注入后追加角色段；空=仅基础角色）
            if (persona != null && persona.Trim().Length > 0)
            {
                sb.Append(System.Environment.NewLine);
                sb.Append(System.Environment.NewLine);
                sb.Append("【角色设定】");
                sb.Append(persona.Trim());
            }
            // [段1] 注入知识——按每猫 injectList 顺序读取（M2d：不再走全局 workspace.inject；寻址复用受控根 id: 命名空间；缺失跳过不阻断会话）
            if (workspace != null && injectList != null && injectList.Length > 0)
            {
                sb.Append(System.Environment.NewLine);
                sb.Append(System.Environment.NewLine);
                sb.Append("【系统前文来源】以下知识文件由本会话注入（清单 cat.cfg injectList）：");
                for (int i = 0; i < injectList.Length; i++)
                {
                    string file = injectList[i];
                    try
                    {
                        WorkspaceConfig.InjectEntry entry = new WorkspaceConfig.InjectEntry();
                        entry.File = file;
                        entry.Optional = true;
                        entry.Label = file;
                        string path = workspace.ResolveInjectFile(entry);
                        if (!File.Exists(path))
                        {
                            LogStore.Add("CH4.Entry", 2, "inject.missing | optional | " + file, "INJECT");
                            continue;
                        }
                        string content = File.ReadAllText(path);
                        sb.Append(System.Environment.NewLine);
                        sb.Append(System.Environment.NewLine);
                        sb.Append("===== 注入文件: ");
                        sb.Append(file);
                        sb.Append(" =====");
                        sb.Append(System.Environment.NewLine);
                        sb.Append(content);
                    }
                    catch (Exception ex)
                    {
                        LogStore.Add("CH4.Entry", 2, "inject.fail | " + file + " | " + ex.Message, "INJECT");
                    }
                }
            }
            // [段2] 工具声明——从工具表动态生成
            sb.Append(System.Environment.NewLine);
            sb.Append(System.Environment.NewLine);
            sb.Append("你有 " + specs.Length.ToString() + " 个工具：");
            for (int i = 0; i < specs.Length; i++)
            {
                sb.Append(System.Environment.NewLine);
                sb.Append("- ");
                sb.Append(specs[i].Name);
                sb.Append(": ");
                sb.Append(specs[i].Description);
            }
            sb.Append(System.Environment.NewLine);
            sb.Append("工具结果返回后，基于结果继续回答用户；修改语料前先读，改完用 mau-verify 验证。");
            return sb.ToString();
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
        /// 从 arguments JSON 提取参数——防御式（解析失败返回空串）
        /// </summary>
        /// <param name="argumentsJson">参数 JSON</param>
        /// <param name="key">参数名</param>
        /// <returns>参数值</returns>
        private static string ExtractArg(string argumentsJson, string key)
        {
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(argumentsJson))
                {
                    return GetStringProp(doc.RootElement, key);
                }
            }
            catch (Exception)
            {
                // 参数 JSON 损坏——返回空串（下游 BAD_ARGS 校验可见拒绝）
                return "";
            }
        }

        /// <summary>
        /// 构建会话历史视图 JSON——B4 对话区（GET /api/v1/history 回调）。
        /// 会话视图转换：system 跳过；user/assistant 文本直出；assistant tool_calls 与后续 tool 结果配对合入工具卡片（参数 ≤200/结果 ≤300）；
        /// 孤立 tool 丢弃；保留尾部 max 条视图消息；seq 1-based 渲染锚点。
        /// </summary>
        /// <param name="session">目标会话（P9.3 按猫参数化——每猫 HttpHost 闭包传各自会话）</param>
        /// <param name="max">视图消息条数上限（1-2000）</param>
        /// <returns>会话视图 JSON</returns>
        internal static string BuildHistoryView(ChatSession session, int max)        {
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
            resp["count"] = view.Count;
            resp["messages"] = view;
            return JsonSerializer.Serialize(resp);
        }

        /// <summary>
        /// 解析 OpenAI tool_calls JSON 数组——视图工具卡 {id,name,arguments 截断}
        /// </summary>
        /// <param name="json">tool_calls JSON</param>
        /// <returns>工具卡列表（解析失败空列表——容错）</returns>
        private static List<object> ParseToolCalls(string json)
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
        /// 文本截断——超长保留头部 + 截断提示（视图/落盘共用）
        /// </summary>
        /// <param name="text">原文</param>
        /// <param name="max">上限字符数</param>
        /// <returns>截断文本</returns>
        private static string TruncateText(string text, int max)
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

        /// <summary>
        /// 注册会话进编排轮转表（P9.1 Bootstrap 建立默认会话时调用；注册序 = 推进序）。
        /// </summary>
        /// <param name="session">会话实体</param>
        private static void RegisterSession(ChatSession session)
        {
            _sessions.Add(session);
        }

        /// <summary>
        /// 从编排轮转表移除会话（P9.3b cat.delete——会话销毁面）。
        /// </summary>
        /// <param name="session">会话实体</param>
        private static void RemoveSession(ChatSession session)
        {
            _sessions.Remove(session);
        }

        /// <summary>
        /// 会话轮转泵——主循环每帧调用：活跃会话各推进一步。LLM 后台流式与工具批等待期间主线程自由泵其他会话——状态机化核心。
        /// </summary>
        private static void PumpSessions()
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
        private static bool IsAnyToolBatchActive()
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
    }
}
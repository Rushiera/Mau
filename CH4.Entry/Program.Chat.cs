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
        /// 会话上下文——MajorDomoCat 消息历史（前文持久化面）
        /// </summary>
        private static ChatContext _chatContext;

        /// <summary>
        /// 会话前文管理器——Data/sessions/majordomo.json 落盘（重启恢复）
        /// </summary>
        private static SessionStore _sessionStore;

        /// <summary>
        /// 工具定义——P8 B1 表驱动 7 件（BuildToolSpecs——Program.Tools.cs）
        /// </summary>
        private static ToolSpec[] _tools;

        /// <summary>
        /// LLM 后台运行中标志（Task.Run 消费 ChatStream）
        /// </summary>
        private static volatile bool _llmBusy;

        /// <summary>
        /// LLM 后台结果——完整回复文本
        /// </summary>
        private static string _llmResultText;

        /// <summary>
        /// LLM 后台结果——完整思考文本（工具轮次回传铁律）
        /// </summary>
        private static string _llmReasoning;

        /// <summary>
        /// LLM 后台结果——tool_calls JSON 数组（聚合后整体）
        /// </summary>
        private static string _llmToolCallsJson;

        /// <summary>
        /// LLM 后台错误标志
        /// </summary>
        private static bool _llmError;

        /// <summary>
        /// LLM 后台错误文本（ERR| 前缀——失败可见性）
        /// </summary>
        private static string _llmErrorText;

        /// <summary>
        /// 工具循环收敛上限——P8.5b 自举全链上调 3→10（读→改→verify→proj→reload 多批工具调用；收敛语义由 LLM 判断完成）
        /// </summary>
        private const int MaxToolRounds = 10;

        /// <summary>
        /// 会话新开请求标志——HTTP 线程置位/主线程泵消费（P8.5 session.new——ThreadGuard 契约同 Chat）
        /// </summary>
        private static volatile bool _sessionNewRequested;

        /// <summary>
        /// 工具批次执行中标志——ExecuteToolBatch 置位/复位；reload 忙时拒绝（防旧批次完成信号永不置位 → 空转 25 分钟）
        /// </summary>
        private static bool _toolBatchActive;

        /// <summary>
        /// 工具单 Dog owner ID——宿主 Dog 域（OA 未开存活校验——任意 ID 可 Post；多 Dog 未来可扩展独立 ID）
        /// </summary>
        private const long ToolOwnerId = 1;

        /// <summary>
        /// 后台消费 LLM 流——消息序列 + 工具定义 → 文本/思考/tool_calls 累积（Task.Run——主线程零阻塞）。
        /// </summary>
        /// <param name="messages">消息序列</param>
        /// <param name="tools">工具定义</param>
        private static void RunLlmInBackground(LlmMessage[] messages, ToolSpec[] tools)
        {
            _llmBusy = true;
            _llmResultText = "";
            _llmReasoning = "";
            _llmToolCallsJson = "";
            _llmError = false;
            _llmErrorText = "";
            System.Threading.Tasks.Task.Run(async delegate
            {
                try
                {
                    System.Text.StringBuilder text = new System.Text.StringBuilder();
                    System.Text.StringBuilder reasoning = new System.Text.StringBuilder();
                    string toolCalls = "";
                    await foreach (LlmStreamEvent ev in _llmRuntime.ChatStream(messages, tools))
                    {
                        if (ev.Kind == LlmStreamKind.Text)
                        {
                            text.Append(ev.Text);
                            // P6 外观层转发——LLM 增量实时推送 SSE（协议 §4.2 llm 事件）
                            if (_httpHost != null)
                            {
                                _httpHost.PushLlm("text", ev.Text);
                            }
                        }
                        else if (ev.Kind == LlmStreamKind.Reasoning)
                        {
                            reasoning.Append(ev.Text);
                            if (_httpHost != null)
                            {
                                _httpHost.PushLlm("reasoning", ev.Text);
                            }
                        }
                        else if (ev.Kind == LlmStreamKind.ToolCalls)
                        {
                            toolCalls = ev.Text;
                            if (_httpHost != null)
                            {
                                _httpHost.PushLlm("toolCalls", ev.Text);
                            }
                        }
                        else if (ev.Kind == LlmStreamKind.Done)
                        {
                            if (_httpHost != null)
                            {
                                _httpHost.PushLlm("done", "");
                            }
                        }
                        else if (ev.Kind == LlmStreamKind.Error)
                        {
                            _llmError = true;
                            _llmErrorText = ev.Text;
                            if (_httpHost != null)
                            {
                                _httpHost.PushLlm("error", ev.Text);
                            }
                        }
                    }
                    _llmResultText = text.ToString();
                    _llmReasoning = reasoning.ToString();
                    _llmToolCallsJson = toolCalls;
                    // L1-META 结算行（D5 分级——观测全链：LLM 流完成一行为准，SSE log 事件实时可见）
                    string llmSummary = "llm STREAM 完成 | text=" + text.Length.ToString() + " | tools=";
                    if (toolCalls.Length > 0)
                    {
                        llmSummary = llmSummary + "Y";
                    }
                    else
                    {
                        llmSummary = llmSummary + "N";
                    }
                    LogStore.Add("LLM", 0, llmSummary, "LLM");
                }
                catch (Exception ex)
                {
                    _llmError = true;
                    _llmErrorText = "ERR|" + ex.GetType().Name + "|" + ex.Message;
                }
                finally
                {
                    _llmBusy = false;
                }
            });
        }

        /// <summary>
        /// 等待 LLM 后台完成——帧驱动 + 帧上限兜底（超时按错误回传）
        /// </summary>
        private static void WaitForLlm()
        {
            for (int i = 0; i < MaxFramesPerRun; i++)
            {
                if (!_llmBusy)
                {
                    return;
                }
                _runner.Tick();
                // O 系列：LLM 等待期间快照/帧流持续泵（旧缺口——LLM 处理期间 SSE 无快照、帧流空白）
                if (_httpHost != null)
                {
                    _httpHost.PumpMainThread();
                }
                Thread.Sleep(FrameSleepMs);
            }
            _llmBusy = false;
            _llmError = true;
            _llmErrorText = "ERR|LLM_TIMEOUT|LLM 调用超时（帧上限 " + MaxFramesPerRun + "）";
        }

        /// <summary>
        /// 显式新会话处理——session.new 指令执行体：按清单重新注入 system + 清前文 + 落盘 + 审计（P8.5 design-ch4-workspace §六）
        /// </summary>
        private static void HandleSessionNew()
        {
            WorkspaceConfig ws = null;
            bool wsBound = DataBox.TryResolve<WorkspaceConfig>(out ws);
            ToolSpec[] specs = BuildToolSpecs();
            string injectPrompt = BuildInjectPrompt(ws, specs);
            _chatContext.SetSystemPrompt(injectPrompt);
            _chatContext.Clear();
            _sessionStore.Save(_chatContext.GetMessages());
            DataBox.Set<string>("global", "chat_state", "idle");
            int injectCount = 0;
            if (ws != null)
            {
                injectCount = ws.Inject.Length;
            }
            string summary = "session.new | 注入 " + injectCount.ToString() + " 文件 | 前文已清";
            LogStore.Add("CH4.Entry", 1, summary, "CHAT");
            if (_httpHost != null)
            {
                _httpHost.PushChatDone();
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
        private static string BuildInjectPrompt(WorkspaceConfig workspace, ToolSpec[] specs)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append("你是 MajorDomoCat——CH4 自举宿主的管理员对话中枢（P8.5 会话配置化）。");
            // [段1] 注入知识——按清单顺序读取（optional 容错跳过 + 审计；必选缺失警告 + 跳过——会话仍可用）
            if (workspace != null && workspace.Inject.Length > 0)
            {
                sb.Append(System.Environment.NewLine);
                sb.Append(System.Environment.NewLine);
                sb.Append("【系统前文来源】以下知识文件由本会话注入（清单 workspace.json inject）：");
                for (int i = 0; i < workspace.Inject.Length; i++)
                {
                    WorkspaceConfig.InjectEntry entry = workspace.Inject[i];
                    string label = entry.Label;
                    if (label.Length == 0)
                    {
                        label = entry.File;
                    }
                    try
                    {
                        string path = workspace.ResolveInjectFile(entry);
                        if (!File.Exists(path))
                        {
                            if (entry.Optional)
                            {
                                LogStore.Add("CH4.Entry", 2, "inject.missing | optional | " + label, "INJECT");
                            }
                            else
                            {
                                LogStore.Add("CH4.Entry", 2, "inject.missing | 必选 | " + label, "INJECT");
                            }
                            continue;
                        }
                        string content = File.ReadAllText(path);
                        sb.Append(System.Environment.NewLine);
                        sb.Append(System.Environment.NewLine);
                        sb.Append("===== 注入文件: ");
                        sb.Append(label);
                        sb.Append("（");
                        sb.Append(entry.File);
                        sb.Append("）=====");
                        sb.Append(System.Environment.NewLine);
                        sb.Append(content);
                    }
                    catch (Exception ex)
                    {
                        LogStore.Add("CH4.Entry", 2, "inject.fail | " + label + " | " + ex.Message, "INJECT");
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
            sb.Append("工具结果返回后，基于结果继续回答用户；修改语料前先读，改完用 mau.verify 验证。");
            return sb.ToString();
        }

        /// <summary>
        /// 构建工具定义——P8 B1 表驱动（7 件集中声明——Program.Tools.cs BuildToolSpecs）
        /// </summary>
        /// <returns>工具数组</returns>
        private static ToolSpec[] BuildTools()
        {
            return BuildToolSpecs();
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
                return "";
            }
        }

        /// <summary>
        /// 截断显示文本——控制台防刷屏
        /// </summary>
        /// <param name="text">原文</param>
        /// <param name="max">上限</param>
        /// <returns>截断文本</returns>
        private static string TrimDisplay(string text, int max)
        {
            if (text == null)
            {
                return "";
            }
            if (text.Length <= max)
            {
                return text;
            }
            return text.Substring(0, max) + "...";
        }

        /// <summary>
        /// 执行工具批——解析 tool_calls JSON → 逐工具宿主直执（P8 一期：无 OA 无语料握手）→ 按序回传上下文。
        /// 未知工具 ERR 回传；空结果 ERR|EMPTY_RESULT（错误可见性铁律）；批量执行期 reload 拒绝（_toolBatchActive）。
        /// </summary>
        /// <param name="toolCallsJson">tool_calls JSON 数组</param>
        private static void ExecuteToolBatch(string toolCallsJson)
        {
            _toolBatchActive = true;
            try
            {
                // [段1] OA 发单——逐工具 ToolOrderDog（CH2 Dog 机制：officeName=工具名 / 载荷 args=整包参数 JSON；宿主=Dog owner）
                List<ToolOrderDog> hostDogs = new List<ToolOrderDog>();
                List<ToolOrderDog> dogs = new List<ToolOrderDog>();
                using (JsonDocument doc = JsonDocument.Parse(toolCallsJson))
                {
                    JsonElement root = doc.RootElement;
                    for (int i = 0; i < root.GetArrayLength(); i++)
                    {
                        JsonElement call = root[i];
                        string id = GetStringProp(call, "id");
                        // OpenAI wire：name/arguments 在 function 嵌套对象内
                        string name = "";
                        string arguments = "";
                        JsonElement funcEl;
                        if (call.TryGetProperty("function", out funcEl))
                        {
                            name = GetStringProp(funcEl, "name");
                            arguments = GetStringProp(funcEl, "arguments");
                        }
                        LogStore.Add("CH4.Entry", 1, "TOOL|" + name + "|start|" + TrimDisplay(arguments, 120), "TOOL");
                        ToolOrderDog dog = new ToolOrderDog(id, name, arguments);
                        if (name.StartsWith("host.", StringComparison.Ordinal))
                        {
                            // P8.5b 宿主级工具——延迟直执登记（批次末尾执行；确保同批 mau.proj 等先完成产物落地——顺序保证）\n                            hostDogs.Add(dog);
                            dog.IsClosed = true;
                            LogStore.Add("CH4.Entry", 1, "TOOL|" + name + "|host-deferred", "TOOL");
                        }
                        else
                        {
                            dog.Post(_oa, ToolOwnerId);
                            if (dog.OfficeId == 0)
                            {
                                // Post 失败——直接 FALLBACK 直执（错误可见性）
                                string fb = ExecuteTool(name, arguments);
                                if (fb == null || fb.Length == 0)
                                {
                                    fb = "ERR|EMPTY_RESULT|工具执行无结果";
                                }
                                dog.Result = "[FALLBACK] " + fb;
                                dog.IsClosed = true;
                            }
                            else
                            {
                                LogStore.Add("CH4.Entry", 1, "TOOL|" + name + "|posted|office=" + dog.OfficeId + "|timeout=" + dog.TimeoutFrames, "TOOL");
                            }
                        }
                        dogs.Add(dog);
                    }
                }
                // [段2] 等待循环——帧驱动（dev_cat 语料认领执行）+ Dog 逐帧轮询；帧上限 = 最大工具超时 + 余量（mau.proj 4800 + 600）
                const long MaxDogWaitFrames = 4800 + 600;
                for (long f = 0; f < MaxDogWaitFrames; f++)
                {
                    bool allDone = true;
                    for (int i = 0; i < dogs.Count; i++)
                    {
                        dogs[i].Tick(_oa);
                        if (!dogs[i].IsClosed && !dogs[i].IsTimedOut)
                        {
                            allDone = false;
                        }
                    }
                    if (allDone)
                    {
                        break;
                    }
                    _runner.Tick();
                    if (_httpHost != null)
                    {
                        _httpHost.PumpMainThread();
                    }
                    Thread.Sleep(FrameSleepMs);
                }
                // [段2b] host.* 延迟直执——批次其他工具完成后宿主直执（顺序保证：mau.proj 产物先落盘；忙时豁免——主线程串行，ExecuteReload 事务三段式兜底）
                for (int h = 0; h < hostDogs.Count; h++)
                {
                    ToolOrderDog dog = hostDogs[h];
                    bool savedBusy = _toolBatchActive;
                    _toolBatchActive = false;
                    string hr = ExecuteTool(dog.Name, dog.ArgsJson);
                    _toolBatchActive = savedBusy;
                    if (hr == null || hr.Length == 0)
                    {
                        hr = "ERR|EMPTY_RESULT|工具执行无结果";
                    }
                    dog.Result = hr;
                    LogStore.Add("CH4.Entry", 1, "TOOL|" + dog.Name + "|host-direct|result=" + TrimDisplay(hr, 100), "TOOL");
                }
                // [段3] 收集——Closed 取回执；TimeOut/等待上限 → 宿主直执 FALLBACK（执行器不变；[FALLBACK] 前缀注明）
                for (int i = 0; i < dogs.Count; i++)
                {
                    ToolOrderDog dog = dogs[i];
                    if (!dog.IsClosed && !dog.IsTimedOut)
                    {
                        dog.IsTimedOut = true;
                    }
                    if (dog.IsTimedOut)
                    {
                        LogStore.Add("CH4.Entry", 2, "TOOL|" + dog.Name + "|timeout|office=" + dog.OfficeId + " → FALLBACK 直执", "TOOL");
                        string result = ExecuteTool(dog.Name, dog.ArgsJson);
                        if (result == null || result.Length == 0)
                        {
                            result = "ERR|EMPTY_RESULT|工具执行无结果";
                        }
                        dog.Result = "[FALLBACK] " + result;
                        dog.IsClosed = true;
                    }
                    if (dog.Result == null || dog.Result.Length == 0)
                    {
                        dog.Result = "ERR|EMPTY_RESULT|工具执行无结果";
                    }
                    // O 系列：工具结果入 Log 截断 100 字符（design-ch4-observe §六拍板）——完整结果在会话消息
                    LogStore.Add("CH4.Entry", 1, "TOOL|" + dog.Name + "|" + dog.Result, "TOOL", "", "", 100);
                    // B4 对话区：工具结果实时推送 SSE（tool 事件——前端按执行序填充占位卡；参数/结果视图截断同 history）
                    if (_httpHost != null)
                    {
                        _httpHost.PushToolResult(dog.Name, TruncateText(dog.ArgsJson, 200), TruncateText(dog.Result, 300));
                    }
                    _chatContext.AddToolResult(dog.ToolCallId, dog.Name, dog.Result);
                }
            }
            catch (Exception ex)
            {
                LogStore.Add("CH4.Entry", 3, "tool_calls 解析失败: " + ex.Message, "TOOL");
            }
            finally
            {
                _toolBatchActive = false;
            }
        }

        /// <summary>
        /// 会话中枢处理——Chat 指令入口（P5 工具协调核心 / P8 B1 直执版）。
        /// 流程：追加用户消息 → 工具循环（≤3 轮）：LLM 后台流式 → 纯文本则完成 / tool_calls 则宿主直执 → 结果回传续轮。
        /// 消息维护与前文落盘在宿主；工具执行在宿主（一期直执——Program.Tools.cs；二期换 OA）。
        /// </summary>
        /// <param name="content">用户消息内容</param>
        private static void HandleChat(string content)
        {
            _chatContext.AddUserMessage(content);
            DataBox.Set<string>("global", "chat_state", "working");
            LogStore.Add("CH4.Entry", 1, "── MajorDomoCat 处理中 ──", "CHAT");
            for (int round = 0; round < MaxToolRounds; round++)
            {
                // [段1] LLM 调用——后台流式（消息序列 + 工具定义）
                LlmMessage[] messages = _chatContext.GetMessages();
                RunLlmInBackground(messages, _tools);
                WaitForLlm();
                if (_llmError)
                {
                    _chatContext.AddAssistantMessage(_llmErrorText);
                    LogStore.Add("LLM", 3, "LLM 错误: " + TrimDisplay(_llmErrorText, 300), "LLM");
                    break;
                }

                if (_llmToolCallsJson.Length == 0)
                {
                    // [段2] 纯文本回复——本轮完成
                    _chatContext.AddAssistantMessage(_llmResultText);
                    Console.WriteLine("[MajorDomoCat] " + _llmResultText);
                    break;
                }

                // [段3] 工具调用——追加 assistant tool_calls + 宿主直执（P8 一期）
                _chatContext.AddAssistantToolCalls(_llmToolCallsJson, _llmReasoning);
                DataBox.Set<string>("global", "chat_state", "tools");
                LogStore.Add("CH4.Entry", 1, "工具调用(" + (round + 1).ToString() + "/" + MaxToolRounds.ToString() + "): " + TrimDisplay(_llmToolCallsJson, 200), "CHAT");
                ExecuteToolBatch(_llmToolCallsJson);
                LogStore.Add("CH4.Entry", 1, "工具结果已回传，续轮", "CHAT");
            }

            // [段4] 前文落盘——会话结束保存（重启恢复面）；D7：tool 结果截断 ≤800 字符（落盘副本，内存保持全文——majordomo.json 防膨胀）
            LlmMessage[] toSave = _chatContext.GetMessages();
            for (int i = 0; i < toSave.Length; i++)
            {
                if (toSave[i].Role == LlmRole.Tool && toSave[i].Content != null && toSave[i].Content.Length > 800)
                {
                    toSave[i].Content = TruncateText(toSave[i].Content, 800);
                }
            }
            _sessionStore.Save(toSave);
            DataBox.Set<string>("global", "chat_state", "idle");
            // B4 对话区：会话终态事件——前端定型（llm done 仅一轮结束；chatdone 才是整次会话结束）
            if (_httpHost != null)
            {
                _httpHost.PushChatDone();
            }
            LogStore.Add("CH4.Entry", 1, "会话前文已落盘: " + _chatContext.GetMessageCount().ToString() + " 条消息", "SYS");
        }

        /// <summary>
        /// 构建会话历史视图 JSON——B4 对话区（GET /api/v1/history 回调）。
        /// 会话视图转换：system 跳过；user/assistant 文本直出；assistant tool_calls 与后续 tool 结果配对合入工具卡片（参数 ≤200/结果 ≤300）；
        /// 孤立 tool 丢弃；保留尾部 max 条视图消息；seq 1-based 渲染锚点。
        /// </summary>
        /// <param name="max">视图消息条数上限（1-2000）</param>
        /// <returns>会话视图 JSON</returns>
        public static string BuildHistoryView(int max)
        {
            List<object> view = new List<object>();
            List<Dictionary<string, object>> pendingTools = new List<Dictionary<string, object>>();
            long seq = 0;
            LlmMessage[] all = _chatContext.GetMessages();
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
            resp["sessionId"] = "majordomo";
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
    }
}
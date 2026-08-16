using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using Mau.Runtime;

namespace CH4
{
    /// <summary>
    /// Program 的 ChatBridge 分部——会话中枢工具协调（P5：MajorDomoCat 工具循环）。
    /// 归属：agent 循环基建（CH4 宿主侧）——LLM 调用 + tool_calls 分发 + 结果回传 + 前文落盘。
    /// </summary>
    public static partial class Program
    {
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
        /// 工具定义——P5 测试期写死两件（read_file 读 + ask 问）
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
        /// 待回传工具调用清单——与工具执行结果配对（按顺序回传）
        /// </summary>
        private static List<ToolCallInfo> _pendingToolCalls;

        /// <summary>
        /// 工具循环收敛上限——3 轮（莎拍板：单读/单问/并发读问覆盖测试场景）
        /// </summary>
        private const int MaxToolRounds = 3;

        /// <summary>
        /// 工具批次执行中标志——DispatchToolCalls 置位 / CollectToolResults 复位；reload 忙时拒绝（防旧批次完成信号永不置位 → 空转 25 分钟）
        /// </summary>
        private static bool _toolBatchActive;

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
                        }
                        else if (ev.Kind == LlmStreamKind.Reasoning)
                        {
                            reasoning.Append(ev.Text);
                        }
                        else if (ev.Kind == LlmStreamKind.ToolCalls)
                        {
                            toolCalls = ev.Text;
                        }
                        else if (ev.Kind == LlmStreamKind.Error)
                        {
                            _llmError = true;
                            _llmErrorText = ev.Text;
                        }
                    }
                    _llmResultText = text.ToString();
                    _llmReasoning = reasoning.ToString();
                    _llmToolCallsJson = toolCalls;
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
                Thread.Sleep(FrameSleepMs);
            }
            _llmBusy = false;
            _llmError = true;
            _llmErrorText = "ERR|LLM_TIMEOUT|LLM 调用超时（帧上限 " + MaxFramesPerRun + "）";
        }

        /// <summary>
        /// 等待语料工具执行完成——轮询 llm_tool_done 标志（语料合流置位）+ 帧上限兜底
        /// </summary>
        private static void WaitForTools()
        {
            for (int i = 0; i < MaxFramesPerRun; i++)
            {
                _runner.Tick();
                string done;
                if (DataBox.TryGet<string>("global", "llm_tool_done", out done) && done != null && done == "1")
                {
                    return;
                }
                Thread.Sleep(FrameSleepMs);
            }
            Console.WriteLine("[MajorDomoCat] 工具执行超时（帧上限 " + MaxFramesPerRun + "）——按空结果回传");
            DataBox.Set<string>("global", "llm_tool_done", "");
        }

        /// <summary>
        /// 构建系统提示词——MajorDomoCat 会话中枢身份 + 工具语义声明
        /// </summary>
        /// <returns>系统提示词</returns>
        private static string BuildSystemPrompt()
        {
            return "你是 MajorDomoCat——CH4 自举宿主的管理员对话中枢（P5 工具协调测试）。" + System.Environment.NewLine + "你有两个工具：" + System.Environment.NewLine + "- read_file(path)：读取指定路径的文本文件内容" + System.Environment.NewLine + "- ask(question)：向 QuickCat 问答子工具提问，获取简洁回答" + System.Environment.NewLine + "需要文件内容时调用 read_file；需要独立问答时调用 ask；可以同时调用多个工具（并发执行）。" + System.Environment.NewLine + "工具结果返回后，基于结果继续回答用户。";
        }

        /// <summary>
        /// 构建工具定义——P5 测试期写死两件（read_file + ask——OpenAI function schema）
        /// </summary>
        /// <returns>工具数组</returns>
        private static ToolSpec[] BuildTools()
        {
            ToolSpec read = new ToolSpec("read_file", "读取指定路径的文本文件内容", "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"要读取的文件路径\"}},\"required\":[\"path\"]}");
            ToolSpec ask = new ToolSpec("ask", "向 QuickCat 问答子工具提问，获取简洁回答", "{\"type\":\"object\",\"properties\":{\"question\":{\"type\":\"string\",\"description\":\"要提问的问题\"}},\"required\":[\"question\"]}");
            return new ToolSpec[]
            {
                read,
                ask
            };
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
        /// 待回传工具清单中是否含指定工具名
        /// </summary>
        /// <param name="name">工具名</param>
        /// <returns>true=含</returns>
        private static bool HasTool(string name)
        {
            for (int i = 0; i < _pendingToolCalls.Count; i++)
            {
                if (_pendingToolCalls[i].Name == name)
                {
                    return true;
                }
            }
            return false;
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
        /// 投递工具调用——解析 tool_calls JSON → 参数落全局盒 + 分支 key 投递（信号名即解析结果）。
        /// 已知工具（read_file/ask）投递语料；未知工具记录待回传 ERR。
        /// </summary>
        /// <param name="toolCallsJson">tool_calls JSON 数组</param>
        /// <returns>true=有已知工具已投递（语料将执行）</returns>
        private static bool DispatchToolCalls(string toolCallsJson)
        {
            _pendingToolCalls.Clear();
            try
            {
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
                        ToolCallInfo info = new ToolCallInfo(id, name, arguments);
                        _pendingToolCalls.Add(info);
                        if (name == "read_file")
                        {
                            _bus.SetText("TOOL_Read_Path", ExtractArg(arguments, "path"), "llm");
                        }
                        else if (name == "ask")
                        {
                            _bus.SetText("TOOL_Ask_Content", ExtractArg(arguments, "question"), "llm");
                        }
                    }
                }
                // [段2] 分支投递——批次开始沿 + 每线必投其一（要执行 / 跳过）
                bool wantRead = HasTool("read_file");
                bool wantAsk = HasTool("ask");
                if (wantRead || wantAsk)
                {
                    // 批次开始——独立沿（与 Exec/Skip 分离，避免同帧沿竞争被先声明导线消费）
                    _bus.SetText("TOOL_Batch_Start", "", "llm");
                    if (wantRead)
                    {
                        _bus.SetText("TOOL_Exec_Read", "", "llm");
                    }
                    else
                    {
                        _bus.SetText("TOOL_Skip_Read", "", "llm");
                    }
                    if (wantAsk)
                    {
                        _bus.SetText("TOOL_Exec_Ask", "", "llm");
                    }
                    else
                    {
                        _bus.SetText("TOOL_Skip_Ask", "", "llm");
                    }
                    _toolBatchActive = true;
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[MajorDomoCat] tool_calls 解析失败: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 收集工具结果——读全局结果盒 → 按 tool_calls 顺序回传上下文 → 清盒。
        /// </summary>
        private static void CollectToolResults()
        {
            string readResult = "";
            string askResult = "";
            DataBox.TryGet<string>("global", "llm_result_read", out readResult);
            DataBox.TryGet<string>("global", "llm_result_ask", out askResult);
            // TryGet 失败时 out 为 default(null)——防御归一（DataBox 契约：失败赋 default）
            if (readResult == null)
            {
                readResult = "";
            }
            if (askResult == null)
            {
                askResult = "";
            }
            // [段1] 清盒——语料下批写入前干净（残留防护）
            DataBox.Set<string>("global", "llm_tool_done", "");
            DataBox.Set<string>("global", "llm_result_read", "");
            DataBox.Set<string>("global", "llm_result_ask", "");
            // [段2] 按 tool_calls 顺序回传——每调用一条 tool result；未知工具 ERR 文本
            for (int i = 0; i < _pendingToolCalls.Count; i++)
            {
                ToolCallInfo info = _pendingToolCalls[i];
                string result;
                if (info.Name == "read_file")
                {
                    result = readResult;
                }
                else if (info.Name == "ask")
                {
                    result = askResult;
                }
                else
                {
                    result = "ERR|UNKNOWN_TOOL|未知工具: " + info.Name;
                }
                if (result.Length == 0)
                {
                    result = "ERR|EMPTY_RESULT|工具执行无结果（超时或失败）";
                }
                _chatContext.AddToolResult(info.Id, info.Name, result);
                Console.WriteLine("  [工具结果] " + info.Name + " → " + TrimDisplay(result, 120));
            }
            _pendingToolCalls.Clear();
            _toolBatchActive = false;
        }

        /// <summary>
        /// 会话中枢处理——Chat 指令入口（P5 工具协调核心）。
        /// 流程：追加用户消息 → 工具循环（≤3 轮）：LLM 后台流式 → 纯文本则完成 / tool_calls 则投递语料执行 → 结果回传续轮。
        /// 消息维护与前文落盘在宿主（CH4 侧实现前文管理器）；工具执行在语料（MajorDomoCat）。
        /// </summary>
        /// <param name="content">用户消息内容</param>
        private static void HandleChat(string content)
        {
            _chatContext.AddUserMessage(content);
            Console.WriteLine("── MajorDomoCat 处理中 ──");
            for (int round = 0; round < MaxToolRounds; round++)
            {
                // [段1] LLM 调用——后台流式（消息序列 + 工具定义）
                LlmMessage[] messages = _chatContext.GetMessages();
                RunLlmInBackground(messages, _tools);
                WaitForLlm();
                if (_llmError)
                {
                    _chatContext.AddAssistantMessage(_llmErrorText);
                    Console.WriteLine("[MajorDomoCat] LLM 错误: " + _llmErrorText);
                    break;
                }

                if (_llmToolCallsJson.Length == 0)
                {
                    // [段2] 纯文本回复——本轮完成
                    _chatContext.AddAssistantMessage(_llmResultText);
                    Console.WriteLine("[MajorDomoCat] " + _llmResultText);
                    break;
                }

                // [段3] 工具调用——追加 assistant tool_calls + 投递语料执行
                _chatContext.AddAssistantToolCalls(_llmToolCallsJson, _llmReasoning);
                Console.WriteLine("[MajorDomoCat] 工具调用(" + (round + 1) + "/" + MaxToolRounds + "): " + TrimDisplay(_llmToolCallsJson, 200));
                bool dispatched = DispatchToolCalls(_llmToolCallsJson);
                if (dispatched)
                {
                    WaitForTools();
                    CollectToolResults();
                    Console.WriteLine("[MajorDomoCat] 工具结果已回传，续轮");
                }
                else
                {
                    // 无已知工具——直接按 ERR 回传（不经过语料）
                    CollectToolResults();
                    Console.WriteLine("[MajorDomoCat] 无已知工具可执行——按错误回传");
                }
            }

            // [段4] 前文落盘——会话结束保存（重启恢复面）
            _sessionStore.Save(_chatContext.GetMessages());
            Console.WriteLine("[CH4.Entry] 会话前文已落盘: " + _chatContext.GetMessageCount() + " 条消息");
        }
    }
}

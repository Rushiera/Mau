using System;
using System.Collections.Generic;

namespace Mau.Runtime
{
    /// <summary>
    /// 会话上下文——MajorDomoCat 消息历史（借鉴 CH2/CH3 ContextManager 形态，2026-08-16 下沉 Mau.Runtime）。
    /// System 提示词独立缓存永久保留；user/assistant/tool 轮次按序追加。
    /// 上下文策略（截断/预算/压缩）P5 不做——不做假设性工程（测试到不了软上限）。
    /// </summary>
    public sealed class ChatContext
    {
        /// <summary>
        /// 消息历史——按时间顺序（第一条为 system）
        /// </summary>
        private readonly List<LlmMessage> _history;

        /// <summary>
        /// 系统提示词缓存——独立存储，Clear 时恢复
        /// </summary>
        private string _systemPrompt;

        /// <summary>
        /// 建立空会话上下文
        /// </summary>
        public ChatContext()
        {
            _history = new List<LlmMessage>();
            _systemPrompt = "";
        }

        /// <summary>
        /// 设置系统提示词——替换已有 system 消息（若存在）
        /// </summary>
        /// <param name="prompt">系统提示词</param>
        public void SetSystemPrompt(string prompt)
        {
            if (prompt == null)
            {
                prompt = "";
            }
            _systemPrompt = prompt;
            for (int i = _history.Count - 1; i >= 0; i = i - 1)
            {
                if (_history[i].Role == LlmRole.System)
                {
                    _history.RemoveAt(i);
                }
            }
            if (prompt.Length > 0)
            {
                _history.Insert(0, CreateMessage(LlmRole.System, prompt));
            }
        }

        /// <summary>
        /// 追加用户消息——返回追加的消息（落盘挂点显式消费；空文本不入上下文）。
        /// </summary>
        /// <param name="text">用户文本</param>
        /// <returns>追加的消息（空文本=null）</returns>
        public LlmMessage? AddUserMessage(string text)
        {
            if (text.Length == 0)
            {
                return null;
            }
            LlmMessage msg = CreateMessage(LlmRole.User, text);
            _history.Add(msg);
            return msg;
        }

        /// <summary>
        /// 追加助手文本回复——返回追加的消息（落盘挂点显式消费）。
        /// </summary>
        /// <param name="text">回复文本</param>
        /// <returns>追加的消息</returns>
        public LlmMessage AddAssistantMessage(string text)
        {
            LlmMessage msg = CreateMessage(LlmRole.Assistant, text);
            _history.Add(msg);
            return msg;
        }

        /// <summary>
        /// 追加助手工具调用声明——tool_calls JSON 原样 + 思考内容（回传铁律）；返回追加的消息（落盘挂点显式消费）。
        /// </summary>
        /// <param name="toolCallsJson">tool_calls JSON 数组</param>
        /// <param name="reasoning">思考内容（可为空串）</param>
        /// <returns>追加的消息（空 tool_calls=null）</returns>
        public LlmMessage? AddAssistantToolCalls(string toolCallsJson, string reasoning)
        {
            if (toolCallsJson.Length == 0)
            {
                return null;
            }
            LlmMessage msg = CreateMessage(LlmRole.Assistant, "");
            msg.ToolCallsJson = toolCallsJson;
            msg.ReasoningContent = reasoning;
            _history.Add(msg);
            return msg;
        }

        /// <summary>
        /// 追加工具结果——与调用 ID 配对；返回追加的消息（落盘挂点显式消费）。
        /// </summary>
        /// <param name="toolCallId">调用 ID</param>
        /// <param name="toolName">工具名</param>
        /// <param name="result">结果正文（失败时 ERR| 前缀）</param>
        /// <returns>追加的消息</returns>
        public LlmMessage AddToolResult(string toolCallId, string toolName, string result)
        {
            LlmMessage msg = CreateMessage(LlmRole.Tool, result);
            msg.ToolCallId = toolCallId;
            msg.ToolName = toolName;
            _history.Add(msg);
            return msg;
        }

        /// <summary>
        /// 获取消息数组副本——直接传给 LLM API
        /// </summary>
        /// <returns>消息数组</returns>
        public LlmMessage[] GetMessages()
        {
            return _history.ToArray();
        }

        /// <summary>
        /// 获取消息数量
        /// </summary>
        /// <returns>历史消息数</returns>
        public int GetMessageCount()
        {
            return _history.Count;
        }

        /// <summary>
        /// 清除历史——保留系统提示词
        /// </summary>
        public void Clear()
        {
            if (_systemPrompt == null)
            {
                _systemPrompt = "";
            }
            _history.Clear();
            if (_systemPrompt.Length > 0)
            {
                _history.Add(CreateMessage(LlmRole.System, _systemPrompt));
            }
        }

        /// <summary>
        /// 以外部历史替换当前上下文（重启恢复）——结构修复：system 唯一（取第一条），tool 无配对 ID 丢弃
        /// </summary>
        /// <param name="messages">持久化或导入的消息</param>
        public void ReplaceMessages(LlmMessage[] messages)
        {
            _history.Clear();
            _systemPrompt = "";
            if (messages == null)
            {
                return;
            }
            // [段1] 全量声明 + 全量结果收集——两遍扫描（声明集 = 配对锚；结果集 = 消费面）
            Dictionary<string, string> declaredNames = new Dictionary<string, string>();
            HashSet<string> resultIds = new HashSet<string>();
            for (int i = 0; i < messages.Length; i++)
            {
                LlmMessage m = messages[i];
                if (m.Content == null)
                {
                    m.Content = "";
                }
                if (m.ToolCallId == null)
                {
                    m.ToolCallId = "";
                }
                if (m.ToolName == null)
                {
                    m.ToolName = "";
                }
                if (m.ToolCallsJson == null)
                {
                    m.ToolCallsJson = "";
                }
                if (m.ReasoningContent == null)
                {
                    m.ReasoningContent = "";
                }
                if (m.Role == LlmRole.Assistant && m.ToolCallsJson.Length > 0)
                {
                    Dictionary<string, string> decls = ParseToolCallDecls(m.ToolCallsJson);
                    foreach (KeyValuePair<string, string> kv in decls)
                    {
                        declaredNames[kv.Key] = kv.Value;
                    }
                }
                if (m.Role == LlmRole.Tool && m.ToolCallId.Length > 0)
                {
                    resultIds.Add(m.ToolCallId);
                }
                // struct 值语义——归一化写回原始数组（段2 重新拷贝时读到归一化值）
                messages[i] = m;
            }
            // [段2] 顺序构建——格式补全：tool_calls 声明无结果 → 占位补全（向 OpenAI 格式匹配，不做信息损失）；tool 结果无声明 → 丢弃（无主可配）
            HashSet<string> placed = new HashSet<string>();
            for (int i = 0; i < messages.Length; i++)
            {
                LlmMessage m = messages[i];
                if (m.Role == LlmRole.System)
                {
                    string content = m.Content;
                    if (content == null)
                    {
                        content = "";
                    }
                    if (_systemPrompt.Length == 0)
                    {
                        _systemPrompt = content;
                        m.Content = content;
                        _history.Add(m);
                    }
                    // 多余 system 丢弃
                    continue;
                }
                if (m.Role == LlmRole.Assistant)
                {
                    // tool_calls 解析失败 → 降级纯文本（保留 content/reasoning——格式匹配优先，不做信息损失）
                    if (m.ToolCallsJson.Length > 0 && ParseToolCallDecls(m.ToolCallsJson).Count == 0)
                    {
                        m.ToolCallsJson = "";
                    }
                    _history.Add(m);
                    if (m.ToolCallsJson.Length > 0)
                    {
                        // 该条声明中无真实结果的调用 → 紧跟其后补占位 tool 消息（协议完整性）
                        Dictionary<string, string> decls = ParseToolCallDecls(m.ToolCallsJson);
                        foreach (KeyValuePair<string, string> kv in decls)
                        {
                            if (!resultIds.Contains(kv.Key))
                            {
                                LlmMessage ph = CreateMessage(LlmRole.Tool, "[系统自动修复] 该工具调用未返回结果（宿主中断）——结果不可知");
                                ph.ToolCallId = kv.Key;
                                ph.ToolName = kv.Value;
                                _history.Add(ph);
                            }
                        }
                    }
                    continue;
                }
                if (m.Role == LlmRole.Tool)
                {
                    if (m.ToolCallId.Length == 0)
                    {
                        // 无配对 ID 的 tool 消息丢弃（无法回传）
                        continue;
                    }
                    if (!declaredNames.ContainsKey(m.ToolCallId))
                    {
                        // 孤立 tool——无声明可配，丢弃（协议不允许游离 tool 消息）
                        continue;
                    }
                    if (placed.Contains(m.ToolCallId))
                    {
                        // 同 ID 重复结果——保留第一条
                        continue;
                    }
                    placed.Add(m.ToolCallId);
                    _history.Add(m);
                    continue;
                }
                _history.Add(m);
            }
        }

        /// <summary>
        /// 建立空 LlmMessage——全字段初始化（struct 默认字段为 null——serialize 判空会 NRE）
        /// </summary>
        /// <param name="role">角色</param>
        /// <param name="content">正文</param>
        /// <returns>初始化后的消息</returns>
        private static LlmMessage CreateMessage(LlmRole role, string content)
        {
            LlmMessage msg = new LlmMessage();
            msg.Role = role;
            msg.Content = content;
            msg.ToolCallId = "";
            msg.ToolName = "";
            msg.ToolCallsJson = "";
            msg.ReasoningContent = "";
            // 视图排序键——真实时序权威（Unix 毫秒；跨重启稳定——帧号为运行时态，重建即失真）
            msg.CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return msg;
        }
        /// <summary>
        /// 解析 assistant tool_calls JSON——提取调用 ID → 工具名映射（防御式：解析失败/非数组返回空字典）。
        /// </summary>
        /// <param name="toolCallsJson">tool_calls JSON 数组</param>
        /// <returns>ID → 工具名映射（空=解析失败或无声明）</returns>
        private static Dictionary<string, string> ParseToolCallDecls(string toolCallsJson)
        {
            Dictionary<string, string> result = new Dictionary<string, string>();
            if (toolCallsJson == null || toolCallsJson.Length == 0)
            {
                return result;
            }
            try
            {
                using (System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(toolCallsJson))
                {
                    System.Text.Json.JsonElement root = doc.RootElement;
                    if (root.ValueKind != System.Text.Json.JsonValueKind.Array)
                    {
                        return result;
                    }
                    for (int i = 0; i < root.GetArrayLength(); i++)
                    {
                        System.Text.Json.JsonElement call = root[i];
                        string id = "";
                        System.Text.Json.JsonElement idEl;
                        if (call.TryGetProperty("id", out idEl) && idEl.ValueKind == System.Text.Json.JsonValueKind.String)
                        {
                            string got = idEl.GetString() ?? "";
                            if (got.Length > 0)
                            {
                                id = got;
                            }
                        }
                        if (id.Length == 0)
                        {
                            continue;
                        }
                        string name = "";
                        System.Text.Json.JsonElement funcEl;
                        if (call.TryGetProperty("function", out funcEl))
                        {
                            System.Text.Json.JsonElement nameEl;
                            if (funcEl.TryGetProperty("name", out nameEl) && nameEl.ValueKind == System.Text.Json.JsonValueKind.String)
                            {
                                string gotName = nameEl.GetString() ?? "";
                                if (gotName.Length > 0)
                                {
                                    name = gotName;
                                }
                            }
                        }
                        result[id] = name;
                    }
                }
            }
            catch (Exception)
            {
                // 解析失败——返回空字典（调用方按无声明/降级处理）
            }
            return result;
        }
    }
}

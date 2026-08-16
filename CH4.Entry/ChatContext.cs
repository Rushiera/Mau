using System;
using System.Collections.Generic;
using Mau.Runtime;

namespace CH4
{
    /// <summary>
    /// 会话上下文——MajorDomoCat 消息历史（借鉴 CH2/CH3 ContextManager 形态）。
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
        /// 追加用户消息
        /// </summary>
        /// <param name="text">用户文本</param>
        public void AddUserMessage(string text)
{
            if (text.Length == 0)
            {
                return;
            }
            _history.Add(CreateMessage(LlmRole.User, text));
        }
        /// <summary>
        /// 追加助手文本回复
        /// </summary>
        /// <param name="text">回复文本</param>
        public void AddAssistantMessage(string text)
{
            _history.Add(CreateMessage(LlmRole.Assistant, text));
        }
        /// <summary>
        /// 追加助手工具调用声明——tool_calls JSON 原样 + 思考内容（回传铁律）
        /// </summary>
        /// <param name="toolCallsJson">tool_calls JSON 数组</param>
        /// <param name="reasoning">思考内容（可为空串）</param>
        public void AddAssistantToolCalls(string toolCallsJson, string reasoning)
{
            if (toolCallsJson.Length == 0)
            {
                return;
            }
            LlmMessage msg = CreateMessage(LlmRole.Assistant, "");
            msg.ToolCallsJson = toolCallsJson;
            msg.ReasoningContent = reasoning;
            _history.Add(msg);
        }
        /// <summary>
        /// 追加工具结果——与调用 ID 配对
        /// </summary>
        /// <param name="toolCallId">调用 ID</param>
        /// <param name="toolName">工具名</param>
        /// <param name="result">结果正文（失败时 ERR| 前缀）</param>
        public void AddToolResult(string toolCallId, string toolName, string result)
{
            LlmMessage msg = CreateMessage(LlmRole.Tool, result);
            msg.ToolCallId = toolCallId;
            msg.ToolName = toolName;
            _history.Add(msg);
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
                if (m.Role == LlmRole.Tool && m.ToolCallId.Length == 0)
                {
                    // 无配对 ID 的 tool 消息丢弃（无法回传）
                    continue;
                }
                _history.Add(m);
            }
        }
/// <summary>
/// 建立空 LlmMessage——全字段初始化（struct 默认字段为 null——serialize 判空会 NRE）
/// </summary>
/// <param name = "role">角色</param>
/// <param name = "content">正文</param>
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
    return msg;
}    }
}

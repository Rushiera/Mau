// ═══════════════════════════════════════════════
// 积木: llm.ctx_set_system / llm.ctx_push_user / llm.ctx_push_assistant /
//       llm.ctx_trim / llm.ctx_count / llm.ctx_build_prompt / llm.ctx_clear
// ID:   BRIK-LLM-003 ~ 009
// 作用: LLM 对话上下文管理——按 sessionKey 隔离的会话历史（多 Cat 各自独立）
// 引用: Mau.Bricks.LLM → Mau.Contracts（BrickRegistry）
// 原理: 静态会话表（sessionKey → 会话）；sessionKey 由宿主注入（Cat GlobeID 派生）
// 常用: CH4 TalkCat 多轮对话 / 多 Cat 并发上下文隔离
// ═══════════════════════════════════════════════
using System;
using System.Collections.Generic;
using Mau.Contracts;

namespace Mau.Bricks
{
    /// <summary>
    /// 厂商无关 LLM 消息
    /// </summary>
    public struct LlmMessage
    {
        /// <summary>
        /// 角色——System/User/Assistant/Tool
        /// </summary>
        public string Role;

        /// <summary>
        /// 正文
        /// </summary>
        public string Content;

        /// <summary>
        /// 工具调用 ID——Tool 消息必填
        /// </summary>
        public string ToolCallId;

        /// <summary>
        /// 工具名
        /// </summary>
        public string ToolName;

        /// <summary>
        /// 工具调用 JSON 数组文本——Assistant 工具声明
        /// </summary>
        public string ToolCallsJson;
    }

    /// <summary>
    /// 上下文会话——单个 sessionKey 的对话历史 + System Prompt
    /// </summary>
    internal sealed class ContextSession
    {
        /// <summary>
        /// 消息列表——按时间顺序
        /// </summary>
        internal readonly List<LlmMessage> History = new List<LlmMessage>();

        /// <summary>
        /// 唯一 System Prompt——Clear 时恢复
        /// </summary>
        internal string SystemPrompt = "";
    }

    /// <summary>
    /// 上下文管理积木——按 sessionKey 隔离的静态会话表（多 Cat 安全）。
    /// sessionKey 由宿主注入（Cat GlobeID 派生），持久化由宿主侧负责。
    /// </summary>
    public static class ContextBrick
    {
        /// <summary>
        /// 会话表锁——多 Cat 并发安全
        /// </summary>
        private static readonly object _gate = new object();

        /// <summary>
        /// 会话表——sessionKey → 会话（Key 由宿主注入）
        /// </summary>
        private static readonly Dictionary<string, ContextSession> _sessions =
            new Dictionary<string, ContextSession>(StringComparer.Ordinal);

        /// <summary>
        /// 获取或创建会话——按 sessionKey 隔离
        /// </summary>
        /// <param name="sessionKey">会话 Key（Cat GlobeID 派生）</param>
        /// <returns>会话</returns>
        private static ContextSession GetOrCreate(string sessionKey)
        {
            lock (_gate)
            {
                ContextSession? session;
                if (!_sessions.TryGetValue(sessionKey, out session) || session == null)
                {
                    session = new ContextSession();
                    _sessions[sessionKey] = session;
                }
                return session;
            }
        }

        /// <summary>
        /// 设置唯一 System Prompt——空文本表示移除
        /// </summary>
        /// <param name="sessionKey">会话 Key</param>
        /// <param name="prompt">System Prompt</param>
        /// <returns>true=成功</returns>
        public static bool CtxSetSystem(string sessionKey, string prompt)
        {
            ContextSession session = GetOrCreate(SafeKey(sessionKey));
            lock (_gate)
            {
                session.SystemPrompt = SafeText(prompt);
                for (int i = session.History.Count - 1; i >= 0; i = i - 1)
                {
                    if (session.History[i].Role == "System")
                    {
                        session.History.RemoveAt(i);
                    }
                }
                if (session.SystemPrompt.Length > 0)
                {
                    session.History.Insert(0, CreateMessage("System", session.SystemPrompt));
                }
            }
            return true;
        }

        /// <summary>
        /// 追加 User 消息
        /// </summary>
        /// <param name="sessionKey">会话 Key</param>
        /// <param name="text">正文</param>
        /// <returns>true=成功</returns>
        public static bool CtxPushUser(string sessionKey, string text)
        {
            ContextSession session = GetOrCreate(SafeKey(sessionKey));
            string safeText = SafeText(text);
            if (safeText.Length > 0)
            {
                lock (_gate)
                {
                    session.History.Add(CreateMessage("User", safeText));
                }
            }
            return true;
        }

        /// <summary>
        /// 追加 Assistant 消息
        /// </summary>
        /// <param name="sessionKey">会话 Key</param>
        /// <param name="text">正文</param>
        /// <returns>true=成功</returns>
        public static bool CtxPushAssistant(string sessionKey, string text)
        {
            ContextSession session = GetOrCreate(SafeKey(sessionKey));
            string safeText = SafeText(text);
            if (safeText.Length > 0)
            {
                lock (_gate)
                {
                    session.History.Add(CreateMessage("Assistant", safeText));
                }
            }
            return true;
        }

        /// <summary>
        /// 按字符预算从最早业务消息删除——System 永久保留
        /// </summary>
        /// <param name="sessionKey">会话 Key</param>
        /// <param name="maxChars">最大字符预算</param>
        /// <param name="removed">删除的消息数量</param>
        /// <returns>true=成功</returns>
        public static bool CtxTrim(string sessionKey, int maxChars, out int removed)
        {
            removed = 0;
            if (maxChars < 0)
            {
                return false;
            }
            ContextSession session = GetOrCreate(SafeKey(sessionKey));
            lock (_gate)
            {
                while (CountAllChars(session) > maxChars)
                {
                    int start = 0;
                    if (session.History.Count > 0 && session.History[0].Role == "System")
                    {
                        start = 1;
                    }
                    if (start >= session.History.Count)
                    {
                        return true;
                    }
                    session.History.RemoveAt(start);
                    removed = removed + 1;
                }
            }
            return true;
        }

        /// <summary>
        /// 读取当前消息数量
        /// </summary>
        /// <param name="sessionKey">会话 Key</param>
        /// <param name="count">消息数量</param>
        /// <returns>true=成功</returns>
        public static bool CtxCount(string sessionKey, out int count)
        {
            ContextSession session = GetOrCreate(SafeKey(sessionKey));
            lock (_gate)
            {
                count = session.History.Count;
            }
            return true;
        }

        /// <summary>
        /// 拼接 System、User 和 Assistant 正文供单次文本模式使用
        /// </summary>
        /// <param name="sessionKey">会话 Key</param>
        /// <param name="prompt">拼接文本</param>
        /// <returns>true=成功</returns>
        public static bool CtxBuildPrompt(string sessionKey, out string prompt)
        {
            ContextSession session = GetOrCreate(SafeKey(sessionKey));
            System.Text.StringBuilder builder = new System.Text.StringBuilder();
            lock (_gate)
            {
                for (int i = 0; i < session.History.Count; i = i + 1)
                {
                    LlmMessage message = session.History[i];
                    if (message.Role != "Tool" && message.Content.Length > 0)
                    {
                        if (builder.Length > 0)
                        {
                            builder.Append("\n\n");
                        }
                        builder.Append(message.Content);
                    }
                }
            }
            prompt = builder.ToString();
            return true;
        }

        /// <summary>
        /// 清除业务历史并恢复唯一 System Prompt
        /// </summary>
        /// <param name="sessionKey">会话 Key</param>
        /// <returns>true=成功</returns>
        public static bool CtxClear(string sessionKey)
        {
            ContextSession session = GetOrCreate(SafeKey(sessionKey));
            lock (_gate)
            {
                session.History.Clear();
                if (session.SystemPrompt.Length > 0)
                {
                    session.History.Add(CreateMessage("System", session.SystemPrompt));
                }
            }
            return true;
        }

        /// <summary>
        /// 追加 Tool 结果消息——工具调用回执（OpenAI 协议 role=tool）
        /// </summary>
        /// <param name="sessionKey">会话 Key</param>
        /// <param name="toolCallId">工具调用 ID（assistant tool_calls 对应）</param>
        /// <param name="content">工具结果正文</param>
        /// <returns>true=成功</returns>
        public static bool CtxPushTool(string sessionKey, string toolCallId, string content)
        {
            ContextSession session = GetOrCreate(SafeKey(sessionKey));
            string safeContent = SafeText(content);
            string safeId = SafeText(toolCallId);
            if (safeContent.Length > 0 || safeId.Length > 0)
            {
                lock (_gate)
                {
                    LlmMessage message = CreateMessage("Tool", safeContent);
                    message.ToolCallId = safeId;
                    session.History.Add(message);
                }
            }
            return true;
        }

        /// <summary>
        /// 追加 Assistant 工具声明消息——LLM 请求了工具（OpenAI 协议 assistant tool_calls）
        /// </summary>
        /// <param name="sessionKey">会话 Key</param>
        /// <param name="toolCallsJson">tool_calls JSON 数组</param>
        /// <returns>true=成功</returns>
        public static bool CtxPushAssistantToolCalls(string sessionKey, string toolCallsJson)
        {
            ContextSession session = GetOrCreate(SafeKey(sessionKey));
            string safeCalls = SafeText(toolCallsJson);
            if (safeCalls.Length > 0)
            {
                lock (_gate)
                {
                    LlmMessage message = CreateMessage("Assistant", "");
                    message.ToolCallsJson = safeCalls;
                    session.History.Add(message);
                }
            }
            return true;
        }

        /// <summary>
        /// 导出 OpenAI 兼容 messages 数组 JSON——结构化过程积木（llm.completions）的请求正文源
        /// System/User/Assistant（含 tool_calls）/Tool 全角色结构保留
        /// </summary>
        /// <param name="sessionKey">会话 Key</param>
        /// <param name="messagesJson">messages 数组 JSON</param>
        /// <returns>true=成功</returns>
        public static bool CtxBuildMessagesJson(string sessionKey, out string messagesJson)
        {
            messagesJson = "";
            ContextSession session = GetOrCreate(SafeKey(sessionKey));
            using System.IO.MemoryStream stream = new System.IO.MemoryStream();
            using (System.Text.Json.Utf8JsonWriter writer = new System.Text.Json.Utf8JsonWriter(stream))
            {
                writer.WriteStartArray();
                lock (_gate)
                {
                    for (int i = 0; i < session.History.Count; i = i + 1)
                    {
                        LlmMessage message = session.History[i];
                        writer.WriteStartObject();
                        writer.WriteString("role", message.Role.ToLowerInvariant());
                        writer.WriteString("content", SafeText(message.Content));
                        if (message.Role == "Tool" && message.ToolCallId.Length > 0)
                        {
                            writer.WriteString("tool_call_id", message.ToolCallId);
                        }
                        if (message.Role == "Assistant" && message.ToolCallsJson.Length > 0)
                        {
                            writer.WritePropertyName("tool_calls");
                            using (System.Text.Json.JsonDocument calls = System.Text.Json.JsonDocument.Parse(message.ToolCallsJson))
                            {
                                calls.RootElement.WriteTo(writer);
                            }
                        }
                        writer.WriteEndObject();
                    }
                }
                writer.WriteEndArray();
            }
            messagesJson = System.Text.Encoding.UTF8.GetString(stream.ToArray());
            return true;
        }

        /// <summary>
        /// 统计所有消息字段的 UTF-16 字符数
        /// </summary>
        /// <param name="session">会话</param>
        /// <returns>字符总数</returns>
        private static int CountAllChars(ContextSession session)
        {
            int total = 0;
            for (int i = 0; i < session.History.Count; i = i + 1)
            {
                total = total + SafeText(session.History[i].Content).Length
                    + SafeText(session.History[i].ToolCallId).Length
                    + SafeText(session.History[i].ToolName).Length
                    + SafeText(session.History[i].ToolCallsJson).Length;
            }
            return total;
        }

        /// <summary>
        /// 创建全部字符串字段非空的消息
        /// </summary>
        /// <param name="role">角色</param>
        /// <param name="content">正文</param>
        /// <returns>消息</returns>
        private static LlmMessage CreateMessage(string role, string content)
        {
            LlmMessage message;
            message.Role = role;
            message.Content = content;
            message.ToolCallId = "";
            message.ToolName = "";
            message.ToolCallsJson = "";
            return message;
        }

        /// <summary>
        /// 会话 Key 空值兜底——空 Key 归 "" 会话
        /// </summary>
        /// <param name="value">Key</param>
        /// <returns>非空 Key</returns>
        private static string SafeKey(string? value)
        {
            if (value == null)
            {
                return "";
            }
            return value;
        }

        /// <summary>
        /// 把可空文本规范为空字符串
        /// </summary>
        /// <param name="value">输入</param>
        /// <returns>非空文本</returns>
        private static string SafeText(string? value)
        {
            if (value == null)
            {
                return "";
            }
            return value;
        }
    }

    /// <summary>
    /// 上下文积木注册——进程启动时调用一次
    /// </summary>
    public static class ContextBrickRegistration
    {
        /// <summary>
        /// 注册全部上下文积木
        /// </summary>
        public static void RegisterAll()
        {
            RegisterCtxSetSystem();
            RegisterCtxPushUser();
            RegisterCtxPushAssistant();
            RegisterCtxPushTool();
            RegisterCtxPushAssistantToolCalls();
            RegisterCtxBuildMessagesJson();
            RegisterCtxTrim();
            RegisterCtxCount();
            RegisterCtxBuildPrompt();
            RegisterCtxClear();
        }

        /// <summary>
        /// 注册 llm.ctx_push_tool——工具结果回执
        /// </summary>
        private static void RegisterCtxPushTool()
        {
            BrickContract contract = new BrickContract("llm.ctx_push_tool", "Mau.Bricks.ContextBrick.CtxPushTool");
            contract.Inputs.Add(new BrickPort("sessionKey", typeof(string), "会话 Key"));
            contract.Inputs.Add(new BrickPort("toolCallId", typeof(string), "工具调用 ID"));
            contract.Inputs.Add(new BrickPort("content", typeof(string), "工具结果正文"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }

        /// <summary>
        /// 注册 llm.ctx_push_assistant_tool_calls——Assistant 工具声明
        /// </summary>
        private static void RegisterCtxPushAssistantToolCalls()
        {
            BrickContract contract = new BrickContract("llm.ctx_push_assistant_tool_calls", "Mau.Bricks.ContextBrick.CtxPushAssistantToolCalls");
            contract.Inputs.Add(new BrickPort("sessionKey", typeof(string), "会话 Key"));
            contract.Inputs.Add(new BrickPort("toolCallsJson", typeof(string), "tool_calls JSON 数组"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }

        /// <summary>
        /// 注册 llm.ctx_build_messages_json——结构化 messages 导出
        /// </summary>
        private static void RegisterCtxBuildMessagesJson()
        {
            BrickContract contract = new BrickContract("llm.ctx_build_messages_json", "Mau.Bricks.ContextBrick.CtxBuildMessagesJson");
            contract.Inputs.Add(new BrickPort("sessionKey", typeof(string), "会话 Key"));
            contract.Outputs.Add(new BrickPort("messagesJson", typeof(string), "messages 数组 JSON"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }

        /// <summary>
        /// 注册 llm.ctx_set_system
        /// </summary>
        private static void RegisterCtxSetSystem()
        {
            BrickContract contract = new BrickContract("llm.ctx_set_system", "Mau.Bricks.ContextBrick.CtxSetSystem");
            contract.Inputs.Add(new BrickPort("sessionKey", typeof(string), "会话 Key"));
            contract.Inputs.Add(new BrickPort("prompt", typeof(string), "System Prompt"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }

        /// <summary>
        /// 注册 llm.ctx_push_user
        /// </summary>
        private static void RegisterCtxPushUser()
        {
            BrickContract contract = new BrickContract("llm.ctx_push_user", "Mau.Bricks.ContextBrick.CtxPushUser");
            contract.Inputs.Add(new BrickPort("sessionKey", typeof(string), "会话 Key"));
            contract.Inputs.Add(new BrickPort("text", typeof(string), "User 正文"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }

        /// <summary>
        /// 注册 llm.ctx_push_assistant
        /// </summary>
        private static void RegisterCtxPushAssistant()
        {
            BrickContract contract = new BrickContract("llm.ctx_push_assistant", "Mau.Bricks.ContextBrick.CtxPushAssistant");
            contract.Inputs.Add(new BrickPort("sessionKey", typeof(string), "会话 Key"));
            contract.Inputs.Add(new BrickPort("text", typeof(string), "Assistant 正文"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }

        /// <summary>
        /// 注册 llm.ctx_trim
        /// </summary>
        private static void RegisterCtxTrim()
        {
            BrickContract contract = new BrickContract("llm.ctx_trim", "Mau.Bricks.ContextBrick.CtxTrim");
            contract.Inputs.Add(new BrickPort("sessionKey", typeof(string), "会话 Key"));
            contract.Inputs.Add(new BrickPort("maxChars", typeof(int), "最大字符预算"));
            contract.Outputs.Add(new BrickPort("removed", typeof(int), "删除的消息数量"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }

        /// <summary>
        /// 注册 llm.ctx_count
        /// </summary>
        private static void RegisterCtxCount()
        {
            BrickContract contract = new BrickContract("llm.ctx_count", "Mau.Bricks.ContextBrick.CtxCount");
            contract.Inputs.Add(new BrickPort("sessionKey", typeof(string), "会话 Key"));
            contract.Outputs.Add(new BrickPort("count", typeof(int), "消息数量"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }

        /// <summary>
        /// 注册 llm.ctx_build_prompt
        /// </summary>
        private static void RegisterCtxBuildPrompt()
        {
            BrickContract contract = new BrickContract("llm.ctx_build_prompt", "Mau.Bricks.ContextBrick.CtxBuildPrompt");
            contract.Inputs.Add(new BrickPort("sessionKey", typeof(string), "会话 Key"));
            contract.Outputs.Add(new BrickPort("prompt", typeof(string), "拼接文本"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }

        /// <summary>
        /// 注册 llm.ctx_clear
        /// </summary>
        private static void RegisterCtxClear()
        {
            BrickContract contract = new BrickContract("llm.ctx_clear", "Mau.Bricks.ContextBrick.CtxClear");
            contract.Inputs.Add(new BrickPort("sessionKey", typeof(string), "会话 Key"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }
    }
}

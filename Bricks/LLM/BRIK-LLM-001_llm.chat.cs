// ═══════════════════════════════════════════════════
// 积木: llm.chat
// ID:   BRIK-LLM-001
// 类别: LLM
// 作用: 非流式 LLM 调用——OpenAI 兼容 Chat Completions 完整响应
// 依赖: 无
// 引用: System · System.IO · System.Text · System.Text.Json
// 原理: 构建 Chat 请求 JSON → POST → 解析 choices[0].message.content
// 常用: CH4 TalkCat / 简单对话
// ═══════════════════════════════════════════════════
using System;
using System.IO;
using System.Text;
using System.Text.Json;

using Mau.Runtime;
namespace Mau.Bricks
{
    /// <summary>
    /// LLM 积木——llm.chat 非流式调用（依赖 LlmBridge）
    /// </summary>
    public static class LlmChatBrick
    {
        /// <summary>
        /// 非流式 LLM 调用——完整响应
        /// </summary>
        /// <param name="model">模型名</param>
        /// <param name="systemPrompt">System Prompt</param>
        /// <param name="userMessage">User 消息</param>
        /// <param name="content">Assistant 完整回复</param>
        /// <returns>true=成功</returns>
        public static bool Chat(string model, string systemPrompt,
            string userMessage, out string content)
        {
            content = "";
            if (string.IsNullOrWhiteSpace(model))
            {
                model = "deepseek-chat";
            }
            string body = BuildChatBody(model, systemPrompt, userMessage);
            try
            {
                string json = LlmBridge.PostJson(body);
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;
                JsonElement choices;
                if (!root.TryGetProperty("choices", out choices)
                    || choices.ValueKind != JsonValueKind.Array
                    || choices.GetArrayLength() == 0)
                {
                    content = "ERR|LLM_RESPONSE_INVALID|No choices in response.";
                    return false;
                }
                JsonElement first = choices[0];
                JsonElement message;
                if (!first.TryGetProperty("message", out message)
                    || message.ValueKind != JsonValueKind.Object)
                {
                    content = "ERR|LLM_RESPONSE_INVALID|No message in choice.";
                    return false;
                }
                JsonElement text;
                if (message.TryGetProperty("content", out text)
                    && text.ValueKind == JsonValueKind.String)
                {
                    content = LlmBridge.SafeText(text.GetString());
                    return true;
                }
                content = "";
                return true;
            }
            catch (Exception ex)
            {
                content = "ERR|LLM_PROVIDER_ERROR|" + ex.GetType().Name;
                return false;
            }
        }

        /// <summary>
        /// 构建 Chat 请求 JSON
        /// </summary>
        /// <param name="model">模型</param>
        /// <param name="systemPrompt">System</param>
        /// <param name="userMessage">User</param>
        /// <returns>JSON 正文</returns>
        private static string BuildChatBody(string model, string systemPrompt,
            string userMessage)
        {
            using MemoryStream stream = new MemoryStream();
            using (Utf8JsonWriter writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WriteString("model", model);
                writer.WriteBoolean("stream", false);
                writer.WriteStartArray("messages");
                if (!string.IsNullOrEmpty(systemPrompt))
                {
                    writer.WriteStartObject();
                    writer.WriteString("role", "system");
                    writer.WriteString("content", LlmBridge.SafeText(systemPrompt));
                    writer.WriteEndObject();
                }
                writer.WriteStartObject();
                writer.WriteString("role", "user");
                writer.WriteString("content", LlmBridge.SafeText(userMessage));
                writer.WriteEndObject();
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            return Encoding.UTF8.GetString(stream.ToArray());
        }
    }
}
// #MAU_CHECKSUM:SHA256:45308AA58615CA9EFC3A5DAEAD777280994ED7AE694128A4F650A3245C740C5A

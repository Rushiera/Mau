// ═══════════════════════════════════════════════════
// 积木: llm.ctx_build_messages_json
// ID:   BRIK-LLM-012
// 类别: LLM
// 作用: 导出 OpenAI 兼容 messages 数组 JSON——结构化过程积木的请求正文源
// 依赖: 无
// 引用: System.IO · System.Text
// 原理: 历史遍历 → Utf8JsonWriter 全角色结构保留（Error 跳过；Tool 带 tool_call_id；Assistant 带 tool_calls）
// 常用: TalkCat 结构化请求构建（T_BuildMessages）
// ═══════════════════════════════════════════════════
using System.IO;
using System.Text;

using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 上下文积木——llm.ctx_build_messages_json 导出 messages（依赖 ContextStore）
    /// </summary>
    public static class CtxBuildMessagesJsonBrick
    {
        /// <summary>
        /// 导出 OpenAI 兼容 messages 数组 JSON——结构化过程积木（llm.completions）的请求正文源
        /// </summary>
        /// <param name="sessionKey">会话 Key</param>
        /// <param name="messagesJson">messages 数组 JSON</param>
        /// <returns>true=成功</returns>
        public static bool CtxBuildMessagesJson(string sessionKey, out string messagesJson)
        {
            messagesJson = "";
            ContextSession session = ContextStore.GetOrCreate(ContextStore.SafeKey(sessionKey));
            using MemoryStream stream = new MemoryStream();
            using (System.Text.Json.Utf8JsonWriter writer = new System.Text.Json.Utf8JsonWriter(stream))
            {
                writer.WriteStartArray();
                lock (ContextStore.Gate)
                {
                    for (int i = 0; i < session.History.Count; i = i + 1)
                    {
                        LlmMessage message = session.History[i];
                        if (message.Role == "Error")
                        {
                            continue;
                        }
                        writer.WriteStartObject();
                        writer.WriteString("role", message.Role.ToLowerInvariant());
                        writer.WriteString("content", ContextStore.SafeText(message.Content));
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
            messagesJson = Encoding.UTF8.GetString(stream.ToArray());
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:A0DBB293FE2A55FB48C8C9E0FF3D37048DFF463F0B1040B18CF7DA145C4E7367

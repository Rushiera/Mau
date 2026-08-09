// ═══════════════════════════════════════════════════
// 积木: ui.snapshot_chat
// ID:   BRIK-UI-001
// 类别: UI
// 作用: 会话快照合成——读 ContextStore 合成 ChatSnapshot JSON（entries + streaming + streamText + stats）
// 依赖: 无
// 引用: System.IO · System.Text
// 原理: 历史遍历 → Utf8JsonWriter 全角色结构保留；末尾连续 Assistant 分片标记为流式尾部
// 常用: UiPet 每帧快照合成（Pet-UI 模式——快照经 DataBox 原子 push 到 UI 线程）
// ═══════════════════════════════════════════════════
using System.IO;
using System.Text;

using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// UI 积木——ui.snapshot_chat 会话快照合成（依赖 ContextStore；Pet-UI 模式）
    /// </summary>
    public static class UiSnapshotChatBrick
    {
        /// <summary>
        /// 合成 ChatSnapshot JSON——entries 全量 + 末尾连续 Assistant 分片为流式尾部
        /// </summary>
        /// <param name="sessionKey">会话 Key</param>
        /// <param name="chatJson">ChatSnapshot JSON</param>
        /// <returns>true=成功</returns>
        public static bool SnapshotChat(string sessionKey, out string chatJson)
        {
            chatJson = "";
            ContextSession session = ContextStore.GetOrCreate(ContextStore.SafeKey(sessionKey));
            using MemoryStream stream = new MemoryStream();
            using (System.Text.Json.Utf8JsonWriter writer = new System.Text.Json.Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WritePropertyName("entries");
                writer.WriteStartArray();
                lock (ContextStore.Gate)
                {
                    for (int i = 0; i < session.History.Count; i = i + 1)
                    {
                        LlmMessage message = session.History[i];
                        if (message.Role == "Tool" || message.Role == "Error")
                        {
                            continue;
                        }
                        writer.WriteStartObject();
                        writer.WriteString("role", message.Role);
                        writer.WriteString("content", ContextStore.SafeText(message.Content));
                        writer.WriteEndObject();
                    }
                }
                writer.WriteEndArray();
                // [段1] 末尾连续 Assistant 分片 → 流式尾部（打字机数据源）
                lock (ContextStore.Gate)
                {
                    int end = session.History.Count;
                    int start = end;
                    while (start > 0 && session.History[start - 1].Role == "Assistant")
                    {
                        start = start - 1;
                    }
                    bool streaming = start < end;
                    writer.WriteBoolean("streaming", streaming);
                    if (streaming)
                    {
                        StringBuilder sb = new StringBuilder();
                        for (int i = start; i < end; i = i + 1)
                        {
                            sb.Append(session.History[i].Content);
                        }
                        writer.WriteString("streamText", sb.ToString());
                    }
                    else
                    {
                        writer.WriteString("streamText", "");
                    }
                    // [段2] 统计——条目数 + 字符数（标题 Token 统计）
                    int count = 0;
                    int chars = 0;
                    for (int i = 0; i < session.History.Count; i = i + 1)
                    {
                        count = count + 1;
                        chars = chars + session.History[i].Content.Length;
                    }
                    writer.WriteNumber("count", count);
                    writer.WriteNumber("chars", chars);
                }
                writer.WriteEndObject();
            }
            chatJson = Encoding.UTF8.GetString(stream.ToArray());
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:3BFA0EE99B7749A08906F9C351F09DF007476DA7229C1B0B1B5505753545DC74

// ═══════════════════════════════════════════════════
// 积木: ui.snapshot_chat
// ID:   BRIK-UI-001
// 类别: UI
// 作用: 会话快照合成——读 ContextStore 合成 ChatSnapshot JSON（entries + streaming + streamText + stats）
// 依赖: tool.display
// 引用: System.IO · System.Text
// 原理: 历史遍历 → Utf8JsonWriter 全角色结构保留；Tool 条目经 tool.display 摘要为单行；末尾连续 Assistant 分片标记为流式尾部
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
                        // v1.5b 修复：放行 Tool/Error 角色——CH2 AppendEntry 有 Tool 分支（RenderQuoted 引用显示 + ColToolText）；
                        // 跳过导致工具结果在对话区不显示（BRIK-UI-001 原 `continue` 吞掉工具条目）
                        // v1.5c：Tool 条目摘要化——tool.display 单行人类可读（[icon n/m] 格式；LLM 上下文保持完整结果）
                        writer.WriteStartObject();
                        writer.WriteString("role", message.Role);
                        string entryContent = ContextStore.SafeText(message.Content);
                        if (message.Role == "Tool" && message.ToolName.Length > 0)
                        {
                            string displayLine;
                            if (Mau.Bricks.ToolDisplayBrick.Display(message.ToolName, message.ArgsJson,
                                entryContent, 0, 0, out displayLine))
                            {
                                entryContent = displayLine;
                            }
                        }
                        writer.WriteString("content", entryContent);
                        writer.WriteEndObject();
                    }
                }
                writer.WriteEndArray();
                // [段1] 末尾连续 Assistant 分片 → 流式尾部（打字机数据源）
                // 🔴 streaming 判定（2026-08-10 修复）：以"活跃 LLM 会话"为准（llm.finish 移除会话后 false）——
                // 原"末尾有 Assistant = streaming"把已完成的回复误判为流式中（UI 按钮永久锁定）
                lock (ContextStore.Gate)
                {
                    int end = session.History.Count;
                    int start = end;
                    while (start > 0 && session.History[start - 1].Role == "Assistant")
                    {
                        start = start - 1;
                    }
                    bool streaming = LlmSession.HasActiveSession();
                    writer.WriteBoolean("streaming", streaming);
                    if (streaming && start < end)
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
                    // [段1.5] 动效子状态（P2-7——CH2 PushAnimState 移植）：Think/Reply（活跃会话 LastChunk 分片类型）/ WaitTools（工具等待通道）/ 空=空闲
                    //   Think = 活跃但无 content 分片（思考中）；Reply = 最近分片是 content；WaitTools = tool_req_flag 置位且 tools_done 未置位
                    string subState = "";
                    if (streaming)
                    {
                        subState = "Think";
                        KeyValuePair<string, LlmStreamSession>[] sessions = LlmSession.GetActiveSessions();
                        if (sessions.Length > 0)
                        {
                            LlmStreamChunk? last = sessions[0].Value.LastChunk;
                            if (last != null && last.ContentDelta.Length > 0)
                            {
                                subState = "Reply";
                            }
                            else if (last != null && last.ReasoningDelta.Length > 0)
                            {
                                subState = "Think";
                            }
                        }
                    }
                    else
                    {
                        // 工具等待——box 通道（TalkCat/ToolPoster 共用 sessionKey 作用域）
                        int reqFlag = 0;
                        int doneFlag = 0;
                        BoxStore.Get(sessionKey, "tool_req_flag", 0, out reqFlag);
                        BoxStore.Get(sessionKey, "tools_done", 0, out doneFlag);
                        if (reqFlag != 0 && doneFlag == 0)
                        {
                            subState = "WaitTools";
                        }
                    }
                    writer.WriteString("subState", subState);
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
// #MAU_CHECKSUM:SHA256:64242CE3D3CABC4919FE7D28A90D43DE2251C9A7E8918CE16043DFA1635E8085

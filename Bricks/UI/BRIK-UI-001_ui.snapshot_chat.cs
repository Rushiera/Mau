// ═══════════════════════════════════════════════════
// 积木: ui.snapshot_chat
// ID:   BRIK-UI-001
// 类别: UI
// 作用: 会话快照合成——读 ContextStore 合成 ChatSnapshot JSON（entries + streaming + streamText + thinkingText + stats + token + note）
// 依赖: tool.display
// 引用: System.IO · System.Text
// 原理: 历史遍历 → Utf8JsonWriter 全角色结构保留；Tool 条目经 tool.display 摘要为单行；流式尾部读活跃会话 ContentBuilder 累积（D.2 分片合并——2026-08-11）
//       思考累积读 ReasoningBuilder（G.2 2026-08-11）；token 统计读会话累计 + catcfg maxContextTokens（G.4）；note 读 NoteStore（G.5）
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
        /// 合成 ChatSnapshot JSON——entries 全量 + 流式尾部读活跃会话累积（D.2 分片合并）
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
                // [段1] 流式尾部 → 打字机数据源（D.2 分片合并：流式期间分片不再 push ContextStore——改为读 LlmStreamSession.ContentBuilder 累积）
                // 🔴 streaming 判定（2026-08-10 修复）：以"活跃 LLM 会话"为准（llm.finish 移除会话后 false）——
                // 原"末尾有 Assistant = streaming"把已完成的回复误判为流式中（UI 按钮永久锁定）
                // 🔴 streamText 数据源（2026-08-11 D.2）：原"末尾连续 Assistant 分片拼接"——T_Append 逐分片 push 的膨胀残留；
                //    修复后分片只在会话累积（SSE 解析 ContentBuilder），快照直接从活跃会话读累积正文
                lock (ContextStore.Gate)
                {
                    bool streaming = LlmSession.HasActiveSession();
                    writer.WriteBoolean("streaming", streaming);
                    string streamText = "";
                    if (streaming)
                    {
                        // 活跃会话累积正文——与 T_PushStream（BRIK-LLM-027 完成时合并 push）同源
                        KeyValuePair<string, LlmStreamSession>[] sessions = LlmSession.GetActiveSessions();
                        if (sessions.Length > 0)
                        {
                            streamText = sessions[0].Value.ContentBuilder.ToString();
                        }
                    }
                    writer.WriteString("streamText", streamText);
                    // [段1.2] 思考累积 → 打字机数据源（G.2 思考显示——2026-08-11 D.3）
                    //     ReasoningBuilder 累积（ParseStreamEvent 推理增量）——与 llm.ctx_push_reasoning（BRIK-LLM-028 完成时合并 push）同源；
                    //     UI 流式浮层在思考阶段显示思考文字（CH2 缓存区语义：思考→回复切换前显示思考，切换后显示回复）
                    string thinkingText = "";
                    if (streaming)
                    {
                        KeyValuePair<string, LlmStreamSession>[] sessions = LlmSession.GetActiveSessions();
                        if (sessions.Length > 0)
                        {
                            thinkingText = sessions[0].Value.ReasoningBuilder.ToString();
                        }
                    }
                    writer.WriteString("thinkingText", thinkingText);
                    // [段1.5] 动效子状态（P2-7——CH2 PushAnimState 移植）：Think/Reply（活跃会话 ContentBuilder 累积）/ WaitTools（工具等待通道）/ 空=空闲
                    //   Think = 活跃但无正文累积（思考中）；Reply = 正文累积非空（回复中）；WaitTools = tool_req_flag 置位且 tools_done 未置位
                    string subState = "";
                    if (streaming)
                    {
                        subState = "Think";
                        KeyValuePair<string, LlmStreamSession>[] sessions = LlmSession.GetActiveSessions();
                        if (sessions.Length > 0)
                        {
                            // D.2 分片合并：正文累积非空 = 已开始回复（比 LastChunk 更稳——累积不被消费清空）
                            if (sessions[0].Value.ContentBuilder.Length > 0)
                            {
                                subState = "Reply";
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
                    // [段3] 标题 Token 统计（G.4——CH2 `[ 名 ] Xk Token Y%（Max. Zk）` 移植）：
                    //   累计 prompt（llm.ctx_push_stream 完成时累加入会话）+ maxContextTokens（catcfg 配置，缺省 1000000）
                    writer.WriteNumber("totalPromptTokens", session.TotalPromptTokens);
                    writer.WriteNumber("totalCompletionTokens", session.TotalCompletionTokens);
                    long maxTokens = 1000000;
                    ConfigStore? tokenStore;
                    DataBox.TryGet<ConfigStore>("catcfg", ContextStore.SafeKey(sessionKey), out tokenStore);
                    if (tokenStore != null)
                    {
                        long parsed;
                        string raw = tokenStore.Get("maxContextTokens", "");
                        if (raw.Length > 0 && long.TryParse(raw, out parsed) && parsed > 0)
                        {
                            maxTokens = parsed;
                        }
                    }
                    writer.WriteNumber("maxContextTokens", maxTokens);
                    // [段4] Note 面板（G.5——CH2 RenderNotePanel 数据源）：NoteStore 序列化 "current|done\ntask1\ntask2..."——UI 渲染 [x]/[>]/[ ] 状态
                    string noteText = NoteStore.Serialize(NoteStore.GetOrCreate(ContextStore.SafeKey(sessionKey)));
                    writer.WriteString("note", noteText);
                }
                writer.WriteEndObject();
            }
            chatJson = Encoding.UTF8.GetString(stream.ToArray());
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:EB2D0CB03B877536DD76C178CDB9706CCD060DF3967DCF60707A02AF10C5AFAB

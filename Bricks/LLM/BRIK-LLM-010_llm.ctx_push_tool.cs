// ═══════════════════════════════════════════════════
// 积木: llm.ctx_push_tool
// ID:   BRIK-LLM-010
// 类别: LLM
// 作用: 追加 Tool 结果消息——工具调用回执（OpenAI 协议 role=tool；toolName 供 UI 摘要显示——tool.display）
// 依赖: 无
// 引用: 无
// 原理: 会话表历史追加 Tool 角色消息（tool_call_id + tool_name 绑定——2026-08-10 摘要链路）
// 常用: TalkCat 工具结果回填（T_AppendTool）
// ═══════════════════════════════════════════════════

using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 上下文积木——llm.ctx_push_tool 追加 Tool 结果（依赖 ContextStore）
    /// </summary>
    public static class CtxPushToolBrick
    {
        /// <summary>
        /// 追加 Tool 结果消息——工具调用回执（OpenAI 协议 role=tool；toolName/argsJson 供 UI 摘要显示）
        /// 🔴 防护：①content 超 8000 字符截断（CH2 移植——防撑爆前文）②空 content 写 "[工具执行失败或超时]"（防失败信息丢失）
        /// </summary>
        /// <param name="sessionKey">会话 Key</param>
        /// <param name="toolCallId">工具调用 ID（assistant tool_calls 对应）</param>
        /// <param name="toolName">工具名（点号化路由名——tool.display 分派依据）</param>
        /// <param name="argsJson">工具参数 JSON 原文（tool.display 摘要数据源）</param>
        /// <param name="content">工具结果正文</param>
        /// <returns>true=成功</returns>
        public static bool CtxPushTool(string sessionKey, string toolCallId, string toolName, string argsJson, string content)
        {
            ContextSession session = ContextStore.GetOrCreate(ContextStore.SafeKey(sessionKey));
            string safeId = ContextStore.SafeText(toolCallId);
            string safeName = ContextStore.SafeText(toolName);
            string safeArgs = ContextStore.SafeText(argsJson);
            string safeContent = ContextStore.SafeText(content);
            // 空结果 = 失败/超时——注入明确标记（CH2：isSuccess ? result : "[工具执行失败或超时]"）
            if (safeContent.Length == 0)
            {
                safeContent = "[工具执行失败或超时]";
            }
            else if (safeContent.Length > 8000)
            {
                // 超长结果截断——防撑爆上下文（CH2：8000 字符 + 截断标注）
                safeContent = safeContent.Substring(0, 8000) + "\n…（已截断，共" + content.Length.ToString() + "字符）";
            }
            if (safeContent.Length > 0 || safeId.Length > 0)
            {
                lock (ContextStore.Gate)
                {
                    LlmMessage message = ContextStore.CreateMessage("Tool", safeContent);
                    message.ToolCallId = safeId;
                    message.ToolName = safeName;
                    message.ArgsJson = safeArgs;
                    session.History.Add(message);
                }
            }
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:FB84513D5C4B723761625CBA7731A58B9C1F0EAE40EA759B0E6F19D3C533D896

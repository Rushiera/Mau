// ═══════════════════════════════════════════════════
// 积木: llm.ctx_build_prompt
// ID:   BRIK-LLM-008
// 类别: LLM
// 作用: 拼接 System、User 和 Assistant 正文供单次文本模式使用
// 依赖: 无
// 引用: System.Text
// 原理: 遍历历史跳过 Tool/Error 角色拼接正文（\n\n 分隔）
// 常用: 单次文本模式请求
// ═══════════════════════════════════════════════════
using System.Text;

using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 上下文积木——llm.ctx_build_prompt 拼接文本（依赖 ContextStore）
    /// </summary>
    public static class CtxBuildPromptBrick
    {
        /// <summary>
        /// 拼接 System、User 和 Assistant 正文供单次文本模式使用
        /// </summary>
        /// <param name="sessionKey">会话 Key</param>
        /// <param name="prompt">拼接文本</param>
        /// <returns>true=成功</returns>
        public static bool CtxBuildPrompt(string sessionKey, out string prompt)
        {
            ContextSession session = ContextStore.GetOrCreate(ContextStore.SafeKey(sessionKey));
            StringBuilder builder = new StringBuilder();
            lock (ContextStore.Gate)
            {
                for (int i = 0; i < session.History.Count; i = i + 1)
                {
                    LlmMessage message = session.History[i];
                    if (message.Role != "Tool" && message.Role != "Error" && message.Content.Length > 0)
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
    }
}
// #MAU_CHECKSUM:SHA256:E8A46A1A66EFDD5F6A525EF415144502C925C7CB6DC2AB985EB59E34CF2D4DA0

// ═══════════════════════════════════════════════════
// 积木: llm.ctx_push_error
// ID:   BRIK-LLM-019
// 类别: LLM
// 作用: 追加错误消息——错误码入持久流（不参与 LLM 请求构建）
// 依赖: 无
// 引用: 无
// 原理: 会话表历史追加 Error 角色消息（build_messages_json 跳过）
// 常用: TalkCat 错误码入持久流（T_RecordError）
// ═══════════════════════════════════════════════════

using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 上下文积木——llm.ctx_push_error 错误码入持久流（依赖 ContextStore）
    /// </summary>
    public static class CtxPushErrorBrick
    {
        /// <summary>
        /// 追加错误消息——错误码入持久流（与 CH2 Error 条目对齐；不参与 LLM 请求构建）
        /// </summary>
        /// <param name="sessionKey">会话 Key</param>
        /// <param name="errorCode">错误码</param>
        /// <returns>true=成功</returns>
        public static bool CtxPushError(string sessionKey, string errorCode)
        {
            ContextSession session = ContextStore.GetOrCreate(ContextStore.SafeKey(sessionKey));
            string safe = ContextStore.SafeText(errorCode);
            if (safe.Length > 0)
            {
                lock (ContextStore.Gate)
                {
                    LlmMessage message = ContextStore.CreateMessage("Error", safe);
                    session.History.Add(message);
                }
            }
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:45F2AA2276E3CD0A67E4D80B8C524ECCF4F4783E054D32EBBD2AD968A75EE35F

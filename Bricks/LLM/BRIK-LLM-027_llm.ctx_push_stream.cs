// ═══════════════════════════════════════════════════
// 积木: llm.ctx_push_stream
// ID:   BRIK-LLM-027
// 类别: LLM
// 作用: 流式分片合并 push——从活跃会话累积缓冲读完整回复，一次性追加 Assistant 消息
// 依赖: 无
// 包: 无
// 引用: Mau.Runtime
// 原理: LlmStreamSession.ContentBuilder（ParseStreamEvent 累积）→ 非空则 ContextStore 追加一条 Assistant
// 常用: TalkCat 语料 T_PushStream——D.2 分片合并（CH2 对齐：分片只进 UI 流式缓冲，完成时合并入上下文）
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// LLM 积木——llm.ctx_push_stream 流式分片合并 push（依赖 LlmSession/ContextStore；D.2 分片合并 2026-08-11）
    /// </summary>
    public static class CtxPushStreamBrick
    {
        /// <summary>
        /// 从会话累积缓冲读完整回复并追加 Assistant 消息——分片不进上下文，完成时合并 push
        /// </summary>
        /// <param name="requestId">流式会话 ID（必须在 llm.finish 之前调用——会话移除后累积丢失）</param>
        /// <param name="sessionKey">会话 Key</param>
        /// <returns>true=成功（会话不存在或内容为空也返回 true——push 跳过不阻塞流程）</returns>
        public static bool CtxPushStream(string requestId, string sessionKey)
        {
            LlmStreamSession? session = LlmSession.FindSession(requestId);
            if (session == null)
            {
                // 会话已被 finish 移除——累积丢失，push 跳过（与 round_stats_text 同容错策略）
                return true;
            }
            string fullReply = session.ContentBuilder.ToString();
            if (fullReply.Length > 0)
            {
                ContextSession ctx = ContextStore.GetOrCreate(ContextStore.SafeKey(sessionKey));
                lock (ContextStore.Gate)
                {
                    ctx.History.Add(ContextStore.CreateMessage("Assistant", fullReply));
                }
            }
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:AEB75F47AA4DF2CF66B94073272605DEF7ED1ECEC7D339919E647727E717D5A1

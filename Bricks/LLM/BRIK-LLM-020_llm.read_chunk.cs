// ═══════════════════════════════════════════════════
// 积木: llm.read_chunk
// ID:   BRIK-LLM-020
// 类别: LLM
// 作用: 消费流式分片——无分片返回 false（调用方稍后重试）
// 依赖: 无
// 引用: 无
// 原理: 会话队列 Dequeue → 更新 LastChunk → 载荷输出
// 常用: TalkCat 流式消费（T_Pump）
// ═══════════════════════════════════════════════════

using Mau.Runtime;
namespace Mau.Bricks
{
    /// <summary>
    /// LLM 积木——llm.read_chunk 消费流式分片（依赖 LlmBridge/LlmSession）
    /// </summary>
    public static class LlmReadChunkBrick
    {
        /// <summary>
        /// 消费流式分片——无分片返回 false（调用方稍后重试）
        /// </summary>
        /// <param name="requestId">流式会话 ID</param>
        /// <param name="contentDelta">正文增量</param>
        /// <param name="reasoningDelta">推理增量</param>
        /// <param name="toolCallsJson">工具调用 JSON（终态时携带聚合结果）</param>
        /// <param name="finished">终态标志——true=流结束</param>
        /// <param name="errorCode">错误码——终态且失败时非空</param>
        /// <returns>true=取到分片；false=暂无可取或会话不存在</returns>
        public static bool ReadChunk(string requestId, out string contentDelta,
            out string reasoningDelta, out string toolCallsJson,
            out bool finished, out string errorCode)
        {
            contentDelta = "";
            reasoningDelta = "";
            toolCallsJson = "";
            finished = false;
            errorCode = "";
            LlmStreamSession? session = LlmSession.FindSession(requestId);
            if (session == null)
            {
                return false;
            }
            LlmStreamChunk? chunk;
            if (!session.Chunks.TryDequeue(out chunk) || chunk == null)
            {
                return false;
            }
            session.LastChunk = chunk;
            contentDelta = chunk.ContentDelta;
            reasoningDelta = chunk.ReasoningDelta;
            toolCallsJson = chunk.ToolCallsJson;
            finished = chunk.Finished;
            errorCode = chunk.ErrorCode;
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:BAAD90440915CBE95542212E32F1A304CB8909135548E92A929D50337A91E545

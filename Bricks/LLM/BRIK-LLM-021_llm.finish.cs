// ═══════════════════════════════════════════════════
// 积木: llm.finish
// ID:   BRIK-LLM-021
// 类别: LLM
// 作用: 终止流式会话——取消请求并清理资源
// 依赖: 无
// 引用: 无
// 原理: 会话表移除 → Cancel → 等待后台任务 ≤2s → Dispose
// 常用: TalkCat 会话收尾（T_Finish/T_HandleError）
// ═══════════════════════════════════════════════════

using Mau.Runtime;
namespace Mau.Bricks
{
    /// <summary>
    /// LLM 积木——llm.finish 终止流式会话（依赖 LlmBridge/LlmSession）
    /// </summary>
    public static class LlmFinishBrick
    {
        /// <summary>
        /// 终止流式会话——取消请求并清理资源
        /// </summary>
        /// <param name="requestId">流式会话 ID</param>
        /// <returns>true=找到并终止</returns>
        public static bool Finish(string requestId)
        {
            return LlmSession.RemoveSession(requestId);
        }
    }
}
// #MAU_CHECKSUM:SHA256:10E696D80309F3E08272D21DF7564C5FBF85D3F2071D17B6C15708F811BE25E1

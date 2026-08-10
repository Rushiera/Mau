// ═══════════════════════════════════════════════════
// 积木: llm.retry_empty
// ID:   BRIK-LLM-023
// 类别: LLM
// 作用: 空回复续传计数——按会话维度计数 + 耗尽判断（CH2 最多 2 次续传移植）
// 依赖: 无
// 包: 无
// 引用: Mau.Runtime
// 原理: DataBox scope "llm.retry"（sessionKey → int）——当前计数 ≥ max → exhausted=true；否则计数+1 写回
// 常用: TalkCat 语料 T_CheckRetry——空回复时判断是否还能续传（计数在 llm.content_empty 非空时清零）
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// LLM 积木——llm.retry_empty 空回复续传计数与耗尽判断（DataBox scope "llm.retry"）
    /// </summary>
    public static class LlmRetryEmptyBrick
    {
        /// <summary>
        /// 空回复续传计数——计数 +1 并判断是否达到上限
        /// </summary>
        /// <param name="sessionKey">会话 Key（计数键——重发换 requestId，计数按会话维度）</param>
        /// <param name="maxRetries">最大续传次数（CH2 语义：2 次）</param>
        /// <param name="retryCount">当前已续传次数（本次 +1 后）</param>
        /// <param name="exhausted">true=已耗尽（不再续传）</param>
        /// <returns>true=成功</returns>
        public static bool RetryEmpty(string sessionKey, long maxRetries, out long retryCount, out bool exhausted)
        {
            retryCount = 0;
            exhausted = false;
            string key = ContextStore.SafeKey(sessionKey);
            long current = 0;
            if (DataBox.TryGet<long>("llm.retry", key, out current))
            {
                retryCount = current;
            }
            if (retryCount >= maxRetries)
            {
                exhausted = true;
                return true;
            }
            retryCount = retryCount + 1;
            DataBox.Set<long>("llm.retry", key, retryCount);
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:FA8E9A188E28C474D17BBB2C8AB7669A8497DA8776BA6342B2A27BC41BA43887

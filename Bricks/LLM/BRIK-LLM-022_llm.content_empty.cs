// ═══════════════════════════════════════════════════
// 积木: llm.content_empty
// ID:   BRIK-LLM-022
// 类别: LLM
// 作用: 本轮回复是否为空——会话正文累计字符数 == 0（空回复续传检测；非空时清空续传计数）
// 依赖: 无
// 包: 无
// 引用: Mau.Runtime
// 原理: LlmStreamSession.ContentChars（ParseStreamEvent 正文累计）→ 返回 true=空（可续传）；false=非空（正常回复，同时清 llm.retry_empty 计数）
//       🔴 返回值语义 = 空（判断积木同构 is_tool：true → 第一后置）——out 参数不可靠（翻译器只看返回值）
// 常用: TalkCat 语料 T_CheckContent——终态无错误无工具时先查空回复（CH2 空 content 续传移植 2026-08-10）
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// LLM 积木——llm.content_empty 本轮回复为空检测（依赖 LlmSession）
    /// </summary>
    public static class LlmContentEmptyBrick
    {
        /// <summary>
        /// 本轮回复是否为空——true=空（可续传）；false=非空（正常回复，同时清空续传计数 DataBox scope "llm.retry"）
        /// </summary>
        /// <param name="sessionKey">会话 Key（续传计数清零键）</param>
        /// <param name="requestId">会话 ID（正文累计查询）</param>
        /// <returns>true=回复为空（可续传）；false=回复非空或会话不存在（正常路径兜底）</returns>
        public static bool ContentEmpty(string sessionKey, string requestId)
        {
            LlmStreamSession? session = LlmSession.FindSession(requestId);
            if (session == null)
            {
                // 会话不存在——按非空处理（正常路径兜底，后续 is_tool/finish 自行处理）
                return false;
            }
            if (session.ContentChars <= 0)
            {
                return true;
            }
            // 正常回复——续传计数清零（跨请求持久；重发会换 requestId，计数按会话维度）
            DataBox.Remove("llm.retry", ContextStore.SafeKey(sessionKey));
            return false;
        }
    }
}
// #MAU_CHECKSUM:SHA256:32A8D9750A00DD3021ACC808EEBA23BA8FA271C46FFA738E1A664A7E5D2C01D9

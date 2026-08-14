// ═══════════════════════════════════════════════════
// 积木: llm.completions
// ID:   BRIK-LLM-001
// 类别: LLM
// 作用: 非流式一次完成——system + content → reply（薄壳：LLM 走 Runtime 接口系列，不走积木内嵌逻辑）
// 依赖: 无
// 引用: Mau.Runtime（ILlmRuntime/DataBox）
// 原理: DataBox.TryResolve<ILlmRuntime> → Completions(system, content, out reply)
// 常用: CH4 第一轮 quick_cat 语料——QuickCat 执行体（多参验证：system + content）
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// LLM 积木——llm.completions 非流式完成（薄壳转发 ILlmRuntime）
    /// </summary>
    public static class LlmCompletionsBrick
    {
        /// <summary>
        /// 非流式一次完成——system + content → reply
        /// </summary>
        /// <param name="system">系统提示词</param>
        /// <param name="content">用户内容</param>
        /// <param name="reply">回复——失败时携带 ERR| 错误文本</param>
        /// <returns>true=成功</returns>
        public static bool Completions(string system, string content, out string reply)
        {
            ILlmRuntime? runtime;
            DataBox.TryResolve<ILlmRuntime>(out runtime);
            if (runtime == null)
            {
                reply = "ERR|LLM_NO_RUNTIME|宿主未注入 ILlmRuntime";
                return false;
            }
            return runtime.Completions(system, content, out reply);
        }
    }
}
// #MAU_CHECKSUM:SHA256:DF164C73F881FBCF2D64ABE20A2C5D3B06F9A8F6EDADD1DD62C4760FCBDDC35E

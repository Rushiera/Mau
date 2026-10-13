// ═══════════════════════════════════════════════════
// 积木: llm.simple_read
// ID:   BRIK-LLM-009
// 类别: LLM
// 作用: 一次性请求取数——读段盒 llm_simple_reply:<requestId>（生成文本或 ERR| 文本）
// 依赖: 无
// 引用: Mau.Runtime（DataBox）
// 原理: DataBox.TryGet<string>("global", "llm_simple_reply:" + requestId, out text)——键缺失/空返回 false（语料分叉依据）
// 常用: LLMSimpleRequest 服务语料 T_Reply——完成取数（与 llm.simple_ready 成对使用）
// 注意: 失败侧 ERR| 文本也在值里（成功/失败由文本内容区分——失败可见）；空串进入失败侧
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// LLM 积木——llm.simple_read 一次性请求取数（读段盒完整文本）。
    /// </summary>
    public static class LlmSimpleReadBrick
    {
        /// <summary>
        /// 读取一次性请求结果——不存在（未完成/未启动）返回空串（空串走失败侧）
        /// </summary>
        /// <param name="requestId">请求标识（取 OA officeId）</param>
        /// <param name="text">结果文本——缺失/未完成为空串</param>
        /// <returns>true=键存在且非空（语料分叉依据）</returns>
        public static bool SimpleRead(long requestId, out string text)
        {
            text = "";
            if (requestId <= 0)
            {
                return false;
            }
            string got;
            if (DataBox.TryGet<string>("global", "llm_simple_reply:" + requestId.ToString(), out got))
            {
                if (got != null && got.Length > 0)
                {
                    text = got;
                    return true;
                }
            }
            return false;
        }
    }
}
// #MAU_CHECKSUM:SHA256:AC66F18C938D0A0E71123D48259425811B0E10077D2E477632444A6E7596D1DB

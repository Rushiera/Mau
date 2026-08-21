// ═══════════════════════════════════════════════════
// 积木: llm.reply_read
// ID:   BRIK-LLM-005
// 类别: LLM
// 作用: 完整回复取数——按 FlowContext.CurrentFlowId 读本实例段盒 llm_reply:<f>（P9.2 Key 段化——语料零 flowId 感知）
// 依赖: 无
// 引用: Mau.Runtime（DataBox/FlowContext）
// 原理: DataBox.TryGet<string>("global", "llm_reply:" + flowId, out reply)——键缺失/未完成/上下文外返回空串（失败侧语义由语料转移线承载）
// 常用: CH4 P9.2 quick_cat 语料 T_Done——替换 data.box_get_str 全局读（多实例隔离取数）
// 注意: 与 llm.done_ready 成对使用（先判定完成再取数）；空串进入失败侧——成功侧语义由 done_ready 前置保证
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// LLM 积木——llm.reply_read 完整回复取数（按当前 Flow 实例段读盒）
    /// </summary>
    public static class LlmReplyReadBrick
    {
        /// <summary>
        /// 读取本实例完整回复——不存在（流未完成/未启动/上下文外）返回空串（空串走失败侧）
        /// </summary>
        /// <param name="reply">完整回复文本——缺失/未完成/上下文外为空串</param>
        /// <returns>true=键存在且读取成功（空值 false——语料分叉依据）</returns>
        public static bool ReplyRead(out string reply)
        {
            long flowId = FlowContext.CurrentFlowId;
            if (flowId <= 0)
            {
                reply = "";
                return false;
            }
            string? got;
            if (DataBox.TryGet<string>("global", "llm_reply:" + flowId.ToString(), out got))
            {
                if (got != null && got.Length > 0)
                {
                    reply = got;
                    return true;
                }
            }
            reply = "";
            return false;
        }
    }
}
// #MAU_CHECKSUM:SHA256:A175B341010544FF00AEBB5461B6BA01014CFD24432F9E21F83DFE1E38CF1A29

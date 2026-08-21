// ═══════════════════════════════════════════════════
// 积木: llm.chunk_read
// ID:   BRIK-LLM-006
// 类别: LLM
// 作用: 最新分块取数——按 FlowContext.CurrentFlowId 读本实例段盒 llm_chunk:<f>（P9.2 Key 段化——语料零 flowId 感知）
// 依赖: 无
// 引用: Mau.Runtime（DataBox/FlowContext）
// 原理: DataBox.TryGet<string>("global", "llm_chunk:" + flowId, out chunk)——键缺失/上下文外返回空串（失败侧语义由语料转移线承载）
// 常用: CH4 P9.2 quick_cat 语料 T_Chunk——替换 data.box_get_str 全局读（多实例隔离取数）
// 注意: 与 llm.chunk_ready 成对使用（先判定新分块再取数）；空串进入失败侧——成功侧语义由 chunk_ready 前置保证
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// LLM 积木——llm.chunk_read 最新分块取数（按当前 Flow 实例段读盒）
    /// </summary>
    public static class LlmChunkReadBrick
    {
        /// <summary>
        /// 读取本实例最新分块——不存在（流未启动/上下文外）返回空串（空串走失败侧）
        /// </summary>
        /// <param name="chunk">最新分块文本——缺失/上下文外为空串</param>
        /// <returns>true=键存在且读取成功（空值 false——语料分叉依据）</returns>
        public static bool ChunkRead(out string chunk)
        {
            long flowId = FlowContext.CurrentFlowId;
            if (flowId <= 0)
            {
                chunk = "";
                return false;
            }
            string? got;
            if (DataBox.TryGet<string>("global", "llm_chunk:" + flowId.ToString(), out got))
            {
                if (got != null && got.Length > 0)
                {
                    chunk = got;
                    return true;
                }
            }
            chunk = "";
            return false;
        }
    }
}
// #MAU_CHECKSUM:SHA256:744952E67B83FF4839A928E52BB0D6FB3E3770067FF4D5A2D4A0BE42821E9629

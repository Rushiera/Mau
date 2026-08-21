// ═══════════════════════════════════════════════════
// 积木: llm.chunk_ready
// ID:   BRIK-LLM-003
// 类别: LLM
// 作用: 分块探测——本实例 llm_chunk_count:<f> 有新增量 → true（判断语义：值比较归宿主）
// 依赖: 无
// 引用: Mau.Runtime（DataBox/FlowContext）
// 原理: 读本实例计数段盒 llm_chunk_count:<f> 对比已消费序号 llm_chunk_seen:<f>；新增 → 更新 seen + true
// 盒子: 读全局 llm_chunk_count:<f>（B1 豁免）· 写 llm_chunk_seen:<f>
// 常用: CH4 P4 quick_cat 语料——分块信号（主动传感器壳探测）
// 注意: P9.2 Key 段化——静态 lastSeen 退役（跨实例共享缺陷）；seen 盒子化（观测可见 + 实例隔离）；count < seen 归零检测语义保留（llm.stream 启动清理 seen）
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// LLM 积木——llm.chunk_ready 分块探测（判断语义：本实例有新分块才 true）
    /// </summary>
    public static class LlmChunkReadyBrick
    {
        /// <summary>
        /// 分块探测——本实例 llm_chunk_count:<f> 有新增量（flowId 由 FlowRunner 驱动注入——主线程语料壳调用）
        /// </summary>
        /// <returns>true=有新分块（本实例）</returns>
        public static bool ChunkReady()
        {
            long flowId = FlowContext.CurrentFlowId;
            if (flowId <= 0)
            {
                return false;
            }
            string seg = flowId.ToString();
            long count;
            if (!DataBox.TryGet<long>("global", "llm_chunk_count:" + seg, out count))
            {
                return false;
            }
            long seen = 0;
            long seenBox;
            if (DataBox.TryGet<long>("global", "llm_chunk_seen:" + seg, out seenBox))
            {
                seen = seenBox;
            }
            if (count < seen)
            {
                // 新流式会话（llm.stream 已重置计数）——seen 归零对齐
                seen = 0;
                DataBox.Set<long>("global", "llm_chunk_seen:" + seg, 0);
            }
            if (count > seen)
            {
                DataBox.Set<long>("global", "llm_chunk_seen:" + seg, count);
                return true;
            }
            return false;
        }
    }
}
// #MAU_CHECKSUM:SHA256:AAF3086614E886D0C89B54D3FF696A7760BF3CDCDB623BE43EFFC27BF45A96DB

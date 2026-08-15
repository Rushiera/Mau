// ═══════════════════════════════════════════════════
// 积木: llm.chunk_ready
// ID:   BRIK-LLM-003
// 类别: LLM
// 作用: 分块探测——llm_chunk_count 有新增量 → true（判断语义：值比较归宿主）
// 依赖: 无
// 引用: Mau.Runtime（DataBox）
// 原理: 静态 lastSeen 计数对比全局盒 llm_chunk_count；新增 → 更新 lastSeen + true
// 盒子: 读全局 llm_chunk_count（B1 豁免）
// 常用: CH4 P4 quick_cat 语料——分块信号（主动传感器壳探测）
// 注意: 静态 lastSeen 跨实例共享——P4 单猫语义；P9 多猫并发时 scope 化改造
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// LLM 积木——llm.chunk_ready 分块探测（判断语义：有新分块才 true）
    /// </summary>
    public static class LlmChunkReadyBrick
    {
        /// <summary>
        /// 上次已消费的分片序号——P4 单猫静态；P9 多猫 scope 化
        /// </summary>
        private static long _lastSeenCount = 0;

        /// <summary>
        /// 分块探测——llm_chunk_count 有新增量
        /// </summary>
        /// <returns>true=有新分块</returns>
        public static bool ChunkReady()
        {
            long count;
            if (!DataBox.TryGet<long>("global", "llm_chunk_count", out count))
            {
                return false;
            }
            if (count < _lastSeenCount)
            {
                // 新流式会话（llm.stream 已重置计数）——lastSeen 归零对齐
                _lastSeenCount = 0;
            }
            if (count > _lastSeenCount)
            {
                _lastSeenCount = count;
                return true;
            }
            return false;
        }
    }
}
// #MAU_CHECKSUM:SHA256:11B851A893B47A73A1F80B75F29BC4860A0FE66551DE970FE744D10A41BC44EC

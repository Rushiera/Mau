// ═══════════════════════════════════════════════════
// 积木: probe.sink_slow
// ID:   BRIK-TEST-003
// 类别: TEST
// 作用: 延迟探针——睡眠 delayMs 后返回（模拟真实慢 IO——∥ Busy 门/并发槽位压测必要）
// 依赖: 无
// 引用: System.Threading
// 原理: Thread.Sleep(delayMs) 后台线程占用 → ∥ 律 Busy 窗口真实化——并发/滞留/超时语义可测
// 常用: T4 模块谱任务流水线——'probe.sink_slow'["task-1", 'delayMs']
// ═══════════════════════════════════════════════════
using System.Threading;

namespace Mau.Bricks
{
    /// <summary>
    /// 测试探针积木——probe.sink_slow 延迟探针（∥ 并发窗口压测）
    /// </summary>
    public static class ProbeSinkSlowBrick
    {
        /// <summary>
        /// 延迟探针——睡眠 delayMs 毫秒后返回成功
        /// </summary>
        /// <param name="a">文本输入</param>
        /// <param name="delayMs">延迟毫秒（0=立即）</param>
        /// <param name="result">结果</param>
        /// <returns>true=成功</returns>
        public static bool SinkSlow(string a, int delayMs, out string result)
        {
            if (delayMs > 0)
            {
                Thread.Sleep(delayMs);
            }
            result = a;
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:89D25F5140647FA081BE1C27688DD6D944AFC71E8865D1961E753DDE5FA7F64E

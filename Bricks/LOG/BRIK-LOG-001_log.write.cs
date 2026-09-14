// ═══════════════════════════════════════════════════
// 积木: log.write
// ID:   BRIK-LOG-001
// 类别: LOG
// 作用: 写一条结构化日志——内存总账 + 可选磁盘持久化（薄壳：LogStore 机制入口）
// 依赖: 无
// 引用: Mau.Runtime（LogStore）
// 原理: LogStore.Add("CH4", level, text, category)——category CMD/OA 专属类别埋点
// 常用: CH4 第一轮三 Cat 语料——执行记录/透明性指标（杂音过滤：level 分级）
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 日志积木——log.write 写结构化日志（薄壳转发 LogStore）
    /// </summary>
    public static class LogWriteBrick
    {
        /// <summary>
        /// 写一条结构化日志——内存总账 + 可选磁盘持久化
        /// </summary>
        /// <param name="text">消息</param>
        /// <param name="level">级别——0/1=INFO（LogStore.Add 归一：level ≤ 0 → 1）2=WARN 3=ERROR</param>
        /// <param name="category">类别——""=普通 / "CMD"=Command 总线 / "OA"=工单系统</param>
        /// <returns>true=已入总账</returns>
        public static bool Write(string text, int level, string category)
        {
            LogStore.Add("CH4", level, text, category);
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:3FB5466D2746925B048F6A61F711151121A722057EF92538A52BCBEDB1F15866

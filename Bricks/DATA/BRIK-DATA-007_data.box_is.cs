// ═══════════════════════════════════════════════════
// 积木: data.box_is
// ID:   BRIK-DATA-007
// 类别: DATA
// 作用: 判断单值是否等于预期——boxId 作用域隔离的键值比较（判断积木：返回=判断结果）
// 依赖: 无
// 引用: Mau.Runtime
// 原理: 委托 BoxStore.Get（未命中取默认值）→ 与 expected 比较
// 常用: 跨模块完成信号轮询——TalkCat 轮询 ToolPoster 批次完成标记（M2c）
// ═══════════════════════════════════════════════════

using Mau.Runtime;
namespace Mau.Bricks
{
    /// <summary>
    /// 数据积木——data.box_is 判断单值是否等于预期（依赖 BoxStore）
    /// </summary>
    public static class DataBoxIsBrick
    {
        /// <summary>
        /// 判断单值是否等于预期——判断积木（返回=判断结果，非查询成功）
        /// </summary>
        /// <param name="boxId">作用域 ID</param>
        /// <param name="key">键</param>
        /// <param name="expected">预期值</param>
        /// <returns>true=当前值等于预期</returns>
        public static bool Is(string boxId, string key, int expected)
        {
            int value;
            BoxStore.Get(boxId, key, -1, out value);
            return value == expected;
        }
    }
}
// #MAU_CHECKSUM:SHA256:5E8A55B6B01E748C4B7F5A8E52D8F7453F1FA3232466DFA457BFDDE7AD8E68A4

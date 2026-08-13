// ═══════════════════════════════════════════════════
// 积木: data.box_inc
// ID:   BRIK-DATA-009
// 类别: DATA
// 作用: 原子递增——box[boxId, key] += 1（锁内读改写，跨线程安全）
// 依赖: 无
// 引用: 无
// 原理: 委托 BoxStore.Inc（IncLock 互斥——∥ 后台线程并发计数安全）
// 常用: 并发完成计数/信号量/事件计数（T4 模块谱新增——原子原语补齐）
// ═══════════════════════════════════════════════════

using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 数据积木——data.box_inc 原子递增（并发安全计数）
    /// </summary>
    public static class DataBoxIncBrick
    {
        /// <summary>
        /// 原子递增——box[boxId, key] += 1
        /// </summary>
        /// <param name="boxId">作用域 ID</param>
        /// <param name="key">键</param>
        /// <returns>true=成功</returns>
        public static bool Inc(string boxId, string key)
        {
            return BoxStore.Inc(boxId, key);
        }
    }
}
// #MAU_CHECKSUM:SHA256:85DFF0D9C585A99B6056E5AC1DD1D3680CE68DC3310F48843962D9214F2DDDEA

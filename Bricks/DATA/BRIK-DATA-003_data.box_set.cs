// ═══════════════════════════════════════════════════
// 积木: data.box_set
// ID:   BRIK-DATA-003
// 类别: DATA
// 作用: 写入单值——boxId 作用域隔离的键值存储
// 依赖: 无
// 引用: 无
// 原理: 委托 BoxStore.Set
// 常用: 跨帧状态保留 / 模块私有数据缓存
// ═══════════════════════════════════════════════════

using Mau.Runtime;
namespace Mau.Bricks
{
    /// <summary>
    /// 数据积木——data.box_set 写入单值（依赖 BoxStore）
    /// </summary>
    public static class DataBoxSetBrick
    {
        /// <summary>
        /// 写入单值
        /// </summary>
        /// <param name="boxId">作用域 ID</param>
        /// <param name="key">键</param>
        /// <param name="value">值</param>
        /// <returns>true=成功</returns>
        public static bool Set(string boxId, string key, int value)
        {
            return BoxStore.Set(boxId, key, value);
        }
    }
}
// #MAU_CHECKSUM:SHA256:FA4689384AD621A678826907217CCD3DC17182F57C373495B527F0F668FA6F0A

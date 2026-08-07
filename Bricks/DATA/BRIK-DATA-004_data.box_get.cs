// ═══════════════════════════════════════════════════
// 积木: data.box_get
// ID:   BRIK-DATA-004
// 类别: DATA
// 作用: 读取单值——boxId 作用域隔离的键值存储
// 依赖: 无
// 引用: 无
// 原理: 委托 BoxStore.Get（未命中返回默认值）
// 常用: 跨帧状态读取 / 模块私有数据
// ═══════════════════════════════════════════════════

using Mau.Runtime;
namespace Mau.Bricks
{
    /// <summary>
    /// 数据积木——data.box_get 读取单值（依赖 BoxStore）
    /// </summary>
    public static class DataBoxGetBrick
    {
        /// <summary>
        /// 读取单值
        /// </summary>
        /// <param name="boxId">作用域 ID</param>
        /// <param name="key">键</param>
        /// <param name="defaultValue">默认值</param>
        /// <param name="value">读取值</param>
        /// <returns>true=成功</returns>
        public static bool Get(string boxId, string key, int defaultValue, out int value)
        {
            return BoxStore.Get(boxId, key, defaultValue, out value);
        }
    }
}
// #MAU_CHECKSUM:SHA256:20D785C0B956DECBD7C25C704312EEA82E433FA61EB721AFBCCC60B6FBC6EE65

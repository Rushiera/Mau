// ═══════════════════════════════════════════════════
// 积木: oa.get_int
// ID:   BRIK-OA-008
// 类别: OA
// 作用: 读取请求载荷 int 值——执行方消费
// 依赖: 无
// 引用: Mau.Runtime
// 原理: 经 OaBridge 实例调用 IOA.GetInt
// 常用: 执行方读请求参数
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// OA 积木——oa.get_int 读请求载荷 int 值（依赖 OaBridge）
    /// </summary>
    public static class OaGetIntBrick
    {
        /// <summary>
        /// 读取请求载荷 int 值——执行方消费
        /// </summary>
        /// <param name="officeId">Office ID</param>
        /// <param name="key">Key</param>
        /// <param name="value">int 值</param>
        /// <returns>true=Key 存在</returns>
        public static bool GetInt(long officeId, string key, out int value)
        {
            IOA? oa;
            DataBox.TryResolve<IOA>(out oa);
            if (oa == null)
            {
                value = 0;
                return false;
            }
            return oa.GetInt(officeId, key, out value);
        }
    }
}
// #MAU_CHECKSUM:SHA256:57104BC28B5D530D2F8B737C3F1E255C9BF08746CC7A3B70AF3EC6804A7BFDC4

// ═══════════════════════════════════════════════════
// 积木: oa.set_str
// ID:   BRIK-OA-007
// 类别: OA
// 作用: 写入请求载荷 str 值——仅 Open 状态 + 本人（挂单方）可操作
// 依赖: 无
// 引用: Mau.Runtime
// 原理: 经 OaBridge 实例调用 IOA.SetStr
// 常用: 挂单方写请求载荷（ID/时间戳/JSON）
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// OA 积木——oa.set_str 写请求载荷 str 值（依赖 OaBridge）
    /// </summary>
    public static class OaSetStrBrick
    {
        /// <summary>
        /// 写入请求载荷 str 值——仅 Open 状态 + 本人（挂单方）可操作
        /// </summary>
        /// <param name="officeId">Office ID</param>
        /// <param name="dogId">所有者 LongId</param>
        /// <param name="key">Key</param>
        /// <param name="value">str 值</param>
        /// <returns>true=写入成功</returns>
        public static bool SetStr(long officeId, long dogId, string key, string value)
        {
            IOA? oa;
            DataBox.TryResolve<IOA>(out oa);
            if (oa == null)
            {
                return false;
            }
            return oa.SetStr(officeId, dogId, key, value);
        }
    }
}
// #MAU_CHECKSUM:SHA256:5C602B968E02BB694AF30BF433DC16DAC4E33E6512784808C7DD7CF9E95AB6D6

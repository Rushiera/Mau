// ═══════════════════════════════════════════════════
// 积木: oa.set_int
// ID:   BRIK-OA-006
// 类别: OA
// 作用: 写入请求载荷 int 值——仅 Open 状态 + 本人（挂单方）可操作
// 依赖: 无
// 引用: Mau.Runtime
// 原理: 经 OaBridge 实例调用 IOA.SetInt
// 常用: 挂单方写请求载荷
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// OA 积木——oa.set_int 写请求载荷 int 值（依赖 OaBridge）
    /// </summary>
    public static class OaSetIntBrick
    {
        /// <summary>
        /// 写入请求载荷 int 值——仅 Open 状态 + 本人（挂单方）可操作
        /// </summary>
        /// <param name="officeId">Office ID</param>
        /// <param name="dogId">所有者 LongId</param>
        /// <param name="key">Key</param>
        /// <param name="value">int 值</param>
        /// <returns>true=写入成功</returns>
        public static bool SetInt(long officeId, long dogId, string key, int value)
        {
            IOA? oa;
            DataBox.TryResolve<IOA>(out oa);
            if (oa == null)
            {
                return false;
            }
            return oa.SetInt(officeId, dogId, key, value);
        }
    }
}
// #MAU_CHECKSUM:SHA256:D99723F9C37456C420522B14890C0C1189B2CA1E746449A5DA7E355451E2A1A2

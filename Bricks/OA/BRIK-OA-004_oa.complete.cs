// ═══════════════════════════════════════════════════
// 积木: oa.complete
// ID:   BRIK-OA-004
// 类别: OA
// 作用: 完成工单——写入回执双字典载荷 → 状态变 Closed
// 依赖: 无
// 引用: Mau.Runtime
// 原理: 经 OaBridge 实例调用 IOA.Complete
// 常用: 执行方完成工具单回执
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// OA 积木——oa.complete 完成工单（依赖 OaBridge）
    /// </summary>
    public static class OaCompleteBrick
    {
        /// <summary>
        /// 完成工单——写入回执双字典载荷 → 状态变 Closed
        /// </summary>
        /// <param name="officeId">Office ID</param>
        /// <param name="catId">认领者 LongId</param>
        /// <param name="result">回执双字典载荷</param>
        /// <returns>true=完成成功</returns>
        public static bool Complete(long officeId, long catId, OfficeData result)
        {
            IOA? oa;
            DataBox.TryResolve<IOA>(out oa);
            if (oa == null)
            {
                return false;
            }
            oa.Complete(officeId, catId, result);
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:BD5BBAABF48ABC1F79053B9E3A42B81D70C7948DC9AE15E0D00516DD805EFCC0

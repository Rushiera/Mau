// ═══════════════════════════════════════════════════
// 积木: oa.claim_one_simple
// ID:   BRIK-OA-014
// 类别: OA
// 作用: 认领单个工单——officeId 单数输入，bool 输出（Office 复杂端口语料不可构造的简化认领）
// 依赖: 无
// 引用: Mau.Runtime
// 原理: 经 OaBridge 实例调用 IOA.ClaimBatch（单元素数组）
// 常用: 执行方语料——已知 officeId 的认领（M2 Dog 演示：自闭环）
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// OA 积木——oa.claim_one_simple 认领单个（依赖 OaBridge）
    /// </summary>
    public static class OaClaimOneSimpleBrick
    {
        /// <summary>
        /// 认领单个工单——officeId 单数输入（对标 claim 数组/claim_one Office 输出——语料可构造）
        /// </summary>
        /// <param name="catId">认领者 LongId</param>
        /// <param name="officeId">待锁 OfficeId</param>
        /// <returns>true=锁单成功</returns>
        public static bool ClaimOneSimple(long catId, long officeId)
        {
            IOA? oa;
            DataBox.TryResolve<IOA>(out oa);
            if (oa == null)
            {
                return false;
            }
            System.Collections.Generic.List<Office> claimed = oa.ClaimBatch(catId, new long[] { officeId });
            return claimed.Count > 0;
        }
    }
}
// #MAU_CHECKSUM:SHA256:547459BD544FAF47B36F548D54ED9CCC1BDD37889A723A5E2491B430615152F8

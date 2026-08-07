// ═══════════════════════════════════════════════════
// 积木: oa.claim_one
// ID:   BRIK-OA-010
// 类别: OA
// 作用: 单单认领——语料展开工具循环用（dispatch_one 输出单值 officeId，无需组数组）
// 依赖: 无
// 引用: Mau.Runtime
// 原理: 经 OaBridge 实例 ClaimBatch 单元素数组，取首个
// 常用: 工具循环逐单认领
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// OA 积木——oa.claim_one 单单认领（依赖 OaBridge）
    /// </summary>
    public static class OaClaimOneBrick
    {
        /// <summary>
        /// 单单认领——语料展开工具循环用（dispatch_one 输出单值 officeId，无需组数组）
        /// </summary>
        /// <param name="catId">认领者 LongId</param>
        /// <param name="officeId">待锁的单个 OfficeId</param>
        /// <param name="claimed">锁成功的 Office（失败为默认值）</param>
        /// <returns>true=锁单成功</returns>
        public static bool ClaimOne(long catId, long officeId, out Office claimed)
        {
            IOA? oa;
            DataBox.TryResolve<IOA>(out oa);
            if (oa == null)
            {
                claimed = new Office();
                return false;
            }
            System.Collections.Generic.List<Office> result = oa.ClaimBatch(catId, new long[] { officeId });
            if (result.Count > 0)
            {
                claimed = result[0];
                return true;
            }
            claimed = new Office();
            return false;
        }
    }
}
// #MAU_CHECKSUM:SHA256:B17344E94B821DDD6F5FFB2A2B4170AA38232BAE4CCBDF495624916043D0ABF2

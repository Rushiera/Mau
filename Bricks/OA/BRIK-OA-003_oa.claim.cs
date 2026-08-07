// ═══════════════════════════════════════════════════
// 积木: oa.claim
// ID:   BRIK-OA-003
// 类别: OA
// 作用: 锁单——逐个尝试认领，已被别人取走的跳过，返回锁成功的名单
// 依赖: 无
// 引用: Mau.Runtime
// 原理: 经 OaBridge 实例调用 IOA.ClaimBatch 转数组
// 常用: 消费者批量认领工单
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// OA 积木——oa.claim 锁单（依赖 OaBridge）
    /// </summary>
    public static class OaClaimBrick
    {
        /// <summary>
        /// 锁单——逐个尝试认领，已被别人取走的跳过，返回锁成功的名单
        /// </summary>
        /// <param name="catId">认领者 LongId</param>
        /// <param name="officeIds">待锁的 OfficeId 数组</param>
        /// <param name="claimed">锁成功的 Office 列表</param>
        /// <returns>true=认领操作成功（可能部分成功）</returns>
        public static bool Claim(long catId, long[] officeIds, out Office[] claimed)
        {
            IOA? oa;
            DataBox.TryResolve<IOA>(out oa);
            if (oa == null)
            {
                claimed = new Office[0];
                return false;
            }
            claimed = oa.ClaimBatch(catId, officeIds).ToArray();
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:5E137CA6AF8CA2B727B8B86BAB598AF66558C67877FE9584993E823C14898081

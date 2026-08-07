// ═══════════════════════════════════════════════════
// 积木: oa.claim_next_simple
// ID:   BRIK-OA-016
// 类别: OA
// 作用: 原子接单——按 officeName 候选 ListOpen 后逐个 ClaimBatch，第一个成功即返回 officeId
// 依赖: 无
// 引用: Mau.Runtime
// 原理: 经 OaBridge 实例调用 IOA.ListOpen + ClaimBatch（单元素）
// 常用: 工具 Cat 语料——接单循环（M2b：claim_next → 读载荷 → 执行 → complete_result）
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// OA 积木——oa.claim_next_simple 原子接单（依赖 OaBridge）
    /// </summary>
    public static class OaClaimNextSimpleBrick
    {
        /// <summary>
        /// 原子接单——按 officeName 候选 ListOpen 后逐个 ClaimBatch，第一个成功即返回
        /// </summary>
        /// <param name="catId">认领者 LongId</param>
        /// <param name="officeType">工单大类</param>
        /// <param name="officeNames">能力候选数组（逗号分隔——语料可构造）</param>
        /// <param name="officeId">锁成功的第一单 OfficeId（无单为 0）</param>
        /// <returns>true=接到单</returns>
        public static bool ClaimNextSimple(long catId, string officeType, string officeNames, out long officeId)
        {
            IOA? oa;
            DataBox.TryResolve<IOA>(out oa);
            if (oa == null)
            {
                officeId = 0;
                return false;
            }
            string[] names = officeNames.Split(',');
            for (int i = 0; i < names.Length; i = i + 1)
            {
                names[i] = names[i].Trim();
            }
            System.Collections.Generic.List<Office> open = oa.ListOpen(officeType, names);
            for (int i = 0; i < open.Count; i = i + 1)
            {
                System.Collections.Generic.List<Office> claimed = oa.ClaimBatch(catId, new long[] { open[i].OfficeId });
                if (claimed.Count > 0)
                {
                    officeId = open[i].OfficeId;
                    return true;
                }
            }
            officeId = 0;
            return false;
        }
    }
}
// #MAU_CHECKSUM:SHA256:85D4A6EF0100540D4E575AEB80C57046782D24EF32630E3645807C590846038E

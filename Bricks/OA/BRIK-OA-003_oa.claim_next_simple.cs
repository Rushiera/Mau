// ═══════════════════════════════════════════════════
// 积木: oa.claim_next_simple
// ID:   BRIK-OA-003
// 类别: OA
// 作用: 原子接单——按 officeName 候选 ListOpen 后逐个 ClaimBatch，第一个成功即返回 officeId
// 依赖: 无
// 引用: Mau.Runtime（IOA/DataBox）
// 原理: DataBox.TryResolve<IOA> → ListOpen(type, names) → ClaimBatch(workerId, [id]) 循环
// 常用: CH4 第一轮 io_test_cat/quick_cat 语料——接单循环
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// OA 积木——oa.claim_next_simple 原子接单
    /// </summary>
    public static class OaClaimNextSimpleBrick
    {
        /// <summary>
        /// 原子接单——按 officeName 候选 ListOpen 后逐个 ClaimBatch，第一个成功即返回
        /// </summary>
        /// <param name="officeType">工单大类</param>
        /// <param name="officeNames">能力候选数组（逗号分隔——语料可构造）</param>
        /// <param name="officeId">锁成功的第一单 OfficeId（无单为 0）</param>
        /// <returns>true=接到单</returns>
        public static bool ClaimNextSimple(string officeType, string officeNames, out long officeId)
        {
            IOA? oa;
            DataBox.TryResolve<IOA>(out oa);
            if (oa == null)
            {
                officeId = 0;
                return false;
            }
            long workerId = FlowContext.CurrentFlowId;
            string[] rawNames = officeNames.Split(',', ';');
            System.Collections.Generic.List<string> names = new System.Collections.Generic.List<string>();
            for (int i = 0; i < rawNames.Length; i = i + 1)
            {
                string item = rawNames[i].Trim();
                if (item.Length > 0)
                {
                    names.Add(item);
                }
            }
            System.Collections.Generic.List<Office> open = oa.ListOpen(officeType, names.ToArray());
            for (int i = 0; i < open.Count; i = i + 1)
            {
                System.Collections.Generic.List<Office> claimed = oa.ClaimBatch(workerId, new long[] { open[i].OfficeId });
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
// #MAU_CHECKSUM:SHA256:C230DB5B46F384B3C813DD836BE606949094771A5DC982E6A39090117A7DFB1A

// ═══════════════════════════════════════════════════
// 积木: oa.complete_simple
// ID:   BRIK-OA-012
// 类别: OA
// 作用: 完成工单（无回执）——空 OfficeData 回执 → 状态变 Closed
// 依赖: 无
// 引用: Mau.Runtime
// 原理: 经 OaBridge 实例调用 IOA.Complete（回执=OfficeData.Empty）
// 常用: 执行方完成工具单（无需回执载荷）——语料层无法构造 OfficeData 的兜底
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// OA 积木——oa.complete_simple 完成工单（无回执，依赖 OaBridge）
    /// </summary>
    public static class OaCompleteSimpleBrick
    {
        /// <summary>
        /// 完成工单——空 OfficeData 回执 → 状态变 Closed
        /// </summary>
        /// <param name="officeId">Office ID</param>
        /// <param name="catId">认领者 LongId</param>
        /// <returns>true=完成成功</returns>
        public static bool CompleteSimple(long officeId, long catId)
        {
            IOA? oa;
            DataBox.TryResolve<IOA>(out oa);
            if (oa == null)
            {
                return false;
            }
            oa.Complete(officeId, catId, OfficeData.Empty());
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:0A35065D49E15992B9071EC9B41617909178B3FAC7298245B079050C1AD6CDA4

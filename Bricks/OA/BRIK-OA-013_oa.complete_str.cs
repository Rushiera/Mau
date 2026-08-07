// ═══════════════════════════════════════════════════
// 积木: oa.complete_str
// ID:   BRIK-OA-013
// 类别: OA
// 作用: 完成工单并写单键 str 回执——OfficeData 语料不可构造的带回执完成
// 依赖: 无
// 引用: Mau.Runtime
// 原理: 经 OaBridge 实例调用 IOA.Complete（构造单键回执 OfficeData）
// 常用: 执行方语料——认领后执行完写回执（M2 Dog 演示：moved=A）
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// OA 积木——oa.complete_str 完成 + 单键 str 回执（依赖 OaBridge）
    /// </summary>
    public static class OaCompleteStrBrick
    {
        /// <summary>
        /// 完成工单并写单键 str 回执——OfficeData 语料不可构造的带回执完成（对标 complete_simple 无回执）
        /// </summary>
        /// <param name="officeId">Office ID</param>
        /// <param name="catId">认领者 LongId</param>
        /// <param name="key">回执 Key</param>
        /// <param name="value">回执 str 值</param>
        /// <returns>true=完成成功</returns>
        public static bool CompleteStr(long officeId, long catId, string key, string value)
        {
            IOA? oa;
            DataBox.TryResolve<IOA>(out oa);
            if (oa == null)
            {
                return false;
            }
            OfficeData result = OfficeData.Empty();
            result.Strs[key] = value;
            oa.Complete(officeId, catId, result);
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:2970098835A4EE37F243E6FC2BAF2566D8B2FB44F001519945BC03DFCA507789

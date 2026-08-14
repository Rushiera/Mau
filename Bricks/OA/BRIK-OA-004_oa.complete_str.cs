// ═══════════════════════════════════════════════════
// 积木: oa.complete_str
// ID:   BRIK-OA-004
// 类别: OA
// 作用: 完成工单——写 result 回执载荷后变 Closed
// 依赖: 无
// 引用: Mau.Runtime（IOA/DataBox/OfficeData）
// 原理: DataBox.TryResolve<IOA> → OfficeData.Empty() + Strs["result"] → Complete
// 常用: CH4 第一轮 io_test_cat/quick_cat 语料——执行完成回执
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// OA 积木——oa.complete_str 回执字符串结果
    /// </summary>
    public static class OaCompleteStrBrick
    {
        /// <summary>
        /// 完成工单——写 result 回执载荷后变 Closed
        /// </summary>
        /// <param name="officeId">Office ID</param>
        /// <param name="result">结果文本——写入回执载荷 result 键</param>
        /// <returns>true=回执已提交</returns>
        public static bool CompleteStr(long officeId, string result)
        {
            IOA? oa;
            DataBox.TryResolve<IOA>(out oa);
            if (oa == null)
            {
                return false;
            }
            OfficeData data = OfficeData.Empty();
            data.Strs["result"] = result;
            oa.Complete(officeId, FlowContext.CurrentFlowId, data);
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:FBE3C00718D61163281DE05DF01BCB2F465615AD4CD42A0540EFB868B31B3A08

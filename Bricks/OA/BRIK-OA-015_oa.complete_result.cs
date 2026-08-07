// ═══════════════════════════════════════════════════
// 积木: oa.complete_result
// ID:   BRIK-OA-015
// 类别: OA
// 作用: 完成工单并写 result/error 双键回执——工具执行结果标准形态
// 依赖: 无
// 引用: Mau.Runtime
// 原理: 经 OaBridge 实例调用 IOA.Complete（构造 result/error 双键 OfficeData）
// 常用: 工具 Cat 语料——执行完成后回执（M2b：result=输出文本 / error=错误摘要）
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// OA 积木——oa.complete_result 完成 + result/error 双键回执（依赖 OaBridge）
    /// </summary>
    public static class OaCompleteResultBrick
    {
        /// <summary>
        /// 完成工单并写 result/error 双键回执——工具执行结果标准形态
        /// </summary>
        /// <param name="officeId">Office ID</param>
        /// <param name="catId">认领者 LongId</param>
        /// <param name="result">执行结果文本（空=不写 result 键）</param>
        /// <param name="error">错误摘要（空=不写 error 键）</param>
        /// <returns>true=完成成功</returns>
        public static bool CompleteResult(long officeId, long catId, string result, string error)
        {
            IOA? oa;
            DataBox.TryResolve<IOA>(out oa);
            if (oa == null)
            {
                return false;
            }
            OfficeData data = OfficeData.Empty();
            if (result != null && result.Length > 0)
            {
                data.Strs["result"] = result;
            }
            if (error != null && error.Length > 0)
            {
                data.Strs["error"] = error;
            }
            oa.Complete(officeId, catId, data);
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:F11BA12B6E146D7DB5CC785BCC8DC5D612D024831671FB16DBA6F539DCF826CF

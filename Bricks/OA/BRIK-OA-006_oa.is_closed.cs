// ═══════════════════════════════════════════════════
// 积木: oa.is_closed
// ID:   BRIK-OA-006
// 类别: OA
// 作用: 查询工单是否 Closed——挂单方轮询收结果
// 依赖: 无
// 引用: Mau.Runtime（IOA/DataBox/OfficeState）
// 原理: DataBox.TryResolve<IOA> → GetStatus(officeId) == OfficeState.Closed
// 常用: CH4 第一轮 tool_test_cat 语料——结果回流轮询
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// OA 积木——oa.is_closed 查询工单闭合
    /// </summary>
    public static class OaIsClosedBrick
    {
        /// <summary>
        /// 查询工单是否 Closed
        /// </summary>
        /// <param name="officeId">Office ID</param>
        /// <param name="closed">是否已闭合</param>
        /// <returns>true=查询成功</returns>
        public static bool IsClosed(long officeId, out bool closed)
        {
            IOA? oa;
            DataBox.TryResolve<IOA>(out oa);
            if (oa == null)
            {
                closed = false;
                return false;
            }
            closed = oa.GetStatus(officeId) == OfficeState.Closed;
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:EA02F363C79A1E2FD91C3B864193E17F05EA96415499B871036ABD014A28DCAB

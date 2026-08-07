// ═══════════════════════════════════════════════════
// 积木: oa.settle
// ID:   BRIK-OA-005
// 类别: OA
// 作用: 结算——失败/超时工单的释放处理。Work 单退回 Open（可重投）；已终结单确认返回
// 依赖: 无
// 引用: Mau.Runtime
// 原理: 状态判断——Work → Relist 重投；TimeOut/Closed → 确认终结
// 常用: 失败/超时工单回收
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// OA 积木——oa.settle 结算（依赖 OaBridge）
    /// </summary>
    public static class OaSettleBrick
    {
        /// <summary>
        /// 结算——失败/超时工单的释放处理。Work 单退回 Open（可重投）；已终结单确认返回。
        /// </summary>
        /// <param name="officeId">Office ID</param>
        /// <param name="catId">请求者 LongId</param>
        /// <returns>true=已终结或已重投</returns>
        public static bool Settle(long officeId, long catId)
        {
            IOA? oa;
            DataBox.TryResolve<IOA>(out oa);
            if (oa == null)
            {
                return false;
            }
            OfficeState state = oa.GetStatus(officeId);
            if (state == OfficeState.Work)
            {
                oa.Relist(officeId, catId);
                return true;
            }
            return state == OfficeState.TimeOut || state == OfficeState.Closed;
        }
    }
}
// #MAU_CHECKSUM:SHA256:3A7915D4F382D5CCAE5CB6336C65B2AF6544992FDEC756D8BBA360F7B25218F5

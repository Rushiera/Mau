// ═══════════════════════════════════════════════════
// 积木: oa.is_done
// ID:   BRIK-OA-008
// 类别: OA
// 作用: 终态判断——Closed 或 TimeOut 都算完成（挂单方结果等待的终态探测；判断语义——壳探测落盒即判断结果）
// 依赖: 无
// 引用: Mau.Runtime（IOA/DataBox/OfficeState）
// 原理: DataBox.TryResolve<IOA> → GetStatus(officeId) ∈ {Closed, TimeOut}
// 常用: CH4 第一轮 tool_test_cat 语料——工单结果等待轮询（超时单也可见；officeId≤0 未建单恒 false）
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// OA 积木——oa.is_done 终态判断（判断语义——返回 true = 终态到达）
    /// </summary>
    public static class OaIsDoneBrick
    {
        /// <summary>
        /// 终态判断——工单已闭合或已超时结算；officeId ≤ 0（未建单）或服务缺失恒 false
        /// </summary>
        /// <param name="officeId">Office ID</param>
        /// <returns>true=终态（Closed 或 TimeOut）</returns>
        public static bool IsDone(long officeId)
        {
            IOA? oa;
            DataBox.TryResolve<IOA>(out oa);
            if (oa == null)
            {
                return false;
            }
            if (officeId <= 0)
            {
                return false;
            }
            OfficeState state = oa.GetStatus(officeId);
            return state == OfficeState.Closed || state == OfficeState.TimeOut;
        }
    }
}
// #MAU_CHECKSUM:SHA256:FBB9108E9E457C0AB4B811C1C906182B4E8EF6C48C5E7E2611D35705070130FD

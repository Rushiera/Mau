// ═══════════════════════════════════════════════════
// 积木: oa.is_closed
// ID:   BRIK-OA-011
// 类别: OA
// 作用: 判断单状态——返回 true=已 Closed（判断结果，语料轮询用）
// 依赖: 无
// 引用: Mau.Runtime
// 原理: 经 OaBridge 实例取状态比较 OfficeState.Closed
// 常用: 工具单轮询等待执行完成
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// OA 积木——oa.is_closed 判断单状态（依赖 OaBridge）
    /// </summary>
    public static class OaIsClosedBrick
    {
        /// <summary>
        /// 判断单状态——返回 true=已 Closed（判断结果，语料轮询用）
        /// </summary>
        /// <param name="officeId">Office ID</param>
        /// <param name="closed">是否已 Closed</param>
        /// <returns>true=已 Closed</returns>
        public static bool IsClosed(long officeId, out bool closed)
        {
            closed = false;
            IOA? oa;
            DataBox.TryResolve<IOA>(out oa);
            if (oa == null)
            {
                return false;
            }
            closed = oa.GetStatus(officeId) == OfficeState.Closed;
            return closed;
        }
    }
}
// #MAU_CHECKSUM:SHA256:C4BA980F961DC59DCEDBBB5FFBE5AA74F61E2696BCB1D7DE3069F217D112C9F7

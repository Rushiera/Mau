// ═══════════════════════════════════════════════════
// 积木: approval.reject
// ID:   BRIK-APPROVAL-003
// 类别: APPROVAL
// 作用: 显式拒绝——不选任何选项
// 依赖: 无
// 引用: 无
// 原理: 锁内检查存在 → 移除
// 常用: 拒绝破坏性操作
// ═══════════════════════════════════════════════════

using Mau.Runtime;
namespace Mau.Bricks
{
    /// <summary>
    /// 审批积木——approval.reject 拒绝审批（依赖 ApprovalStore）
    /// </summary>
    public static class ApprovalRejectBrick
    {
        /// <summary>
        /// 显式拒绝——不选任何选项
        /// </summary>
        /// <param name="requestId">请求 ID</param>
        /// <returns>true=完成仍等待的请求</returns>
        public static bool Reject(string requestId)
        {
            // 统一入口——ApprovalStore.Reject（结果留痕 + 锁内原子）
            return ApprovalStore.Reject(requestId);
        }
    }
}
// #MAU_CHECKSUM:SHA256:5B50E59D2FBE74ED1964021B8FB8479460B9366AE664018E66184FAB66E8027D

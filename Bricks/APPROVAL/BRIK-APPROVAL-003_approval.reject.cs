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
            lock (ApprovalStore.Gate)
            {
                if (!ApprovalStore.Pending.ContainsKey(requestId))
                {
                    return false;
                }
                ApprovalStore.Pending.Remove(requestId);
            }
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:6CB9369B484E9C798F139F27199349E0B4F53BBCA7EEFF0AF9E65B09DC3CBA67

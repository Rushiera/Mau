// ═══════════════════════════════════════════════════
// 积木: approval.resolve
// ID:   BRIK-APPROVAL-002
// 类别: APPROVAL
// 作用: 选择一个有效选项完成审批
// 依赖: 无
// 引用: 无
// 原理: 锁内查找请求 → 索引有效性检查 → 移除 → 组装结果
// 常用: 外观层 Resolve 审批
// ═══════════════════════════════════════════════════

using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 审批积木——approval.resolve 完成审批（依赖 ApprovalStore）
    /// </summary>
    public static class ApprovalResolveBrick
    {
        /// <summary>
        /// 选择一个有效选项完成审批
        /// </summary>
        /// <param name="requestId">请求 ID</param>
        /// <param name="selectedIndex">选项索引</param>
        /// <param name="result">结果</param>
        /// <returns>true=完成仍在等待的审批</returns>
        public static bool Resolve(string requestId, int selectedIndex,
            out ApprovalResult result)
        {
            // 统一入口——ApprovalStore.Resolve（结果留痕 + 锁内原子）
            return ApprovalStore.Resolve(requestId, selectedIndex, out result);
        }
    }
}
// #MAU_CHECKSUM:SHA256:682EF7B311614DAB7BC6B163242FFB33C1CFB7E18ED4F9A13DD42C09D37F7A52

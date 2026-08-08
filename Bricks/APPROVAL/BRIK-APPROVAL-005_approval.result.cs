// ═══════════════════════════════════════════════════
// 积木: approval.result
// ID:   BRIK-APPROVAL-005
// 类别: APPROVAL
// 作用: 读取审批结果——resolve/reject 后留痕，请求方轮询感知（无=未完成或未知）
// 依赖: 无
// 引用: Mau.Runtime
// 原理: 锁内查结果表 → 有=文本行（requestId|index|label|timedOut），无=空
// 常用: 请求方语料轮询审批完成状态（M3.2——UI 弹窗 resolve/reject 后请求方感知）
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 审批积木——approval.result 读取审批结果（依赖 ApprovalStore.Results）
    /// </summary>
    public static class ApprovalResultBrick
    {
        /// <summary>
        /// 读取审批结果——resolve/reject 后留痕；SelectedIndex=-1=拒绝
        /// </summary>
        /// <param name="requestId">请求 ID</param>
        /// <param name="result">结果文本（requestId|index|label|timedOut），无=空</param>
        /// <returns>true=有结果</returns>
        public static bool GetResult(string requestId, out string result)
        {
            result = "";
            lock (ApprovalStore.Gate)
            {
                ApprovalResult value;
                if (ApprovalStore.Results.TryGetValue(requestId, out value))
                {
                    result = requestId + "|" + value.SelectedIndex.ToString()
                        + "|" + value.SelectedLabel + "|"
                        + (value.TimedOut ? "1" : "0");
                    return true;
                }
            }
            return false;
        }
    }
}
// #MAU_CHECKSUM:SHA256:5F882DC104E2C21AAFAEFCCA50CABBA78B5E68A284C86CD48D0DB7AB83FE4D53

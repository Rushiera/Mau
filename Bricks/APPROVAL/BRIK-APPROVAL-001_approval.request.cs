// ═══════════════════════════════════════════════════
// 积木: approval.request
// ID:   BRIK-APPROVAL-001
// 类别: APPROVAL
// 作用: 请求审批——登记后外观层可读取
// 依赖: 无
// 引用: System
// 原理: 参数校验 → 构造请求 → 锁内登记（重复 requestId 拒绝）
// 常用: CH4 HumanAsk / Shell 命令确认 / 破坏性操作审批
// ═══════════════════════════════════════════════════
using System;

using Mau.Runtime;
namespace Mau.Bricks
{
    /// <summary>
    /// 审批积木——approval.request 请求审批（依赖 ApprovalStore）
    /// </summary>
    public static class ApprovalRequestBrick
    {
        /// <summary>
        /// 请求审批——登记后外观层可读取
        /// </summary>
        /// <param name="requestId">请求 ID</param>
        /// <param name="question">问题</param>
        /// <param name="options">选项数组（≥2）</param>
        /// <param name="defaultIndex">默认索引</param>
        /// <param name="timeoutSeconds">超时秒数</param>
        /// <returns>true=登记成功</returns>
        public static bool Request(string requestId, string question,
            string[] options, int defaultIndex, int timeoutSeconds)
        {
            if (string.IsNullOrWhiteSpace(requestId)
                || string.IsNullOrWhiteSpace(question)
                || options == null || options.Length < 2)
            {
                return false;
            }
            ApprovalRequest request = new ApprovalRequest();
            request.RequestId = requestId;
            request.Question = question;
            request.Options = options;
            request.DefaultIndex = defaultIndex;
            request.TimeoutSeconds = timeoutSeconds;
            lock (ApprovalStore.Gate)
            {
                if (ApprovalStore.Pending.ContainsKey(requestId))
                {
                    return false;
                }
                ApprovalStore.Pending[requestId] = request;
            }
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:269D126ABEDC74532738BAADAFD42CC19CA17AFD5D48BCA4E8928503BE300F59

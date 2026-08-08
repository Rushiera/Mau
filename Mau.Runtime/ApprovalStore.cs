using System.Collections.Generic;

namespace Mau.Runtime
{
    /// <summary>
    /// 审批中枢（程序级）——状态经 DataBox scope 存储（BRIK 唯一数据协议）
    /// </summary>
    public static class ApprovalStore
    {
        /// <summary>
        /// 等待审批表——DataBox scope "approval"
        /// </summary>
        public static Dictionary<string, ApprovalRequest> Pending
        {
            get { return DataBox.GetOrCreate<Dictionary<string, ApprovalRequest>>("approval", "pending"); }
        }

        /// <summary>
        /// 审批结果表——resolve/reject 后留痕（M3.2：请求方轮询感知）——DataBox scope "approval"
        /// </summary>
        public static Dictionary<string, ApprovalResult> Results
        {
            get { return DataBox.GetOrCreate<Dictionary<string, ApprovalResult>>("approval", "results"); }
        }

        /// <summary>
        /// 审批表锁——DataBox scope "approval"
        /// </summary>
        public static object Gate
        {
            get { return DataBox.GetOrCreate<object>("approval", "gate"); }
        }

        /// <summary>
        /// 记录审批结果——结果表上限 100 条，超出移除最早（请求方读后自行判断）
        /// </summary>
        /// <param name="result">审批结果</param>
        public static void RecordResult(ApprovalResult result)
        {
            Dictionary<string, ApprovalResult> results = Results;
            if (results.Count >= 100)
            {
                string? firstKey = null;
                foreach (string key in results.Keys)
                {
                    firstKey = key;
                    break;
                }
                if (firstKey != null)
                {
                    results.Remove(firstKey);
                }
            }
            results[result.RequestId] = result;
        }

        /// <summary>
        /// 选择有效选项完成审批——C# 消费端统一入口（UI/Console 弹窗），积木同源转发
        /// </summary>
        /// <param name="requestId">请求 ID</param>
        /// <param name="selectedIndex">选项索引</param>
        /// <param name="result">审批结果</param>
        /// <returns>true=完成仍在等待的审批</returns>
        public static bool Resolve(string requestId, int selectedIndex, out ApprovalResult result)
        {
            result = new ApprovalResult();
            ApprovalRequest? request;
            lock (Gate)
            {
                if (!Pending.TryGetValue(requestId, out request) || request == null
                    || selectedIndex < 0 || selectedIndex >= request.Options.Length)
                {
                    return false;
                }
                Pending.Remove(requestId);
            }
            result.RequestId = requestId;
            result.SelectedIndex = selectedIndex;
            result.SelectedLabel = request.Options[selectedIndex];
            result.TimedOut = false;
            RecordResult(result);
            return true;
        }

        /// <summary>
        /// 显式拒绝——不选任何选项；结果 SelectedIndex=-1=拒绝
        /// </summary>
        /// <param name="requestId">请求 ID</param>
        /// <returns>true=完成仍等待的请求</returns>
        public static bool Reject(string requestId)
        {
            lock (Gate)
            {
                if (!Pending.ContainsKey(requestId))
                {
                    return false;
                }
                Pending.Remove(requestId);
            }
            ApprovalResult result = new ApprovalResult();
            result.RequestId = requestId;
            result.SelectedIndex = -1;
            result.SelectedLabel = "";
            result.TimedOut = false;
            RecordResult(result);
            return true;
        }
    }
}

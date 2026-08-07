namespace Mau.Runtime
{
    /// <summary>
    /// 审批结果——人类选择或超时默认（契约类型——approval.resolve 输出）
    /// </summary>
    public struct ApprovalResult
    {
        /// <summary>
        /// 请求 ID
        /// </summary>
        public string RequestId;

        /// <summary>
        /// 选中索引
        /// </summary>
        public int SelectedIndex;

        /// <summary>
        /// 选中标签
        /// </summary>
        public string SelectedLabel;

        /// <summary>
        /// 是否由超时默认完成
        /// </summary>
        public bool TimedOut;
    }

    /// <summary>
    /// 审批请求——等待人类选择，可被任意外观层读取（契约类型）
    /// </summary>
    public sealed class ApprovalRequest
    {
        /// <summary>
        /// 请求 ID
        /// </summary>
        public string RequestId;

        /// <summary>
        /// 面向人的问题
        /// </summary>
        public string Question;

        /// <summary>
        /// 至少两个选项
        /// </summary>
        public string[] Options;

        /// <summary>
        /// 超时默认索引
        /// </summary>
        public int DefaultIndex;

        /// <summary>
        /// 超时秒数
        /// </summary>
        public int TimeoutSeconds;

        /// <summary>
        /// 风险说明
        /// </summary>
        public string RiskSummary;

        /// <summary>
        /// 创建空请求
        /// </summary>
        public ApprovalRequest()
        {
            RequestId = "";
            Question = "";
            Options = System.Array.Empty<string>();
            DefaultIndex = 0;
            TimeoutSeconds = 20;
            RiskSummary = "";
        }
    }
}

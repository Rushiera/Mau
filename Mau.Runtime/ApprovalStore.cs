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
        /// 审批表锁——DataBox scope "approval"
        /// </summary>
        public static object Gate
        {
            get { return DataBox.GetOrCreate<object>("approval", "gate"); }
        }
    }
}

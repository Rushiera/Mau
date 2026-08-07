namespace Mau.Runtime
{
    /// <summary>
    /// Shell 审批处理器——非白名单命令的执行前人工确认（宿主实现；DataBox 绑定）
    /// 安全审查项 P0-5：默认拒绝任意 Shell——白名单直行，其余经审批，无处理器=拒绝
    /// </summary>
    public interface IShellApprovalHandler
    {
        /// <summary>
        /// 审批命令——宿主弹窗/人工确认
        /// </summary>
        /// <param name="command">完整命令文本</param>
        /// <returns>true=允许执行</returns>
        bool Approve(string command);
    }
}

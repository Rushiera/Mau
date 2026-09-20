namespace Mau.Runtime
{
    /// <summary>
    /// 宿主指令服务——工具面触达宿主内核指令族的唯一通道（工具组 Flow → 桥 → 宿主统一出口）。
    /// 宿主注入实现（CH4.HostCommandService——前缀白名单 + 指令统一出口 ExecuteCommandLine）；
    /// 语料经 host.command 积木（HOST 类别）触达——DataBox.TryResolve 面。
    /// 主线程契约：指令族触碰注册表与会话面（ThreadGuard 守卫），须宿主主线程调用（与 SysCommand 同规）。
    /// 分层：本接口只声明「宿主有指令面」——业务指令语义（cat.* 等）由宿主实现承载，Runtime 不认识。
    /// </summary>
    public interface IHostCommandService
    {
        /// <summary>
        /// 执行一行宿主指令——交给宿主统一路由（与 CLI / HTTP 面板同源），返回内核回执原文。
        /// 前缀白名单之外的输入由实现侧拒绝（ERR| 前缀文本），不投递。
        /// </summary>
        /// <param name="line">指令行（如 cat.list / cat.cfg.set &lt;key&gt; &lt;field&gt; &lt;value&gt;）</param>
        /// <returns>内核回执原文；越界 / 未识别返回 ERR| 前缀文本</returns>
        string Execute(string line);
    }
}

namespace Mau.Runtime
{
    /// <summary>
    /// 浏览器实例生命周期接口——browser-* 工具实例的起停管理（A110）。
    /// 与 <see cref="IBrowserService"/> 分离（I5：单接口方法 ≤5）：服务面管"对页面做什么"，本接口管"实例活多久"。
    /// 实现者与 IBrowserService 同体（EdgeCdpBrowserService），宿主分别 Bind。
    /// 🔴 清理动因：Chromium 启动后 detach——宿主退出 / 被强杀都不带走子进程；
    /// 孤儿实例持续持有 profile，导致下次启动时新进程把命令行转交旧实例后退出、DevToolsActivePort 永不出现。
    /// 三层闭环：域结束关（主路径）· 宿主退出钩子（正常退出）· PID 自愈（异常遗留）。
    /// </summary>
    public interface IBrowserLifecycle
    {
        /// <summary>
        /// 关闭并清理指定猫的浏览器实例——timeback 作用域回收时调用（域级生命周期主路径）。
        /// </summary>
        /// <param name="catId">猫 key（空=全部）</param>
        void CloseCat(string catId);

        /// <summary>
        /// 关闭并清理本服务管理的全部实例——宿主退出钩子调用（Main finally 与 CloseWriters 同处）。
        /// 正常退出路径；被强杀时不会执行，靠启动前 PID 自愈兜底。
        /// </summary>
        void Shutdown();
    }
}

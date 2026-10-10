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
        /// 预热指定猫的浏览器实例——timeback 作用域**开锚**时调用（A123 域级生命周期主路径）：
        /// 域 = 浏览器进程容器，锚内工具只操作该进程，不涉及孤儿与交接。
        /// 起失败不阻断开锚（失败只记日志；锚内工具调用会再试一次并如实返回 ERR）。
        /// </summary>
        /// <param name="catId">猫 key</param>
        void PrepareCat(string catId);

        /// <summary>
        /// 关闭并清理本服务管理的全部实例——宿主退出钩子调用（Main finally 与 CloseWriters 同处）。
        /// 正常退出路径；被强杀时不会执行，靠启动前 PID 自愈兜底。
        /// </summary>
        void Shutdown();

        /// <summary>
        /// 有头登录实例（A1）——browser-headful 执行面：域外专属（主干人工登录用，与其余 browser-* 域内专属相反）。
        /// open：关现有实例 → 同 profile 起有头浏览器（窗口供人工登录）；close：优雅关闭（profile 落盘，登录态保留）。
        /// </summary>
        /// <param name="catId">猫 key</param>
        /// <param name="action">动作：open（起有头）/ close（关有头）</param>
        /// <param name="url">open 时可选导航地址（http/https；空=空白页）</param>
        /// <returns>状态摘要或 ERR| 错误文本</returns>
        string Headful(string catId, string action, string url);
    }
}

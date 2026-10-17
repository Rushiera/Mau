namespace Mau.Runtime
{
    /// <summary>
    /// Python 执行服务接口——py 工具组的执行面与 py 服务进程管理（design-ch4-pytools §二）。
    /// <para>
    /// 实现在基座（<see cref="PyService"/>——不独立工程）；进程句柄归本服务持有，<b>不在 Flow 层</b>
    /// （Flow 可热重载，句柄存 Flow 里一次 reload 即悬空成孤儿）。
    /// </para>
    /// <para>
    /// 解释器路径读配置 <c>py.python_path</c>（未配置即明示不可用——不回落探测路径）。
    /// 两类入口：Run（短命脚本，起-跑-退）· Service / Invoke（常驻服务，stdio 严格协议）。
    /// 语料经 py.* 积木触达——DataBox.TryResolve 面。
    /// </para>
    /// </summary>
    public interface IPyService
    {
        /// <summary>
        /// 执行一个 py 脚本（短命进程）——起-跑-退。
        /// </summary>
        /// <param name="argsJson">参数 JSON（script 必填——受控根内路径；args 可选——命令行参数；timeout_ms 可选）</param>
        /// <returns>结果 JSON（exit/stdout/stderr/truncated/timeout/elapsedMs）或 ERR| 前缀错误文本（错误可见性）</returns>
        string Run(string argsJson);

        /// <summary>
        /// 列出可用脚本——受控根内 *.py 扫描（可发现性）。
        /// </summary>
        /// <param name="argsJson">参数 JSON（dir 可选——受控根内子目录；空 = 仓库根 tools 面）</param>
        /// <returns>结果 JSON（scripts 数组）或 ERR| 前缀错误文本</returns>
        string List(string argsJson);

        /// <summary>
        /// 按配置拉起 follow=true 的服务——宿主启动链调用（服务清单自配置扫描得出）。
        /// </summary>
        /// <returns>结果 JSON（started / skipped / errors 数组）或 ERR| 前缀错误文本</returns>
        string Autostart();

        /// <summary>
        /// 服务控制——start（拉起并等 ready 握手）/ stop（优雅退出 + 强杀兜底）/ status（查进程态）。
        /// </summary>
        /// <param name="argsJson">参数 JSON（name 必填——服务名；action 必填——start|stop|status）</param>
        /// <returns>结果 JSON 或 ERR| 前缀错误文本</returns>
        string Service(string argsJson);

        /// <summary>
        /// 服务调用——按 stdio 严格协议发一行 JSON、读一行 JSON（单服务纯串行；超时报错不杀进程）。
        /// </summary>
        /// <param name="argsJson">参数 JSON（name 必填——服务名；payload 必填——请求体 JSON 文本）</param>
        /// <returns>服务回执 JSON 或 ERR| 前缀错误文本</returns>
        string Invoke(string argsJson);

        /// <summary>
        /// 服务枚举——列出全部已声明服务（自配置扫描）及运行态与进程内存占用（A254 资源占用页签）。
        /// </summary>
        /// <returns>结果 JSON（services 数组：name / state / alive / pid / ws——ws 为进程工作集字节数，未运行 0）或 ERR| 前缀错误文本</returns>
        string Enumerate();
    }
}

namespace Mau.Runtime
{
    /// <summary>
    /// PowerShell 执行服务接口——powershell 工具的执行面（PsCat 工具组）。
    /// 宿主注入实现（CH4.PsService——EncodedCommand 免转义 + UTF-8 输出内建 + 写文件拦截 + 超时进程树杀）；
    /// 语料经 ps.exec 积木（PS 类别）触达——DataBox.TryResolve 面。
    /// 拦截语义：写文件（Set-Content/Out-File/&gt; 等）→ PS_WRITE_FORBIDDEN（走 text-* 读写工具）；
    ///           Start-Process → PS_START_FORBIDDEN（禁启动宿主/无交互进程）；ReadKey/ReadLine → PS_READ_FORBIDDEN（自动化重定向死锁）；
    ///           目录列举（Get-ChildItem / gci / ls / dir / tree）→ PS_LIST_FORBIDDEN（走 file-tree 工具）。
    /// git 命令豁免写文件拦截（仓库级操作，非文件内容写）。
    /// </summary>
    public interface IPsService
    {
        /// <summary>
        /// 执行一段 PowerShell 命令——整段 EncodedCommand 传递（零转义）；返回 JSON（exit/stdout/stderr/truncated/timeout）。
        /// 同步执行（阻塞调用线程——工具循环等待语义，与 web-search 一致）。
        /// </summary>
        /// <param name="argsJson">工具参数 JSON（command 必填 / cwd 可选 / timeout_ms 可选）</param>
        /// <param name="shell">解释器线标识（powershell=默认解释器 / powershell7=PowerShell 7；空值按默认线处理）</param>
        /// <returns>结果 JSON 或 ERR| 前缀错误文本（错误可见性）</returns>
        string Exec(string argsJson, string shell);
    }
}

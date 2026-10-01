namespace Mau.Runtime
{
    /// <summary>
    /// C# 工具桥——Roslyn 编码工具域接口（P8 三期）。
    /// 宿主注入实现（Mau.Development.MauRoslynBridge）；语料经 csharp.bridge 积木（PACK 协议）触达。
    /// 方法白名单 12 件：check / build / list / read / find_ref / find / patch / member / comment / dead / comment_check / format。
    /// 磁盘权威 + 快照监管 + 项目键隔离常驻池（design-ch4-cs.md D1-D3）——实现细节在实现侧。
    /// </summary>
    public interface ICSharpBridge
    {
        /// <summary>
        /// 单方法调度——method 白名单 + argsJson 展平参数
        /// </summary>
        /// <param name="method">操作名（check/build/list/read/find_ref/find/patch/member/comment/dead）</param>
        /// <param name="argsJson">展平参数 JSON</param>
        /// <param name="result">结果文本</param>
        /// <returns>true=调用成功（result 含 OK/ROLLED_BACK/ERR| 语义文本）</returns>
        bool Invoke(string method, string argsJson, out string result);
    }
}
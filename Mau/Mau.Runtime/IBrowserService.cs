namespace Mau.Runtime
{
    /// <summary>
    /// 浏览器服务接口——browser-* 工具的执行面（A110 本地浏览器能力）。
    /// 宿主注入实现（EdgeCdpBrowserService——系统 Edge headless + 裸 CDP，零第三方依赖）。
    /// 语料经 browser.open / browser.read / browser.eval / browser.shot 积木（BROWSER 类别）触达——DataBox.TryResolve 面。
    /// 非主干信息：调用侧受 timeback 域门禁约束（宿主派发面判定，本接口不重复判定）。
    /// 每猫独立 profile 与产物目录：Data/browser/&lt;catId&gt;/（profile/ + out/）。
    /// </summary>
    public interface IBrowserService
    {
        /// <summary>
        /// 打开页面并等待加载完成（readyState=complete + 静默期）——返回状态摘要。
        /// 懒启动：首次调用拉起本猫 Edge 进程，之后常驻复用。
        /// </summary>
        /// <param name="catId">猫 key（会话标识——定位本猫 profile 与实例）</param>
        /// <param name="url">目标地址（任意 http(s)）</param>
        /// <returns>状态摘要文本（标题 / 最终 URL / 状态 / 耗时——失败 ERR| 前缀）</returns>
        string Open(string catId, string url);

        /// <summary>
        /// 读取当前页面——产物落盘（Data/browser/&lt;catId&gt;/out/），返回摘要（路径 + 规模统计 + 头部预览）。
        /// </summary>
        /// <param name="catId">猫 key</param>
        /// <param name="mode">观察面：text（正文 innerText）/ ax（无障碍树精简）/ links（a[href] 链接面）</param>
        /// <returns>摘要文本（失败 ERR| 前缀）</returns>
        string Read(string catId, string mode);

        /// <summary>
        /// 在当前页面执行 JS 表达式——返回取值结果（小直返 / 大落盘）。
        /// 万能取值口：需要 onclick 里的目标 URL 等 DOM 之外信息时使用（非常驻动作框架）。
        /// </summary>
        /// <param name="catId">猫 key</param>
        /// <param name="expression">JS 表达式（求值后取 returnByValue）</param>
        /// <returns>JSON 文本（失败 ERR| 前缀）</returns>
        string Eval(string catId, string expression);

        /// <summary>
        /// 截图当前页面——PNG 落盘，返回绝对路径（供 image-inject 直读）。
        /// </summary>
        /// <param name="catId">猫 key</param>
        /// <param name="fullPage">true=全页（captureBeyondViewport）；false=当前视口</param>
        /// <returns>PNG 绝对路径（失败 ERR| 前缀）</returns>
        string Shot(string catId, bool fullPage);

        /// <summary>
        /// 页签管理——同一实例内的多 target 操作（CDP Target 域 / DevTools HTTP 端点）。
        /// 用途：URL 寻路时"开着不关"——保留来过哪一页，可回退与对比。
        /// </summary>
        /// <param name="catId">猫 key</param>
        /// <param name="action">动作：list（列出页签）/ new（新建，可带 url）/ select（切换，需 target）/ close（关闭，需 target）</param>
        /// <param name="target">页签 id（new 可空；select / close 必填）</param>
        /// <param name="url">新建页签的地址（仅 new 用；空则开空白页）</param>
        /// <returns>结果摘要（list 返回页签清单）或 ERR| 错误文本</returns>
        string Tabs(string catId, string action, string target, string url);
    }
}

namespace Mau.Runtime
{
    /// <summary>
    /// 联网搜索服务接口——web-search 工具的执行面（R2.1 工具组首发）。
    /// 宿主注入实现（Mau.Providers.DeepSeekWebSearchService——DeepSeek /responses + web_search 工具，服务端自动执行全链）；
    /// 语料经 web.search 积木（WEB 类别）触达——DataBox.TryResolve 面。
    /// 配置：search.api_config_id（引用 LLM 池配置——未配置=搜索不可用）+ search.endpoint/search.model（可选覆盖）。
    /// </summary>
    public interface IWebSearchService
    {
        /// <summary>
        /// 执行一次联网搜索——服务端自动完成"搜索→注入→生成回答"全链，返回最终回答文本。
        /// 同步执行（阻塞调用线程；R2.1 拍板——后台化未来走子会话代理/多猫交火设计）。
        /// </summary>
        /// <param name="query">搜索查询</param>
        /// <returns>搜索结果文本（失败 ERR| 前缀——错误可见性）</returns>
        string Search(string query);
    }
}

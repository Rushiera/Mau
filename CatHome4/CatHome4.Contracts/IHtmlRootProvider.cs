namespace CatHome4.Contracts
{
    /// <summary>
    /// 前端 html 根解析——Http 域消费（静态页服务），入口壳实现（源码区优先 + 部署区回退）。
    /// 拆分前 HttpHost 直接调用 Program.ResolveHtmlRoot；拆分后经本接口注入。
    /// </summary>
    public interface IHtmlRootProvider
    {
        /// <summary>解析 html 根目录——源码区优先（仓库根/CatHome4/html——唯一事实源），回退部署区（AppContext.BaseDirectory/html）</summary>
        /// <returns>html 根绝对路径</returns>
        string ResolveHtmlRoot();
    }
}

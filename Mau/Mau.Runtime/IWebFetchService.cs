using System;

namespace Mau.Runtime
{
    /// <summary>
    /// 网页资产下载服务接口——web-fetch / web-fetch-jobs 工具的执行面（网页资产下载能力）。
    /// 宿主注入实现（WebFetchService——全局单一下载器：HttpClient + 后台 Task；任务态落盘、跨宿主重启续传）。
    /// 语料经 web.fetch / web.fetch_jobs 积木（WEB 类别）触达——DataBox.TryResolve 面。
    /// 不域限定（对照 browser-* 五件的域内限定）：产物落盘、不占前文，无开域理由。
    /// 全局单一下载器：任务表全猫共用，任务携带来源猫（CatKey）供完成回调定位收件人。
    /// 规格：Project/CH4/design-ch4-webfetch.md
    /// </summary>
    public interface IWebFetchService
    {
        /// <summary>
        /// 推入下载任务——登记并起后台下载，立即返回任务号（非阻塞；完成 / 失败时回调注入）。
        /// </summary>
        /// <param name="catKey">来源猫（回调注入收件人）</param>
        /// <param name="url">目标地址（http/https）</param>
        /// <param name="fileName">目标文件名（空 = 按 URL 派生）</param>
        /// <returns>回执文本（失败 ERR| 前缀）</returns>
        string Fetch(string catKey, string url, string fileName);

        /// <summary>
        /// 任务管理——list（全部任务）/ status（单任务详情）/ cancel（取消进行中任务）。
        /// </summary>
        /// <param name="action">动作：list / status / cancel</param>
        /// <param name="id">任务号（status / cancel 必填）</param>
        /// <returns>任务清单 / 详情 / 动作结果（失败 ERR| 前缀）</returns>
        string Jobs(string action, string id);

        /// <summary>
        /// 配置接线——任务表落盘路径 + 产物目录 + 完成注入回调（宿主启动期注入）。
        /// 注入回调返回 true = 受理 / false = 拒收（服务侧留存，后续重试投递）。
        /// </summary>
        /// <param name="storePath">任务表路径（downloads.json；空 = 仅内存）</param>
        /// <param name="downloadsDir">产物目录（WorkSpace:Downloads）</param>
        /// <param name="inject">注入回调（catKey, content）→ 是否受理</param>
        void Configure(string storePath, string downloadsDir, Func<string, string, bool> inject);

        /// <summary>
        /// 启动恢复——载入任务表并对未完成任务续传（Range 续 / 服务端不支持则重下）。
        /// </summary>
        void Start();

        /// <summary>
        /// 收尾——停止后台任务（宿主退出钩子调用；不置终态，半截 .part 留待下次续传）。
        /// </summary>
        void Shutdown();
    }
}

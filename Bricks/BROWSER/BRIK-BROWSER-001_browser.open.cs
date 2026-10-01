// ═══════════════════════════════════════════════════
// 积木: browser.open
// ID:   BRIK-BROWSER-001
// 类别: BROWSER
// 作用: 打开网页并等待加载完成——返回状态摘要（标题 / 最终 URL / 状态 / 耗时）
// 依赖: 无
// 引用: Mau.Runtime（IBrowserService/DataBox）
// 原理: DataBox.TryResolve<IBrowserService> → Open(catId, url)；懒启动本猫 Edge 实例（首调拉起，之后复用）
// 常用: browser_cat.mau 认领线——'browser.open'[@args] > @result
// 门禁: 本工具域限定（仅 timeback 作用域内可用）——宿主派发面拦截，积木不重复判定
// ═══════════════════════════════════════════════════
using System;
using System.Text.Json;
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 浏览器打开积木——browser-open 工具执行面（A110 本地浏览器能力）
    /// </summary>
    public static class BrowserOpenBrick
    {
        /// <summary>
        /// 打开页面并等待加载完成
        /// </summary>
        /// <param name="argsJson">工具参数 JSON（url；catId 保留键由宿主注入）</param>
        /// <param name="result">状态摘要或 ERR| 错误文本</param>
        /// <returns>true=执行成功</returns>
        public static bool Open(string argsJson, out string result)
        {
            result = "";
            string badArgs = JsonArgs.Validate(argsJson, "url", "url", "", "");
            if (badArgs.Length > 0)
            {
                result = badArgs;
                return false;
            }
            string catId = JsonArgs.Get(argsJson, "catId");
            string url = JsonArgs.Get(argsJson, "url");
            if (url.Length == 0)
            {
                result = "ERR|BAD_ARGS|缺少参数 url";
                return false;
            }
            try
            {
                IBrowserService? service;
                DataBox.TryResolve<IBrowserService>(out service);
                if (service == null)
                {
                    result = "ERR|BROWSER_NO_SERVICE|宿主未注入 IBrowserService";
                    return false;
                }
                string body = service.Open(catId, url);
                if (body.StartsWith("ERR|", StringComparison.Ordinal))
                {
                    result = body;
                    return true;
                }
                result = MetaHead(url, body);
                if (body.Length > 0)
                {
                    result = result + "\n" + body;
                }
                return true;
            }
            catch (Exception ex)
            {
                result = "ERR|BROWSER_BRICK|" + ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 结构化元数据头——首行单行 JSON（ok/tool/url/chars；键序稳定 = 插入序）
        /// </summary>
        /// <param name="url">目标地址</param>
        /// <param name="body">正文</param>
        /// <returns>单行 JSON</returns>
        private static string MetaHead(string url, string body)
        {
            System.Collections.Generic.Dictionary<string, object> head = new System.Collections.Generic.Dictionary<string, object>();
            head["ok"] = true;
            head["tool"] = "browser-open";
            head["url"] = url;
            head["chars"] = body.Length;
            return JsonSerializer.Serialize(head);
        }
    }
}
// #MAU_CHECKSUM:SHA256:910AC4803DC30186BDDD75B63FA750F5E57F53F6506BF76B9E8FE2C9EA4562E0

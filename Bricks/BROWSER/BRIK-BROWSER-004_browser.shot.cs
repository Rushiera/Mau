// ═══════════════════════════════════════════════════
// 积木: browser.shot
// ID:   BRIK-BROWSER-004
// 类别: BROWSER
// 作用: 截图当前页面——PNG 落盘，返回绝对路径（供 image-inject 直读）
// 依赖: 无
// 引用: Mau.Runtime（IBrowserService/DataBox）
// 原理: DataBox.TryResolve<IBrowserService> → Shot(catId, fullPage)；默认视口截图（"看到的就是这样"），full=true 走全页
// 常用: browser_cat.mau 认领线——'browser.shot'[@args] > @result
// 门禁: 本工具域限定（仅 timeback 作用域内可用）——宿主派发面拦截，积木不重复判定
// ═══════════════════════════════════════════════════
using System;
using System.Text.Json;
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 浏览器截图积木——browser-shot 工具执行面（A110 本地浏览器能力）
    /// </summary>
    public static class BrowserShotBrick
    {
        /// <summary>
        /// 截图当前页面——PNG 落盘并返回绝对路径
        /// </summary>
        /// <param name="argsJson">工具参数 JSON（full：true=全页，缺省视口）</param>
        /// <param name="result">PNG 路径摘要或 ERR| 错误文本</param>
        /// <returns>true=执行成功</returns>
        public static bool Shot(string argsJson, out string result)
        {
            result = "";
            string badArgs = JsonArgs.Validate(argsJson, "full", "", "", "");
            if (badArgs.Length > 0)
            {
                result = badArgs;
                return false;
            }
            string catId = JsonArgs.Get(argsJson, "catId");
            string fullText = JsonArgs.Get(argsJson, "full");
            bool fullPage = fullText == "true";
            try
            {
                IBrowserService? service;
                DataBox.TryResolve<IBrowserService>(out service);
                if (service == null)
                {
                    result = "ERR|BROWSER_NO_SERVICE|宿主未注入 IBrowserService";
                    return false;
                }
                string body = service.Shot(catId, fullPage);
                if (body.StartsWith("ERR|", StringComparison.Ordinal))
                {
                    result = body;
                    return true;
                }
                result = MetaHead(fullPage, body);
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
        /// 结构化元数据头——首行单行 JSON（ok/tool/full/chars；键序稳定 = 插入序）
        /// </summary>
        /// <param name="fullPage">是否全页截图</param>
        /// <param name="body">正文</param>
        /// <returns>单行 JSON</returns>
        private static string MetaHead(bool fullPage, string body)
        {
            System.Collections.Generic.Dictionary<string, object> head = new System.Collections.Generic.Dictionary<string, object>();
            head["ok"] = true;
            head["tool"] = "browser-shot";
            head["full"] = fullPage;
            head["chars"] = body.Length;
            return JsonSerializer.Serialize(head);
        }
    }
}
// #MAU_CHECKSUM:SHA256:B92F1ADD99B1C45D04975A711437C6DAF93CD8F6F782BA6AD711902B5CA329CE

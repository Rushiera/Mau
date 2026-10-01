// ═══════════════════════════════════════════════════
// 积木: browser.read
// ID:   BRIK-BROWSER-002
// 类别: BROWSER
// 作用: 读取当前页面——text（正文）/ ax（无障碍树精简）/ links（链接面）；产物落盘 + 返回摘要
// 依赖: 无
// 引用: Mau.Runtime（IBrowserService/DataBox）
// 原理: DataBox.TryResolve<IBrowserService> → Read(catId, mode)；产物落 Data/browser/<catId>/out/
// 常用: browser_cat.mau 认领线——'browser.read'[@args] > @result
// 门禁: 本工具域限定（仅 timeback 作用域内可用）——宿主派发面拦截，积木不重复判定
// ═══════════════════════════════════════════════════
using System;
using System.Text.Json;
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 浏览器读取积木——browser-read 工具执行面（A110 本地浏览器能力）
    /// </summary>
    public static class BrowserReadBrick
    {
        /// <summary>
        /// 读取当前页面（三种观察面）——产物落盘，返回摘要
        /// </summary>
        /// <param name="argsJson">工具参数 JSON（mode：text|ax|links，缺省 text）</param>
        /// <param name="result">摘要或 ERR| 错误文本</param>
        /// <returns>true=执行成功</returns>
        public static bool Read(string argsJson, out string result)
        {
            result = "";
            string badArgs = JsonArgs.Validate(argsJson, "mode", "", "mode", "text|ax|links");
            if (badArgs.Length > 0)
            {
                result = badArgs;
                return false;
            }
            string catId = JsonArgs.Get(argsJson, "catId");
            string mode = JsonArgs.Get(argsJson, "mode");
            if (mode.Length == 0)
            {
                mode = "text";
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
                string body = service.Read(catId, mode);
                if (body.StartsWith("ERR|", StringComparison.Ordinal))
                {
                    result = body;
                    return true;
                }
                result = MetaHead(mode, body);
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
        /// 结构化元数据头——首行单行 JSON（ok/tool/mode/chars；键序稳定 = 插入序）
        /// </summary>
        /// <param name="mode">观察面模式</param>
        /// <param name="body">正文</param>
        /// <returns>单行 JSON</returns>
        private static string MetaHead(string mode, string body)
        {
            System.Collections.Generic.Dictionary<string, object> head = new System.Collections.Generic.Dictionary<string, object>();
            head["ok"] = true;
            head["tool"] = "browser-read";
            head["mode"] = mode;
            head["chars"] = body.Length;
            return JsonSerializer.Serialize(head);
        }
    }
}
// #MAU_CHECKSUM:SHA256:094220E32FE78CFC8207B330A5B63FDF8DD2AAF5676AF26C4A3C2A257DBD34FB

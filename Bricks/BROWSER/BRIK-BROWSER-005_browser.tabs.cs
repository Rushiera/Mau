// ═══════════════════════════════════════════════════
// 积木: browser.tabs
// ID:   BRIK-BROWSER-005
// 类别: BROWSER
// 作用: 页签管理——list（列出）/ new（新建）/ select（切换）/ close（关闭）
// 依赖: 无
// 引用: Mau.Runtime（IBrowserService/DataBox）
// 原理: DataBox.TryResolve<IBrowserService> → Tabs(catId, action, target, url)；DevTools HTTP 端点 + 当前页 WS 重连
// 常用: browser_cat.mau 认领线——'browser.tabs'[@args] > @result
// 门禁: 本工具域限定（仅 timeback 作用域内可用）——宿主派发面拦截，积木不重复判定
// ═══════════════════════════════════════════════════
using System;
using System.Text.Json;
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 浏览器页签积木——browser-tabs 工具执行面（A110 多页签）
    /// </summary>
    public static class BrowserTabsBrick
    {
        /// <summary>
        /// 页签管理——list / new / select / close
        /// </summary>
        /// <param name="argsJson">工具参数 JSON（action / target / url）</param>
        /// <param name="result">结果摘要或 ERR| 错误文本</param>
        /// <returns>true=执行成功</returns>
        public static bool Tabs(string argsJson, out string result)
        {
            result = "";
            string badArgs = JsonArgs.Validate(argsJson, "action target url", "", "action", "list|new|select|close");
            if (badArgs.Length > 0)
            {
                result = badArgs;
                return false;
            }
            string catId = JsonArgs.Get(argsJson, "catId");
            string action = JsonArgs.Get(argsJson, "action");
            string target = JsonArgs.Get(argsJson, "target");
            string url = JsonArgs.Get(argsJson, "url");
            try
            {
                IBrowserService? service;
                DataBox.TryResolve<IBrowserService>(out service);
                if (service == null)
                {
                    result = "ERR|BROWSER_NO_SERVICE|宿主未注入 IBrowserService";
                    return false;
                }
                string body = service.Tabs(catId, action, target, url);
                if (body.StartsWith("ERR|", StringComparison.Ordinal))
                {
                    result = body;
                    return true;
                }
                result = MetaHead(action, body);
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
        /// 结构化元数据头——首行单行 JSON（ok/tool/action/chars；键序稳定 = 插入序）
        /// </summary>
        /// <param name="action">页签动作</param>
        /// <param name="body">正文</param>
        /// <returns>单行 JSON</returns>
        private static string MetaHead(string action, string body)
        {
            System.Collections.Generic.Dictionary<string, object> head = new System.Collections.Generic.Dictionary<string, object>();
            head["ok"] = true;
            head["tool"] = "browser-tabs";
            head["action"] = action;
            head["chars"] = body.Length;
            return JsonSerializer.Serialize(head);
        }
    }
}
// #MAU_CHECKSUM:SHA256:15090BB123BB50D1A867E915A7DEC4E2AE127F676AF263AC4CDB60DF0A4AAD8C

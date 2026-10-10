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
                System.Collections.Generic.Dictionary<string, object> fields = new System.Collections.Generic.Dictionary<string, object>();
                fields["chars"] = body.Length;
                // A214——target 主来源（action）+ 正文摘要行
                result = MetaHead("browser-tabs", true, action, -1, fields) + "\n" + action + " | " + body.Length.ToString() + " 字";
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
        /// 结构化元数据头（统一口径·A214）——恒定 ok / tool + 主来源 target + 主计数 items + 专有字段（插入序）。
        /// </summary>
        /// <param name="tool">工具名</param>
        /// <param name="ok">成败</param>
        /// <param name="target">主来源（空串 = 省略）</param>
        /// <param name="items">主计数（负值 = 省略）</param>
        /// <param name="fields">专有字段（按插入序）</param>
        /// <returns>单行 JSON</returns>
        private static string MetaHead(string tool, bool ok, string target, int items, System.Collections.Generic.Dictionary<string, object> fields)
        {
            System.Collections.Generic.Dictionary<string, object> head = new System.Collections.Generic.Dictionary<string, object>();
            head["ok"] = ok;
            head["tool"] = tool;
            if (target.Length > 0)
            {
                head["target"] = target;
            }
            if (items >= 0)
            {
                head["items"] = items;
            }
            foreach (System.Collections.Generic.KeyValuePair<string, object> kv in fields)
            {
                head[kv.Key] = kv.Value;
            }
            return JsonSerializer.Serialize(head);
        }
    }
}
// #MAU_CHECKSUM:SHA256:36EED4C06AFBA3C825C71912F7B9C4D2D4E79EBA4B79244EDFAD7CC7CCDE9360

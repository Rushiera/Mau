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
                System.Collections.Generic.Dictionary<string, object> fields = new System.Collections.Generic.Dictionary<string, object>();
                fields["full"] = fullPage;
                fields["chars"] = body.Length;
                // A214——无来源语义（target 省略）+ 正文摘要行
                result = MetaHead("browser-shot", true, "", -1, fields) + "\n" + "截图 · " + body.Length.ToString() + " 字";
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
// #MAU_CHECKSUM:SHA256:CFCE55E1ED6DDCB8C01043D8D4CB69A25C9E85716B23059B753EE576568F1EF4

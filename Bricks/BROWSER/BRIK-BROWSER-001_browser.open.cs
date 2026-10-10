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
                System.Collections.Generic.Dictionary<string, object> fields = new System.Collections.Generic.Dictionary<string, object>();
                fields["chars"] = body.Length;
                // A214——target 主来源（url）+ 正文摘要行
                result = MetaHead("browser-open", true, url, -1, fields) + "\n" + url + " | " + body.Length.ToString() + " 字";
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
// #MAU_CHECKSUM:SHA256:F4E518F6C4387778CF0660C7CC6B09AEA317A28AE95FC1A05D97B95ED0F09FE7

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
                System.Collections.Generic.Dictionary<string, object> fields = new System.Collections.Generic.Dictionary<string, object>();
                fields["mode"] = mode;
                fields["chars"] = body.Length;
                // A214——无来源语义（target 省略）+ 正文摘要行
                result = MetaHead("browser-read", true, "", -1, fields) + "\n" + "读 " + body.Length.ToString() + " 字";
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
// #MAU_CHECKSUM:SHA256:3F10D58791267D7B26B4447F0B7BA5AFF1E037496F4B7DB4278E061A840547A6

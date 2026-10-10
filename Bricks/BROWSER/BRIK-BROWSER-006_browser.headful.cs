// ═══════════════════════════════════════════════════
// 积木: browser.headful
// ID:   BRIK-BROWSER-006
// 类别: BROWSER
// 作用: 有头登录实例——open（起有头窗口供人工登录）/ close（优雅关闭，profile 落盘保留登录态）
// 依赖: 无
// 引用: Mau.Runtime（IBrowserLifecycle/DataBox）
// 原理: DataBox.TryResolve<IBrowserLifecycle> → Headful(catId, action, url)；关旧实例 + 同 profile 起有头
// 常用: browser_cat.mau 认领线——'browser.headful'[@args] > @result
// 门禁: **域外专属**（主干登录工具）——其余 browser-* 域内专属，本件方向相反（宿主 IsTimebackScopedTool 例外放行）
// ═══════════════════════════════════════════════════
using System;
using System.Text.Json;
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 浏览器有头登录积木——browser-headful 工具执行面（A1：域外专属 · 主干人工登录）
    /// </summary>
    public static class BrowserHeadfulBrick
    {
        /// <summary>
        /// 有头实例开关——open（起有头）/ close（关有头）
        /// </summary>
        /// <param name="argsJson">工具参数 JSON（action / url；catId 保留键由宿主注入）</param>
        /// <param name="result">状态摘要或 ERR| 错误文本</param>
        /// <returns>true=执行成功</returns>
        public static bool Headful(string argsJson, out string result)
        {
            result = "";
            string badArgs = JsonArgs.Validate(argsJson, "action url", "", "", "");
            if (badArgs.Length > 0)
            {
                result = badArgs;
                return false;
            }
            string catId = JsonArgs.Get(argsJson, "catId");
            string action = JsonArgs.Get(argsJson, "action");
            string url = JsonArgs.Get(argsJson, "url");
            if (action.Length == 0)
            {
                action = "open";
            }
            try
            {
                IBrowserLifecycle? lifecycle;
                DataBox.TryResolve<IBrowserLifecycle>(out lifecycle);
                if (lifecycle == null)
                {
                    result = "ERR|BROWSER_NO_SERVICE|宿主未注入 IBrowserLifecycle";
                    return false;
                }
                string body = lifecycle.Headful(catId, action, url);
                if (body.StartsWith("ERR|", StringComparison.Ordinal))
                {
                    result = body;
                    return true;
                }
                System.Collections.Generic.Dictionary<string, object> fields = new System.Collections.Generic.Dictionary<string, object>();
                fields["chars"] = body.Length;
                result = MetaHead("browser-headful", true, action, -1, fields) + "\n" + action + " | " + body.Length.ToString() + " 字";
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
// #MAU_CHECKSUM:SHA256:7BC6512C53C8FB2C9A27BB7841F81648D9D4603DC825529A2A4DD74D80E47D0F

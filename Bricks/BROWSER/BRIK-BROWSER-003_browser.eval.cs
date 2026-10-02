// ═══════════════════════════════════════════════════
// 积木: browser.eval
// ID:   BRIK-BROWSER-003
// 类别: BROWSER
// 作用: 在当前页面执行 JS 表达式——小结果直返，大结果落盘（万能取值口）
// 依赖: 无
// 引用: Mau.Runtime（IBrowserService/DataBox）
// 原理: DataBox.TryResolve<IBrowserService> → Eval(catId, expression)；DOM 之外信息（如 onclick 里的目标 URL）用本工具兜底
// 常用: browser_cat.mau 认领线——'browser.eval'[@args] > @result
// 门禁: 本工具域限定（仅 timeback 作用域内可用）——宿主派发面拦截，积木不重复判定
// ═══════════════════════════════════════════════════
using System;
using System.Text.Json;
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 浏览器取值积木——browser-eval 工具执行面（A110 本地浏览器能力）
    /// </summary>
    public static class BrowserEvalBrick
    {
        /// <summary>
        /// 执行 JS 表达式并取回结果
        /// </summary>
        /// <param name="argsJson">工具参数 JSON（expression 必填）</param>
        /// <param name="result">结果文本或 ERR| 错误文本</param>
        /// <returns>true=执行成功</returns>
        public static bool Eval(string argsJson, out string result)
        {
            result = "";
            string badArgs = JsonArgs.Validate(argsJson, "expression", "expression", "", "");
            if (badArgs.Length > 0)
            {
                result = badArgs;
                return false;
            }
            string catId = JsonArgs.Get(argsJson, "catId");
            string expression = JsonArgs.Get(argsJson, "expression");
            if (expression.Length == 0)
            {
                result = "ERR|BAD_ARGS|缺少参数 expression";
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
                string body = service.Eval(catId, expression);
                if (body.StartsWith("ERR|", StringComparison.Ordinal))
                {
                    result = body;
                    return true;
                }
                result = MetaHead(body);
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
        /// 结构化元数据头——首行单行 JSON（ok/tool/chars；键序稳定 = 插入序）
        /// 表达式本身不进头（可能很长——头只放稳定的元数据）
        /// </summary>
        /// <param name="body">正文</param>
        /// <returns>单行 JSON</returns>
        private static string MetaHead(string body)
        {
            System.Collections.Generic.Dictionary<string, object> head = new System.Collections.Generic.Dictionary<string, object>();
            head["ok"] = true;
            head["tool"] = "browser-eval";
            head["chars"] = body.Length;
            return JsonSerializer.Serialize(head);
        }
    }
}
// #MAU_CHECKSUM:SHA256:1F099A04FD89B71FA81842DF4A2B323D24718679D95A49C1D212A6ABD9ED7834

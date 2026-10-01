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
            string badArgs = ValidateArgs(argsJson, "full", "", "", "");
            if (badArgs.Length > 0)
            {
                result = badArgs;
                return false;
            }
            string catId = ExtractArg(argsJson, "catId");
            string fullText = ExtractArg(argsJson, "full");
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

        /// <summary>
        /// 参数面校验——未知 / 缺值 / 枚举非法一律拒绝（参数面零容忍；catId 保留键放行）
        /// </summary>
        /// <param name="argsJson">参数 JSON</param>
        /// <param name="allowed">允许的参数名（空格分隔）</param>
        /// <param name="required">必填参数名（空格分隔）</param>
        /// <param name="enumName">枚举参数名（空=无）</param>
        /// <param name="enumValues">枚举合法值（| 分隔）</param>
        /// <returns>错误文本（空=通过）</returns>
        private static string ValidateArgs(string argsJson, string allowed, string required, string enumName, string enumValues)
        {
            if (argsJson == null || argsJson.Length == 0)
            {
                return "";
            }
            try
            {
                JsonDocument doc = JsonDocument.Parse(argsJson);
                try
                {
                    JsonElement root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object)
                    {
                        return "ERR|BAD_ARGS|参数必须是 JSON 对象";
                    }
                    foreach (JsonProperty property in root.EnumerateObject())
                    {
                        if (property.Name == "catId")
                        {
                            continue;
                        }
                        if (allowed.Length == 0)
                        {
                            return "ERR|BAD_ARGS|未知参数: " + property.Name + "（本工具无参数）";
                        }
                        if ((" " + allowed + " ").IndexOf(" " + property.Name + " ", StringComparison.Ordinal) < 0)
                        {
                            return "ERR|BAD_ARGS|未知参数: " + property.Name + "（支持 " + allowed + "）";
                        }
                    }
                    if (required.Length > 0)
                    {
                        string[] must = required.Split(' ');
                        for (int i = 0; i < must.Length; i = i + 1)
                        {
                            JsonElement mustValue;
                            if (!root.TryGetProperty(must[i], out mustValue) ||
                                (mustValue.ValueKind == JsonValueKind.String && (mustValue.GetString() ?? "").Length == 0))
                            {
                                return "ERR|BAD_ARGS|缺参数 " + must[i] + "（必填：" + required + "）";
                            }
                        }
                    }
                    if (enumName.Length > 0)
                    {
                        JsonElement enumValue;
                        if (root.TryGetProperty(enumName, out enumValue) && enumValue.ValueKind == JsonValueKind.String)
                        {
                            string value = enumValue.GetString() ?? "";
                            if (value.Length > 0 && ("|" + enumValues + "|").IndexOf("|" + value + "|", StringComparison.Ordinal) < 0)
                            {
                                return "ERR|BAD_ARGS|" + enumName + " 非法值: " + value + "（" + enumValues + "）";
                            }
                        }
                    }
                    return "";
                }
                finally
                {
                    doc.Dispose();
                }
            }
            catch (Exception ex)
            {
                return "ERR|BAD_ARGS|参数 JSON 解析失败: " + ex.Message;
            }
        }

        /// <summary>
        /// 展平参数提取——argsJson 中取字符串值（不存在返回空串）
        /// </summary>
        /// <param name="argumentsJson">参数 JSON</param>
        /// <param name="key">键名</param>
        /// <returns>值文本（非字符串按 JSON 文本返回）</returns>
        private static string ExtractArg(string argumentsJson, string key)
        {
            try
            {
                JsonDocument doc = JsonDocument.Parse(argumentsJson);
                try
                {
                    JsonElement el;
                    if (doc.RootElement.TryGetProperty(key, out el))
                    {
                        if (el.ValueKind == JsonValueKind.String)
                        {
                            return el.GetString() ?? "";
                        }
                        return el.GetRawText();
                    }
                }
                finally
                {
                    doc.Dispose();
                }
            }
            catch (Exception ex)
            {
                LogStore.Add("BROWSER", 2, "browser-shot 参数提取失败: " + ex.Message, "TOOL");
            }
            return "";
        }
    }
}
// #MAU_CHECKSUM:SHA256:B92F1ADD99B1C45D04975A711437C6DAF93CD8F6F782BA6AD711902B5CA329CE

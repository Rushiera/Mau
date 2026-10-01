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
            string badArgs = ValidateArgs(argsJson, "url", "url", "", "");
            if (badArgs.Length > 0)
            {
                result = badArgs;
                return false;
            }
            string catId = ExtractArg(argsJson, "catId");
            string url = ExtractArg(argsJson, "url");
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
                result = MetaHead(url, body);
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
        /// 结构化元数据头——首行单行 JSON（ok/tool/url/chars；键序稳定 = 插入序）
        /// </summary>
        /// <param name="url">目标地址</param>
        /// <param name="body">正文</param>
        /// <returns>单行 JSON</returns>
        private static string MetaHead(string url, string body)
        {
            System.Collections.Generic.Dictionary<string, object> head = new System.Collections.Generic.Dictionary<string, object>();
            head["ok"] = true;
            head["tool"] = "browser-open";
            head["url"] = url;
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
                LogStore.Add("BROWSER", 2, "browser-open 参数提取失败: " + ex.Message, "TOOL");
            }
            return "";
        }
    }
}
// #MAU_CHECKSUM:SHA256:910AC4803DC30186BDDD75B63FA750F5E57F53F6506BF76B9E8FE2C9EA4562E0

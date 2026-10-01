// ═══════════════════════════════════════════════════
// 积木: image.inject
// ID:   BRIK-VISION-002
// 类别: VISION
// 作用: 图片插入主干——工具执行面（参数面校验 + 登记回执）；真图由宿主批后段合并注入一条 user 消息（design-ch4-chat-images §8.4-1）
// 依赖: 无
// 引用: Mau.Runtime（LogStore）
// 原理: argsJson 内解析（语料零 JSON 解析）；五参 ValidateArgs 与工具声明面逐键对齐（门禁 ToolDeclarationContract）
// 常用: vision_cat.mau 认领线——'image.inject'[@args] > @result
// ═══════════════════════════════════════════════════
using System;
using System.Text.Json;
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 注入积木——image-inject 图片插入主干（LLM 工具执行面：参数整包 argsJson；宿主批后段执行实际注入）
    /// </summary>
    public static class ImageInjectBrick
    {
        /// <summary>
        /// 执行登记——参数面校验后回登记回执；图片实际注入由宿主批后段读取本工具参数完成（本积木不触前文）
        /// </summary>
        /// <param name="argsJson">工具参数 JSON（path）</param>
        /// <param name="result">登记回执或 ERR| 错误文本</param>
        /// <returns>true=执行成功</returns>
        public static bool Inject(string argsJson, out string result)
        {
            result = "";
            // [参数面] 声明面口径零容忍——未知 / 缺值一律 ERR|BAD_ARGS（catId 保留键放行）
            string badArgs = ValidateArgs(argsJson, "path", "path", "", "");
            if (badArgs.Length > 0)
            {
                result = badArgs;
                return false;
            }
            string path = ExtractArg(argsJson, "path");
            if (path.Length == 0)
            {
                result = "ERR|BAD_ARGS|缺少参数 path";
                return false;
            }
            result = MetaHead(path);
            return true;
        }

        /// <summary>
        /// 结构化元数据头——首行单行 JSON（ok/tool/path；键序稳定 = 插入序）
        /// 约定（design-ch4-tools 附录）：返回体 = 首行 JSON 头 + 正文定界行
        /// </summary>
        /// <param name="path">图片路径</param>
        /// <returns>单行 JSON</returns>
        private static string MetaHead(string path)
        {
            System.Collections.Generic.Dictionary<string, object> head = new System.Collections.Generic.Dictionary<string, object>();
            head["ok"] = true;
            head["tool"] = "image-inject";
            head["path"] = path;
            return JsonSerializer.Serialize(head);
        }

        /// <summary>
        /// 展平参数提取——argsJson 中取字符串值（不存在返回空串）
        /// </summary>
        /// <param name="argumentsJson">参数 JSON</param>
        /// <param name="key">参数名</param>
        /// <returns>参数值</returns>
        private static string ExtractArg(string argumentsJson, string key)
        {
            try
            {
                JsonDocument doc = JsonDocument.Parse(argumentsJson);
                try
                {
                    if (doc.RootElement.TryGetProperty(key, out JsonElement el))
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
                LogStore.Add("VISION", 2, "image-inject 参数提取失败: " + ex.Message, "TOOL");
            }
            return "";
        }

        /// <summary>
        /// 参数面校验——声明面口径零容忍：未知参数 / 必填缺值 / 非法枚举值一律 ERR|BAD_ARGS（宿主注入保留键 catId 放行）。
        /// </summary>
        /// <param name="argsJson">工具参数 JSON</param>
        /// <param name="allowed">允许键（空格分隔；空=无参数）</param>
        /// <param name="required">必填键（空格分隔；空=无必填）</param>
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
    }
}
// #MAU_CHECKSUM:SHA256:6AE72381DFEDDCE1315152BFE28DD6060C91C23927A38C1EA959F32C043BDAC3

// ═══════════════════════════════════════════════════
// 积木: vision.analyze
// ID:   BRIK-VISION-001
// 类别: VISION
// 作用: 图像识别——读取图片（本地路径 base64 内联 / 外部 URL 直传）→ 视觉模型分析 → 返回基于提示词的文本；返回体 = 首行 JSON 头 + 正文
// 依赖: 无
// 引用: Mau.Runtime（IVisionService/DataBox）
// 原理: DataBox.TryResolve<IVisionService> → Analyze(path, question)；argsJson 内解析（语料零 JSON 解析）；
//       配置 vision.api_config_id 未配置 → ERR|API_NOT_CONFIGURED（先配置后才可用）
// 常用: vision_cat.mau 认领线——'vision.analyze'[@args] > @result
// ═══════════════════════════════════════════════════
using System;
using System.Text.Json;
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 识别积木——image-analyze 图像识别（LLM 工具执行面：参数整包 argsJson；同步执行——R2.2 拍板走等待）
    /// </summary>
    public static class VisionAnalyzeBrick
    {
        /// <summary>
        /// 执行图像识别——读取图片 + 视觉模型分析，返回基于提示词的文本
        /// </summary>
        /// <param name="argsJson">工具参数 JSON（path/question）</param>
        /// <param name="result">分析结果或 ERR| 错误文本</param>
        /// <returns>true=执行成功</returns>
        public static bool Analyze(string argsJson, out string result)
        {
            result = "";
            // [参数面] 声明面口径零容忍——未知 / 缺值一律 ERR|BAD_ARGS（catId 保留键放行）
            string badArgs = ValidateArgs(argsJson, "path question", "path", "", "");
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
            string question = ExtractArg(argsJson, "question");
            try
            {
                IVisionService? service;
                DataBox.TryResolve<IVisionService>(out service);
                if (service == null)
                {
                    result = "ERR|VISION_NO_SERVICE|宿主未注入 IVisionService";
                    return false;
                }
                string body = service.Analyze(path, question);
                if (body.StartsWith("ERR|", StringComparison.Ordinal))
                {
                    result = body;
                    return true;
                }
                // 结构化返回体（design-ch4-tools 附录）——首行 JSON 元数据头 + 正文定界
                result = MetaHead(path, question, body);
                if (body.Length > 0)
                {
                    result = result + "\n" + body;
                }
                return true;
            }
            catch (Exception ex)
            {
                result = "ERR|" + ex.GetType().Name + "|" + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 展平参数提取——argsJson 中取字符串值（不存在返回空串）
        /// </summary>
        /// <param name="argumentsJson">参数 JSON</param>
        /// <param name="key">参数名</param>
        /// <returns>参数值</returns>
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

        /// <summary>
        /// 结构化元数据头——首行单行 JSON（ok/tool/path/question/chars；键序稳定 = 插入序）
        /// 约定（design-ch4-tools 附录）：返回体 = 首行 JSON 头 + 正文定界行（正文不塞进 JSON——避免转义膨胀）
        /// </summary>
        /// <param name="path">图片路径</param>
        /// <param name="question">提示词</param>
        /// <param name="body">分析正文</param>
        /// <returns>单行 JSON</returns>
        private static string MetaHead(string path, string question, string body)
        {
            System.Collections.Generic.Dictionary<string, object> head = new System.Collections.Generic.Dictionary<string, object>();
            head["ok"] = true;
            head["tool"] = "image-analyze";
            head["path"] = path;
            head["question"] = question;
            head["chars"] = body.Length;
            return JsonSerializer.Serialize(head);
        }

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
            catch (Exception)
            {
            }
            return "";
        }
    }
}
// #MAU_CHECKSUM:SHA256:025EAC891010E179E08D67F057F8A90DED6DDFA1EBA5357230B0920F9BFEF551

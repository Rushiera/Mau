using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using Mau.Runtime;

namespace Mau.Providers
{
    /// <summary>
    /// DeepSeek 图像识别服务——deepseek-v4-flash-vision-exp 视觉模型（R2.2 工具组）。
    /// 非流式：POST /chat/completions + user content 块数组（text + image_url base64 内联 / 外部 URL 直传）→ 解析 choices[0].message.content。
    /// 图片读取与格式化上传全在工具内部——语料只传 图片路径 + 提示词 两个参数（莎拍板 2026-08-27）。
    /// 配置：vision.api_config_id 引用 LLM 池配置（未配置=不可用——ERR|API_NOT_CONFIGURED）；
    /// vision.endpoint / vision.model 可选覆盖（空=池配置推导：已含 /chat/completions 原样，否则拼尾）；
    /// vision.detail 可选（low/high/original/auto——默认 auto 保留原图，图片 token 消耗极小 ≤384/图）；
    /// vision.timeout_ms 可选（空=120000 毫秒兜底）。
    /// 同步执行：Analyze 同步阻塞（R2.2 拍板——与 web-search 同构走等待）。
    /// </summary>
    public sealed class DeepSeekVisionService : IVisionService
    {
        /// <summary>
        /// HTTP 客户端——每实例独立（超时兜底极大值——真正超时由每次请求 CTS 按 vision.timeout_ms 控制）
        /// </summary>
        private readonly HttpClient _client;

        /// <summary>
        /// LLM API 配置池——视觉 API 的 endpoint/model/key 来源（引用池配置——M1 llm-api.json + secrets）
        /// </summary>
        private readonly CH_LlmApiConfigStore _apiStore;

        /// <summary>
        /// 全局配置——vision.* 键段实时读取（vision.api_config_id/endpoint/model/detail/timeout_ms）
        /// </summary>
        private readonly ConfigStore _globalConfig;

        /// <summary>
        /// 兜底模型名——视觉模型官方现役
        /// </summary>
        private const string FallbackModel = "deepseek-v4-flash-vision-exp";

        /// <summary>
        /// 默认超时毫秒——视觉分析同步等待兜底
        /// </summary>
        private const int DefaultTimeoutMs = 120000;

        /// <summary>
        /// 单图最大字节——base64 内联 32 MiB 限制（官方文档）
        /// </summary>
        private const long MaxInlineImageBytes = 32L * 1024 * 1024;

        /// <summary>
        /// 建立图像识别服务——配置实时读取（vision.* 全局键 + LLM 池配置；未配置 api_config_id = 不可用态）
        /// </summary>
        /// <param name="apiStore">LLM API 配置池</param>
        /// <param name="globalConfig">全局配置存储（vision.* 键段）</param>
        public DeepSeekVisionService(CH_LlmApiConfigStore apiStore, ConfigStore globalConfig)
        {
            _apiStore = apiStore;
            _globalConfig = globalConfig;
            _client = new HttpClient();
            _client.Timeout = TimeSpan.FromMilliseconds(600000);
        }

        /// <summary>
        /// 执行一次图像识别——同步阻塞；返回模型基于提示词的分析文本。
        /// 未配置视觉 API → ERR|API_NOT_CONFIGURED（拍板：配置后才可用，不做回退）。
        /// 图片读取失败/超限 → ERR|IMAGE_*（错误可见性——失败侧也落盒）。
        /// </summary>
        /// <param name="imagePath">图片路径（本地绝对路径或 http(s) URL）</param>
        /// <param name="question">提示词（对图片的提问/指令）</param>
        /// <returns>分析结果文本；失败 ERR| 前缀（错误可见性）</returns>
        public string Analyze(string imagePath, string question)
        {
            // [段1] 配置解析——api_config_id 未配置/无效/缺 key → API_NOT_CONFIGURED（先配置后才可用）
            string endpoint = "";
            string model = "";
            string apiKey = "";
            string configError = ResolveConfig(out endpoint, out model, out apiKey);
            if (configError.Length > 0)
            {
                return configError;
            }

            // [段2] 图片载荷——本地路径 base64 内联 / 外部 URL 直传（读取与格式化全在工具内部）
            string imageUrl = "";
            string imageError = BuildImageUrl(imagePath, out imageUrl);
            if (imageError.Length > 0)
            {
                return imageError;
            }

            // [段3] 构造请求体——非流式 chat completions + user content 块数组（text + image_url）
            string detail = GetDetail();
            string body = BuildRequestBody(model, question, imageUrl, detail);

            // [段4] 发送请求——同步等待（R2.2 拍板：必须走等待）；超时按配置 CTS（vision.timeout_ms）
            string result = "";
            string usageText = "";
            string rawSummary = "";
            int timeoutMs = GetTimeoutMs();
            try
            {
                using (CancellationTokenSource cts = new CancellationTokenSource(timeoutMs))
                {
                    using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, endpoint))
                    {
                        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + apiKey);
                        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                        HttpResponseMessage response = _client.SendAsync(request, cts.Token).GetAwaiter().GetResult();
                        using (response)
                        {
                            // [段5] HTTP 层失败——错误 JSON 双形态解析（error.message 兜底，HTTP 码不可作唯一判据）
                            if (!response.IsSuccessStatusCode)
                            {
                                string raw = "";
                                try
                                {
                                    raw = response.Content.ReadAsStringAsync(cts.Token).GetAwaiter().GetResult();
                                }
                                catch (Exception ex)
                                {
                                    return "ERR|TRANSPORT|" + ex.GetType().Name + "|" + ex.Message;
                                }
                                return ParseErrorText((int)response.StatusCode, raw);
                            }

                            // [段6] 成功响应解析——choices[0].message.content + usage 提取
                            string json = "";
                            try
                            {
                                json = response.Content.ReadAsStringAsync(cts.Token).GetAwaiter().GetResult();
                            }
                            catch (Exception ex)
                            {
                                return "ERR|TRANSPORT|" + ex.GetType().Name + "|" + ex.Message;
                            }
                            result = ParseResponse(json, out usageText, out rawSummary);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return "ERR|TIMEOUT|图像识别超时（" + timeoutMs.ToString() + "ms——config vision.timeout_ms 可调）";
            }
            catch (Exception ex)
            {
                return "ERR|TRANSPORT|" + ex.GetType().Name + "|" + ex.Message;
            }

            // [段7] usage 结算行——观测（用户可感知单次识别消耗）
            if (usageText.Length > 0)
            {
                LogStore.Add("VISION", 1, "图像识别消耗：" + usageText, "TOOL");
            }

            if (result.Length == 0)
            {
                return "ERR|EMPTY_RESULT|图像识别响应无输出文本（响应头 300 字符: " + rawSummary + "——请检查端点是否兼容 chat completions 结构）";
            }
            return result;
        }

        /// <summary>
        /// 配置解析——vision.api_config_id → 池配置 → endpoint/model/key（可选覆盖）
        /// </summary>
        /// <param name="endpoint">目标端点（含 /chat/completions）</param>
        /// <param name="model">模型名</param>
        /// <param name="apiKey">API 密钥</param>
        /// <returns>错误文本（空=解析成功）</returns>
        private string ResolveConfig(out string endpoint, out string model, out string apiKey)
        {
            endpoint = "";
            model = "";
            apiKey = "";

            // 1) api_config_id——未配置 = 工具不可用（拍板：非回退，直接 API_NOT_CONFIGURED）
            string rawId = _globalConfig.Get("vision.api_config_id", "");
            if (rawId.Length == 0)
            {
                return "ERR|API_NOT_CONFIGURED|视觉 API 未配置（配置区 vision.api_config_id 需引用 LLM 池配置）";
            }
            Guid apiConfigId;
            if (!Guid.TryParse(rawId, out apiConfigId))
            {
                return "ERR|API_NOT_CONFIGURED|视觉 API 配置无效（vision.api_config_id 不是合法 GUID）";
            }

            // 2) 池配置——ID 必须命中
            CH_LlmApiConfig config;
            if (!_apiStore.TryGet(apiConfigId, out config))
            {
                return "ERR|API_NOT_CONFIGURED|视觉 API 配置不存在（LLM 池中找不到该 apiConfigId）";
            }

            // 3) key——secrets 面（缺 key = 不可用）
            apiKey = _apiStore.GetSecret(apiConfigId);
            if (apiKey.Length == 0)
            {
                return "ERR|API_NOT_CONFIGURED|视觉 API 密钥未配置（LLM 池配置缺 key）";
            }

            // 4) endpoint——vision.endpoint 覆盖；空 = 池配置推导（chat completions 尾）
            string overrideEndpoint = _globalConfig.Get("vision.endpoint", "");
            if (overrideEndpoint.Length > 0)
            {
                endpoint = overrideEndpoint;
            }
            else
            {
                string derived = DeriveChatEndpoint(config.Endpoint);
                if (derived.Length == 0)
                {
                    return "ERR|API_NOT_CONFIGURED|视觉端点推导失败（池配置 endpoint 结尾不识别: " + config.Endpoint + "——请配置 vision.endpoint 显式指定）";
                }
                endpoint = derived;
            }

            // 5) model——vision.model 覆盖；空 = 池默认模型；再空 = 兜底
            string overrideModel = _globalConfig.Get("vision.model", "");
            if (overrideModel.Length > 0)
            {
                model = overrideModel;
            }
            else if (config.DefaultModel.Length > 0)
            {
                model = config.DefaultModel;
            }
            else
            {
                model = FallbackModel;
            }
            return "";
        }

        /// <summary>
        /// 池配置 endpoint → chat completions 端点推导——已含 /chat/completions 原样；其余直接拼 /chat/completions。失败返回空串（不猜——调用侧显式提示）。
        /// </summary>
        /// <param name="poolEndpoint">池配置 endpoint</param>
        /// <returns>推导后端点（失败空串）</returns>
        private static string DeriveChatEndpoint(string poolEndpoint)
        {
            if (poolEndpoint == null || poolEndpoint.Length == 0)
            {
                return "";
            }
            string trimmed = poolEndpoint.TrimEnd('/');
            if (trimmed.EndsWith("/chat/completions", StringComparison.Ordinal))
            {
                return trimmed;
            }
            return trimmed + "/chat/completions";
        }

        /// <summary>
        /// 图片载荷构建——本地路径 base64 内联 / 外部 URL 直传（官方文档：格式由文件实际内容判断）。
        /// 本地：读取字节 → 32MiB 上限检查 → base64 data URL（media type 由扩展名推断，兜底 image/jpeg）。
        /// 外部：http(s) URL 直传（URL 长度 ≤8192 校验）。
        /// </summary>
        /// <param name="imagePath">图片路径或 URL</param>
        /// <param name="imageUrl">构建后的 image_url（data URL 或外部 URL）</param>
        /// <returns>错误文本（空=成功）</returns>
        private static string BuildImageUrl(string imagePath, out string imageUrl)
        {
            imageUrl = "";
            if (imagePath == null || imagePath.Length == 0)
            {
                return "ERR|BAD_ARGS|缺少图片路径";
            }
            string trimmed = imagePath.Trim();
            if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                if (trimmed.Length > 8192)
                {
                    return "ERR|IMAGE_URL_TOO_LONG|外部图片 URL 超长（>8192 字符——请改用本地路径）";
                }
                imageUrl = trimmed;
                return "";
            }
            if (!File.Exists(trimmed))
            {
                return "ERR|IMAGE_NOT_FOUND|图片文件不存在: " + trimmed;
            }
            long length = new FileInfo(trimmed).Length;
            if (length > MaxInlineImageBytes)
            {
                return "ERR|IMAGE_TOO_LARGE|图片超过 32MiB 内联上限（" + length.ToString() + " 字节——请压缩或改用 Files API）";
            }
            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(trimmed);
            }
            catch (Exception ex)
            {
                return "ERR|IMAGE_READ|图片读取失败: " + ex.Message;
            }
            string mime = GuessImageMime(trimmed);
            imageUrl = "data:" + mime + ";base64," + Convert.ToBase64String(bytes);
            return "";
        }

        /// <summary>
        /// 图片 media type 推断——扩展名映射（官方：格式由内容判断，media type 兜底 image/jpeg）
        /// </summary>
        /// <param name="path">图片路径</param>
        /// <returns>media type</returns>
        private static string GuessImageMime(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".jpg" || ext == ".jpeg")
            {
                return "image/jpeg";
            }
            if (ext == ".png")
            {
                return "image/png";
            }
            if (ext == ".gif")
            {
                return "image/gif";
            }
            if (ext == ".webp")
            {
                return "image/webp";
            }
            return "image/jpeg";
        }

        /// <summary>
        /// 实时读取 detail 配置——vision.detail（low/high/original/auto）；缺省/非法回退 auto（保留原图——莎拍板：图片 token 消耗极小）
        /// </summary>
        /// <returns>detail 值</returns>
        private string GetDetail()
        {
            string raw = _globalConfig.Get("vision.detail", "");
            if (raw.Length > 0)
            {
                string lower = raw.ToLowerInvariant();
                if (lower == "low" || lower == "high" || lower == "original" || lower == "auto")
                {
                    return lower;
                }
            }
            return "auto";
        }

        /// <summary>
        /// 实时读取超时毫秒——vision.timeout_ms 配置 → 默认 120000；非法/<=0 → 默认
        /// </summary>
        /// <returns>超时毫秒</returns>
        private int GetTimeoutMs()
        {
            string raw = _globalConfig.Get("vision.timeout_ms", "");
            if (raw.Length > 0)
            {
                int parsed;
                if (int.TryParse(raw, out parsed) && parsed > 0)
                {
                    return parsed;
                }
            }
            return DefaultTimeoutMs;
        }

        /// <summary>
        /// 构造非流式 chat completions 请求体——user content 块数组（text + image_url base64/URL + detail）
        /// </summary>
        /// <param name="model">模型名</param>
        /// <param name="question">提示词</param>
        /// <param name="imageUrl">图片载荷（data URL 或外部 URL）</param>
        /// <param name="detail">detail 值（low/high/original/auto）</param>
        /// <returns>请求体 JSON</returns>
        private static string BuildRequestBody(string model, string question, string imageUrl, string detail)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{\"model\":\"");
            sb.Append(EscapeJson(model));
            sb.Append("\",\"messages\":[{\"role\":\"user\",\"content\":[{\"type\":\"text\",\"text\":\"");
            sb.Append(EscapeJson(question.Length > 0 ? question : "描述这张图片的内容。"));
            sb.Append("\"},{\"type\":\"image_url\",\"image_url\":{\"url\":\"");
            sb.Append(EscapeJson(imageUrl));
            sb.Append("\",\"detail\":\"");
            sb.Append(EscapeJson(detail));
            sb.Append("\"}}]}],\"stream\":false}");
            return sb.ToString();
        }

        /// <summary>
        /// 响应解析——OpenAI chat completions 结构防御式：choices[0].message.content 提取 + usage。
        /// 诊断：rawSummary 响应体前 300 字符——EMPTY_RESULT/解析失败时可见实际返回（排错透明性）。
        /// </summary>
        /// <param name="json">响应体</param>
        /// <param name="usageText">usage 摘要（prompt/completion——空=无）</param>
        /// <param name="rawSummary">响应体前 300 字符诊断摘要</param>
        /// <returns>输出文本或 ERR| 错误</returns>
        private static string ParseResponse(string json, out string usageText, out string rawSummary)
        {
            usageText = "";
            rawSummary = "";
            if (json == null)
            {
                json = "";
            }
            if (json.Length > 300)
            {
                rawSummary = json.Substring(0, 300);
            }
            else
            {
                rawSummary = json;
            }
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    JsonElement root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object)
                    {
                        return "ERR|PARSE|响应非 JSON 对象";
                    }

                    // [段1] usage 提取（prompt_tokens / completion_tokens）
                    StringBuilder usageSb = new StringBuilder();
                    ExtractUsage(root, usageSb);
                    usageText = usageSb.ToString();

                    // [段2] choices[0].message.content——标准 OpenAI chat completions
                    string content = "";
                    if (root.TryGetProperty("choices", out JsonElement choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
                    {
                        JsonElement first = choices[0];
                        if (first.ValueKind == JsonValueKind.Object)
                        {
                            if (first.TryGetProperty("message", out JsonElement message) && message.ValueKind == JsonValueKind.Object)
                            {
                                if (message.TryGetProperty("content", out JsonElement contentEl) && contentEl.ValueKind == JsonValueKind.String)
                                {
                                    string? got = contentEl.GetString();
                                    if (got != null)
                                    {
                                        content = got;
                                    }
                                }
                            }
                        }
                    }
                    return content;
                }
            }
            catch (Exception)
            {
                return "ERR|PARSE|响应解析异常（响应头 300 字符: " + rawSummary + "）";
            }
        }

        /// <summary>
        /// usage 提取——prompt_tokens / completion_tokens（防御式）
        /// </summary>
        /// <param name="root">响应根对象</param>
        /// <param name="usage">摘要缓冲</param>
        private static void ExtractUsage(JsonElement root, StringBuilder usage)
        {
            JsonElement usageEl;
            if (!root.TryGetProperty("usage", out usageEl) || usageEl.ValueKind != JsonValueKind.Object)
            {
                return;
            }
            long promptTokens = 0;
            long completionTokens = 0;
            if (usageEl.TryGetProperty("prompt_tokens", out JsonElement pEl) && pEl.ValueKind == JsonValueKind.Number)
            {
                promptTokens = pEl.GetInt64();
            }
            if (usageEl.TryGetProperty("completion_tokens", out JsonElement cEl) && cEl.ValueKind == JsonValueKind.Number)
            {
                completionTokens = cEl.GetInt64();
            }
            usage.Append("prompt=");
            usage.Append(promptTokens.ToString());
            usage.Append("|completion=");
            usage.Append(completionTokens.ToString());
        }

        /// <summary>
        /// HTTP 错误解析——错误 JSON 的 error.message/type 双形态兜底（对齐 DeepSeekLlmRuntime 语义）
        /// </summary>
        /// <param name="statusCode">HTTP 状态码</param>
        /// <param name="raw">响应体原文</param>
        /// <returns>ERR 文本</returns>
        private static string ParseErrorText(int statusCode, string raw)
        {
            string message = "";
            if (raw != null && raw.Length > 0)
            {
                try
                {
                    using (JsonDocument doc = JsonDocument.Parse(raw))
                    {
                        JsonElement root = doc.RootElement;
                        JsonElement error;
                        if (root.TryGetProperty("error", out error) && error.ValueKind == JsonValueKind.Object)
                        {
                            if (error.TryGetProperty("message", out JsonElement msg) && msg.ValueKind == JsonValueKind.String)
                            {
                                string? got = msg.GetString();
                                if (got != null)
                                {
                                    message = got;
                                }
                            }
                            if (message.Length == 0 && error.TryGetProperty("type", out JsonElement t) && t.ValueKind == JsonValueKind.String)
                            {
                                string? got = t.GetString();
                                if (got != null)
                                {
                                    message = got;
                                }
                            }
                        }
                    }
                }
                catch (Exception)
                {
                    message = raw;
                }
            }
            return "ERR|HTTP_" + statusCode.ToString() + "|" + (message.Length > 0 ? message : "空响应");
        }

        /// <summary>
        /// JSON 字符串转义——双引号/反斜杠/控制字符（请求体组装安全）
        /// </summary>
        /// <param name="value">原始字符串</param>
        /// <returns>转义后字符串（不含外层引号）</returns>
        private static string EscapeJson(string value)
        {
            if (value == null)
            {
                return "";
            }
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '"' || c == '\\')
                {
                    sb.Append('\\');
                    sb.Append(c);
                }
                else if (c == '\n')
                {
                    sb.Append("\\n");
                }
                else if (c == '\r')
                {
                    sb.Append("\\r");
                }
                else if (c == '\t')
                {
                    sb.Append("\\t");
                }
                else if (c < 0x20)
                {
                    sb.Append("\\u");
                    sb.Append(((int)c).ToString("x4"));
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }
    }
}

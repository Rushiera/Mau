using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Mau.Runtime;

namespace Mau.Providers
{
    /// <summary>
    /// DeepSeek 联网搜索服务——Responses API + web_search 内置工具（R2.1 工具组首发）。
    /// 服务端自动执行：一次请求完成"搜索→注入→生成回答"全链（官方文档：web_search 由服务端托管，
    /// 模型在同一请求内自主决定是否搜索/执行搜索/注入上下文——非显式子会话模式）。
    /// 非流式：POST /responses + tools:[{"type":"web_search"}] → 解析 response.completed → output_text 直接返回。
    /// 配置：search.api_config_id 引用 LLM 池配置（未配置=不可用——请求直接返回 ERR|API_NOT_CONFIGURED）；
    /// search.endpoint / search.model 可选覆盖（空=池配置推导：去 /v1|/chat/completions 尾 + /responses）；
    /// search.timeout_ms 可选（空=120000 毫秒兜底）。
    /// 同步执行：Search 同步阻塞（R2.1 拍板——后台化未来走子会话代理/多猫交火设计）。
    /// P0 评审修正（DSH 评审 2026-08-26）：假搜索检测（无 web_search_call → ERR|NO_SEARCH）；
    /// incomplete ≠ failed（截断降级返回已有文本+标记，仅 failed 硬错误）；endpoint 去尾规则明确化。
    /// </summary>
    public sealed class DeepSeekWebSearchService : IWebSearchService
    {
        /// <summary>
        /// HTTP 客户端——每实例独立（超时兜底极大值——真正超时由每次请求 CTS 按 search.timeout_ms 控制）
        /// </summary>
        private readonly HttpClient _client;

        /// <summary>
        /// LLM API 配置池——搜索 API 的 endpoint/model/key 来源（引用池配置——M1 llm-api.json + secrets）
        /// </summary>
        private readonly CH_LlmApiConfigStore _apiStore;

        /// <summary>
        /// 全局配置——search.* 键段实时读取（search.api_config_id/search.endpoint/search.model/search.timeout_ms）
        /// </summary>
        private readonly ConfigStore _globalConfig;

        /// <summary>
        /// 兜底模型名——官方现役 v4-flash（Responses 支持 deepseek-v4-flash/v4-pro/v4-flash-vision-exp）
        /// </summary>
        private const string FallbackModel = "deepseek-v4-flash";

        /// <summary>
        /// 默认超时毫秒——官方服务端自动续推上限 10 轮，最坏情况远超常见 22 秒；120 秒兜底，可配置
        /// </summary>
        private const int DefaultTimeoutMs = 120000;

        /// <summary>
        /// 建立搜索服务——配置实时读取（search.* 全局键 + LLM 池配置；未配置 api_config_id = 不可用态）
        /// </summary>
        /// <param name="apiStore">LLM API 配置池</param>
        /// <param name="globalConfig">全局配置存储（search.* 键段）</param>
        public DeepSeekWebSearchService(CH_LlmApiConfigStore apiStore, ConfigStore globalConfig)
        {
            _apiStore = apiStore;
            _globalConfig = globalConfig;
            _client = new HttpClient();
            _client.Timeout = TimeSpan.FromMilliseconds(600000);
        }

        /// <summary>
        /// 执行一次联网搜索——同步阻塞；返回模型最终回答文本（引用标注 [citation:x] 原样保留）。
        /// 未配置搜索 API → ERR|API_NOT_CONFIGURED（拍板：配置后才可用，不做回退）。
        /// 模型未触发 web_search → ERR|NO_SEARCH（假搜索检测——P0 评审修正）。
        /// 截断（incomplete）→ 降级返回已有文本 + 尾部标记（仅 failed 硬错误——P0 评审修正）。
        /// </summary>
        /// <param name="query">搜索查询</param>
        /// <returns>最终回答文本；失败 ERR| 前缀（错误可见性——失败侧也落盒）</returns>
        public string Search(string query)
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

            // [段2] 构造请求体——非流式 Responses（input 字符串 + web_search 工具声明 + 搜索助手指令）
            string body = BuildRequestBody(model, query);

            // [段3] 发送请求——同步等待（R2.1 拍板：必须走等待）；超时按配置 CTS（search.timeout_ms）
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
                            // [段4] HTTP 层失败——错误 JSON 双形态解析（error.message 兜底，HTTP 码不可作唯一判据）
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

                            // [段5] 成功响应解析——status/假搜索/截断判定 + usage 提取
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
                return "ERR|TIMEOUT|搜索超时（" + timeoutMs.ToString() + "ms——config search.timeout_ms 可调）";
            }
            catch (Exception ex)
            {
                return "ERR|TRANSPORT|" + ex.GetType().Name + "|" + ex.Message;
            }

            // [段6] usage 结算行——观测（CH4 不做内部计费——决策 6；用户可感知单次搜索消耗）
            if (usageText.Length > 0)
            {
                LogStore.Add("WEB", 1, "联网搜索消耗：" + usageText, "TOOL");
            }

            if (result.Length == 0)
            {
                return "ERR|EMPTY_RESULT|搜索响应无输出文本（响应头 300 字符: " + rawSummary + "——请检查端点是否兼容 Responses 结构）";
            }
            return result;
        }

        /// <summary>
        /// 配置解析——search.api_config_id → 池配置 → endpoint/model/key（可选覆盖）
        /// </summary>
        /// <param name="endpoint">目标端点（含 /responses）</param>
        /// <param name="model">模型名</param>
        /// <param name="apiKey">API 密钥</param>
        /// <returns>错误文本（空=解析成功）</returns>
        private string ResolveConfig(out string endpoint, out string model, out string apiKey)
        {
            endpoint = "";
            model = "";
            apiKey = "";

            // 1) api_config_id——未配置 = 工具不可用（拍板：非回退，直接 API_NOT_CONFIGURED）
            string rawId = _globalConfig.Get("search.api_config_id", "");
            if (rawId.Length == 0)
            {
                return "ERR|API_NOT_CONFIGURED|搜索 API 未配置（配置区 search.api_config_id 需引用 LLM 池配置）";
            }
            Guid apiConfigId;
            if (!Guid.TryParse(rawId, out apiConfigId))
            {
                return "ERR|API_NOT_CONFIGURED|搜索 API 配置无效（search.api_config_id 不是合法 GUID）";
            }

            // 2) 池配置——ID 必须命中
            CH_LlmApiConfig config;
            if (!_apiStore.TryGet(apiConfigId, out config))
            {
                return "ERR|API_NOT_CONFIGURED|搜索 API 配置不存在（LLM 池中找不到该 apiConfigId）";
            }

            // 3) key——secrets 面（缺 key = 不可用）
            apiKey = _apiStore.GetSecret(apiConfigId);
            if (apiKey.Length == 0)
            {
                return "ERR|API_NOT_CONFIGURED|搜索 API 密钥未配置（LLM 池配置缺 key）";
            }

            // 4) endpoint——search.endpoint 覆盖；空 = 池配置推导（去尾规则明确化——P0-3 评审修正）
            string overrideEndpoint = _globalConfig.Get("search.endpoint", "");
            if (overrideEndpoint.Length > 0)
            {
                endpoint = overrideEndpoint;
            }
            else
            {
                string derived = DeriveResponsesEndpoint(config.Endpoint);
                if (derived.Length == 0)
                {
                    return "ERR|API_NOT_CONFIGURED|搜索端点推导失败（池配置 endpoint 结尾不识别: " + config.Endpoint + "——请配置 search.endpoint 显式指定）";
                }
                endpoint = derived;
            }

            // 5) model——search.model 覆盖；空 = 池默认模型；再空 = 兜底
            string overrideModel = _globalConfig.Get("search.model", "");
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
        /// 池配置 endpoint → Responses 端点推导——去尾规则（P0-3 评审 + FoldinAI 实测修正）：
        /// 已含 /responses → 原样；去尾部 /chat/completions（保留 /v1 前缀——OpenAI 兼容代理挂 /v1 下，实测
        /// FoldinAI /v1/responses 成功；官方 DeepSeek 无 /v1 前缀 → /responses）→ 拼 /responses；
        /// 结尾 /v1 → 直接拼 /responses；结尾都不认识 → 返回空（不猜——调用侧 ERR|API_NOT_CONFIGURED 显式提示）。
        /// </summary>
        /// <param name="poolEndpoint">池配置 endpoint</param>
        /// <returns>推导后端点（失败空串）</returns>
        private static string DeriveResponsesEndpoint(string poolEndpoint)
        {
            if (poolEndpoint == null || poolEndpoint.Length == 0)
            {
                return "";
            }
            string trimmed = poolEndpoint.TrimEnd('/');
            if (trimmed.EndsWith("/responses", StringComparison.Ordinal))
            {
                return trimmed;
            }
            string baseUrl = "";
            if (trimmed.EndsWith("/chat/completions", StringComparison.Ordinal))
            {
                baseUrl = trimmed.Substring(0, trimmed.Length - "/chat/completions".Length);
            }
            else if (trimmed.EndsWith("/v1", StringComparison.Ordinal))
            {
                baseUrl = trimmed;
            }
            else
            {
                return "";
            }
            if (baseUrl.EndsWith("/", StringComparison.Ordinal))
            {
                baseUrl = baseUrl.TrimEnd('/');
            }
            return baseUrl + "/responses";
        }

        /// <summary>
        /// 实时读取超时毫秒——search.timeout_ms 配置 → 默认 120000；非法/<=0 → 默认
        /// </summary>
        /// <returns>超时毫秒</returns>
        private int GetTimeoutMs()
        {
            string raw = _globalConfig.Get("search.timeout_ms", "");
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
        /// 构造非流式 Responses 请求体——input 字符串 + web_search 工具声明 + 搜索助手指令（P1-1 评审：包裹指令稳定输出）
        /// </summary>
        /// <param name="model">模型名</param>
        /// <param name="query">搜索查询</param>
        /// <returns>请求体 JSON</returns>
        private static string BuildRequestBody(string model, string query)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{\"model\":\"");
            sb.Append(EscapeJson(model));
            sb.Append("\",\"instructions\":\"");
            sb.Append(EscapeJson("You are a web search assistant. Search the web for the user's query and answer based on the search results. Keep citations [citation:x] when referencing sources."));
            sb.Append("\",\"input\":\"");
            sb.Append(EscapeJson(query));
            sb.Append("\",\"tools\":[{\"type\":\"web_search\"}],\"tool_choice\":{\"type\":\"web_search\"}}");
            return sb.ToString();
        }

        /// <summary>
        /// 响应解析——OpenAI Responses 结构防御式：status → 假搜索检测 → output_text 提取 → 截断降级。
        /// P0 评审修正：①无 web_search_call 项 → NO_SEARCH（假搜索不降级为文本抓取）②incomplete=截断降级返回
        /// 已有文本+尾部标记（仅 failed 硬错误）③usage 提取随 out 传出（观测）。
        /// 诊断：rawSummary 响应体前 300 字符——EMPTY_RESULT/解析失败时可见实际返回（排错透明性）。
        /// </summary>
        /// <param name="json">响应体</param>
        /// <param name="usageText">usage 摘要（input/output/reasoning——空=无）</param>
        /// <param name="rawSummary">响应体前 300 字符诊断摘要</param>
        /// <returns>输出文本（截断时带标记）或 ERR| 错误</returns>
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
                        return "";
                    }

                    // [段1] status 判定——failed=硬错误；incomplete=截断（降级）
                    string status = "";
                    if (root.TryGetProperty("status", out JsonElement statusEl) && statusEl.ValueKind == JsonValueKind.String)
                    {
                        string? got = statusEl.GetString();
                        if (got != null)
                        {
                            status = got;
                        }
                    }
                    if (status == "failed")
                    {
                        string error = ExtractError(root);
                        return "ERR|RESPONSES_FAILED|" + (error.Length > 0 ? error : "响应失败");
                    }

                    // [段2] usage 提取（input_tokens / output_tokens / reasoning_tokens）
                    StringBuilder usageSb = new StringBuilder();
                    ExtractUsage(root, usageSb);
                    usageText = usageSb.ToString();

                    // [段3] 遍历 output[]——找 web_search_call（假搜索检测）+ 提取 message.output_text
                    StringBuilder text = new StringBuilder();
                    bool hasSearch = false;
                    if (root.TryGetProperty("output", out JsonElement outputEl) && outputEl.ValueKind == JsonValueKind.Array)
                    {
                        for (int i = 0; i < outputEl.GetArrayLength(); i++)
                        {
                            JsonElement item = outputEl[i];
                            if (item.ValueKind == JsonValueKind.Object)
                            {
                                string itemType = "";
                                if (item.TryGetProperty("type", out JsonElement itEl) && itEl.ValueKind == JsonValueKind.String)
                                {
                                    string? got = itEl.GetString();
                                    if (got != null)
                                    {
                                        itemType = got;
                                    }
                                }
                                if (itemType == "web_search_call")
                                {
                                    hasSearch = true;
                                }
                                else if (itemType == "message")
                                {
                                    AppendOutputText(item, text);
                                }
                            }
                        }
                    }

                    // [段4] 顶层 output_text 兜底（SDK 便捷字段——部分端点直接给全量文本）
                    if (text.Length == 0 && root.TryGetProperty("output_text", out JsonElement topText) && topText.ValueKind == JsonValueKind.String)
                    {
                        string? got = topText.GetString();
                        if (got != null)
                        {
                            text.Append(got);
                        }
                    }

                    // [段5] 假搜索检测——无 web_search_call 项 → NO_SEARCH（P0-1：不把普通回答当搜索结果——DSH 严格模式同源）
                    if (!hasSearch)
                    {
                        return "ERR|NO_SEARCH|模型未触发 web_search（响应中无 web_search_call 项——请确认端点支持服务端搜索）";
                    }

                    // [段6] 截断降级——incomplete 返回已有文本 + 尾部标记（P0-2：不丢弃可用结果；仅 failed 硬错误）
                    if (status == "incomplete")
                    {
                        if (text.Length > 0)
                        {
                            text.Append("\n\n[搜索响应截断——服务端达到输出上限；以上内容为部分结果]");
                        }
                        else
                        {
                            return "ERR|TRUNCATED|搜索响应被截断且无可用输出文本";
                        }
                    }
                    return text.ToString();
                }
            }
            catch (Exception)
            {
                return "ERR|PARSE|响应解析异常（响应头 300 字符: " + rawSummary + "）";
            }
        }

        /// <summary>
        /// 提取输出文本——单个 output item：type=message → content[] 逐块 type=output_text 拼接
        /// </summary>
        /// <param name="item">output item</param>
        /// <param name="text">拼接缓冲</param>
        private static void AppendOutputText(JsonElement item, StringBuilder text)
        {
            JsonElement content;
            if (!item.TryGetProperty("content", out content) || content.ValueKind != JsonValueKind.Array)
            {
                return;
            }
            for (int i = 0; i < content.GetArrayLength(); i++)
            {
                JsonElement block = content[i];
                if (block.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }
                string blockType = "";
                if (block.TryGetProperty("type", out JsonElement blockTypeEl) && blockTypeEl.ValueKind == JsonValueKind.String)
                {
                    string? got = blockTypeEl.GetString();
                    if (got != null)
                    {
                        blockType = got;
                    }
                }
                if (blockType != "output_text")
                {
                    continue;
                }
                if (block.TryGetProperty("text", out JsonElement textEl) && textEl.ValueKind == JsonValueKind.String)
                {
                    string? got = textEl.GetString();
                    if (got != null)
                    {
                        text.Append(got);
                    }
                }
            }
        }

        /// <summary>
        /// usage 提取——input_tokens / output_tokens / output_tokens_details.reasoning_tokens（防御式）
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
            long inputTokens = 0;
            long outputTokens = 0;
            long reasoningTokens = 0;
            if (usageEl.TryGetProperty("input_tokens", out JsonElement inEl) && inEl.ValueKind == JsonValueKind.Number)
            {
                inputTokens = inEl.GetInt64();
            }
            if (usageEl.TryGetProperty("output_tokens", out JsonElement outEl) && outEl.ValueKind == JsonValueKind.Number)
            {
                outputTokens = outEl.GetInt64();
            }
            if (usageEl.TryGetProperty("output_tokens_details", out JsonElement details) && details.ValueKind == JsonValueKind.Object)
            {
                if (details.TryGetProperty("reasoning_tokens", out JsonElement rEl) && rEl.ValueKind == JsonValueKind.Number)
                {
                    reasoningTokens = rEl.GetInt64();
                }
            }
            usage.Append("in=");
            usage.Append(inputTokens.ToString());
            usage.Append("|out=");
            usage.Append(outputTokens.ToString());
            usage.Append("|reasoning=");
            usage.Append(reasoningTokens.ToString());
        }

        /// <summary>
        /// 提取错误详情——root.error.message（防御式）
        /// </summary>
        /// <param name="root">响应根对象</param>
        /// <returns>错误消息（缺省空串）</returns>
        private static string ExtractError(JsonElement root)
        {
            JsonElement error;
            if (!root.TryGetProperty("error", out error) || error.ValueKind != JsonValueKind.Object)
            {
                return "";
            }
            if (error.TryGetProperty("message", out JsonElement msg) && msg.ValueKind == JsonValueKind.String)
            {
                string? got = msg.GetString();
                if (got != null)
                {
                    return got;
                }
            }
            return "";
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
                    // 非 JSON——原文截断作消息
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
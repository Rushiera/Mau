using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Mau.Runtime;

namespace CatHome4.Admin
{
    /// <summary>
    /// Admin 域 LLM API 池探针分部——配置页「测试」按钮数据源（2026-10-01 易用性轮）。
    /// 三探：/v1/models（模型清单，带 Key——验证 Key 有效）+ /api/status（站点信息，免 Key）+ /api/pricing（定价与分组，免 Key）。
    /// 判据：三段各自独立成败——上游非 new-api 站时站点两探 404，不拖垮模型探（面板按段分块展示）。
    /// </summary>
    internal static partial class AdminService
    {
        /// <summary>单探超时（秒）——上游慢响应不阻塞面板（三探串行，最坏约 3 倍本值）。</summary>
        private const int LlmProbeTimeoutSeconds = 15;

        /// <summary>探针共享 HTTP 客户端——客户端级超时关闭，改由单次请求 CancellationToken 控制（分探计时与超时互不干扰）。</summary>
        private static readonly HttpClient LlmProbeClient = CreateLlmProbeClient();

        /// <summary>站点信息探针剔除项——公告属会话性内容，不进测试报告（Rushiera 2026-10-01 定）。</summary>
        private static readonly string[] LlmProbeSiteExcludes = new string[] { "announcements", "announcements_enabled" };

        /// <summary>
        /// 创建探针 HTTP 客户端——客户端级超时关闭（改由单次请求 CancellationToken 控制）。
        /// </summary>
        /// <returns>共享 HttpClient</returns>
        private static HttpClient CreateLlmProbeClient()
        {
            HttpClient client = new HttpClient();
            client.Timeout = Timeout.InfiniteTimeSpan;
            return client;
        }

        /// <summary>
        /// LLM API 池连通性测试——POST /api/v1/llm-apis/probe（body: apiConfigId）。
        /// 返回：模型清单 + 站点信息 + 定价与分组；ok = 模型探通过（Key 可用判据）。
        /// </summary>
        /// <param name="ctx">HTTP 上下文</param>
        /// <returns>测试报告 JSON</returns>
        internal static async Task<IResult> HandleLlmApisProbe(HttpContext ctx)
        {
            // [段1] 参数解析与配置解析
            string body = await ReadBodyText(ctx);
            string apiConfigId = "";
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(body))
                {
                    apiConfigId = GetJsonString(doc.RootElement, "apiConfigId");
                }
            }
            catch (Exception)
            {
                return Results.Json(new { ok = false, error = "body 非 JSON" });
            }
            Guid id;
            if (!Guid.TryParse(apiConfigId, out id) || id == Guid.Empty)
            {
                return Results.Json(new { ok = false, error = "apiConfigId 非法" });
            }
            CH_LlmApiConfigStore store = null;
            DataBox.TryResolve<CH_LlmApiConfigStore>(out store);
            if (store == null)
            {
                return Results.Json(new { ok = false, error = "配置池未绑定" });
            }
            CH_LlmApiConfig config;
            if (!store.TryGet(id, out config))
            {
                return Results.Json(new { ok = false, error = "配置不存在" });
            }
            string apiKey = store.GetSecret(id);
            // [段2] 端点推导——chat 端点 → 模型基址（去 /chat/completions）+ 站点根（去 /v1）
            string modelsUrl = "";
            string siteOrigin = "";
            DeriveProbeUrls(config.Endpoint, out modelsUrl, out siteOrigin);
            // [段3] 三探串行——各段独立成败与错误留痕
            DateTime startedAt = DateTime.Now;
            LlmProbeSection modelsSection = await ProbeModelsAsync(modelsUrl, apiKey);
            LlmProbeSection siteSection = await ProbeSiteAsync(siteOrigin);
            LlmProbeSection pricingSection = await ProbePricingAsync(siteOrigin);
            int elapsedMs = (int)(DateTime.Now - startedAt).TotalMilliseconds;
            // [段4] 报告组装与落盘日志
            string probedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            LogStore.Add("CatHome4", 1, "LLM API 池测试「" + config.DisplayName + "」：模型探 " + (modelsSection.Ok ? "通过" : "失败") + " / 站点探 " + (siteSection.Ok ? "通过" : "失败") + " / 定价探 " + (pricingSection.Ok ? "通过" : "失败"), "CONFIG");
            var report = new
            {
                ok = modelsSection.Ok,
                apiConfigId = id.ToString("D"),
                displayName = config.DisplayName,
                endpoint = config.Endpoint,
                defaultModel = config.DefaultModel,
                hasKey = apiKey.Length > 0,
                modelsUrl = modelsUrl,
                siteOrigin = siteOrigin,
                probedAt = probedAt,
                elapsedMs = elapsedMs,
                models = new { ok = modelsSection.Ok, httpStatus = modelsSection.HttpStatus, error = modelsSection.Error, payload = modelsSection.Payload },
                site = new { ok = siteSection.Ok, httpStatus = siteSection.HttpStatus, error = siteSection.Error, payload = siteSection.Payload },
                pricing = new { ok = pricingSection.Ok, httpStatus = pricingSection.HttpStatus, error = pricingSection.Error, payload = pricingSection.Payload }
            };
            return Results.Json(report);
        }

        /// <summary>
        /// 端点推导——池配置 endpoint → 模型清单 URL + 站点根 URL。
        /// 规则：先去尾 /chat/completions 得模型基址；站点根再去尾 /v1（new-api 站点面挂在根下）。
        /// </summary>
        /// <param name="endpoint">池配置 endpoint（base 或完整 chat 路径）</param>
        /// <param name="modelsUrl">出参：模型清单 URL</param>
        /// <param name="siteOrigin">出参：站点根 URL</param>
        private static void DeriveProbeUrls(string endpoint, out string modelsUrl, out string siteOrigin)
        {
            string trimmed = "";
            if (endpoint != null)
            {
                trimmed = endpoint.Trim().TrimEnd('/');
            }
            string baseUrl = trimmed;
            if (baseUrl.EndsWith("/chat/completions", StringComparison.Ordinal))
            {
                baseUrl = baseUrl.Substring(0, baseUrl.Length - "/chat/completions".Length);
            }
            modelsUrl = baseUrl + "/models";
            siteOrigin = baseUrl;
            if (siteOrigin.EndsWith("/v1", StringComparison.Ordinal))
            {
                siteOrigin = siteOrigin.Substring(0, siteOrigin.Length - "/v1".Length);
            }
        }

        /// <summary>
        /// 模型清单探针——GET /v1/models（带 Bearer Key）。ok = HTTP 200 且响应含 data 数组。
        /// </summary>
        /// <param name="url">模型清单 URL</param>
        /// <param name="apiKey">API Key（空则不带头）</param>
        /// <returns>分段结果（payload = 模型对象数组）</returns>
        private static async Task<LlmProbeSection> ProbeModelsAsync(string url, string apiKey)
        {
            LlmProbeSection section = NewProbeSection();
            LlmProbeHttpResponse response = await ProbeGetAsync(url, apiKey);
            section.HttpStatus = response.HttpStatus;
            if (response.Error.Length > 0)
            {
                section.Error = response.Error;
                return section;
            }
            if (response.HttpStatus != 200)
            {
                section.Error = "HTTP " + response.HttpStatus.ToString() + " " + TrimProbeBody(response.Body);
                return section;
            }
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(response.Body))
                {
                    JsonElement dataEl = GetProbeProperty(doc.RootElement, "data");
                    if (dataEl.ValueKind != JsonValueKind.Array)
                    {
                        section.Error = "响应无 data 数组";
                        return section;
                    }
                    List<object> items = new List<object>();
                    foreach (JsonElement item in dataEl.EnumerateArray())
                    {
                        items.Add(new
                        {
                            id = GetJsonString(item, "id"),
                            ownedBy = GetJsonString(item, "owned_by"),
                            endpointTypes = ConvertProbeJson(GetProbeProperty(item, "supported_endpoint_types"))
                        });
                    }
                    section.Payload = items;
                    section.Ok = true;
                }
            }
            catch (Exception ex)
            {
                section.Error = "响应非 JSON：" + ex.Message;
            }
            return section;
        }

        /// <summary>
        /// 站点信息探针——GET /api/status（免 Key）。公告段剔除（会话性内容不进报告）。
        /// </summary>
        /// <param name="origin">站点根 URL</param>
        /// <returns>分段结果（payload = 站点字段字典）</returns>
        private static async Task<LlmProbeSection> ProbeSiteAsync(string origin)
        {
            LlmProbeSection section = NewProbeSection();
            if (origin.Length == 0)
            {
                section.Error = "端点为空";
                return section;
            }
            LlmProbeHttpResponse response = await ProbeGetAsync(origin + "/api/status", "");
            section.HttpStatus = response.HttpStatus;
            if (response.Error.Length > 0)
            {
                section.Error = response.Error;
                return section;
            }
            if (response.HttpStatus != 200)
            {
                section.Error = "HTTP " + response.HttpStatus.ToString() + " " + TrimProbeBody(response.Body);
                return section;
            }
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(response.Body))
                {
                    JsonElement dataEl = GetProbeProperty(doc.RootElement, "data");
                    if (dataEl.ValueKind != JsonValueKind.Object)
                    {
                        section.Error = "响应无 data 对象";
                        return section;
                    }
                    Dictionary<string, object> fields = new Dictionary<string, object>(StringComparer.Ordinal);
                    foreach (JsonProperty prop in dataEl.EnumerateObject())
                    {
                        if (IsProbeSiteExcluded(prop.Name))
                        {
                            continue;
                        }
                        fields[prop.Name] = ConvertProbeJson(prop.Value);
                    }
                    section.Payload = fields;
                    section.Ok = true;
                }
            }
            catch (Exception ex)
            {
                section.Error = "响应非 JSON：" + ex.Message;
            }
            return section;
        }

        /// <summary>
        /// 定价与分组探针——GET /api/pricing（免 Key）。取 data（模型倍率）+ group_ratio + usable_group + vendors + supported_endpoint + auto_groups。
        /// </summary>
        /// <param name="origin">站点根 URL</param>
        /// <returns>分段结果（payload = 定价字段字典）</returns>
        private static async Task<LlmProbeSection> ProbePricingAsync(string origin)
        {
            LlmProbeSection section = NewProbeSection();
            if (origin.Length == 0)
            {
                section.Error = "端点为空";
                return section;
            }
            LlmProbeHttpResponse response = await ProbeGetAsync(origin + "/api/pricing", "");
            section.HttpStatus = response.HttpStatus;
            if (response.Error.Length > 0)
            {
                section.Error = response.Error;
                return section;
            }
            if (response.HttpStatus != 200)
            {
                section.Error = "HTTP " + response.HttpStatus.ToString() + " " + TrimProbeBody(response.Body);
                return section;
            }
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(response.Body))
                {
                    JsonElement root = doc.RootElement;
                    Dictionary<string, object> fields = new Dictionary<string, object>(StringComparer.Ordinal);
                    fields["models"] = ConvertProbeJson(GetProbeProperty(root, "data"));
                    fields["groupRatio"] = ConvertProbeJson(GetProbeProperty(root, "group_ratio"));
                    fields["usableGroup"] = ConvertProbeJson(GetProbeProperty(root, "usable_group"));
                    fields["vendors"] = ConvertProbeJson(GetProbeProperty(root, "vendors"));
                    fields["supportedEndpoint"] = ConvertProbeJson(GetProbeProperty(root, "supported_endpoint"));
                    fields["autoGroups"] = ConvertProbeJson(GetProbeProperty(root, "auto_groups"));
                    section.Payload = fields;
                    section.Ok = true;
                }
            }
            catch (Exception ex)
            {
                section.Error = "响应非 JSON：" + ex.Message;
            }
            return section;
        }

        /// <summary>
        /// 单次 GET 探针——带可选 Bearer 头；请求级超时；传输异常按类型名与消息留痕。
        /// </summary>
        /// <param name="url">目标 URL</param>
        /// <param name="apiKey">API Key（空则不带头）</param>
        /// <returns>HTTP 结果（状态码 / 响应体 / 异常）</returns>
        private static async Task<LlmProbeHttpResponse> ProbeGetAsync(string url, string apiKey)
        {
            LlmProbeHttpResponse result = new LlmProbeHttpResponse();
            result.HttpStatus = 0;
            result.Body = "";
            result.Error = "";
            if (url.Length == 0)
            {
                result.Error = "URL 为空";
                return result;
            }
            try
            {
                using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    if (apiKey.Length > 0)
                    {
                        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + apiKey);
                    }
                    using (CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(LlmProbeTimeoutSeconds)))
                    {
                        using (HttpResponseMessage response = await LlmProbeClient.SendAsync(request, cts.Token))
                        {
                            result.HttpStatus = (int)response.StatusCode;
                            result.Body = await response.Content.ReadAsStringAsync();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                result.Error = ex.GetType().Name + "|" + ex.Message;
            }
            return result;
        }

        /// <summary>
        /// 站点字段剔除判定——公告类字段不进测试报告。
        /// </summary>
        /// <param name="name">字段名</param>
        /// <returns>true=剔除</returns>
        private static bool IsProbeSiteExcluded(string name)
        {
            for (int i = 0; i < LlmProbeSiteExcludes.Length; i = i + 1)
            {
                if (string.Equals(LlmProbeSiteExcludes[i], name, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// JSON 元素 → 通用对象（字典 / 列表 / 字符串 / 双精度 / 布尔 / null）——脱离 JsonDocument 生命周期。
        /// </summary>
        /// <param name="element">JSON 元素</param>
        /// <returns>通用对象（Undefined 与 Null 一律 null）</returns>
        private static object ConvertProbeJson(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                Dictionary<string, object> map = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (JsonProperty prop in element.EnumerateObject())
                {
                    map[prop.Name] = ConvertProbeJson(prop.Value);
                }
                return map;
            }
            if (element.ValueKind == JsonValueKind.Array)
            {
                List<object> list = new List<object>();
                foreach (JsonElement item in element.EnumerateArray())
                {
                    list.Add(ConvertProbeJson(item));
                }
                return list;
            }
            if (element.ValueKind == JsonValueKind.String)
            {
                string text = element.GetString();
                if (text == null)
                {
                    return "";
                }
                return text;
            }
            if (element.ValueKind == JsonValueKind.Number)
            {
                double number;
                if (element.TryGetDouble(out number))
                {
                    return number;
                }
                return element.GetRawText();
            }
            if (element.ValueKind == JsonValueKind.True)
            {
                return true;
            }
            if (element.ValueKind == JsonValueKind.False)
            {
                return false;
            }
            return null;
        }

        /// <summary>
        /// 读取 JSON 属性——缺字段返回 Undefined 元素（防御式）。
        /// </summary>
        /// <param name="obj">JSON 对象</param>
        /// <param name="name">属性名</param>
        /// <returns>属性元素；缺失 Undefined</returns>
        private static JsonElement GetProbeProperty(JsonElement obj, string name)
        {
            JsonElement value;
            if (obj.TryGetProperty(name, out value))
            {
                return value;
            }
            return default(JsonElement);
        }

        /// <summary>
        /// 截断响应体——失败摘要用（防大响应刷屏）。
        /// </summary>
        /// <param name="body">响应体原文</param>
        /// <returns>至多 200 字符的摘要</returns>
        private static string TrimProbeBody(string body)
        {
            if (body == null)
            {
                return "";
            }
            string flat = body.Replace("\r", " ").Replace("\n", " ");
            if (flat.Length <= 200)
            {
                return flat;
            }
            return flat.Substring(0, 200) + "…";
        }

        /// <summary>
        /// 新建空白分段结果。
        /// </summary>
        /// <returns>未探测状态的分段结果</returns>
        private static LlmProbeSection NewProbeSection()
        {
            LlmProbeSection section = new LlmProbeSection();
            section.Ok = false;
            section.HttpStatus = 0;
            section.Error = "";
            section.Payload = null;
            return section;
        }

        /// <summary>
        /// 探针分段结果——成败 + 状态码 + 错误 + 结构化载荷。
        /// </summary>
        private sealed class LlmProbeSection
        {
            /// <summary>本段是否可用（HTTP 200 且响应形态符合预期）</summary>
            public bool Ok { get; set; }

            /// <summary>HTTP 状态码（0 = 传输异常未取到响应）</summary>
            public int HttpStatus { get; set; }

            /// <summary>失败原因（成功时为空串）</summary>
            public string Error { get; set; }

            /// <summary>结构化载荷（模型段 = 数组 / 站点段与定价段 = 字典）</summary>
            public object Payload { get; set; }
        }

        /// <summary>
        /// 探针 HTTP 原始结果——状态码 + 响应体 + 传输异常。
        /// </summary>
        private sealed class LlmProbeHttpResponse
        {
            /// <summary>HTTP 状态码（0 = 传输异常）</summary>
            public int HttpStatus { get; set; }

            /// <summary>响应体原文</summary>
            public string Body { get; set; }

            /// <summary>传输异常描述（空 = 无异常）</summary>
            public string Error { get; set; }
        }
    }
}

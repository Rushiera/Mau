using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Mau.Providers
{
    /// <summary>
    /// LLM 端点探针——单站健康探测：发一条低 token 请求并要求模型原样返回哨兵词（PONG）。
    /// 判据：HTTP 200 且返回文本含哨兵词 = 可用；超时 / 传输失败 / 非预期内容 = 不可用（false）。
    /// 消费方：DeepSeekLlmRuntime 的故障转移决策——同时探「当前站」与「候选站」，
    /// 当前站不可用且候选站可用才切换（避免把单站故障误判成链路故障）。
    /// </summary>
    public static class LlmEndpointProbe
    {
        /// <summary>哨兵词——探针要求模型原样返回，用于判定「响应内容是否可信」（非预期内容 = 不可用）。</summary>
        public const string ExpectToken = "PONG";

        /// <summary>探针超时（秒）——与「请求发出 60 秒无回应即判异常」同口径。</summary>
        public const int TimeoutSeconds = 60;

        /// <summary>
        /// 探测一个端点是否可用——低 token 请求 + 哨兵词判据（探针自身不抛异常，一律以布尔回答）。
        /// </summary>
        /// <param name="endpoint">完整对话端点（含 /chat/completions）</param>
        /// <param name="apiKey">API Key（空=不带鉴权头）</param>
        /// <param name="model">模型名（空=请求体不传 model）</param>
        /// <param name="ct">取消令牌（用户暂停时随之中止）</param>
        /// <returns>true=返回了预期内容；false=超时 / 失败 / 非预期</returns>
        public static async Task<bool> ProbeAsync(string endpoint, string apiKey, string model, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(endpoint))
            {
                return false;
            }
            string body = BuildProbeBody(model);
            using (HttpClient client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(TimeoutSeconds);
                using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, endpoint))
                {
                    if (!string.IsNullOrEmpty(apiKey))
                    {
                        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + apiKey);
                    }
                    request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                    try
                    {
                        using (HttpResponseMessage response = await client.SendAsync(request, ct))
                        {
                            if (!response.IsSuccessStatusCode)
                            {
                                return false;
                            }
                            string raw = await response.Content.ReadAsStringAsync(ct);
                            return ContainsExpectToken(raw);
                        }
                    }
                    catch (Exception)
                    {
                        // 传输失败 / 超时 / 取消——探针只回答「可用与否」，不向调用方抛错（取消语义由调用方判定）
                        return false;
                    }
                }
            }
        }

        /// <summary>
        /// 构造探针请求体——单条 user 消息 + max_tokens 上限（低 token 成本；非流式便于一次性判定）。
        /// </summary>
        /// <param name="model">模型名（空=不传）</param>
        /// <returns>请求体 JSON</returns>
        private static string BuildProbeBody(string model)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{\"messages\":[{\"role\":\"user\",\"content\":\"Reply with exactly one word: ");
            sb.Append(ExpectToken);
            sb.Append("\"}],\"max_tokens\":8,\"stream\":false");
            if (!string.IsNullOrEmpty(model))
            {
                sb.Append(",\"model\":\"");
                sb.Append(model.Replace("\\", "\\\\").Replace("\"", "\\\""));
                sb.Append("\"");
            }
            sb.Append("}");
            return sb.ToString();
        }

        /// <summary>
        /// 响应体是否含哨兵词——非 JSON / 缺 choices / 缺 content 一律视为不可用。
        /// </summary>
        /// <param name="raw">响应体原文</param>
        /// <returns>true=choices[0].message.content 含哨兵词</returns>
        private static bool ContainsExpectToken(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return false;
            }
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(raw))
                {
                    JsonElement choices;
                    if (!doc.RootElement.TryGetProperty("choices", out choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
                    {
                        return false;
                    }
                    JsonElement message;
                    if (!choices[0].TryGetProperty("message", out message))
                    {
                        return false;
                    }
                    JsonElement content;
                    if (!message.TryGetProperty("content", out content) || content.ValueKind != JsonValueKind.String)
                    {
                        return false;
                    }
                    string? text = content.GetString();
                    if (text == null)
                    {
                        return false;
                    }
                    return text.IndexOf(ExpectToken, StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }
}

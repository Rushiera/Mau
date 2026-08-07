using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

namespace Mau.Runtime
{
    /// <summary>
    /// LLM 请求桥（程序级）——HTTP 基础程序级 + 配置经 DataBox scope 存储（BRIK 唯一数据协议）
    /// </summary>
    public static class LlmBridge
    {
        /// <summary>
        /// 共享 HTTP 客户端——复用 TCP 连接池（程序级）
        /// </summary>
        public static readonly HttpClient Http = new HttpClient();

        /// <summary>
        /// 当前 API Key——DataBox scope "llm"
        /// </summary>
        public static string ApiKey
        {
            get
            {
                string key;
                if (DataBox.TryGet<string>("llm", "apiKey", out key))
                {
                    return key;
                }
                return "";
            }
        }

        /// <summary>
        /// 当前端点——DataBox scope "llm"
        /// </summary>
        public static string Endpoint
        {
            get
            {
                string endpoint;
                if (DataBox.TryGet<string>("llm", "endpoint", out endpoint) && endpoint.Length > 0)
                {
                    return endpoint;
                }
                return "https://api.deepseek.com/v1/chat/completions";
            }
        }

        /// <summary>
        /// 当前超时——DataBox scope "llm"
        /// </summary>
        public static TimeSpan Timeout
        {
            get
            {
                int seconds;
                if (DataBox.TryGet<int>("llm", "timeoutSeconds", out seconds) && seconds > 0)
                {
                    return TimeSpan.FromSeconds(seconds);
                }
                return TimeSpan.FromMinutes(3);
            }
        }

        /// <summary>
        /// 配置 API Key——宿主启动时调用
        /// </summary>
        /// <param name="apiKey">API Key</param>
        public static void ConfigureApiKey(string apiKey)
        {
            DataBox.Set<string>("llm", "apiKey", apiKey == null ? "" : apiKey.Trim());
        }

        /// <summary>
        /// 配置端点和超时
        /// </summary>
        /// <param name="endpoint">Chat Completions 端点</param>
        /// <param name="timeoutSeconds">超时秒数</param>
        public static void ConfigureEndpoint(string endpoint, int timeoutSeconds)
        {
            if (!string.IsNullOrWhiteSpace(endpoint))
            {
                DataBox.Set<string>("llm", "endpoint", endpoint);
            }
            if (timeoutSeconds > 0)
            {
                DataBox.Set<int>("llm", "timeoutSeconds", timeoutSeconds);
            }
        }

        /// <summary>
        /// 文本规范化——转发 BrickText.SafeText（积木文本兼容入口）
        /// </summary>
        /// <param name="value">原始文本</param>
        /// <returns>非空文本</returns>
        public static string SafeText(string? value)
        {
            return BrickText.SafeText(value);
        }

        /// <summary>
        /// POST JSON 并读取响应
        /// </summary>
        /// <param name="json">请求 JSON</param>
        /// <returns>响应 JSON</returns>
        public static string PostJson(string json)
        {
            using HttpRequestMessage message = new HttpRequestMessage(
                HttpMethod.Post, Endpoint);
            message.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer", ApiKey);
            message.Content = new StringContent(json, Encoding.UTF8,
                "application/json");
            using HttpResponseMessage response = Http
                .Send(message, System.Net.Http.HttpCompletionOption.ResponseContentRead);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException("HTTP "
                    + ((int)response.StatusCode).ToString());
            }
            return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// 把 HTTP 状态映射为稳定错误码
        /// </summary>
        /// <param name="status">HTTP 状态</param>
        /// <returns>稳定错误码</returns>
        public static string MapHttpError(HttpStatusCode status)
        {
            if (status == HttpStatusCode.Unauthorized
                || status == HttpStatusCode.Forbidden)
            {
                return "LLM_AUTH_FAILED";
            }
            if ((int)status == 429)
            {
                return "LLM_RATE_LIMITED";
            }
            if ((int)status >= 500)
            {
                return "LLM_REMOTE_UNAVAILABLE";
            }
            return "LLM_HTTP_ERROR";
        }
    }
}

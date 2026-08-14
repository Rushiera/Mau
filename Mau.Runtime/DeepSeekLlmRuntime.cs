using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Mau.Runtime
{
    /// <summary>
    /// DeepSeek 非流式适配器——内建最小实现（OpenAI 兼容 /chat/completions）。
    /// 第一轮固定写死：宿主构造时注入 baseUrl/apiKey/model；配置系统随 OS 能力阶段立项。
    /// </summary>
    public sealed class DeepSeekLlmRuntime : ILlmRuntime
    {
        /// <summary>
        /// HTTP 客户端——每实例独立（超时互不干扰）
        /// </summary>
        private readonly HttpClient _client;

        /// <summary>
        /// API 基址——如 https://api.deepseek.com
        /// </summary>
        private readonly string _baseUrl;

        /// <summary>
        /// API 密钥
        /// </summary>
        private readonly string _apiKey;

        /// <summary>
        /// 模型名
        /// </summary>
        private readonly string _model;

        /// <summary>
        /// 建立非流式适配器
        /// </summary>
        /// <param name="baseUrl">API 基址</param>
        /// <param name="apiKey">API 密钥</param>
        /// <param name="model">模型名</param>
        public DeepSeekLlmRuntime(string baseUrl, string apiKey, string model)
        {
            _baseUrl = baseUrl == null ? "" : baseUrl;
            _apiKey = apiKey == null ? "" : apiKey;
            _model = model == null ? "" : model;
            _client = new HttpClient();
            _client.Timeout = TimeSpan.FromSeconds(60);
        }

        /// <summary>
        /// 非流式完成——POST /chat/completions，取 choices[0].message.content。
        /// </summary>
        /// <param name="system">系统提示词</param>
        /// <param name="content">用户内容</param>
        /// <param name="reply">回复——失败时携带 ERR| 错误文本</param>
        /// <returns>true=成功</returns>
        public bool Completions(string system, string content, out string reply)
        {
            try
            {
                // [段1] 构造 OpenAI 兼容请求体并发送
                string body = BuildRequestBody(system, content);
                using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, _baseUrl.TrimEnd('/') + "/chat/completions"))
                {
                    request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _apiKey);
                    request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                    using (HttpResponseMessage response = _client.Send(request))
                    {
                        string raw = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                        // [段2] HTTP 层失败——状态码进错误文本
                        if (!response.IsSuccessStatusCode)
                        {
                            reply = "ERR|HTTP_" + ((int)response.StatusCode).ToString() + "|" + TrimText(raw, 200);
                            return false;
                        }
                        // [段3] 业务层解析——choices[0].message.content
                        reply = ParseReply(raw);
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                reply = "ERR|" + ex.GetType().Name + "|" + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 构造 OpenAI 兼容请求体——零依赖 JSON 序列化
        /// </summary>
        /// <param name="system">系统提示词</param>
        /// <param name="content">用户内容</param>
        /// <returns>请求体 JSON</returns>
        private string BuildRequestBody(string system, string content)
        {
            object[] messages = new object[]
            {
                new { role = "system", content = system },
                new { role = "user", content = content }
            };
            object payload = new { model = _model, messages = messages, stream = false };
            return JsonSerializer.Serialize(payload);
        }

        /// <summary>
        /// 解析回复——防御式逐层检查
        /// </summary>
        /// <param name="raw">响应 JSON</param>
        /// <returns>回复文本或 ERR| 错误文本</returns>
        private static string ParseReply(string raw)
        {
            using (JsonDocument doc = JsonDocument.Parse(raw))
            {
                JsonElement root = doc.RootElement;
                JsonElement choices;
                if (root.TryGetProperty("choices", out choices) && choices.GetArrayLength() > 0)
                {
                    JsonElement first = choices[0];
                    JsonElement message;
                    if (first.TryGetProperty("message", out message))
                    {
                        JsonElement content;
                        if (message.TryGetProperty("content", out content))
                        {
                            string? text = content.GetString();
                            if (text != null)
                            {
                                return text;
                            }
                        }
                    }
                }
                return "ERR|EMPTY_RESPONSE|响应无 choices[0].message.content";
            }
        }

        /// <summary>
        /// 截断错误详情——防止超长错误文本刷屏
        /// </summary>
        /// <param name="text">原文</param>
        /// <param name="max">上限</param>
        /// <returns>截断文本</returns>
        private static string TrimText(string text, int max)
        {
            if (text == null)
            {
                return "";
            }
            if (text.Length <= max)
            {
                return text;
            }
            return text.Substring(0, max);
        }
    }
}

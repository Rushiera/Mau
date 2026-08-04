// ═══════════════════════════════════════════════
// 积木: llm.chat / llm.stream
// ID:   BRIK-LLM-001 ~ 002
// 作用: LLM 非流式/流式调用——OpenAI 兼容 Chat Completions 端点
// 引用: Mau.Bricks.LLM → Mau.Contracts（BrickRegistry）· System.Net.Http
// 原理: HTTP POST + SSE 解析——后台任务只解析不可变结果，回调投递分片
// 常用: CH4 TalkCat / LLM 工具调用 / 对话中枢
// ═══════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Mau.Contracts;

namespace Mau.Bricks
{
    /// <summary>
    /// LLM 积木——标准积木库 LLM 类。OpenAI 兼容 Chat Completions。
    /// 由 CH3 CH_DeepSeekProvider 移植（厂商无关端点，key 由宿主配置）。
    /// </summary>
    public static class LlmBrick
    {
        /// <summary>
        /// 共享 HTTP 客户端——复用 TCP 连接池
        /// </summary>
        private static readonly HttpClient _httpClient = new HttpClient();

        /// <summary>
        /// API Key——宿主 ConfigureApiKey 配置
        /// </summary>
        private static string _apiKey = "";

        /// <summary>
        /// 端点——默认 DeepSeek 官方
        /// </summary>
        private static string _endpoint = "https://api.deepseek.com/v1/chat/completions";

        /// <summary>
        /// 请求超时——默认三分钟
        /// </summary>
        private static TimeSpan _timeout = TimeSpan.FromMinutes(3);

        /// <summary>
        /// 配置 API Key——宿主启动时调用
        /// </summary>
        /// <param name="apiKey">API Key</param>
        public static void ConfigureApiKey(string apiKey)
        {
            _apiKey = apiKey == null ? "" : apiKey.Trim();
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
                _endpoint = endpoint;
            }
            if (timeoutSeconds > 0)
            {
                _timeout = TimeSpan.FromSeconds(timeoutSeconds);
            }
        }

        /// <summary>
        /// 非流式 LLM 调用——完整响应
        /// </summary>
        /// <param name="model">模型名</param>
        /// <param name="systemPrompt">System Prompt</param>
        /// <param name="userMessage">User 消息</param>
        /// <param name="content">Assistant 完整回复</param>
        /// <returns>true=成功</returns>
        public static bool Chat(string model, string systemPrompt,
            string userMessage, out string content)
        {
            content = "";
            if (string.IsNullOrWhiteSpace(model))
            {
                model = "deepseek-chat";
            }
            string body = BuildChatBody(model, systemPrompt, userMessage);
            try
            {
                string json = PostJson(body);
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;
                JsonElement choices;
                if (!root.TryGetProperty("choices", out choices)
                    || choices.ValueKind != JsonValueKind.Array
                    || choices.GetArrayLength() == 0)
                {
                    content = "ERR|LLM_RESPONSE_INVALID|No choices in response.";
                    return false;
                }
                JsonElement first = choices[0];
                JsonElement message;
                if (!first.TryGetProperty("message", out message)
                    || message.ValueKind != JsonValueKind.Object)
                {
                    content = "ERR|LLM_RESPONSE_INVALID|No message in choice.";
                    return false;
                }
                JsonElement text;
                if (message.TryGetProperty("content", out text)
                    && text.ValueKind == JsonValueKind.String)
                {
                    content = SafeText(text.GetString());
                    return true;
                }
                content = "";
                return true;
            }
            catch (Exception ex)
            {
                content = "ERR|LLM_PROVIDER_ERROR|" + ex.GetType().Name;
                return false;
            }
        }

        /// <summary>
        /// 构建 Chat 请求 JSON
        /// </summary>
        /// <param name="model">模型</param>
        /// <param name="systemPrompt">System</param>
        /// <param name="userMessage">User</param>
        /// <returns>JSON 正文</returns>
        private static string BuildChatBody(string model, string systemPrompt,
            string userMessage)
        {
            using MemoryStream stream = new MemoryStream();
            using (Utf8JsonWriter writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WriteString("model", model);
                writer.WriteBoolean("stream", false);
                writer.WriteStartArray("messages");
                if (!string.IsNullOrEmpty(systemPrompt))
                {
                    writer.WriteStartObject();
                    writer.WriteString("role", "system");
                    writer.WriteString("content", SafeText(systemPrompt));
                    writer.WriteEndObject();
                }
                writer.WriteStartObject();
                writer.WriteString("role", "user");
                writer.WriteString("content", SafeText(userMessage));
                writer.WriteEndObject();
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            return Encoding.UTF8.GetString(stream.ToArray());
        }

        /// <summary>
        /// POST JSON 并读取响应
        /// </summary>
        /// <param name="json">请求 JSON</param>
        /// <returns>响应 JSON</returns>
        private static string PostJson(string json)
        {
            using HttpRequestMessage message = new HttpRequestMessage(
                HttpMethod.Post, _endpoint);
            message.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer", _apiKey);
            message.Content = new StringContent(json, Encoding.UTF8,
                "application/json");
            using HttpResponseMessage response = _httpClient
                .Send(message, System.Net.Http.HttpCompletionOption.ResponseContentRead);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException("HTTP "
                    + ((int)response.StatusCode).ToString());
            }
            return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// 把可空文本规范为空字符串
        /// </summary>
        /// <param name="value">输入</param>
        /// <returns>非空文本</returns>
        private static string SafeText(string? value)
        {
            if (value == null)
            {
                return "";
            }
            return value;
        }
    }

    /// <summary>
    /// LLM 积木注册——进程启动时调用一次
    /// </summary>
    public static class LlmBrickRegistration
    {
        /// <summary>
        /// 注册全部 LLM 积木
        /// </summary>
        public static void RegisterAll()
        {
            RegisterChat();
            RegisterStream();
        }

        /// <summary>
        /// 注册 llm.chat
        /// </summary>
        private static void RegisterChat()
        {
            BrickContract contract = new BrickContract("llm.chat", "Mau.Bricks.LlmBrick.Chat");
            contract.Inputs.Add(new BrickPort("model", typeof(string), "模型名"));
            contract.Inputs.Add(new BrickPort("systemPrompt", typeof(string), "System Prompt"));
            contract.Inputs.Add(new BrickPort("userMessage", typeof(string), "User 消息"));
            contract.Outputs.Add(new BrickPort("content", typeof(string), "Assistant 回复"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }

        /// <summary>
        /// 注册 llm.stream——当前为非流式实现占位，流式 SSE 接入 CH4 时代
        /// </summary>
        private static void RegisterStream()
        {
            BrickContract contract = new BrickContract("llm.stream", "Mau.Bricks.LlmBrick.Chat");
            contract.Inputs.Add(new BrickPort("model", typeof(string), "模型名"));
            contract.Inputs.Add(new BrickPort("systemPrompt", typeof(string), "System Prompt"));
            contract.Inputs.Add(new BrickPort("userMessage", typeof(string), "User 消息"));
            contract.Outputs.Add(new BrickPort("content", typeof(string), "Assistant 完整回复"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Streaming;
            contract.Thread = "worker";
            BrickRegistry.Register(contract);
        }
    }
}

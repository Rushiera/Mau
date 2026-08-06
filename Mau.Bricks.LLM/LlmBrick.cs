// ═══════════════════════════════════════════════
// 积木: llm.chat / llm.stream
// ID:   BRIK-LLM-001 ~ 002
// 作用: LLM 非流式/流式调用——OpenAI 兼容 Chat Completions 端点
// 引用: Mau.Bricks.LLM → Mau.Contracts（BrickRegistry）· System.Net.Http
// 依赖: System.Net.Http
// 原理: HTTP POST + SSE 解析——后台任务只解析不可变结果，回调投递分片
// 常用: CH4 TalkCat / LLM 工具调用 / 对话中枢
// ═══════════════════════════════════════════════
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
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
        /// 流式会话表——requestId → 后台会话
        /// </summary>
        private static readonly ConcurrentDictionary<string, LlmStreamSession> _sessions = new ConcurrentDictionary<string, LlmStreamSession>();

        /// <summary>
        /// 流式会话序号——requestId 生成源
        /// </summary>
        private static int _sessionSeq = 0;

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

        /// <summary>
        /// 流式 LLM 调用——启动后台 SSE 请求，分片经 ReadChunk 消费
        /// </summary>
        /// <param name="model">模型名</param>
        /// <param name="systemPrompt">System Prompt</param>
        /// <param name="userMessage">User 消息</param>
        /// <param name="toolsJson">工具定义 JSON 数组（空=无工具）</param>
        /// <param name="requestId">流式会话 ID——ReadChunk/Finish 用</param>
        /// <returns>true=启动成功</returns>
        public static bool Stream(string model, string systemPrompt,
            string userMessage, string toolsJson, out string requestId)
        {
            requestId = "";
            if (string.IsNullOrWhiteSpace(model))
            {
                model = "deepseek-chat";
            }
            string id = "LLM_" + Interlocked.Increment(ref _sessionSeq).ToString();
            LlmStreamSession session = new LlmStreamSession(_timeout);
            if (!_sessions.TryAdd(id, session))
            {
                session.Cancel.Dispose();
                return false;
            }
            requestId = id;
            Task worker = RunStreamAsync(id, model, systemPrompt,
                userMessage, toolsJson, session);
            session.Worker = worker;
            return true;
        }

        /// <summary>
        /// 消费流式分片——无分片返回 false（调用方稍后重试）
        /// </summary>
        /// <param name="requestId">流式会话 ID</param>
        /// <param name="contentDelta">正文增量</param>
        /// <param name="reasoningDelta">推理增量</param>
        /// <param name="toolCallsJson">工具调用 JSON（终态时携带聚合结果）</param>
        /// <param name="finished">终态标志——true=流结束</param>
        /// <param name="errorCode">错误码——终态且失败时非空</param>
        /// <returns>true=取到分片；false=暂无可取或会话不存在</returns>
        public static bool ReadChunk(string requestId, out string contentDelta,
            out string reasoningDelta, out string toolCallsJson,
            out bool finished, out string errorCode)
        {
            contentDelta = "";
            reasoningDelta = "";
            toolCallsJson = "";
            finished = false;
            errorCode = "";
            LlmStreamSession? session;
            if (!_sessions.TryGetValue(SafeText(requestId), out session)
                || session == null)
            {
                return false;
            }
            LlmStreamChunk? chunk;
            if (!session.Chunks.TryDequeue(out chunk) || chunk == null)
            {
                return false;
            }
            session.LastChunk = chunk;
            contentDelta = chunk.ContentDelta;
            reasoningDelta = chunk.ReasoningDelta;
            toolCallsJson = chunk.ToolCallsJson;
            finished = chunk.Finished;
            errorCode = chunk.ErrorCode;
            return true;
        }

        /// <summary>
        /// 判断最后消费分片是否终态——返回 true=终态（流结束）；false=非终态（含查询失败）
        /// </summary>
        /// <param name="requestId">流式会话 ID</param>
        /// <param name="ended">终态标志</param>
        /// <param name="errorCode">终态且失败时非空</param>
        /// <returns>true=最后分片是终态</returns>
        public static bool IsEnd(string requestId, out bool ended, out string errorCode)
        {
            ended = false;
            errorCode = "";
            LlmStreamSession? session;
            if (!_sessions.TryGetValue(SafeText(requestId), out session)
                || session == null)
            {
                return false;
            }
            LlmStreamChunk? last = session.LastChunk;
            if (last == null)
            {
                return false;
            }
            ended = last.Finished;
            errorCode = last.ErrorCode;
            return ended;
        }

        /// <summary>
        /// 判断最后消费分片是否含工具调用——返回 true=含工具；false=不含（含查询失败）
        /// </summary>
        /// <param name="requestId">流式会话 ID</param>
        /// <param name="isTool">是否含工具调用</param>
        /// <param name="toolCallsJson">工具调用 JSON（含工具时输出）</param>
        /// <returns>true=最后分片含工具调用</returns>
        public static bool IsTool(string requestId, out bool isTool, out string toolCallsJson)
        {
            isTool = false;
            toolCallsJson = "";
            LlmStreamSession? session;
            if (!_sessions.TryGetValue(SafeText(requestId), out session)
                || session == null)
            {
                return false;
            }
            LlmStreamChunk? last = session.LastChunk;
            if (last == null)
            {
                return false;
            }
            isTool = last.ToolCallsJson.Length > 0;
            toolCallsJson = last.ToolCallsJson;
            return isTool;
        }
/// <summary>
/// 判断最后消费分片是否带 LLM 错误——返回=判断结果（true=有错误；false=无错误或不可判）
/// 语义与 is_end/is_tool 同款：错误码是 LLM 的正常返回值，业务层消费（终止+回滚），非框架级错误
/// </summary>
/// <param name = "requestId">流式会话 ID</param>
/// <param name = "hasError">是否带错误码</param>
/// <param name = "errorCode">错误码（有错误时输出）</param>
/// <returns>true=最后分片带错误码</returns>
public static bool HasError(string requestId, out bool hasError, out string errorCode)
{
    hasError = false;
    errorCode = "";
    LlmStreamSession? session;
    if (!_sessions.TryGetValue(SafeText(requestId), out session) || session == null)
    {
        return false;
    }

    LlmStreamChunk? last = session.LastChunk;
    if (last == null)
    {
        return false;
    }

    hasError = last.ErrorCode.Length > 0;
    errorCode = last.ErrorCode;
    return hasError;
}
        /// <summary>
        /// 终止流式会话——取消请求并清理资源
        /// </summary>
        /// <param name="requestId">流式会话 ID</param>
        /// <returns>true=找到并终止</returns>
        public static bool Finish(string requestId)
        {
            LlmStreamSession? session;
            if (!_sessions.TryRemove(SafeText(requestId), out session)
                || session == null)
            {
                return false;
            }
            session.Cancel.Cancel();
            Task? worker = session.Worker;
            if (worker != null)
            {
                try
                {
                    worker.Wait(2000);
                }
                catch (AggregateException)
                {
                    // 后台任务异常已入队终态分片——等待超时不影响清理
                }
            }
            session.Cancel.Dispose();
            return true;
        }

        /// <summary>
        /// 结构化流式 LLM 调用——messagesJson 消息数组（OpenAI 协议全角色：system/user/assistant/tool）
        /// 过程积木（C2）：结构化回填的请求端——ctx_build_messages_json 导出 → completions 消费
        /// </summary>
        /// <param name="model">模型名</param>
        /// <param name="messagesJson">messages 数组 JSON</param>
        /// <param name="toolsJson">工具定义 JSON 数组（空=无工具）</param>
        /// <param name="requestId">流式会话 ID——ReadChunk/Finish 用</param>
        /// <returns>true=启动成功</returns>
        public static bool Completions(string model, string messagesJson,
            string toolsJson, out string requestId)
        {
            requestId = "";
            if (string.IsNullOrWhiteSpace(model))
            {
                model = "deepseek-chat";
            }
            if (string.IsNullOrWhiteSpace(messagesJson))
            {
                return false;
            }
            string id = "LLM_" + Interlocked.Increment(ref _sessionSeq).ToString();
            LlmStreamSession session = new LlmStreamSession(_timeout);
            if (!_sessions.TryAdd(id, session))
            {
                session.Cancel.Dispose();
                return false;
            }
            requestId = id;
            Task worker = RunCompletionsAsync(id, model, messagesJson,
                toolsJson, session);
            session.Worker = worker;
            return true;
        }

        /// <summary>
        /// 执行结构化流式 HTTP 请求并解析 SSE——分片写入会话队列
        /// </summary>
        /// <param name="requestId">会话 ID</param>
        /// <param name="model">模型名</param>
        /// <param name="messagesJson">messages 数组 JSON</param>
        /// <param name="toolsJson">工具定义 JSON</param>
        /// <param name="session">会话</param>
        /// <returns>异步工作</returns>
        private static async Task RunCompletionsAsync(string requestId,
            string model, string messagesJson, string toolsJson,
            LlmStreamSession session)
        {
            try
            {
                string apiKey = _apiKey;
                if (apiKey.Length == 0)
                {
                    PushTerminal(session, "LLM_CREDENTIAL_MISSING",
                        "LLM credential is not configured.", "");
                    return;
                }
                using (HttpRequestMessage message = BuildCompletionsRequest(model,
                    messagesJson, toolsJson, apiKey))
                using (HttpResponseMessage response = await _httpClient
                    .SendAsync(message, HttpCompletionOption.ResponseHeadersRead,
                        session.Cancel.Token).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        string code = MapHttpError(response.StatusCode);
                        PushTerminal(session, code, "LLM returned HTTP "
                            + ((int)response.StatusCode).ToString() + ".", "");
                        return;
                    }
                    using (Stream stream = await response.Content
                        .ReadAsStreamAsync(session.Cancel.Token).ConfigureAwait(false))
                    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8,
                        true, 4096, false))
                    {
                        await ReadStreamEventsAsync(reader, session)
                            .ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                PushTerminal(session, "LLM_TIMEOUT",
                    "LLM request timed out or cancelled.", "");
            }
            catch (HttpRequestException)
            {
                PushTerminal(session, "LLM_NETWORK_ERROR",
                    "LLM network request failed.", "");
            }
            catch (JsonException)
            {
                PushTerminal(session, "LLM_RESPONSE_INVALID",
                    "LLM returned invalid JSON.", "");
            }
            catch (Exception exception)
            {
                PushTerminal(session, "LLM_PROVIDER_ERROR",
                    exception.GetType().Name + " occurred in LLM provider.", "");
            }
        }

        /// <summary>
        /// 构建结构化流式 HTTP 请求
        /// </summary>
        /// <param name="model">模型名</param>
        /// <param name="messagesJson">messages 数组 JSON</param>
        /// <param name="toolsJson">工具定义 JSON</param>
        /// <param name="apiKey">API Key</param>
        /// <returns>HTTP 请求</returns>
        private static HttpRequestMessage BuildCompletionsRequest(string model,
            string messagesJson, string toolsJson, string apiKey)
        {
            HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Post,
                _endpoint);
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer",
                apiKey);
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(
                "text/event-stream"));
            message.Content = new StringContent(BuildCompletionsBody(model,
                messagesJson, toolsJson), Encoding.UTF8, "application/json");
            return message;
        }

        /// <summary>
        /// 构建结构化流式请求 JSON 正文——messages 数组直接透传（OpenAI 协议）
        /// </summary>
        /// <param name="model">模型名</param>
        /// <param name="messagesJson">messages 数组 JSON</param>
        /// <param name="toolsJson">工具定义 JSON 数组</param>
        /// <returns>JSON 正文</returns>
        private static string BuildCompletionsBody(string model,
            string messagesJson, string toolsJson)
        {
            using MemoryStream stream = new MemoryStream();
            using (Utf8JsonWriter writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WriteString("model", model);
                writer.WriteBoolean("stream", true);
                writer.WritePropertyName("messages");
                using (JsonDocument messages = JsonDocument.Parse(messagesJson))
                {
                    messages.RootElement.WriteTo(writer);
                }
                if (!string.IsNullOrWhiteSpace(toolsJson))
                {
                    writer.WritePropertyName("tools");
                    using (JsonDocument tools = JsonDocument.Parse(toolsJson))
                    {
                        tools.RootElement.WriteTo(writer);
                    }
                }
                writer.WriteEndObject();
            }
            return Encoding.UTF8.GetString(stream.ToArray());
        }

        /// <summary>
        /// 执行流式 HTTP 请求并解析 SSE——分片写入会话队列
        /// </summary>
        /// <param name="requestId">会话 ID</param>
        /// <param name="model">模型名</param>
        /// <param name="systemPrompt">System Prompt</param>
        /// <param name="userMessage">User 消息</param>
        /// <param name="toolsJson">工具定义 JSON</param>
        /// <param name="session">会话</param>
        /// <returns>异步工作</returns>
        private static async Task RunStreamAsync(string requestId,
            string model, string systemPrompt, string userMessage,
            string toolsJson, LlmStreamSession session)
        {
            try
            {
                string apiKey = _apiKey;
                if (apiKey.Length == 0)
                {
                    PushTerminal(session, "LLM_CREDENTIAL_MISSING",
                        "LLM credential is not configured.", "");
                    return;
                }
                using (HttpRequestMessage message = BuildStreamRequest(model,
                    systemPrompt, userMessage, toolsJson, apiKey))
                using (HttpResponseMessage response = await _httpClient
                    .SendAsync(message, HttpCompletionOption.ResponseHeadersRead,
                        session.Cancel.Token).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        string code = MapHttpError(response.StatusCode);
                        PushTerminal(session, code, "LLM returned HTTP "
                            + ((int)response.StatusCode).ToString() + ".", "");
                        return;
                    }
                    using (Stream stream = await response.Content
                        .ReadAsStreamAsync(session.Cancel.Token).ConfigureAwait(false))
                    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8,
                        true, 4096, false))
                    {
                        await ReadStreamEventsAsync(reader, session)
                            .ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                PushTerminal(session, "LLM_TIMEOUT",
                    "LLM request timed out or cancelled.", "");
            }
            catch (HttpRequestException)
            {
                PushTerminal(session, "LLM_NETWORK_ERROR",
                    "LLM network request failed.", "");
            }
            catch (JsonException)
            {
                PushTerminal(session, "LLM_RESPONSE_INVALID",
                    "LLM returned invalid JSON.", "");
            }
            catch (Exception exception)
            {
                PushTerminal(session, "LLM_PROVIDER_ERROR",
                    exception.GetType().Name + " occurred in LLM provider.", "");
            }
        }

        /// <summary>
        /// 构建流式 HTTP 请求
        /// </summary>
        /// <param name="model">模型名</param>
        /// <param name="systemPrompt">System Prompt</param>
        /// <param name="userMessage">User 消息</param>
        /// <param name="toolsJson">工具定义 JSON</param>
        /// <param name="apiKey">API Key</param>
        /// <returns>HTTP 请求</returns>
        private static HttpRequestMessage BuildStreamRequest(string model,
            string systemPrompt, string userMessage, string toolsJson,
            string apiKey)
        {
            HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Post,
                _endpoint);
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer",
                apiKey);
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(
                "text/event-stream"));
            message.Content = new StringContent(BuildStreamBody(model,
                systemPrompt, userMessage, toolsJson), Encoding.UTF8,
                "application/json");
            return message;
        }

        /// <summary>
        /// 构建流式请求 JSON 正文
        /// </summary>
        /// <param name="model">模型名</param>
        /// <param name="systemPrompt">System Prompt</param>
        /// <param name="userMessage">User 消息</param>
        /// <param name="toolsJson">工具定义 JSON 数组</param>
        /// <returns>JSON 正文</returns>
        private static string BuildStreamBody(string model, string systemPrompt,
            string userMessage, string toolsJson)
        {
            using MemoryStream stream = new MemoryStream();
            using (Utf8JsonWriter writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WriteString("model", model);
                writer.WriteBoolean("stream", true);
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
                if (!string.IsNullOrWhiteSpace(toolsJson))
                {
                    writer.WritePropertyName("tools");
                    using (JsonDocument tools = JsonDocument.Parse(toolsJson))
                    {
                        tools.RootElement.WriteTo(writer);
                    }
                }
                writer.WriteEndObject();
            }
            return Encoding.UTF8.GetString(stream.ToArray());
        }

        /// <summary>
        /// 逐行读取 SSE——分片入队，终态保证只入队一次
        /// </summary>
        /// <param name="reader">响应读取器</param>
        /// <param name="session">会话</param>
        /// <returns>异步工作</returns>
        private static async Task ReadStreamEventsAsync(StreamReader reader,
            LlmStreamSession session)
        {
            bool sawTerminalMarker = false;
            bool sawFinishReason = false;
            Dictionary<int, LlmToolCallBuilder> toolCalls = new Dictionary<int, LlmToolCallBuilder>();
            while (!reader.EndOfStream)
            {
                string? line = await reader.ReadLineAsync(session.Cancel.Token)
                    .ConfigureAwait(false);
                if (line == null)
                {
                    break;
                }
                if (!line.StartsWith("data:", StringComparison.Ordinal))
                {
                    continue;
                }
                string payload = line.Substring(5).Trim();
                if (payload == "[DONE]")
                {
                    sawTerminalMarker = true;
                    break;
                }
                if (payload.Length > 0)
                {
                    bool sawFinish;
                    ParseStreamEvent(payload, session, toolCalls, out sawFinish);
                    if (sawFinish)
                    {
                        sawFinishReason = true;
                    }
                }
            }
            if (!sawTerminalMarker && !sawFinishReason)
            {
                PushTerminal(session, "LLM_STREAM_INCOMPLETE",
                    "LLM stream ended unexpectedly.", "");
                return;
            }
            PushTerminal(session, "", "", BuildToolCallsJson(toolCalls));
        }

        /// <summary>
        /// 解析单个 OpenAI 兼容 SSE 事件
        /// </summary>
        /// <param name="payload">data 字段 JSON</param>
        /// <param name="session">会话</param>
        /// <param name="toolCalls">工具调用聚合表</param>
        /// <param name="sawFinish">是否观察到 finish_reason</param>
        private static void ParseStreamEvent(string payload, LlmStreamSession session,
            Dictionary<int, LlmToolCallBuilder> toolCalls, out bool sawFinish)
        {
            sawFinish = false;
            using (JsonDocument document = JsonDocument.Parse(payload))
            {
                JsonElement choices;
                if (!document.RootElement.TryGetProperty("choices", out choices)
                    || choices.ValueKind != JsonValueKind.Array
                    || choices.GetArrayLength() == 0)
                {
                    return;
                }
                JsonElement choice = choices[0];
                JsonElement finish;
                if (choice.TryGetProperty("finish_reason", out finish)
                    && finish.ValueKind == JsonValueKind.String)
                {
                    sawFinish = true;
                }
                JsonElement delta;
                if (!choice.TryGetProperty("delta", out delta)
                    || delta.ValueKind != JsonValueKind.Object)
                {
                    return;
                }
                string content = ReadString(delta, "content");
                string reasoning = ReadString(delta, "reasoning_content");
                JsonElement toolCallsElement;
                if (delta.TryGetProperty("tool_calls", out toolCallsElement)
                    && toolCallsElement.ValueKind == JsonValueKind.Array)
                {
                    AppendToolCalls(toolCallsElement, toolCalls);
                }
                if (content.Length > 0 || reasoning.Length > 0)
                {
                    LlmStreamChunk chunk = new LlmStreamChunk();
                    chunk.ContentDelta = content;
                    chunk.ReasoningDelta = reasoning;
                    session.Chunks.Enqueue(chunk);
                }
            }
        }

        /// <summary>
        /// 聚合事件内的工具调用分片
        /// </summary>
        /// <param name="calls">tool_calls 数组</param>
        /// <param name="toolCalls">聚合表</param>
        private static void AppendToolCalls(JsonElement calls,
            Dictionary<int, LlmToolCallBuilder> toolCalls)
        {
            foreach (JsonElement call in calls.EnumerateArray())
            {
                JsonElement indexValue;
                if (!call.TryGetProperty("index", out indexValue)
                    || indexValue.ValueKind != JsonValueKind.Number)
                {
                    continue;
                }
                int index = indexValue.GetInt32();
                LlmToolCallBuilder? builder;
                if (!toolCalls.TryGetValue(index, out builder) || builder == null)
                {
                    builder = new LlmToolCallBuilder();
                    toolCalls.Add(index, builder);
                }
                builder.Append(call);
            }
        }

        /// <summary>
        /// 按 index 顺序生成工具调用 JSON 数组
        /// </summary>
        /// <param name="toolCalls">聚合表</param>
        /// <returns>JSON 数组或空串</returns>
        private static string BuildToolCallsJson(
            Dictionary<int, LlmToolCallBuilder> toolCalls)
        {
            if (toolCalls.Count == 0)
            {
                return "";
            }
            List<int> indexes = new List<int>(toolCalls.Keys);
            indexes.Sort();
            using MemoryStream stream = new MemoryStream();
            using (Utf8JsonWriter writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartArray();
                for (int i = 0; i < indexes.Count; i = i + 1)
                {
                    toolCalls[indexes[i]].WriteTo(writer);
                }
                writer.WriteEndArray();
            }
            return Encoding.UTF8.GetString(stream.ToArray());
        }

        /// <summary>
        /// 读取对象内的可选字符串属性
        /// </summary>
        /// <param name="element">JSON 对象</param>
        /// <param name="name">属性名</param>
        /// <returns>字符串或空串</returns>
        private static string ReadString(JsonElement element, string name)
        {
            JsonElement value;
            if (element.TryGetProperty(name, out value)
                && value.ValueKind == JsonValueKind.String)
            {
                return SafeText(value.GetString());
            }
            return "";
        }

        /// <summary>
        /// 把 HTTP 状态映射为稳定错误码
        /// </summary>
        /// <param name="status">HTTP 状态</param>
        /// <returns>稳定错误码</returns>
        private static string MapHttpError(HttpStatusCode status)
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

        /// <summary>
        /// 入队终态分片——Interlocked 保证只入队一次
        /// </summary>
        /// <param name="session">会话</param>
        /// <param name="errorCode">错误码（成功为空）</param>
        /// <param name="errorMessage">错误摘要（成功为空）</param>
        /// <param name="toolCallsJson">工具调用聚合 JSON（可空）</param>
        private static void PushTerminal(LlmStreamSession session,
            string errorCode, string errorMessage, string toolCallsJson)
        {
            if (Interlocked.Exchange(ref session.IsTerminal, 1) != 0)
            {
                return;
            }
            LlmStreamChunk chunk = new LlmStreamChunk();
            chunk.Finished = true;
            chunk.ErrorCode = errorCode;
            chunk.ErrorMessage = errorMessage;
            chunk.ToolCallsJson = toolCallsJson;
            session.Chunks.Enqueue(chunk);
        }

        /// <summary>
        /// 流式会话——单次 llm.stream 的后台状态。后台线程只写队列，消费线程只读队列。
        /// </summary>
        private sealed class LlmStreamSession
        {
            /// <summary>
            /// 分片队列——后台写入，消费端读取
            /// </summary>
            internal readonly ConcurrentQueue<LlmStreamChunk> Chunks = new ConcurrentQueue<LlmStreamChunk>();

            /// <summary>
            /// 取消与超时源
            /// </summary>
            internal readonly CancellationTokenSource Cancel;

            /// <summary>
            /// 后台任务句柄——Finish 时等待
            /// </summary>
            internal Task? Worker;

            /// <summary>
            /// 是否已入队终态分片——保证只提交一个终态
            /// </summary>
            internal int IsTerminal;

            /// <summary>
            /// 最后消费的分片——判断积木（is_end/is_tool）的查询源
            /// </summary>
            internal LlmStreamChunk? LastChunk;

            /// <summary>
            /// 构造带超时的会话
            /// </summary>
            /// <param name="timeout">请求超时</param>
            internal LlmStreamSession(TimeSpan timeout)
            {
                Cancel = new CancellationTokenSource(timeout);
                Worker = null;
                IsTerminal = 0;
                LastChunk = null;
            }
        }

        /// <summary>
        /// 流式分片——单次 read_chunk 的载荷
        /// </summary>
        private sealed class LlmStreamChunk
        {
            /// <summary>
            /// 正文增量
            /// </summary>
            internal string ContentDelta;

            /// <summary>
            /// 推理增量
            /// </summary>
            internal string ReasoningDelta;

            /// <summary>
            /// 工具调用 JSON（终态分片携带聚合结果）
            /// </summary>
            internal string ToolCallsJson;

            /// <summary>
            /// 终态标志——true=流结束（成功或失败）
            /// </summary>
            internal bool Finished;

            /// <summary>
            /// 错误码——终态且失败时非空
            /// </summary>
            internal string ErrorCode;

            /// <summary>
            /// 错误摘要
            /// </summary>
            internal string ErrorMessage;

            /// <summary>
            /// 构造分片
            /// </summary>
            internal LlmStreamChunk()
            {
                ContentDelta = "";
                ReasoningDelta = "";
                ToolCallsJson = "";
                Finished = false;
                ErrorCode = "";
                ErrorMessage = "";
            }
        }

        /// <summary>
        /// 跨 SSE 事件累积单个工具调用字段
        /// </summary>
        private sealed class LlmToolCallBuilder
        {
            /// <summary>
            /// 工具调用 ID
            /// </summary>
            private string _id;

            /// <summary>
            /// 工具名称累积
            /// </summary>
            private readonly StringBuilder _name;

            /// <summary>
            /// 参数 JSON 累积
            /// </summary>
            private readonly StringBuilder _arguments;

            /// <summary>
            /// 创建空分片聚合器
            /// </summary>
            internal LlmToolCallBuilder()
            {
                _id = "";
                _name = new StringBuilder();
                _arguments = new StringBuilder();
            }

            /// <summary>
            /// 追加一个工具调用 delta
            /// </summary>
            /// <param name="call">delta 对象</param>
            internal void Append(JsonElement call)
            {
                JsonElement idValue;
                if (call.TryGetProperty("id", out idValue)
                    && idValue.ValueKind == JsonValueKind.String)
                {
                    _id = SafeText(idValue.GetString());
                }
                JsonElement function;
                if (!call.TryGetProperty("function", out function)
                    || function.ValueKind != JsonValueKind.Object)
                {
                    return;
                }
                JsonElement nameValue;
                if (function.TryGetProperty("name", out nameValue)
                    && nameValue.ValueKind == JsonValueKind.String)
                {
                    _name.Append(nameValue.GetString());
                }
                JsonElement argsValue;
                if (function.TryGetProperty("arguments", out argsValue)
                    && argsValue.ValueKind == JsonValueKind.String)
                {
                    _arguments.Append(argsValue.GetString());
                }
            }

            /// <summary>
            /// 将完整工具调用写入 JSON
            /// </summary>
            /// <param name="writer">JSON 写入器</param>
            internal void WriteTo(Utf8JsonWriter writer)
            {
                writer.WriteStartObject();
                writer.WriteString("id", _id);
                writer.WriteString("type", "function");
                writer.WriteStartObject("function");
                writer.WriteString("name", _name.ToString());
                writer.WriteString("arguments", _arguments.ToString());
                writer.WriteEndObject();
                writer.WriteEndObject();
            }
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
            RegisterCompletions();
            RegisterReadChunk();
            RegisterIsEnd();
            RegisterIsTool();
            RegisterFinish();
            RegisterHasError();
        }

        /// <summary>
        /// 注册 llm.completions——结构化流式（messagesJson 数组）
        /// </summary>
        private static void RegisterCompletions()
        {
            BrickContract contract = new BrickContract("llm.completions", "Mau.Bricks.LlmBrick.Completions");
            contract.Inputs.Add(new BrickPort("model", typeof(string), "模型名"));
            contract.Inputs.Add(new BrickPort("messagesJson", typeof(string), "messages 数组 JSON"));
            contract.Inputs.Add(new BrickPort("toolsJson", typeof(string), "工具定义 JSON 数组（空=无工具）"));
            contract.Outputs.Add(new BrickPort("requestId", typeof(string), "流式会话 ID"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Streaming;
            contract.Thread = "worker";
            BrickRegistry.Register(contract);
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
        /// 注册 llm.stream——真流式：启动后台 SSE 请求，分片经 read_chunk 消费
        /// </summary>
        private static void RegisterStream()
        {
            BrickContract contract = new BrickContract("llm.stream", "Mau.Bricks.LlmBrick.Stream");
            contract.Inputs.Add(new BrickPort("model", typeof(string), "模型名"));
            contract.Inputs.Add(new BrickPort("systemPrompt", typeof(string), "System Prompt"));
            contract.Inputs.Add(new BrickPort("userMessage", typeof(string), "User 消息"));
            contract.Inputs.Add(new BrickPort("toolsJson", typeof(string), "工具定义 JSON 数组（空=无工具）"));
            contract.Outputs.Add(new BrickPort("requestId", typeof(string), "流式会话 ID"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Streaming;
            contract.Thread = "worker";
            BrickRegistry.Register(contract);
        }

        /// <summary>
        /// 注册 llm.read_chunk——消费流式分片
        /// </summary>
        private static void RegisterReadChunk()
        {
            BrickContract contract = new BrickContract("llm.read_chunk", "Mau.Bricks.LlmBrick.ReadChunk");
            contract.Inputs.Add(new BrickPort("requestId", typeof(string), "流式会话 ID"));
            contract.Outputs.Add(new BrickPort("contentDelta", typeof(string), "正文增量"));
            contract.Outputs.Add(new BrickPort("reasoningDelta", typeof(string), "推理增量"));
            contract.Outputs.Add(new BrickPort("toolCallsJson", typeof(string), "工具调用 JSON（终态携带）"));
            contract.Outputs.Add(new BrickPort("finished", typeof(bool), "终态标志"));
            contract.Outputs.Add(new BrickPort("errorCode", typeof(string), "错误码（终态失败时非空）"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }

        /// <summary>
        /// 注册 llm.is_end——最后分片是否终态
        /// </summary>
        private static void RegisterIsEnd()
        {
            BrickContract contract = new BrickContract("llm.is_end", "Mau.Bricks.LlmBrick.IsEnd");
            contract.Inputs.Add(new BrickPort("requestId", typeof(string), "流式会话 ID"));
            contract.Outputs.Add(new BrickPort("ended", typeof(bool), "终态标志"));
            contract.Outputs.Add(new BrickPort("errorCode", typeof(string), "终态且失败时非空"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }

        /// <summary>
        /// 注册 llm.is_tool——最后分片是否含工具调用
        /// </summary>
        private static void RegisterIsTool()
        {
            BrickContract contract = new BrickContract("llm.is_tool", "Mau.Bricks.LlmBrick.IsTool");
            contract.Inputs.Add(new BrickPort("requestId", typeof(string), "流式会话 ID"));
            contract.Outputs.Add(new BrickPort("isTool", typeof(bool), "是否含工具调用"));
            contract.Outputs.Add(new BrickPort("toolCallsJson", typeof(string), "工具调用 JSON"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }
/// <summary>
/// 注册 llm.has_error——最后分片是否带错误码（返回=判断结果）
/// </summary>
private static void RegisterHasError()
{
    BrickContract contract = new BrickContract("llm.has_error", "Mau.Bricks.LlmBrick.HasError");
    contract.Inputs.Add(new BrickPort("requestId", typeof(string), "流式会话 ID"));
    contract.Outputs.Add(new BrickPort("hasError", typeof(bool), "是否带错误码"));
    contract.Outputs.Add(new BrickPort("errorCode", typeof(string), "错误码（有错误时输出）"));
    contract.Return = BrickReturnKind.Bool;
    contract.Duration = BrickDuration.Sync;
    contract.Thread = "main";
    BrickRegistry.Register(contract);
}
        /// <summary>
        /// 注册 llm.finish——终止流式会话
        /// </summary>
        private static void RegisterFinish()
        {
            BrickContract contract = new BrickContract("llm.finish", "Mau.Bricks.LlmBrick.Finish");
            contract.Inputs.Add(new BrickPort("requestId", typeof(string), "流式会话 ID"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }
    }
}

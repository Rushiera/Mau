using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Mau.Runtime
{
    /// <summary>
    /// DeepSeek 非流式/流式适配器——内建最小实现（OpenAI 兼容 /chat/completions）。
    /// 非流式：宿主构造时注入 baseUrl/apiKey/model；流式：ConfigStore 拉起（P4 配置项机制）。
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
        /// 默认兜底模型名——官方现役 v4-pro / v4-flash（deepseek-chat 已废弃）
        /// </summary>
        private const string FallbackModel = "deepseek-v4-flash";

        /// <summary>
        /// 思考模式开关——true=思考模式（默认）；配置 llm.thinking 控制
        /// </summary>
        private readonly bool _thinkingEnabled;

        /// <summary>
        /// 推理强度——low/high/max（默认 high；配置 llm.reasoning_effort 控制）
        /// </summary>
        private readonly string _reasoningEffort;

        /// <summary>
        /// 建立非流式适配器
        /// </summary>
        /// <param name="baseUrl">API 基址</param>
        /// <param name="apiKey">API 密钥</param>
        /// <param name="model">模型名</param>
        public DeepSeekLlmRuntime(string baseUrl, string apiKey, string model)
        {
            _baseUrl = baseUrl;
            if (_baseUrl == null)
            {
                _baseUrl = "";
            }
            _apiKey = apiKey;
            if (_apiKey == null)
            {
                _apiKey = "";
            }
            _model = model;
            if (string.IsNullOrEmpty(_model))
            {
                _model = FallbackModel;
            }
            _thinkingEnabled = true;
            _reasoningEffort = "high";
            _client = new HttpClient();
            _client.Timeout = TimeSpan.FromSeconds(60);
        }

        /// <summary>
        /// 从配置存储建立适配器——base_url/api_key/model/thinking/reasoning_effort 从文件拉起（P4 配置项机制）。
        /// key 优先级：配置 llm.api_key → 环境变量 DEEPSEEK_API_KEY → 空串（ERR 路径）。
        /// </summary>
        /// <param name="config">配置存储（llm.* 键段）</param>
        public DeepSeekLlmRuntime(ConfigStore config)
        {
            // [段1] base_url——缺省空串（调用方显式传全端点）
            string baseUrl;
            if (config == null)
            {
                baseUrl = "";
            }
            else
            {
                baseUrl = config.Get("llm.base_url", "");
            }
            _baseUrl = baseUrl;
            if (_baseUrl == null)
            {
                _baseUrl = "";
            }
            // [段2] api_key——配置优先，环境变量兜底
            string apiKey = "";
            if (config != null)
            {
                apiKey = config.Get("llm.api_key", "");
            }
            if (string.IsNullOrEmpty(apiKey))
            {
                string? envKey = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
                if (!string.IsNullOrEmpty(envKey))
                {
                    apiKey = envKey;
                }
            }
            _apiKey = apiKey;
            // [段3] model——缺省兜底模型
            string model;
            if (config == null)
            {
                model = "";
            }
            else
            {
                model = config.Get("llm.model", "");
            }
            _model = model;
            if (string.IsNullOrEmpty(_model))
            {
                _model = FallbackModel;
            }
            // [段4] 思考模式与推理强度——官方默认 enabled + high
            string thinking;
            if (config == null)
            {
                thinking = "enabled";
            }
            else
            {
                thinking = config.Get("llm.thinking", "enabled");
            }
            _thinkingEnabled = thinking == "enabled";
            if (config == null)
            {
                _reasoningEffort = "high";
            }
            else
            {
                _reasoningEffort = config.Get("llm.reasoning_effort", "high");
            }
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
        /// 流式完成——SSE 增量事件流（P4：官方 SseParser + translate 状态机）。
        /// Text/Reasoning 增量（双通道独立回收）；[DONE] → Done；错误 → Error（ERR|码|详情）。
        /// 注意：C# 迭代器禁止 try-catch 内 yield——网络层错误用 catch 赋值 + catch 后 yield 模式。
        /// </summary>
        /// <param name="system">系统提示词</param>
        /// <param name="content">用户内容</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>流式事件序列</returns>
        public async IAsyncEnumerable<LlmStreamEvent> StreamCompletions(string system, string content, [EnumeratorCancellation] CancellationToken ct = default)
        {
            // [段1] 构造流式请求体并发送（ResponseHeadersRead——流式读取）
            string body = BuildStreamRequestBody(system, content);
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, _baseUrl.TrimEnd('/') + "/chat/completions"))
            {
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _apiKey);
                request.Headers.TryAddWithoutValidation("Accept", "text/event-stream");
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                HttpResponseMessage? response = null;
                string netError = "";
                try
                {
                    response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                }
                catch (Exception ex)
                {
                    netError = "ERR|TRANSPORT|" + ex.GetType().Name + "|" + ex.Message;
                }
                if (netError.Length > 0)
                {
                    yield return new LlmStreamEvent(LlmStreamKind.Error, netError);
                    yield break;
                }
                using (response)
                {
                    // [段2] HTTP 层失败——错误 JSON 双形态解析（error.type/message 兜底，HTTP 码不可作唯一判据）
                    if (response == null)
                    {
                        yield return new LlmStreamEvent(LlmStreamKind.Error, "ERR|TRANSPORT|空响应");
                        yield break;
                    }
                    if (!response.IsSuccessStatusCode)
                    {
                        string? raw = "";
                        string readError = "";
                        try
                        {
                            raw = await response.Content.ReadAsStringAsync(ct);
                        }
                        catch (Exception ex)
                        {
                            readError = "ERR|TRANSPORT|" + ex.GetType().Name + "|" + ex.Message;
                        }
                        if (readError.Length > 0)
                        {
                            yield return new LlmStreamEvent(LlmStreamKind.Error, readError);
                            yield break;
                        }
                        string rawText;
                        if (raw == null)
                        {
                            rawText = "";
                        }
                        else
                        {
                            rawText = raw;
                        }
                        yield return new LlmStreamEvent(LlmStreamKind.Error, ParseErrorText((int)response.StatusCode, rawText));
                        yield break;
                    }
                    // [段3] SSE 解析——官方 SseParser（注释行/多行 data/空行分隔自动处理）
                    Stream? stream = null;
                    string streamError = "";
                    try
                    {
                        stream = await response.Content.ReadAsStreamAsync(ct);
                    }
                    catch (Exception ex)
                    {
                        streamError = "ERR|TRANSPORT|" + ex.GetType().Name + "|" + ex.Message;
                    }
                    if (streamError.Length > 0)
                    {
                        yield return new LlmStreamEvent(LlmStreamKind.Error, streamError);
                        yield break;
                    }
                    SseParser<string> parser = SseParser.Create(stream!);
                    // [段4] translate——独立迭代器（零 catch：所有错误事件化；取消异常冒泡给调用方）
                    await foreach (LlmStreamEvent ev in TranslateSse(parser, ct))
                    {
                        yield return ev;
                    }
                }
            }
        }

        /// <summary>
        /// SSE 帧翻译——增量事件流（零 catch：畸形帧/缺 [DONE] 全部事件化；取消异常冒泡给调用方）。
        /// [DONE] 到达即终止（忽略余帧——实测 [DONE] 后可能有余帧）。
        /// </summary>
        /// <param name="parser">SseParser 实例</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>流式事件序列</returns>
        private static async IAsyncEnumerable<LlmStreamEvent> TranslateSse(SseParser<string> parser, [EnumeratorCancellation] CancellationToken ct)
        {
            bool done = false;
            await foreach (SseItem<string> item in parser.EnumerateAsync(ct))
            {
                // [段1] [DONE] 哨兵——唯一可信终止；忽略余帧
                if (item.Data == "[DONE]")
                {
                    done = true;
                    break;
                }
                // [段2] 帧解析——delta.content/reasoning_content 判空再产事件（空首帧不开块；空 delta 帧跳过）
                string text;
                string reasoning;
                bool parsed = TryParseDelta(item.Data, out text, out reasoning);
                if (parsed)
                {
                    if (text.Length > 0)
                    {
                        yield return new LlmStreamEvent(LlmStreamKind.Text, text);
                    }
                    if (reasoning.Length > 0)
                    {
                        yield return new LlmStreamEvent(LlmStreamKind.Reasoning, reasoning);
                    }
                }
            }
            // [段3] 完整性检查——无 [DONE] 提前结束 = STREAM_CLOSED（模型调用不可信，按失败处理）
            if (!done)
            {
                yield return new LlmStreamEvent(LlmStreamKind.Error, "ERR|STREAM_CLOSED|SSE 流未以 [DONE] 结束");
                yield break;
            }
            yield return new LlmStreamEvent(LlmStreamKind.Done, "");
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
        /// 构造流式请求体——stream:true；思考模式/推理强度按配置（P4 思考参数实测结论：v4 支持）。
        /// 思考模式不传 temperature（官方：思考模式不生效）。
        /// </summary>
        /// <param name="system">系统提示词</param>
        /// <param name="content">用户内容</param>
        /// <returns>请求体 JSON</returns>
        private string BuildStreamRequestBody(string system, string content)
        {
            // [段1] 消息数组——system + user
            object[] messages = new object[]
            {
                new
                {
                    role = "system",
                    content = system
                },
                new
                {
                    role = "user",
                    content = content
                }
            };
            // [段2] 思考模式——enabled 带 effort；disabled 关闭思考
            if (_thinkingEnabled)
            {
                object payload = new
                {
                    model = _model,
                    messages = messages,
                    stream = true,
                    thinking = new
                    {
                        type = "enabled"
                    },
                    reasoning_effort = _reasoningEffort
                };
                return JsonSerializer.Serialize(payload);
            }
            object payloadDisabled = new
            {
                model = _model,
                messages = messages,
                stream = true,
                thinking = new
                {
                    type = "disabled"
                }
            };
            return JsonSerializer.Serialize(payloadDisabled);
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
        /// 解析 SSE 帧 JSON——提取 choices[0].delta.content / reasoning_content。
        /// 畸形帧返回 false（跳过不产事件）；choices 空数组返回 false（usage-only 尾帧）。
        /// </summary>
        /// <param name="data">帧 data 载荷</param>
        /// <param name="text">回复增量（无则空串）</param>
        /// <param name="reasoning">思考增量（无则空串）</param>
        /// <returns>true=解析成功（增量可能为空——调用方判空再产事件）</returns>
        private static bool TryParseDelta(string data, out string text, out string reasoning)
        {
            text = "";
            reasoning = "";
            if (data == null || data.Length == 0)
            {
                return false;
            }
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(data))
                {
                    JsonElement root = doc.RootElement;
                    JsonElement choices;
                    if (!root.TryGetProperty("choices", out choices) || choices.GetArrayLength() == 0)
                    {
                        return false;
                    }
                    JsonElement first = choices[0];
                    JsonElement delta;
                    if (!first.TryGetProperty("delta", out delta))
                    {
                        return false;
                    }
                    JsonElement value;
                    if (delta.TryGetProperty("content", out value) && value.ValueKind == JsonValueKind.String)
                    {
                        string? gotText = value.GetString();
                        if (gotText != null)
                        {
                            text = gotText;
                        }
                    }
                    if (delta.TryGetProperty("reasoning_content", out value) && value.ValueKind == JsonValueKind.String)
                    {
                        string? gotReasoning = value.GetString();
                        if (gotReasoning != null)
                        {
                            reasoning = gotReasoning;
                        }
                    }
                    return true;
                }
            }
            catch
            {
                // 畸形 JSON 帧——调用方跳过（MALFORMED 帧不做硬失败，容忍上游抖动）
                return false;
            }
        }

        /// <summary>
        /// 解析错误响应——错误 JSON 双形态兜底（官方 error{type,code,message} / 代理 type+error.type）。
        /// 格式：ERR|HTTP_{码}|{type}|{message}（截断 200 字防刷屏）。
        /// </summary>
        /// <param name="statusCode">HTTP 状态码</param>
        /// <param name="raw">响应体</param>
        /// <returns>错误文本</returns>
        private static string ParseErrorText(int statusCode, string raw)
        {
            // [段1] 解析错误 JSON——error.type/error.message 或外层 type
            string type = "";
            string message = "";
            if (raw != null && raw.Length > 0)
            {
                try
                {
                    using (JsonDocument doc = JsonDocument.Parse(raw))
                    {
                        JsonElement root = doc.RootElement;
                        JsonElement err;
                        if (root.TryGetProperty("error", out err) && err.ValueKind == JsonValueKind.Object)
                        {
                            JsonElement value;
                            if (err.TryGetProperty("type", out value) && value.ValueKind == JsonValueKind.String)
                            {
                                type = value.GetString() ?? "";
                            }
                            if (err.TryGetProperty("message", out value) && value.ValueKind == JsonValueKind.String)
                            {
                                message = value.GetString() ?? "";
                            }
                        }
                        else
                        {
                            JsonElement value;
                            if (root.TryGetProperty("type", out value) && value.ValueKind == JsonValueKind.String)
                            {
                                type = value.GetString() ?? "";
                            }
                        }
                    }
                }
                catch
                {
                    message = TrimText(raw, 200);
                }
            }
            // [段2] 兜底——无 type 用 HTTP 码；无 message 用原文
            if (type.Length == 0)
            {
                type = "HTTP_" + statusCode.ToString();
            }
            string detail;
            if (message.Length > 0)
            {
                detail = message;
            }
            else if (raw == null)
            {
                detail = "";
            }
            else
            {
                detail = raw;
            }
            return "ERR|" + type + "|" + TrimText(detail, 200);
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

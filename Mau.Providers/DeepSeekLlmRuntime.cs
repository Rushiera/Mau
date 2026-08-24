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
using Mau.Runtime;

namespace Mau.Providers
{
    /// <summary>
    /// DeepSeek 非流式/流式适配器——内建最小实现（OpenAI 兼容 /chat/completions）。
    /// 非流式：宿主构造时注入 baseUrl/apiKey/model；流式：ConfigStore 拉起（P4 配置项机制）。
    /// 接入主入口（构造/ChatStream/配置实时读取）在本文件；
    /// SSE 解析面分部在 DeepSeekLlmRuntime.Sse.cs；wire 序列化面分部在 DeepSeekLlmRuntime.Serialize.cs（P7b partial 拆分）。
    /// </summary>
    public sealed partial class DeepSeekLlmRuntime : ILlmRuntime
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
        /// 配置存储——运行期实时读取（P7 热载：配置以本地持久化为准，构造期快照退役）
        /// </summary>
        private readonly ConfigStore? _config;

        /// <summary>
        /// 建立非流式适配器
        /// </summary>
        /// <param name="baseUrl">API 基址</param>
        /// <param name="apiKey">API 密钥</param>
        /// <param name="model">模型名</param>
        public DeepSeekLlmRuntime(string baseUrl, string apiKey, string model)
        {
            _config = null;
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
        /// 拉取优先级（Mau 全局环境变量兜底——2026-08-17 拍板）：配置文件 llm.* → Mau 全局环境变量 MAU_LLM_* → 兼容/默认。
        /// key 优先级：配置 llm.api_key → MAU_LLM_API_KEY → DEEPSEEK_API_KEY（兼容） → 空串（ERR 路径）。
        /// </summary>
        /// <param name="config">配置存储（llm.* 键段）</param>
        public DeepSeekLlmRuntime(ConfigStore config)
        {
            // [段1] 配置存储持有——运行期实时读取（P7 热载拍板：配置以本地持久化为准，构造期快照退役）
            _config = config;
            _baseUrl = "";
            _apiKey = "";
            _model = "";
            _thinkingEnabled = true;
            _reasoningEffort = "high";
            _client = new HttpClient();
            _client.Timeout = TimeSpan.FromSeconds(60);
        }
/// <summary>
/// 流式对话完成——消息序列 + 工具定义 → 事件流（P5：OpenAI 兼容 tool_calls）。
/// Text/Reasoning 增量；ToolCalls 完整工具调用 JSON（聚合后一次性发出）；[DONE] → Done；错误 → Error。
/// 注意：C# 迭代器禁止 try-catch 内 yield——网络层错误用 catch 赋值 + catch 后 yield 模式。
/// </summary>
/// <param name = "messages">完整消息序列（system/user/assistant/tool 多 role）</param>
/// <param name = "tools">工具定义数组（可为空——纯对话）</param>
/// <param name = "userId">会话用户标识——请求体 user_id（P9.4 CH2 对齐：KVCache 隔离；空=不携带）</param>
/// <param name = "ct">取消令牌</param>
/// <returns>流式事件序列</returns>
public async IAsyncEnumerable<LlmStreamEvent> ChatStream(LlmMessage[] messages, ToolSpec[] tools, string userId = "", [EnumeratorCancellation] CancellationToken ct = default)
{
    // [段1] 构造流式请求体并发送（ResponseHeadersRead——流式读取）
    string body = BuildChatRequestBody(messages, tools, userId);
    using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, GetBaseUrl().TrimEnd('/') + "/chat/completions"))
    {
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + GetApiKey());
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
            // [段4] translate——独立迭代器（零 catch：所有错误事件化；tool_calls 聚合在 translate 内）
            await foreach (LlmStreamEvent ev in TranslateSse(parser, ct))
            {
                yield return ev;
            }
        }
    }
}

        /// <summary>
        /// 实时读取 API 基址——配置 llm.base_url → MAU_LLM_BASE_URL → 注入值/空串（P7 热载：每次调用取当前值）
        /// </summary>
        /// <returns>API 基址</returns>
        private string GetBaseUrl()
        {
            if (_config != null)
            {
                string value = _config.Get("llm.base_url", "");
                if (value.Length > 0)
                {
                    return value;
                }
                string? env = Environment.GetEnvironmentVariable("MAU_LLM_BASE_URL");
                if (!string.IsNullOrEmpty(env))
                {
                    return env;
                }
            }
            return _baseUrl;
        }

        /// <summary>
        /// 实时读取 API 密钥——配置 llm.api_key → MAU_LLM_API_KEY → DEEPSEEK_API_KEY（兼容） → 注入值/空串
        /// </summary>
        /// <returns>API 密钥</returns>
        private string GetApiKey()
        {
            if (_config != null)
            {
                string value = _config.Get("llm.api_key", "");
                if (value.Length > 0)
                {
                    return value;
                }
                string? env = Environment.GetEnvironmentVariable("MAU_LLM_API_KEY");
                if (!string.IsNullOrEmpty(env))
                {
                    return env;
                }
                env = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
                if (!string.IsNullOrEmpty(env))
                {
                    return env;
                }
            }
            return _apiKey;
        }

        /// <summary>
        /// 实时读取模型名——配置 llm.model → MAU_LLM_MODEL → 注入值 → 兜底模型
        /// </summary>
        /// <returns>模型名</returns>
        private string GetModel()
        {
            if (_config != null)
            {
                string value = _config.Get("llm.model", "");
                if (value.Length > 0)
                {
                    return value;
                }
                string? env = Environment.GetEnvironmentVariable("MAU_LLM_MODEL");
                if (!string.IsNullOrEmpty(env))
                {
                    return env;
                }
            }
            if (string.IsNullOrEmpty(_model))
            {
                return FallbackModel;
            }
            return _model;
        }

        /// <summary>
        /// 实时读取思考模式开关——配置 llm.thinking → MAU_LLM_THINKING → 默认 enabled
        /// </summary>
        /// <returns>true=思考模式</returns>
        private bool GetThinkingEnabled()
        {
            if (_config != null)
            {
                string value = _config.Get("llm.thinking", "");
                if (value.Length == 0)
                {
                    string? env = Environment.GetEnvironmentVariable("MAU_LLM_THINKING");
                    if (!string.IsNullOrEmpty(env))
                    {
                        value = env;
                    }
                }
                if (value.Length == 0)
                {
                    value = "enabled";
                }
                return value == "enabled";
            }
            return _thinkingEnabled;
        }

        /// <summary>
        /// 实时读取推理强度——配置 llm.reasoning_effort → MAU_LLM_REASONING_EFFORT → 默认 high
        /// </summary>
        /// <returns>推理强度 low/high/max</returns>
        private string GetReasoningEffort()
        {
            if (_config != null)
            {
                string value = _config.Get("llm.reasoning_effort", "");
                if (value.Length == 0)
                {
                    string? env = Environment.GetEnvironmentVariable("MAU_LLM_REASONING_EFFORT");
                    if (!string.IsNullOrEmpty(env))
                    {
                        value = env;
                    }
                }
                if (value.Length == 0)
                {
                    value = "high";
                }
                return value;
            }
            return _reasoningEffort;
        }
    }
}
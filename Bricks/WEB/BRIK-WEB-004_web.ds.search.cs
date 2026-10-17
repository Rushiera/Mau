// ═══════════════════════════════════════════════════
// 积木: web.ds.search
// ID:   BRIK-WEB-004
// 类别: WEB
// 作用: DeepSeek 官方 Search API 结构化检索——返回结果原料（标题 / 链接 / 摘要 / 分数），不做模型总结
// 依赖: 无
// 引用: Mau.Runtime（DataBox/ConfigStore/CH_LlmApiConfigStore）
// 原理: 配置解析（search.api_config_id → LLM 池配置 → key）→ POST /v1/tools/search（Bearer）→ results[] 结构化渲染
// 热拔插: 积木层自实现——改本文件 → CLI 组翻译 → host-reload 热生效（无需重启宿主）
// 常用: web_cat.mau 认领线——'web.ds.search'[@args] > @result
// 门禁: 429 等 1 秒单次重试；正文按条截断（防前文爆量）；参数面零容忍不回落
// ═══════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// DeepSeek Search API 积木——web-ds-search 工具执行面（结构化原料，不经模型二次总结）
    /// 端点固定为官方 Tools Search；鉴权复用 LLM 池配置的 key（同 host 同一套 Bearer）。
    /// </summary>
    public static class WebDsSearchBrick
    {
        /// <summary>
        /// DeepSeek 官方检索端点（Base URL 固定；代理场景需改此处并重翻本组）
        /// </summary>
        private const string ToolsSearchEndpoint = "https://api.deepseek.com/v1/tools/search";

        /// <summary>
        /// 单条正文截断上限（字符）——need_content 开启时防止单次几十 KB 入前文
        /// </summary>
        private const int MaxContentChars = 1500;

        /// <summary>
        /// 请求超时毫秒——纯检索接口，正常 1～3 秒返回；20 秒封顶（该线在途期间不接新单）
        /// </summary>
        private const int RequestTimeoutMs = 20000;

        /// <summary>
        /// HTTP 客户端——静态复用（超时按每次请求 CTS 控制）
        /// </summary>
        private static readonly HttpClient _client = new HttpClient();

        /// <summary>
        /// 执行结构化检索——参数校验 → 配置解析 → HTTP 请求（含 429 单次重试）→ 结果渲染
        /// </summary>
        /// <param name="argsJson">工具参数 JSON（query / ori_question / count / need_content；catId 保留键由宿主注入）</param>
        /// <param name="result">结构化结果文本或 ERR| 错误文本</param>
        /// <returns>true=执行成功</returns>
        public static bool Search(string argsJson, out string result)
        {
            result = "";
            // [参数面] 未知 / 缺值一律 ERR|BAD_ARGS（零容忍不回落）
            string badArgs = JsonArgs.Validate(argsJson, "query ori_question count need_content", "query ori_question", "", "");
            if (badArgs.Length > 0)
            {
                result = badArgs;
                return false;
            }
            string query = JsonArgs.Get(argsJson, "query");
            if (query == "§PARSE_FAIL§")
            {
                result = "ERR|BAD_ARGS|工具参数 JSON 解析失败（LLM 生成参数可能被截断）";
                return false;
            }
            string oriQuestion = JsonArgs.Get(argsJson, "ori_question");
            if (oriQuestion == "§PARSE_FAIL§")
            {
                oriQuestion = "";
            }
            int count = 10;
            string countError = ReadCount(argsJson, out count);
            if (countError.Length > 0)
            {
                result = countError;
                return false;
            }
            bool needContent = false;
            string needError = ReadNeedContent(argsJson, out needContent);
            if (needError.Length > 0)
            {
                result = needError;
                return false;
            }
            try
            {
                string apiKey = "";
                string configError = ResolveApiKey(out apiKey);
                if (configError.Length > 0)
                {
                    result = configError;
                    return false;
                }
                string body = BuildBody(query, oriQuestion, count, needContent);
                string json = Send(body, apiKey);
                if (json.StartsWith("ERR|", StringComparison.Ordinal))
                {
                    result = json;
                    return true;
                }
                string rendered = "";
                int items = 0;
                double avgScore = 0;
                string parseError = ParseResults(json, out rendered, out items, out avgScore);
                if (parseError.Length > 0)
                {
                    result = parseError;
                    return true;
                }
                // 结构化返回体（design-ch4-tools 附录 · A214）——首行 JSON 头 + 正文摘要行 + 载荷
                Dictionary<string, object> fields = new Dictionary<string, object>();
                fields["results"] = items;
                fields["avg_score"] = Math.Round(avgScore, 1);
                fields["chars"] = rendered.Length;
                string headline = query + " | " + items.ToString() + " 条结果";
                if (items > 0)
                {
                    headline = headline + " · 均分 " + Math.Round(avgScore, 1).ToString("0.0");
                }
                result = MetaHead("web-ds-search", true, query, items, fields) + "\n" + headline + "\n" + rendered;
                return true;
            }
            catch (Exception ex)
            {
                result = "ERR|" + ex.GetType().Name + "|" + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 读取并校验 count——缺省 10；非法（非整数 / 越界）返回 ERR|BAD_ARGS 文本
        /// </summary>
        /// <param name="argsJson">工具参数 JSON</param>
        /// <param name="count">输出：条数</param>
        /// <returns>错误文本（空=通过）</returns>
        private static string ReadCount(string argsJson, out int count)
        {
            count = 10;
            string raw = JsonArgs.Get(argsJson, "count");
            if (raw.Length == 0 || raw == "§PARSE_FAIL§")
            {
                return "";
            }
            int parsed = 0;
            if (!int.TryParse(raw, out parsed) || parsed < 1 || parsed > 50)
            {
                return "ERR|BAD_ARGS|count 非法值: " + raw + "（取值 1～50 的整数）";
            }
            count = parsed;
            return "";
        }

        /// <summary>
        /// 读取并校验 need_content——缺省 false；仅接受 true / false（字符串或 JSON 布尔原文）
        /// </summary>
        /// <param name="argsJson">工具参数 JSON</param>
        /// <param name="needContent">输出：是否带正文</param>
        /// <returns>错误文本（空=通过）</returns>
        private static string ReadNeedContent(string argsJson, out bool needContent)
        {
            needContent = false;
            string raw = JsonArgs.Get(argsJson, "need_content");
            if (raw.Length == 0 || raw == "§PARSE_FAIL§")
            {
                return "";
            }
            string normalized = raw.Trim().ToLowerInvariant();
            if (normalized == "true")
            {
                needContent = true;
                return "";
            }
            if (normalized == "false")
            {
                return "";
            }
            return "ERR|BAD_ARGS|need_content 非法值: " + raw + "（true / false）";
        }

        /// <summary>
        /// 解析鉴权——search.api_config_id → LLM 池配置 → key（与 web-search 共用同一份配置）
        /// </summary>
        /// <param name="apiKey">输出：Bearer key</param>
        /// <returns>错误文本（空=通过）</returns>
        private static string ResolveApiKey(out string apiKey)
        {
            apiKey = "";
            ConfigStore? config;
            if (!DataBox.TryResolve<ConfigStore>(out config) || config == null)
            {
                return "ERR|API_NOT_CONFIGURED|宿主未注入 ConfigStore";
            }
            CH_LlmApiConfigStore? apiStore;
            if (!DataBox.TryResolve<CH_LlmApiConfigStore>(out apiStore) || apiStore == null)
            {
                return "ERR|API_NOT_CONFIGURED|宿主未注入 CH_LlmApiConfigStore";
            }
            string rawId = config.Get("search.api_config_id", "");
            if (rawId.Length == 0)
            {
                return "ERR|API_NOT_CONFIGURED|搜索 API 未配置（配置区 search.api_config_id 需引用 LLM 池配置）";
            }
            Guid apiConfigId;
            if (!Guid.TryParse(rawId, out apiConfigId))
            {
                return "ERR|API_NOT_CONFIGURED|搜索 API 配置无效（search.api_config_id 不是合法 GUID）";
            }
            CH_LlmApiConfig poolConfig;
            if (!apiStore.TryGet(apiConfigId, out poolConfig))
            {
                return "ERR|API_NOT_CONFIGURED|搜索 API 配置不存在（LLM 池中找不到该 apiConfigId）";
            }
            apiKey = apiStore.GetSecret(apiConfigId);
            if (apiKey.Length == 0)
            {
                return "ERR|API_NOT_CONFIGURED|搜索 API 密钥未配置（LLM 池配置缺 key）";
            }
            return "";
        }

        /// <summary>
        /// 构造请求体——query / ori_question（空则省略）/ count / need_content；值一律走 JsonUtil.Str
        /// </summary>
        /// <param name="query">关键词 / 检索式（支持 site: / after: / lang: 等语法）</param>
        /// <param name="oriQuestion">触发搜索的原始问题（空串 = 不传）</param>
        /// <param name="count">返回条数 1～50</param>
        /// <param name="needContent">是否带网页正文</param>
        /// <returns>请求体 JSON</returns>
        private static string BuildBody(string query, string oriQuestion, int count, bool needContent)
        {
            Dictionary<string, object> body = new Dictionary<string, object>();
            body["query"] = query;
            if (oriQuestion.Length > 0)
            {
                body["ori_question"] = oriQuestion;
            }
            body["count"] = count;
            body["need_content"] = needContent;
            return JsonSerializer.Serialize(body);
        }

        /// <summary>
        /// 发送请求——Bearer 鉴权；429 等 1 秒重试一次，仍失败返回 ERR|；其它错误不重试
        /// </summary>
        /// <param name="body">请求体 JSON</param>
        /// <param name="apiKey">Bearer key</param>
        /// <returns>响应体 JSON 或 ERR| 错误文本</returns>
        private static string Send(string body, string apiKey)
        {
            string first = SendOnce(body, apiKey, out int status);
            if (status != 429)
            {
                return first;
            }
            Thread.Sleep(1000);
            string second = SendOnce(body, apiKey, out status);
            if (status == 429)
            {
                return "ERR|HTTP_429|搜索接口限速（账号级 QPS 5，已重试一次）: " + second;
            }
            return second;
        }

        /// <summary>
        /// 单次发送——返回响应体 JSON；非 2xx 返回 ERR|HTTP_xxx 文本（status 输出原始状态码）
        /// </summary>
        /// <param name="body">请求体 JSON</param>
        /// <param name="apiKey">Bearer key</param>
        /// <param name="status">输出：HTTP 状态码（传输异常为 0）</param>
        /// <returns>响应体 JSON 或 ERR| 错误文本</returns>
        private static string SendOnce(string body, string apiKey, out int status)
        {
            status = 0;
            CancellationTokenSource cts = new CancellationTokenSource(RequestTimeoutMs);
            try
            {
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, ToolsSearchEndpoint);
                try
                {
                    request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + apiKey);
                    request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                    HttpResponseMessage response = _client.SendAsync(request, cts.Token).GetAwaiter().GetResult();
                    try
                    {
                        status = (int)response.StatusCode;
                        string json = response.Content.ReadAsStringAsync(cts.Token).GetAwaiter().GetResult();
                        if (!response.IsSuccessStatusCode)
                        {
                            return "ERR|HTTP_" + status.ToString() + "|" + ReadErrorMessage(json);
                        }
                        return json;
                    }
                    finally
                    {
                        response.Dispose();
                    }
                }
                finally
                {
                    request.Dispose();
                }
            }
            catch (OperationCanceledException)
            {
                return "ERR|TIMEOUT|搜索请求超时（" + RequestTimeoutMs.ToString() + " ms）";
            }
            finally
            {
                cts.Dispose();
            }
        }

        /// <summary>
        /// 读取错误响应体 message——OpenAI 兼容格式 {"error":{"message":"..."}}；解析失败回退原文前 200 字
        /// </summary>
        /// <param name="json">错误响应体</param>
        /// <returns>错误消息</returns>
        private static string ReadErrorMessage(string json)
        {
            string message = "";
            if (json != null && json.Length > 0)
            {
                JsonDocument? doc = JsonUtil.Parse(json, "搜索错误响应", out string parseError);
                if (doc != null)
                {
                    try
                    {
                        JsonElement root = doc.RootElement;
                        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out JsonElement errEl) &&
                            errEl.ValueKind == JsonValueKind.Object && errEl.TryGetProperty("message", out JsonElement msgEl) &&
                            msgEl.ValueKind == JsonValueKind.String)
                        {
                            string? got = msgEl.GetString();
                            if (got != null)
                            {
                                message = got;
                            }
                        }
                    }
                    finally
                    {
                        doc.Dispose();
                    }
                }
            }
            if (message.Length == 0 && json != null && json.Length > 200)
            {
                message = json.Substring(0, 200) + "...";
            }
            if (message.Length == 0)
            {
                message = "响应体为空";
            }
            return message;
        }

        /// <summary>
        /// 解析 results[] 并渲染——条数与均分；空数组按合法空集合处理（不是失败）
        /// </summary>
        /// <param name="json">响应体 JSON</param>
        /// <param name="rendered">输出：渲染文本</param>
        /// <param name="items">输出：结果条数</param>
        /// <param name="avgScore">输出：rerank_score 均分（无分数条目时为 0）</param>
        /// <returns>错误文本（空=通过）</returns>
        private static string ParseResults(string json, out string rendered, out int items, out double avgScore)
        {
            rendered = "";
            items = 0;
            avgScore = 0;
            JsonDocument? doc = JsonUtil.Parse(json, "搜索结果", out string parseError);
            if (doc == null)
            {
                return "ERR|BAD_RESPONSE|搜索结果解析失败: " + parseError;
            }
            try
            {
                JsonElement root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return "ERR|BAD_RESPONSE|搜索响应不是 JSON 对象";
                }
                JsonElement results;
                if (!root.TryGetProperty("results", out results) || results.ValueKind != JsonValueKind.Array)
                {
                    return "ERR|BAD_RESPONSE|搜索响应缺少 results 数组";
                }
                int total = results.GetArrayLength();
                if (total == 0)
                {
                    rendered = "无匹配结果（0 条）——可放宽关键词或去掉 site: / after: 等限定";
                    return "";
                }
                StringBuilder sb = new StringBuilder();
                double scoreSum = 0;
                int scoreCount = 0;
                int index = 0;
                foreach (JsonElement item in results.EnumerateArray())
                {
                    index = index + 1;
                    if (item.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }
                    AppendOne(sb, index, item, ref scoreSum, ref scoreCount);
                }
                items = total;
                if (scoreCount > 0)
                {
                    avgScore = scoreSum / scoreCount;
                }
                rendered = sb.ToString().TrimEnd('\r', '\n');
            }
            finally
            {
                doc.Dispose();
            }
            return "";
        }

        /// <summary>
        /// 渲染单条结果——标题行（站点 / 日期 / 权威 / 分数）+ 链接 + 长摘要 + 正文（截断）
        /// </summary>
        /// <param name="sb">输出缓冲</param>
        /// <param name="index">序号（1 起）</param>
        /// <param name="item">结果项</param>
        /// <param name="scoreSum">累计分数</param>
        /// <param name="scoreCount">有分数的条数</param>
        private static void AppendOne(StringBuilder sb, int index, JsonElement item, ref double scoreSum, ref int scoreCount)
        {
            string title = StrOf(item, "title");
            if (title.Length == 0)
            {
                title = "（无标题）";
            }
            // [段1] 标题 + 元信息行
            sb.Append("[" + index.ToString() + "] " + title);
            List<string> meta = new List<string>();
            string siteName = StrOf(item, "site_name");
            if (siteName.Length > 0)
            {
                meta.Add(siteName);
            }
            string date = StrOf(item, "date");
            if (date.Length >= 10)
            {
                meta.Add(date.Substring(0, 10));
            }
            JsonElement authorityEl;
            if (item.TryGetProperty("authority", out authorityEl) && authorityEl.ValueKind == JsonValueKind.Number)
            {
                int authority = authorityEl.GetInt32();
                if (authority >= 0)
                {
                    meta.Add("权威" + authority.ToString());
                }
            }
            JsonElement scoreEl;
            if (item.TryGetProperty("rerank_score", out scoreEl) && scoreEl.ValueKind == JsonValueKind.Number)
            {
                double score = scoreEl.GetDouble();
                meta.Add("分" + score.ToString("0.0"));
                scoreSum = scoreSum + score;
                scoreCount = scoreCount + 1;
            }
            if (meta.Count > 0)
            {
                sb.Append(" (");
                for (int i = 0; i < meta.Count; i = i + 1)
                {
                    if (i > 0)
                    {
                        sb.Append(" · ");
                    }
                    sb.Append(meta[i]);
                }
                sb.Append(")");
            }
            sb.Append("\n");
            // [段2] 链接 + 长摘要
            string url = StrOf(item, "url");
            if (url.Length > 0)
            {
                sb.Append("    " + url + "\n");
            }
            string summary = StrOf(item, "summary");
            if (summary.Length == 0)
            {
                summary = StrOf(item, "snippet");
            }
            if (summary.Length > 0)
            {
                sb.Append("    " + summary.Replace("\r\n", "\n").Replace("\n", "\n    ") + "\n");
            }
            // [段3] 正文（need_content=true 时）——按条硬截断，标出总字数
            string content = StrOf(item, "content");
            if (content.Length > 0)
            {
                string flat = content.Replace("\r\n", "\n");
                if (flat.Length > MaxContentChars)
                {
                    sb.Append("    " + flat.Substring(0, MaxContentChars).Replace("\n", "\n    "));
                    sb.Append("\n    [正文截断 共 " + flat.Length.ToString() + " 字]\n");
                }
                else
                {
                    sb.Append("    " + flat.Replace("\n", "\n    ") + "\n");
                }
            }
            sb.Append("\n");
        }

        /// <summary>
        /// 取字符串字段——缺失 / null / 非字符串返回空串
        /// </summary>
        /// <param name="item">JSON 对象</param>
        /// <param name="key">字段名</param>
        /// <returns>字段值</returns>
        private static string StrOf(JsonElement item, string key)
        {
            JsonElement el;
            if (!item.TryGetProperty(key, out el))
            {
                return "";
            }
            if (el.ValueKind != JsonValueKind.String)
            {
                return "";
            }
            string? value = el.GetString();
            if (value == null)
            {
                return "";
            }
            return value;
        }

        /// <summary>
        /// 结构化元数据头（统一口径·A214）——恒定 ok / tool + 主来源 target + 主计数 items + 专有字段（插入序）
        /// </summary>
        /// <param name="tool">工具名</param>
        /// <param name="ok">成败</param>
        /// <param name="target">主来源（空串 = 省略）</param>
        /// <param name="items">主计数（负值 = 省略）</param>
        /// <param name="fields">专有字段（按插入序）</param>
        /// <returns>单行 JSON</returns>
        private static string MetaHead(string tool, bool ok, string target, int items, Dictionary<string, object> fields)
        {
            Dictionary<string, object> head = new Dictionary<string, object>();
            head["ok"] = ok;
            head["tool"] = tool;
            if (target.Length > 0)
            {
                head["target"] = target;
            }
            if (items >= 0)
            {
                head["items"] = items;
            }
            foreach (KeyValuePair<string, object> kv in fields)
            {
                head[kv.Key] = kv.Value;
            }
            return JsonSerializer.Serialize(head);
        }
    }
}
// #MAU_CHECKSUM:SHA256:382F55A2ECB139F3284C49DC592FC7EFE4AE64FD18EB5B3F27AC805F86607F7C

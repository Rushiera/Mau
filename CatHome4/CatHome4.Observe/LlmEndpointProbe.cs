using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Mau.Runtime;

namespace CatHome4.Observe
{
    /// <summary>
    /// LLM 端点池探活（A253 扩展）——手动触发，对池内每份配置发一次最小请求，逐个报可用状态。
    /// 真实链路对齐 DeepSeekLlmRuntime：端点 URL 取配置 Endpoint（已含 /chat/completions）· 鉴权 Bearer ·
    /// 网关会话头 x-opencode-session（缺失即拒）· 请求体 max_tokens=1。
    /// 成本：每次触发 = 池内端点数次请求——故只走手动入口，不进快照周期探测（D3：端点类常态只做配置完整性）。
    /// </summary>
    internal static class LlmEndpointProbe
    {
        /// <summary>单端点探活超时（毫秒）</summary>
        private const int TimeoutMs = 20000;

        /// <summary>探活请求体尾段——最小对话（模型名在前的骨架由调用方拼）</summary>
        private const string BodyTail = ",\"messages\":[{\"role\":\"user\",\"content\":\"ping\"}],\"max_tokens\":1}";

        /// <summary>失败详情截断长度（回执可读）</summary>
        private const int DetailLimit = 160;

        /// <summary>
        /// 探活全部端点——逐个请求（串行，避免并发打爆限额），返回 {"ok":true,"items":[...]}。
        /// </summary>
        /// <returns>JSON 文本</returns>
        internal static async Task<string> ProbeAsync()
        {
            CH_LlmApiConfigStore store = null;
            DataBox.TryResolve<CH_LlmApiConfigStore>(out store);
            if (store == null)
            {
                return "{\"ok\":false,\"error\":" + JsonUtil.Str("端点池存储不可用（未注入）") + "}";
            }
            CH_LlmApiConfig[] all = store.GetAll();
            StringBuilder sb = new StringBuilder();
            sb.Append("{\"ok\":true,\"items\":[");
            for (int i = 0; i < all.Length; i = i + 1)
            {
                if (i > 0)
                {
                    sb.Append(",");
                }
                sb.Append(await ProbeOneAsync(store, all[i]).ConfigureAwait(false));
            }
            sb.Append("]}");
            return sb.ToString();
        }

        /// <summary>
        /// 探活单份配置——端点 / Key 前置校验 → 发最小请求 → 归类状态。
        /// 状态：ok（2xx）/ nokey（未配 Key——不请求）/ fail（URL 空 / 非 2xx / 传输异常）。
        /// </summary>
        /// <param name="store">端点池（取 Key）</param>
        /// <param name="config">待探配置</param>
        /// <returns>结果条目 JSON 片段</returns>
        private static async Task<string> ProbeOneAsync(CH_LlmApiConfigStore store, CH_LlmApiConfig config)
        {
            string name = config.DisplayName;
            string model = config.DefaultModel;
            string endpoint = config.Endpoint;
            if (endpoint.Length == 0)
            {
                return BuildItem(name, model, endpoint, "fail", 0, "端点 URL 为空");
            }
            string key = store.GetSecret(config.ApiConfigId);
            if (key.Length == 0)
            {
                return BuildItem(name, model, endpoint, "nokey", 0, "未配置 Key——跳过请求");
            }
            string body = "{\"model\":" + JsonUtil.Str(model) + BodyTail;
            Stopwatch watch = Stopwatch.StartNew();
            using (HttpClient client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromMilliseconds(TimeoutMs);
                using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, endpoint))
                {
                    request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + key);
                    request.Headers.TryAddWithoutValidation("x-opencode-session", "cat-home4-probe");
                    request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                    try
                    {
                        using (HttpResponseMessage response = await client.SendAsync(request).ConfigureAwait(false))
                        {
                            watch.Stop();
                            int status = (int)response.StatusCode;
                            if (response.IsSuccessStatusCode)
                            {
                                return BuildItem(name, model, endpoint, "ok", watch.ElapsedMilliseconds, "HTTP " + status.ToString());
                            }
                            string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                            return BuildItem(name, model, endpoint, "fail", watch.ElapsedMilliseconds, "HTTP " + status.ToString() + " " + Trim(text));
                        }
                    }
                    catch (Exception ex)
                    {
                        watch.Stop();
                        return BuildItem(name, model, endpoint, "fail", watch.ElapsedMilliseconds, ex.GetType().Name + ": " + Trim(ex.Message));
                    }
                }
            }
        }

        /// <summary>
        /// 构造单条结果——JsonUtil.Object 产出（键固定 + 值走入口）。
        /// </summary>
        /// <param name="name">显示名</param>
        /// <param name="model">模型名</param>
        /// <param name="endpoint">端点 URL</param>
        /// <param name="state">状态（ok / nokey / fail）</param>
        /// <param name="ms">耗时毫秒（未请求 = 0）</param>
        /// <param name="detail">详情</param>
        /// <returns>JSON 对象片段</returns>
        private static string BuildItem(string name, string model, string endpoint, string state, long ms, string detail)
        {
            return JsonUtil.Object(
                ("name", name),
                ("model", model),
                ("endpoint", endpoint),
                ("state", state),
                ("ms", ms),
                ("detail", detail));
        }

        /// <summary>
        /// 详情截断——超限保留前缀 + 省略号（单行可读）。
        /// </summary>
        /// <param name="text">原始文本</param>
        /// <returns>截断文本</returns>
        private static string Trim(string text)
        {
            if (text == null)
            {
                return "";
            }
            string flat = text.Replace("\r", " ").Replace("\n", " ").Trim();
            if (flat.Length <= DetailLimit)
            {
                return flat;
            }
            return flat.Substring(0, DetailLimit) + "…";
        }
    }
}

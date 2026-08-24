using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Mau.Runtime;
using Mau.Providers;

namespace CH4
{
    /// <summary>
    /// Program 管理端点面分部——M3 前后端配置管理（LLM API 池 CRUD + 每猫配置读写）。
    /// 端点处理器经 HttpHost.BuildApp 主端口注册（catsBuilder 非空——管理面收敛主端口）。
    /// 线程模型：读端点 HTTP 线程直读（BuildCatsJson 同先例）；写端点落盘 HTTP 线程 + 运行时生效入队主线程泵（catcfg.apply 指令）。
    /// </summary>
    public static partial class Program
    {
        /// <summary>LLM API 配置池——Bootstrap 绑定（catcfg.apply 重建 Runtime 消费）</summary>
        private static CH_LlmApiConfigStore _apiStore;

        /// <summary>全局配置存储——Bootstrap 绑定（catcfg.apply 重建 Runtime 消费）</summary>
        private static ConfigStore _globalConfig;

        /// <summary>默认猫当前 API 配置身份——Bootstrap 赋值（catcfg.apply 变更比对）</summary>
        private static Guid _defaultApiConfigId;

        /// <summary>
        /// LLM API 池列表——GET /api/v1/llm-apis（key 掩码展示；hasKey 供前端判断是否已配置）。
        /// </summary>
        /// <returns>列表 JSON——version + items</returns>
        internal static IResult HandleLlmApisGet()
        {
            CH_LlmApiConfigStore store = null;
            DataBox.TryResolve<CH_LlmApiConfigStore>(out store);
            List<object> items = new List<object>();
            if (store != null)
            {
                CH_LlmApiConfig[] configs = store.GetAll();
                for (int i = 0; i < configs.Length; i++)
                {
                    CH_LlmApiConfig c = configs[i];
                    string key = store.GetSecret(c.ApiConfigId);
                    string keyShown;
                    if (key.Length > 0)
                    {
                        keyShown = MaskApiKey(key);
                    }
                    else
                    {
                        keyShown = "";
                    }
                    items.Add(new
                    {
                        apiConfigId = c.ApiConfigId.ToString("D"),
                        displayName = c.DisplayName,
                        apiType = c.ApiType,
                        endpoint = c.Endpoint,
                        defaultModel = c.DefaultModel,
                        apiKey = keyShown,
                        hasKey = key.Length > 0
                    });
                }
            }
            var resp = new
            {
                version = 1,
                items = items
            };
            return Results.Json(resp);
        }

        /// <summary>
        /// LLM API 池新建——POST /api/v1/llm-apis（body: displayName/apiType/endpoint/defaultModel/apiKey）。
        /// </summary>
        /// <param name="ctx">HTTP 上下文</param>
        /// <returns>回执 JSON</returns>
        internal static async Task<IResult> HandleLlmApisPost(HttpContext ctx)
        {
            string body = await ReadBodyText(ctx);
            string displayName = "";
            string apiType = "";
            string endpoint = "";
            string defaultModel = "";
            string apiKey = "";
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(body))
                {
                    JsonElement root = doc.RootElement;
                    displayName = GetJsonString(root, "displayName");
                    apiType = GetJsonString(root, "apiType");
                    endpoint = GetJsonString(root, "endpoint");
                    defaultModel = GetJsonString(root, "defaultModel");
                    apiKey = GetJsonString(root, "apiKey");
                }
            }
            catch (Exception)
            {
                return Results.Json(new { ok = false, error = "body 非 JSON" });
            }
            if (displayName.Length == 0)
            {
                return Results.Json(new { ok = false, error = "displayName 为空" });
            }
            if (endpoint.Length == 0)
            {
                return Results.Json(new { ok = false, error = "endpoint 为空" });
            }
            CH_LlmApiConfigStore store = null;
            DataBox.TryResolve<CH_LlmApiConfigStore>(out store);
            if (store == null)
            {
                return Results.Json(new { ok = false, error = "配置池未绑定" });
            }
            if (apiType.Length == 0)
            {
                apiType = "deepseek";
            }
            CH_LlmApiConfig config = new CH_LlmApiConfig();
            config.ApiConfigId = Guid.NewGuid();
            config.DisplayName = displayName;
            config.ApiType = apiType;
            config.Endpoint = endpoint;
            config.DefaultModel = defaultModel;
            store.Save(config, apiKey);
            LogStore.Add("CH4.Entry", 1, "llm-apis | 新建 | " + config.ApiConfigId.ToString("D") + " | " + displayName, "CONFIG");
            return Results.Json(new { ok = true, apiConfigId = config.ApiConfigId.ToString("D") });
        }

        /// <summary>
        /// LLM API 池编辑——POST /api/v1/llm-apis/edit（body: apiConfigId + 字段；apiKey 空=保留原 key）。
        /// endpoint/model/key 编辑立即生效——Runtime 每次 ChatStream 实时读 Store（M2e 零机制）。
        /// </summary>
        /// <param name="ctx">HTTP 上下文</param>
        /// <returns>回执 JSON</returns>
        internal static async Task<IResult> HandleLlmApisEdit(HttpContext ctx)
        {
            string body = await ReadBodyText(ctx);
            string apiConfigId = "";
            string displayName = "";
            string apiType = "";
            string endpoint = "";
            string defaultModel = "";
            string apiKey = "";
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(body))
                {
                    JsonElement root = doc.RootElement;
                    apiConfigId = GetJsonString(root, "apiConfigId");
                    displayName = GetJsonString(root, "displayName");
                    apiType = GetJsonString(root, "apiType");
                    endpoint = GetJsonString(root, "endpoint");
                    defaultModel = GetJsonString(root, "defaultModel");
                    apiKey = GetJsonString(root, "apiKey");
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
            if (displayName.Length == 0)
            {
                return Results.Json(new { ok = false, error = "displayName 为空" });
            }
            if (endpoint.Length == 0)
            {
                return Results.Json(new { ok = false, error = "endpoint 为空" });
            }
            CH_LlmApiConfigStore store = null;
            DataBox.TryResolve<CH_LlmApiConfigStore>(out store);
            if (store == null)
            {
                return Results.Json(new { ok = false, error = "配置池未绑定" });
            }
            CH_LlmApiConfig config = new CH_LlmApiConfig();
            config.ApiConfigId = id;
            config.DisplayName = displayName;
            config.ApiType = apiType;
            config.Endpoint = endpoint;
            config.DefaultModel = defaultModel;
            store.Save(config, apiKey);
            LogStore.Add("CH4.Entry", 1, "llm-apis | 编辑 | " + id.ToString("D") + " | " + displayName + " | key=" + (apiKey.Length > 0 ? "Y" : "保留"), "CONFIG");
            return Results.Json(new { ok = true, apiConfigId = id.ToString("D") });
        }

        /// <summary>
        /// LLM API 池删除——POST /api/v1/llm-apis/delete（body: apiConfigId；普通配置 + key 同删）。
        /// 默认配置可删（M3 拍板：多配置组场景；EnsureDefaults 首次初始化语义——删除不复活）。
        /// </summary>
        /// <param name="ctx">HTTP 上下文</param>
        /// <returns>回执 JSON</returns>
        internal static async Task<IResult> HandleLlmApisDelete(HttpContext ctx)
        {
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
            if (!store.Delete(id))
            {
                return Results.Json(new { ok = false, error = "配置不存在" });
            }
            LogStore.Add("CH4.Entry", 1, "llm-apis | 删除 | " + id.ToString("D"), "CONFIG");
            return Results.Json(new { ok = true, apiConfigId = id.ToString("D") });
        }

        /// <summary>
        /// 每猫配置读取——GET /api/v1/cat-config?cat=&lt;id|majordomo&gt;。
        /// 返回：apiConfigId/persona/toolNames/injectList + allToolNames（工具勾选清单）+ apiOptions（API 下拉清单）。
        /// </summary>
        /// <param name="ctx">HTTP 上下文</param>
        /// <returns>配置 JSON</returns>
        internal static IResult HandleCatConfigGet(HttpContext ctx)
        {
            string catKey = ctx.Request.Query["cat"].ToString();
            if (catKey.Length == 0)
            {
                return Results.Json(new { ok = false, error = "cat 参数为空" });
            }
            CatCfgData cfg = LoadCatCfg(Path.Combine(_dataRoot, "Data", "sessions", catKey, "cat.cfg"));
            if (cfg == null)
            {
                return Results.Json(new { ok = false, error = "cat.cfg 不存在: " + catKey });
            }
            // API 下拉清单——配置池全量（id + 显示名）
            CH_LlmApiConfigStore store = null;
            DataBox.TryResolve<CH_LlmApiConfigStore>(out store);
            List<object> apiOptions = new List<object>();
            if (store != null)
            {
                CH_LlmApiConfig[] configs = store.GetAll();
                for (int i = 0; i < configs.Length; i++)
                {
                    apiOptions.Add(new
                    {
                        apiConfigId = configs[i].ApiConfigId.ToString("D"),
                        displayName = configs[i].DisplayName
                    });
                }
            }
            var resp = new
            {
                ok = true,
                cat = catKey,
                apiConfigId = cfg.ApiConfigId,
                persona = cfg.Persona,
                toolNames = cfg.ToolNames,
                injectList = cfg.InjectList,
                allToolNames = GetAllToolNames(),
                apiOptions = apiOptions
            };
            return Results.Json(resp);
        }

        /// <summary>
        /// 每猫配置写入——POST /api/v1/cat-config（body: cat/apiConfigId/persona/toolNames/injectList）。
        /// toolNames 写时校验（非法名过滤）；落盘 HTTP 线程原子写；运行时生效（字段更新 + Swap）入队主线程泵 catcfg.apply。
        /// 生效语义：apiConfigId 立即生效（SwapLlmRuntime）；persona/toolNames/injectList 新会话生效（session.new 重注入）。
        /// </summary>
        /// <param name="ctx">HTTP 上下文</param>
        /// <returns>回执 JSON（ok=已受理落盘）</returns>
        internal static async Task<IResult> HandleCatConfigPost(HttpContext ctx)
        {
            string body = await ReadBodyText(ctx);
            string catKey = "";
            string apiConfigId = "";
            string persona = "";
            string toolNames = "";
            List<string> injectList = new List<string>();
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(body))
                {
                    JsonElement root = doc.RootElement;
                    catKey = GetJsonString(root, "cat");
                    apiConfigId = GetJsonString(root, "apiConfigId");
                    persona = GetJsonString(root, "persona");
                    toolNames = GetJsonString(root, "toolNames");
                    JsonElement injectEl;
                    if (root.TryGetProperty("injectList", out injectEl) && injectEl.ValueKind == JsonValueKind.Array)
                    {
                        for (int i = 0; i < injectEl.GetArrayLength(); i++)
                        {
                            JsonElement item = injectEl[i];
                            if (item.ValueKind == JsonValueKind.String)
                            {
                                string got = item.GetString();
                                if (got != null && got.Length > 0)
                                {
                                    injectList.Add(got);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                return Results.Json(new { ok = false, error = "body 非 JSON" });
            }
            if (catKey.Length == 0)
            {
                return Results.Json(new { ok = false, error = "cat 为空" });
            }
            if (catKey != "majordomo" && FindCat(catKey) == null)
            {
                return Results.Json(new { ok = false, error = "猫不存在: " + catKey });
            }
            // M2c 写时校验——非法工具名过滤（持久化面只落合法名）
            string validToolNames = ValidateToolNames(toolNames);
            // 落盘——读旧配置保留 id/displayName/running/port；apiConfigId 空=保留旧值
            CatCfgData cfg = LoadCatCfg(Path.Combine(_dataRoot, "Data", "sessions", catKey, "cat.cfg"));
            if (cfg == null)
            {
                return Results.Json(new { ok = false, error = "cat.cfg 不存在: " + catKey });
            }
            if (apiConfigId.Length > 0)
            {
                Guid parsed;
                if (!Guid.TryParse(apiConfigId, out parsed) || parsed == Guid.Empty)
                {
                    return Results.Json(new { ok = false, error = "apiConfigId 非法" });
                }
                cfg.ApiConfigId = apiConfigId;
            }
            cfg.Persona = persona;
            cfg.ToolNames = validToolNames;
            cfg.InjectList = injectList.ToArray();
            SaveCatCfgData(catKey, cfg);
            // 运行时生效——入队主线程泵（注册表/会话面仅主线程触碰）
            _catQueue.Enqueue("catcfg.apply " + catKey);
            LogStore.Add("CH4.Entry", 1, "cat-config | 已受理 | " + catKey + " | tools=" + validToolNames, "CONFIG");
            return Results.Json(new { ok = true, cat = catKey, toolNames = validToolNames });
        }

        /// <summary>
        /// catcfg.apply 执行体——主线程泵消费：重读 cat.cfg → 更新注册表字段 + 重裁剪声明面 + apiConfigId 变更 SwapLlmRuntime。
        /// </summary>
        /// <param name="key">猫寻址键（majordomo=默认猫）</param>
        /// <returns>结果文本</returns>
        internal static string HandleCatCfgApply(string key)
        {
            CatCfgData cfg = LoadCatCfg(Path.Combine(_dataRoot, "Data", "sessions", key, "cat.cfg"));
            if (cfg == null)
            {
                return "catcfg.apply | cat.cfg 不存在: " + key;
            }
            string persona = "";
            if (cfg.Persona != null)
            {
                persona = cfg.Persona;
            }
            string[] injectList = cfg.InjectList;
            if (injectList == null)
            {
                injectList = new string[0];
            }
            string toolNames = "";
            if (cfg.ToolNames != null)
            {
                toolNames = cfg.ToolNames;
            }
            ToolSpec[] specs = FilterToolSpecs(ResolveToolNames(toolNames));
            if (key == "majordomo")
            {
                // 默认猫——静态面更新（session.new 重注入消费）
                _defaultPersona = persona;
                _defaultInjectList = injectList;
                _defaultToolSpecs = specs;
                Guid newApi = ResolveApiConfigId(cfg);
                if (_defaultApiConfigId != newApi)
                {
                    _defaultApiConfigId = newApi;
                    _defaultSession.SwapLlmRuntime(new DeepSeekLlmRuntime(_apiStore, newApi, _globalConfig));
                    LogStore.Add("CH4.Entry", 1, "catcfg.apply | majordomo | api 切换 → " + newApi.ToString("D"), "CONFIG");
                }
                return "catcfg.apply | majordomo | 已生效（前文项新会话生效）";
            }
            CatEntry cat = FindCat(key);
            if (cat == null)
            {
                return "catcfg.apply | 猫不存在: " + key;
            }
            cat.Persona = persona;
            cat.InjectList = injectList;
            cat.ToolNames = toolNames;
            cat.ToolSpecs = specs;
            Guid newApiId = ResolveApiConfigId(cfg);
            if (cat.ApiConfigId != newApiId)
            {
                cat.ApiConfigId = newApiId;
                CH_LlmApiConfig apiConfig = new CH_LlmApiConfig();
                if (_apiStore != null)
                {
                    _apiStore.TryGet(newApiId, out apiConfig);
                }
                cat.ApiConfig = apiConfig;
                cat.Session.SwapLlmRuntime(new DeepSeekLlmRuntime(_apiStore, newApiId, _globalConfig));
                LogStore.Add("CH4.Entry", 1, "catcfg.apply | " + cat.DisplayName + " | api 切换 → " + newApiId.ToString("D"), "CONFIG");
            }
            return "catcfg.apply | " + cat.DisplayName + " | 已生效（前文项新会话生效）";
        }

        /// <summary>
        /// 解析 cat.cfg 的 API 配置身份——缺省回退内置 DeepSeek 稳定 ID（M1b 同规）。
        /// </summary>
        /// <param name="cfg">配置数据</param>
        /// <returns>API 配置身份</returns>
        private static Guid ResolveApiConfigId(CatCfgData cfg)
        {
            Guid id = CH_LlmApiConfigStore.DeepSeekApiConfigId;
            if (cfg != null && cfg.ApiConfigId != null && cfg.ApiConfigId.Length > 0)
            {
                Guid parsed;
                if (Guid.TryParse(cfg.ApiConfigId, out parsed) && parsed != Guid.Empty)
                {
                    id = parsed;
                }
            }
            return id;
        }

        /// <summary>
        /// cat.cfg 原子写——按配置数据落盘（M3 端点写面；与 SaveCatCfg(CatEntry) 同源）。
        /// </summary>
        /// <param name="id">会话 ID（majordomo=默认猫）</param>
        /// <param name="data">配置数据</param>
        private static void SaveCatCfgData(string id, CatCfgData data)
        {
            string dir = Path.Combine(_dataRoot, "Data", "sessions", id);
            string path = Path.Combine(dir, "cat.cfg");
            var payload = new
            {
                id = data.Id,
                displayName = data.DisplayName,
                running = data.Running,
                port = data.Port,
                apiConfigId = data.ApiConfigId,
                persona = data.Persona,
                // M2c 写时校验——序列化前比对内置清单过滤非法名（外部损坏防御：持久化面只落合法名）
                toolNames = ValidateToolNames(data.ToolNames),
                injectList = data.InjectList
            };
            try
            {
                ConfigStore.AtomicWrite(path, JsonSerializer.Serialize(payload));
            }
            catch (Exception ex)
            {
                LogStore.Add("CH4.Entry", 2, "cat.cfg | 写入失败 | " + ex.Message, "CHAT");
            }
        }

        /// <summary>
        /// 读取请求 body 全文。
        /// </summary>
        /// <param name="ctx">HTTP 上下文</param>
        /// <returns>body 文本</returns>
        private static async Task<string> ReadBodyText(HttpContext ctx)
        {
            using (StreamReader reader = new StreamReader(ctx.Request.Body))
            {
                return await reader.ReadToEndAsync();
            }
        }

        /// <summary>
        /// 读取 JSON 对象字符串属性——防御式（缺字段返回空串）。
        /// </summary>
        /// <param name="obj">JSON 元素</param>
        /// <param name="prop">属性名</param>
        /// <returns>属性值</returns>
        private static string GetJsonString(JsonElement obj, string prop)
        {
            JsonElement value;
            if (obj.TryGetProperty(prop, out value) && value.ValueKind == JsonValueKind.String)
            {
                string got = value.GetString();
                if (got != null)
                {
                    return got;
                }
            }
            return "";
        }

        /// <summary>
        /// API key 掩码——保留头尾 4 字符（列表展示；长度 ≤8 全掩）。
        /// </summary>
        /// <param name="key">原始 key</param>
        /// <returns>掩码文本</returns>
        private static string MaskApiKey(string key)
        {
            if (key.Length <= 8)
            {
                return "****";
            }
            return key.Substring(0, 4) + "****" + key.Substring(key.Length - 4);
        }
    }
}

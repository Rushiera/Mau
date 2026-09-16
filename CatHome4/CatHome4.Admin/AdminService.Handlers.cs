using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using CatHome4.Contracts;
using CatHome4.QQ;
using Mau.Runtime;
using Mau.Providers;
using CH4;

namespace CatHome4.Admin
{
    /// <summary>
    /// Program 管理端点面分部——M3 前后端配置管理（LLM API 池 CRUD + 每猫配置读写）。
    /// 端点处理器经 HttpHost.BuildApp 主端口注册（catsBuilder 非空——管理面收敛主端口）。
    /// 线程模型：读端点 HTTP 线程直读（BuildCatsJson 同先例）；写端点落盘 HTTP 线程 + 运行时生效入队主线程泵（catcfg.apply 指令）。
    /// </summary>
    internal static partial class AdminService
    {
        /// <summary>LLM API 配置池——Bootstrap 绑定（catcfg.apply 重建 Runtime 消费）</summary>
        internal static CH_LlmApiConfigStore _apiStore;

        /// <summary>QQ Bot 配置池——Bootstrap 绑定（qqbot 管理器消费）</summary>
        internal static CH_QqBotConfigStore _qqBotStore;

        /// <summary>全局配置存储——Bootstrap 绑定（catcfg.apply 重建 Runtime 消费）</summary>
        internal static ConfigStore _globalConfig;

        /// <summary>默认猫当前 API 配置身份——Bootstrap 赋值（catcfg.apply 变更比对）</summary>
        internal static Guid _defaultApiConfigId;

        /// <summary>
        /// 管理路由注册——S2 解耦：入口壳经 IHttpRouteSink 向 Http 域注册管理端点（主端口仅一次）。
        /// 归属：Admin 域——llm-apis/qqbot-apis/cat-config/cat-default/workspace 管理面（S4 随 Admin 域迁 CatHome4.Admin）。
        /// </summary>
        /// <param name="sink">HTTP 路由注册面（HttpHost 实现）</param>
        internal static void RegisterAdminRoutes(IHttpRouteSink sink)
        {
            // LLM API 池 CRUD——M3 管理面
            sink.MapGet("/api/v1/llm-apis", (Delegate)HandleLlmApisGet);
            sink.MapPost("/api/v1/llm-apis", (Delegate)HandleLlmApisPost);
            sink.MapPost("/api/v1/llm-apis/edit", (Delegate)HandleLlmApisEdit);
            sink.MapPost("/api/v1/llm-apis/delete", (Delegate)HandleLlmApisDelete);
            sink.MapPost("/api/v1/llm-apis/default", (Delegate)HandleLlmApisDefault);
            // QQ Bot 池 CRUD——R2.3 管理面
            sink.MapGet("/api/v1/qqbot-apis", (Delegate)HandleQqBotApisGet);
            sink.MapPost("/api/v1/qqbot-apis", (Delegate)HandleQqBotApisPost);
            sink.MapPost("/api/v1/qqbot-apis/edit", (Delegate)HandleQqBotApisEdit);
            sink.MapPost("/api/v1/qqbot-apis/delete", (Delegate)HandleQqBotApisDelete);
            // 每猫配置读写——M3
            sink.MapGet("/api/v1/cat-config", (Delegate)HandleCatConfigGet);
            sink.MapPost("/api/v1/cat-config", (Delegate)HandleCatConfigPost);
            // 新猫默认模板——全局配置管理面（M3d）
            sink.MapGet("/api/v1/cat-default", (Delegate)HandleCatDefaultGet);
            sink.MapPost("/api/v1/cat-default", (Delegate)HandleCatDefaultPost);
            // 受控根编辑面——管理员面（M4d）
            sink.MapGet("/api/v1/workspace", (Delegate)HandleWorkspaceGet);
            sink.MapPost("/api/v1/workspace", (Delegate)HandleWorkspacePost);
            // 加载包池——全局池管理面（R4 加载包）
            sink.MapGet("/api/v1/packs", (Delegate)HandlePacksGet);
            sink.MapPost("/api/v1/packs", (Delegate)HandlePacksPost);
            sink.MapPost("/api/v1/packs/delete", (Delegate)HandlePacksDelete);
            // 打开数据目录——前端按钮（explorer.exe 打开持久化 Data 目录——三级锚定解析）
            sink.MapPost("/api/v1/open-data-dir", (Delegate)HandleOpenDataDir);
        }

        /// <summary>
        /// 打开数据目录——POST /api/v1/open-data-dir（explorer.exe 打开持久化 Data 目录）。
        /// Data 根由 ResolveDataRoot 三级锚定（env→仓库根→AppData）——前端零路径知识。
        /// 纯前端 JS 无法直接打开本地资源管理器（浏览器沙箱）——必须后端端点调 explorer.exe。
        /// </summary>
        /// <returns>回执 JSON（含打开的目录路径）</returns>
        internal static IResult HandleOpenDataDir()
        {
            string dataDir = Path.Combine(_dataRoot, "Data");
            if (!Directory.Exists(dataDir))
            {
                Directory.CreateDirectory(dataDir);
            }
            try
            {
                System.Diagnostics.Process.Start("explorer.exe", dataDir);
                LogStore.Add("CatHome4", 1, "打开数据目录: " + dataDir, "CONFIG");
                return Results.Json(new { ok = true, path = dataDir });
            }
            catch (Exception ex)
            {
                LogStore.Add("CatHome4", 2, "打开数据目录失败：" + ex.Message, "CONFIG");
                return Results.Json(new { ok = false, error = ex.Message });
            }
        }

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
                        isDefault = c.IsDefault,
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
            LogStore.Add("CatHome4", 1, "LLM API 池新增配置「" + displayName + "」（" + config.ApiConfigId.ToString("D") + "）", "CONFIG");
            return Results.Json(new { ok = true, apiConfigId = config.ApiConfigId.ToString("D") });
        }

        /// <summary>
        /// LLM API 池设为默认——POST /api/v1/llm-apis/default（body: apiConfigId）。
        /// 默认端点语义：QuickCat 语料面与未显式配置的猫固定走默认（每次调用实时解析——切换立即生效）。
        /// </summary>
        /// <param name="ctx">HTTP 上下文</param>
        /// <returns>回执 JSON</returns>
        internal static async Task<IResult> HandleLlmApisDefault(HttpContext ctx)
        {
            string body = await ReadBodyText(ctx);
            string apiConfigId = "";
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(body))
                {
                    JsonElement root = doc.RootElement;
                    apiConfigId = GetJsonString(root, "apiConfigId");
                }
            }
            catch (Exception)
            {
                return Results.Json(new { ok = false, error = "body 非 JSON" });
            }
            Guid parsed;
            if (!Guid.TryParse(apiConfigId, out parsed) || parsed == Guid.Empty)
            {
                return Results.Json(new { ok = false, error = "apiConfigId 无效" });
            }
            CH_LlmApiConfigStore store = null;
            DataBox.TryResolve<CH_LlmApiConfigStore>(out store);
            if (store == null)
            {
                return Results.Json(new { ok = false, error = "配置池未绑定" });
            }
            if (!store.SetDefault(parsed))
            {
                return Results.Json(new { ok = false, error = "配置不存在" });
            }
            LogStore.Add("CatHome4", 1, "LLM API 池已设为默认：" + apiConfigId, "CONFIG");
            return Results.Json(new { ok = true });
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
            LogStore.Add("CatHome4", 1, "LLM API 池「" + displayName + "」已更新（" + id.ToString("D") + "），密钥" + (apiKey.Length > 0 ? "已更换" : "保持原值"), "CONFIG");
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
            LogStore.Add("CatHome4", 1, "LLM API 池配置已删除：" + id.ToString("D"), "CONFIG");
            return Results.Json(new { ok = true, apiConfigId = id.ToString("D") });
        }

        /// <summary>
        /// QQ Bot 池列表——GET /api/v1/qqbot-apis（secret 掩码展示；hasSecret 供前端判断是否已配置）。
        /// </summary>
        /// <returns>列表 JSON——version + items</returns>
        internal static IResult HandleQqBotApisGet()
        {
            CH_QqBotConfigStore store = null;
            DataBox.TryResolve<CH_QqBotConfigStore>(out store);
            List<object> items = new List<object>();
            if (store != null)
            {
                CH_QqBotConfig[] configs = store.GetAll();
                for (int i = 0; i < configs.Length; i++)
                {
                    CH_QqBotConfig c = configs[i];
                    string secret = store.GetSecret(c.QqBotId);
                    string secretShown;
                    if (secret.Length > 0)
                    {
                        secretShown = MaskApiKey(secret);
                    }
                    else
                    {
                        secretShown = "";
                    }
                    items.Add(new
                    {
                        qqBotId = c.QqBotId.ToString("D"),
                        displayName = c.DisplayName,
                        appId = c.AppId,
                        sandbox = c.Sandbox,
                        secret = secretShown,
                        hasSecret = secret.Length > 0
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
        /// QQ Bot 池新建——POST /api/v1/qqbot-apis（body: displayName/appId/secret）。
        /// </summary>
        /// <param name="ctx">HTTP 上下文</param>
        /// <returns>回执 JSON</returns>
        internal static async Task<IResult> HandleQqBotApisPost(HttpContext ctx)
        {
            string body = await ReadBodyText(ctx);
            string displayName = "";
            string appId = "";
            string secret = "";
            bool sandbox = true;
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(body))
                {
                    JsonElement root = doc.RootElement;
                    displayName = GetJsonString(root, "displayName");
                    appId = GetJsonString(root, "appId");
                    secret = GetJsonString(root, "secret");
                    sandbox = GetBoolProp(root, "sandbox");
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
            if (appId.Length == 0)
            {
                return Results.Json(new { ok = false, error = "appId 为空" });
            }
            CH_QqBotConfigStore store = null;
            DataBox.TryResolve<CH_QqBotConfigStore>(out store);
            if (store == null)
            {
                return Results.Json(new { ok = false, error = "配置池未绑定" });
            }
            CH_QqBotConfig config = new CH_QqBotConfig();
            config.QqBotId = Guid.NewGuid();
            config.DisplayName = displayName;
            config.AppId = appId;
            config.Sandbox = sandbox;
            store.Save(config, secret);
            LogStore.Add("CatHome4", 1, "QQ Bot 池新增配置「" + displayName + "」（" + config.QqBotId.ToString("D") + "）", "CONFIG");
            QQBotService.Refresh();
            return Results.Json(new { ok = true, qqBotId = config.QqBotId.ToString("D") });
        }

        /// <summary>
        /// QQ Bot 池编辑——POST /api/v1/qqbot-apis/edit（body: qqBotId + 字段；secret 空=保留原 secret）。
        /// </summary>
        /// <param name="ctx">HTTP 上下文</param>
        /// <returns>回执 JSON</returns>
        internal static async Task<IResult> HandleQqBotApisEdit(HttpContext ctx)
        {
            string body = await ReadBodyText(ctx);
            string qqBotId = "";
            string displayName = "";
            string appId = "";
            string secret = "";
            bool sandbox = true;
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(body))
                {
                    JsonElement root = doc.RootElement;
                    qqBotId = GetJsonString(root, "qqBotId");
                    displayName = GetJsonString(root, "displayName");
                    appId = GetJsonString(root, "appId");
                    secret = GetJsonString(root, "secret");
                    sandbox = GetBoolProp(root, "sandbox");
                }
            }
            catch (Exception)
            {
                return Results.Json(new { ok = false, error = "body 非 JSON" });
            }
            Guid id;
            if (!Guid.TryParse(qqBotId, out id) || id == Guid.Empty)
            {
                return Results.Json(new { ok = false, error = "qqBotId 非法" });
            }
            if (displayName.Length == 0)
            {
                return Results.Json(new { ok = false, error = "displayName 为空" });
            }
            if (appId.Length == 0)
            {
                return Results.Json(new { ok = false, error = "appId 为空" });
            }
            CH_QqBotConfigStore store = null;
            DataBox.TryResolve<CH_QqBotConfigStore>(out store);
            if (store == null)
            {
                return Results.Json(new { ok = false, error = "配置池未绑定" });
            }
            CH_QqBotConfig config = new CH_QqBotConfig();
            config.QqBotId = id;
            config.DisplayName = displayName;
            config.AppId = appId;
            config.Sandbox = sandbox;
            store.Save(config, secret);
            LogStore.Add("CatHome4", 1, "QQ Bot 池「" + displayName + "」已更新（" + id.ToString("D") + "），secret" + (secret.Length > 0 ? "已更换" : "保持原值"), "CONFIG");
            QQBotService.Refresh();
            return Results.Json(new { ok = true, qqBotId = id.ToString("D") });
        }

        /// <summary>
        /// QQ Bot 池删除——POST /api/v1/qqbot-apis/delete（body: qqBotId；普通配置 + secret 同删）。
        /// </summary>
        /// <param name="ctx">HTTP 上下文</param>
        /// <returns>回执 JSON</returns>
        internal static async Task<IResult> HandleQqBotApisDelete(HttpContext ctx)
        {
            string body = await ReadBodyText(ctx);
            string qqBotId = "";
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(body))
                {
                    qqBotId = GetJsonString(doc.RootElement, "qqBotId");
                }
            }
            catch (Exception)
            {
                return Results.Json(new { ok = false, error = "body 非 JSON" });
            }
            Guid id;
            if (!Guid.TryParse(qqBotId, out id) || id == Guid.Empty)
            {
                return Results.Json(new { ok = false, error = "qqBotId 非法" });
            }
            CH_QqBotConfigStore store = null;
            DataBox.TryResolve<CH_QqBotConfigStore>(out store);
            if (store == null)
            {
                return Results.Json(new { ok = false, error = "配置池未绑定" });
            }
            if (!store.Delete(id))
            {
                return Results.Json(new { ok = false, error = "配置不存在" });
            }
            LogStore.Add("CatHome4", 1, "QQ Bot 池配置已删除：" + id.ToString("D"), "CONFIG");
            QQBotService.Refresh();
            return Results.Json(new { ok = true, qqBotId = id.ToString("D") });
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
            // 边界校验——catKey 不承载路径语义（拒绝分隔符/穿越/通配——GET 与 POST 对称收口）
            if (!IsSafeCatKey(catKey))
            {
                return Results.Json(new { ok = false, error = "cat 参数非法: " + catKey });
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
                        displayName = configs[i].DisplayName,
                        isDefault = configs[i].IsDefault
                    });
                }
            }
            // R2.3 QQ Bot 下拉清单——Bot 池全量（id + 显示名 + 占用者；A58 1:1）
            CH_QqBotConfigStore qqStore = null;
            DataBox.TryResolve<CH_QqBotConfigStore>(out qqStore);
            List<object> qqbotOptions = new List<object>();
            if (qqStore != null)
            {
                CH_QqBotConfig[] qqConfigs = qqStore.GetAll();
                for (int i = 0; i < qqConfigs.Length; i++)
                {
                    // A58——boundCat = 除本猫外的占用者显示名（前端据此禁用已绑他猫的 Bot；后端保存时仍显式校验）
                    qqbotOptions.Add(new
                    {
                        qqBotId = qqConfigs[i].QqBotId.ToString("D"),
                        displayName = qqConfigs[i].DisplayName,
                        boundCat = FindQqBotBindingOwner(catKey, qqConfigs[i].QqBotId.ToString("D"))
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
                packs = cfg.Packs,
                allPacks = BuildAllPacks(),
                qqbotId = cfg.QqBotId,
                qqbotEnable = cfg.QqBotEnable,
                enabledRoots = cfg.EnabledRoots,
                allRoots = BuildAllRootsJson(),
                allToolNames = GetAllToolNames(),
                allTools = GetAllToolsWithGroup(),
                apiOptions = apiOptions,
                qqbotOptions = qqbotOptions
            };
            return Results.Json(resp);
        }
        /// <summary>
        /// catKey 边界校验——cat.cfg 路径直拼前收口（拒绝路径分隔符/穿越/通配/空）。
        /// 语义：catKey 只能是单层目录名（id / 显示名 / majordomo）——不承载任何路径语义。
        /// </summary>
        /// <param name="key">寻址键</param>
        /// <returns>true=可安全拼入 sessions 目录</returns>
        private static bool IsSafeCatKey(string key)
        {
            if (key == null || key.Length == 0 || key.Length > 128)
            {
                return false;
            }
            if (key == "." || key == "..")
            {
                return false;
            }
            for (int i = 0; i < key.Length; i = i + 1)
            {
                char c = key[i];
                if (c < ' ')
                {
                    return false;
                }
                if (c == '/' || c == '\\' || c == ':' || c == '*' || c == '?' || c == '"' || c == '<' || c == '>' || c == '|')
                {
                    return false;
                }
            }
            return true;
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
            // A41-3 显式空值语义——字段出现且为空/全零 = 清空回默认端点（字段缺省 = 保留旧值）
            bool apiConfigIdPresent = false;
            string persona = "";
            string toolNames = "";
            string qqbotId = "";
            bool qqbotEnable = false;
            List<string> injectList = new List<string>();
            List<string> enabledRoots = new List<string>();
            List<string> packs = new List<string>();
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(body))
                {
                    JsonElement root = doc.RootElement;
                    catKey = GetJsonString(root, "cat");
                    JsonElement apiConfigIdEl;
                    if (root.TryGetProperty("apiConfigId", out apiConfigIdEl))
                    {
                        apiConfigIdPresent = true;
                    }
                    apiConfigId = GetJsonString(root, "apiConfigId");
                    persona = GetJsonString(root, "persona");
                    toolNames = GetJsonString(root, "toolNames");
                    qqbotId = GetJsonString(root, "qqbotId");
                    qqbotEnable = GetBoolProp(root, "qqbotEnable");
                    JsonElement rootsEl;
                    if (root.TryGetProperty("enabledRoots", out rootsEl) && rootsEl.ValueKind == JsonValueKind.Array)
                    {
                        for (int i = 0; i < rootsEl.GetArrayLength(); i++)
                        {
                            JsonElement item = rootsEl[i];
                            if (item.ValueKind == JsonValueKind.String)
                            {
                                string got = item.GetString();
                                if (got != null && got.Length > 0)
                                {
                                    enabledRoots.Add(got);
                                }
                            }
                        }
                    }
                    JsonElement packsEl;
                    if (root.TryGetProperty("packs", out packsEl) && packsEl.ValueKind == JsonValueKind.Array)
                    {
                        for (int i = 0; i < packsEl.GetArrayLength(); i = i + 1)
                        {
                            JsonElement item = packsEl[i];
                            if (item.ValueKind == JsonValueKind.String)
                            {
                                string got = item.GetString();
                                if (got != null && got.Length > 0)
                                {
                                    // 挂载清单——池内 key；未挂载/池外项在 pack 调用期校验（此处只做去空白归一）
                                    packs.Add(got.Trim());
                                }
                            }
                        }
                    }
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
                                    // 路径校验——只接受完整路径（绝对路径/id: 命名空间）；相对路径/非法格式不录入（莎拍板 2026-08-25）
                                    string trimmed = got.Trim();
                                    if (IsValidInjectPath(trimmed))
                                    {
                                        injectList.Add(trimmed);
                                    }
                                    else
                                    {
                                        LogStore.Add("CatHome4", 2, "猫配置：注入路径被拒绝（非法格式）" + got, "CONFIG");
                                    }
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
            // 边界校验——catKey 不承载路径语义（GET 与 POST 对称收口）
            if (!IsSafeCatKey(catKey))
            {
                return Results.Json(new { ok = false, error = "cat 参数非法: " + catKey });
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
            // A41-3——显式清空（空串/全零）回默认端点；显式非法值拒绝；字段缺省保留旧值
            if (apiConfigIdPresent)
            {
                string trimmedApiConfigId = apiConfigId.Trim();
                if (trimmedApiConfigId.Length == 0 || trimmedApiConfigId == Guid.Empty.ToString("D"))
                {
                    // 存全零串——与 SaveCatCfg 出口一致（读侧以 Guid.Empty 判默认端点，实时解析全局默认）
                    cfg.ApiConfigId = Guid.Empty.ToString("D");
                }
                else
                {
                    Guid parsed;
                    if (!Guid.TryParse(trimmedApiConfigId, out parsed) || parsed == Guid.Empty)
                    {
                        return Results.Json(new { ok = false, error = "apiConfigId 非法" });
                    }
                    cfg.ApiConfigId = trimmedApiConfigId;
                }
            }
            if (qqbotId.Length > 0)
            {
                Guid parsed;
                if (!Guid.TryParse(qqbotId, out parsed) || parsed == Guid.Empty)
                {
                    return Results.Json(new { ok = false, error = "qqbotId 非法" });
                }
                // A58 1:1 查重——同一 Bot 不得被两只猫绑定（入口面显式拒绝；运行时不做去重）
                string boundBy = FindQqBotBindingOwner(catKey, qqbotId);
                if (boundBy.Length > 0)
                {
                    return Results.Json(new { ok = false, error = "该 QQ Bot 已绑定猫「" + boundBy + "」——一只 Bot 只能绑一只猫" });
                }
                cfg.QqBotId = qqbotId;
            }
            cfg.QqBotEnable = qqbotEnable;
            cfg.Persona = persona;
            cfg.ToolNames = validToolNames;
            cfg.InjectList = injectList.ToArray();
            cfg.Packs = packs.ToArray();
            // 启用根校验——workspace 强制 + 全局池子集；非法 id 剔除
            cfg.EnabledRoots = ValidateEnabledRoots(enabledRoots.ToArray());
            SaveCatCfgData(catKey, cfg);
            // 运行时生效——入队主线程泵（注册表/会话面仅主线程触碰）
            _catQueue.Enqueue("catcfg.apply " + catKey);
            LogStore.Add("CatHome4", 1, "猫配置已受理：" + catKey + "（工具面 " + validToolNames + "）", "CONFIG");
            return Results.Json(new { ok = true, cat = catKey, toolNames = validToolNames });
        }

        /// <summary>
        /// 全局默认模板读取——GET /api/v1/cat-default（新猫默认配置管理面）。
        /// 返回：baseRole/defaultPersona/defaultToolNames/defaultInjectList + allToolNames（工具勾选清单）。
        /// </summary>
        /// <param name="ctx">HTTP 上下文</param>
        /// <returns>模板 JSON</returns>
        internal static IResult HandleCatDefaultGet(HttpContext ctx)
        {
            CatDefaultCfgData tpl = LoadCatDefaultCfg();
            string baseRole = FallbackBaseRole;
            string defaultPersona = "";
            string defaultToolNames = "";
            string[] defaultInjectList = new string[0];
            string[] defaultPacks = new string[0];
            if (tpl != null)
            {
                if (tpl.BaseRole != null)
                {
                    baseRole = tpl.BaseRole;
                }
                if (tpl.DefaultPersona != null)
                {
                    defaultPersona = tpl.DefaultPersona;
                }
                if (tpl.DefaultToolNames != null)
                {
                    defaultToolNames = tpl.DefaultToolNames;
                }
                if (tpl.DefaultInjectList != null)
                {
                    defaultInjectList = tpl.DefaultInjectList;
                }
                if (tpl.DefaultPacks != null)
                {
                    defaultPacks = tpl.DefaultPacks;
                }
            }
            var resp = new
            {
                ok = true,
                baseRole = baseRole,
                defaultPersona = defaultPersona,
                defaultToolNames = defaultToolNames,
                defaultInjectList = defaultInjectList,
                defaultPacks = defaultPacks,
                allToolNames = GetAllToolNames(),
                allTools = GetAllToolsWithGroup(),
                allPacks = BuildAllPacks()
            };
            return Results.Json(resp);
        }

        /// <summary>
        /// 全局默认模板写入——POST /api/v1/cat-default（body: baseRole/defaultPersona/defaultToolNames/defaultInjectList）。
        /// injectList 路径校验（只接受完整路径——非法不录入）；toolNames 写时校验；原子写落盘。
        /// 生效语义：新猫创建时继承（已创建猫不受影响）；baseRole 新会话生效（session.new 重注入）。
        /// </summary>
        /// <param name="ctx">HTTP 上下文</param>
        /// <returns>回执 JSON</returns>
        internal static async Task<IResult> HandleCatDefaultPost(HttpContext ctx)
        {
            string body = await ReadBodyText(ctx);
            string baseRole = "";
            string defaultPersona = "";
            string defaultToolNames = "";
            List<string> defaultInjectList = new List<string>();
            List<string> defaultPacks = new List<string>();
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(body))
                {
                    JsonElement root = doc.RootElement;
                    baseRole = GetJsonString(root, "baseRole");
                    defaultPersona = GetJsonString(root, "defaultPersona");
                    defaultToolNames = GetJsonString(root, "defaultToolNames");
                    JsonElement dPacksEl;
                    if (root.TryGetProperty("defaultPacks", out dPacksEl) && dPacksEl.ValueKind == JsonValueKind.Array)
                    {
                        for (int i = 0; i < dPacksEl.GetArrayLength(); i = i + 1)
                        {
                            JsonElement item = dPacksEl[i];
                            if (item.ValueKind == JsonValueKind.String)
                            {
                                string gotPack = item.GetString();
                                if (gotPack != null && gotPack.Length > 0)
                                {
                                    defaultPacks.Add(gotPack.Trim());
                                }
                            }
                        }
                    }
                    JsonElement injectEl;
                    if (root.TryGetProperty("defaultInjectList", out injectEl) && injectEl.ValueKind == JsonValueKind.Array)
                    {
                        for (int i = 0; i < injectEl.GetArrayLength(); i++)
                        {
                            JsonElement item = injectEl[i];
                            if (item.ValueKind == JsonValueKind.String)
                            {
                                string got = item.GetString();
                                if (got != null && got.Length > 0)
                                {
                                    // 路径校验——与 cat-config 同规（只接受完整路径；非法不录入）
                                    string trimmed = got.Trim();
                                    if (IsValidInjectPath(trimmed))
                                    {
                                        defaultInjectList.Add(trimmed);
                                    }
                                    else
                                    {
                                        LogStore.Add("CatHome4", 2, "全局默认模板：注入路径被拒绝（非法格式）" + got, "CONFIG");
                                    }
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
            CatDefaultCfgData data = new CatDefaultCfgData();
            data.BaseRole = baseRole;
            data.DefaultPersona = defaultPersona;
            data.DefaultToolNames = defaultToolNames;
            data.DefaultInjectList = defaultInjectList.ToArray();
            data.DefaultPacks = defaultPacks.ToArray();
            SaveCatDefaultCfg(data);
            LogStore.Add("CatHome4", 1, "全局默认模板已保存（注入 " + defaultInjectList.Count.ToString() + " 条）", "CONFIG");
            return Results.Json(new { ok = true });
        }

        /// <summary>受控根写入输入条目——POST /api/v1/workspace body 解析形态</summary>
        private sealed class WorkspaceRootInput
        {
            /// <summary>根标识</summary>
            public string Id;

            /// <summary>根路径</summary>
            public string Path;

            /// <summary>是否可写</summary>
            public bool Writable;

            /// <summary>认路注释（≤20 字——给 LLM 的语义提示；超长由宿主截断）</summary>
            public string Note = "";
        }

        /// <summary>
        /// 受控根读取——GET /api/v1/workspace（管理面：roots 编辑面数据源）。
        /// roots 是安全边界——LLM 工具面（config.*）保持只读；本端点仅管理员面（主端口）。
        /// </summary>
        /// <param name="ctx">HTTP 上下文</param>
        /// <returns>roots JSON</returns>
        internal static IResult HandleWorkspaceGet(HttpContext ctx)
        {
            WorkspaceConfig ws = null;
            DataBox.TryResolve<WorkspaceConfig>(out ws);
            if (ws == null)
            {
                return Results.Json(new { ok = false, error = "工作区配置未绑定" });
            }
            List<object> roots = new List<object>();
            for (int i = 0; i < ws.Roots.Length; i++)
            {
                roots.Add(new
                {
                    id = ws.Roots[i].Id,
                    path = ws.Roots[i].Path,
                    writable = ws.Roots[i].Writable,
                    note = ws.Roots[i].Note
                });
            }
            return Results.Json(new { ok = true, roots = roots });
        }

        /// <summary>
        /// 全局根池 JSON——供猫配置界面勾选（cat-config GET allRoots 字段；workspace 强制必选）。
        /// </summary>
        /// <returns>根池数组 [{id,path,writable}]</returns>
        internal static object[] BuildAllRootsJson()
        {
            WorkspaceConfig ws = null;
            DataBox.TryResolve<WorkspaceConfig>(out ws);
            List<object> roots = new List<object>();
            if (ws != null)
            {
                for (int i = 0; i < ws.Roots.Length; i++)
                {
                    roots.Add(new
                    {
                        id = ws.Roots[i].Id,
                        path = ws.Roots[i].Path,
                        writable = ws.Roots[i].Writable,
                        note = ws.Roots[i].Note
                    });
                }
            }
            return roots.ToArray();
        }

        /// <summary>
        /// 猫启用根校验——workspace 强制必选 + 全局池子集；非法 id 剔除、去重、保序。
        /// 语义（收紧·X）：空数组 = 仅常驻系统根（物化为显式清单——与 ResolveCatRootEntries 同源）；非空 = workspace 强制 + 用户子集。
        /// </summary>
        /// <param name="ids">前端提交的启用根 id 数组（空=仅常驻根）</param>
        /// <returns>合法启用根 id 数组（至少含常驻根；池不可用时为空）</returns>
        internal static string[] ValidateEnabledRoots(string[] ids)
        {
            // 空白名单（未配置/全不勾）= 仅常驻根——物化落盘，消除"未配置"二义（前端已如实渲染）
            if (ids == null || ids.Length == 0)
            {
                string residentOnly = ResolveResidentRootId();
                if (residentOnly.Length == 0)
                {
                    return new string[0];
                }
                return new string[] { residentOnly };
            }
            WorkspaceConfig ws = null;
            DataBox.TryResolve<WorkspaceConfig>(out ws);
            if (ws == null || ws.Roots == null || ws.Roots.Length == 0)
            {
                return new string[0];
            }
            List<string> result = new List<string>();
            // 全局池合法 id 集合
            List<string> pool = new List<string>();
            for (int i = 0; i < ws.Roots.Length; i++)
            {
                string rootId = ws.Roots[i].Id;
                if (rootId == null || rootId.Length == 0)
                {
                    continue;
                }
                pool.Add(rootId);
            }
            // workspace 强制——用户说必选不可取消；兼容旧配置 id=runtime
            string wsId = "";
            for (int p = 0; p < pool.Count; p++)
            {
                if (string.Equals(pool[p], "workspace", StringComparison.OrdinalIgnoreCase))
                {
                    wsId = pool[p];
                    break;
                }
            }
            if (wsId.Length == 0)
            {
                for (int p = 0; p < pool.Count; p++)
                {
                    if (string.Equals(pool[p], "runtime", StringComparison.OrdinalIgnoreCase))
                    {
                        wsId = pool[p];
                        break;
                    }
                }
            }
            if (wsId.Length > 0)
            {
                result.Add(wsId);
            }
            // 用户选择子集——只保留全局池内 id，去重
            for (int i = 0; i < ids.Length; i++)
            {
                string id = ids[i];
                if (id == null || id.Length == 0)
                {
                    continue;
                }
                if (string.Equals(id, wsId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                bool inPool = false;
                for (int p = 0; p < pool.Count; p++)
                {
                    if (string.Equals(pool[p], id, StringComparison.OrdinalIgnoreCase))
                    {
                        inPool = true;
                        break;
                    }
                }
                bool already = false;
                for (int r = 0; r < result.Count; r++)
                {
                    if (string.Equals(result[r], id, StringComparison.OrdinalIgnoreCase))
                    {
                        already = true;
                        break;
                    }
                }
                if (inPool && !already)
                {
                    result.Add(id);
                }
            }
            return result.ToArray();
        }

        /// <summary>
        /// 常驻系统根 id——workspace（旧配置 runtime）；池不可用/无匹配返回空串。
        /// </summary>
        /// <returns>常驻根 id（按池内原大小写）</returns>
        private static string ResolveResidentRootId()
        {
            WorkspaceConfig ws = null;
            DataBox.TryResolve<WorkspaceConfig>(out ws);
            if (ws == null || ws.Roots == null)
            {
                return "";
            }
            string legacy = "";
            for (int i = 0; i < ws.Roots.Length; i++)
            {
                string rootId = ws.Roots[i].Id;
                if (rootId == null || rootId.Length == 0)
                {
                    continue;
                }
                if (string.Equals(rootId, "workspace", StringComparison.OrdinalIgnoreCase))
                {
                    return rootId;
                }
                if (legacy.Length == 0 && string.Equals(rootId, "runtime", StringComparison.OrdinalIgnoreCase))
                {
                    legacy = rootId;
                }
            }
            return legacy;
        }

        /// <summary>
        /// 受控根写入——POST /api/v1/workspace（body: {roots:[{id,path,writable}]}）。
        /// 校验：id 安全标识符不重复 + path 非空绝对路径 + 目录存在；空列表=重置默认（Load 兜底单根 runtime）。
        /// 生效语义：落盘 + 重启生效（roots 是 Bootstrap 一次性读取——低频事件，莎拍板 2026-08-25）。
        /// </summary>
        /// <param name="ctx">HTTP 上下文</param>
        /// <returns>回执 JSON</returns>
        internal static async Task<IResult> HandleWorkspacePost(HttpContext ctx)
        {
            string body = await ReadBodyText(ctx);
            List<WorkspaceRootInput> rootsIn = new List<WorkspaceRootInput>();
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(body))
                {
                    JsonElement root = doc.RootElement;
                    JsonElement rootsEl;
                    if (root.TryGetProperty("roots", out rootsEl) && rootsEl.ValueKind == JsonValueKind.Array)
                    {
                        for (int i = 0; i < rootsEl.GetArrayLength(); i++)
                        {
                            JsonElement item = rootsEl[i];
                            WorkspaceRootInput input = new WorkspaceRootInput();
                            input.Id = GetJsonString(item, "id").Trim();
                            input.Path = GetJsonString(item, "path").Trim();
                            input.Writable = true;
                            JsonElement wEl;
                            if (item.TryGetProperty("writable", out wEl) && wEl.ValueKind == JsonValueKind.False)
                            {
                                input.Writable = false;
                            }
                            input.Note = WorkspaceConfig.TruncateNote(GetJsonString(item, "note"));
                            rootsIn.Add(input);
                        }
                    }
                }
            }
            catch (Exception)
            {
                return Results.Json(new { ok = false, error = "body 非 JSON" });
            }
            // [段1] 校验——id 安全标识符不重复 / path 绝对路径 + 目录存在；空列表=重置默认（Load 兜底单根 runtime）
            List<string> ids = new List<string>();
            for (int i = 0; i < rootsIn.Count; i++)
            {
                WorkspaceRootInput input = rootsIn[i];
                if (input.Id.Length == 0)
                {
                    return Results.Json(new { ok = false, error = "roots[" + i.ToString() + "] id 为空" });
                }
                for (int c = 0; c < input.Id.Length; c++)
                {
                    char ch = input.Id[c];
                    bool ok = (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9') || ch == '_';
                    if (!ok)
                    {
                        return Results.Json(new { ok = false, error = "roots[" + i.ToString() + "] id 非法（仅字母/数字/下划线）: " + input.Id });
                    }
                }
                if (ids.Contains(input.Id))
                {
                    return Results.Json(new { ok = false, error = "roots id 重复: " + input.Id });
                }
                ids.Add(input.Id);
                if (input.Path.Length == 0)
                {
                    return Results.Json(new { ok = false, error = "roots[" + i.ToString() + "] path 为空" });
                }
                if (!Path.IsPathRooted(input.Path))
                {
                    return Results.Json(new { ok = false, error = "roots[" + i.ToString() + "] path 非绝对路径: " + input.Path });
                }
                if (!Directory.Exists(input.Path))
                {
                    return Results.Json(new { ok = false, error = "roots[" + i.ToString() + "] 目录不存在: " + input.Path });
                }
            }
            // [段2] 落盘——保留 inject 字段（读旧文件；M2 后宿主不消费但结构保留）
            string wsPath = Path.Combine(_dataRoot, "Data", "config", "workspace.json");
            string injectJson = "[]";
            if (File.Exists(wsPath))
            {
                try
                {
                    using (JsonDocument old = JsonDocument.Parse(File.ReadAllText(wsPath)))
                    {
                        JsonElement injectEl;
                        if (old.RootElement.TryGetProperty("inject", out injectEl))
                        {
                            injectJson = injectEl.GetRawText();
                        }
                    }
                }
                catch (Exception)
                {
                    injectJson = "[]";
                }
            }
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append("{\"roots\":[");
            for (int i = 0; i < rootsIn.Count; i++)
            {
                WorkspaceRootInput input = rootsIn[i];
                if (i > 0)
                {
                    sb.Append(",");
                }
                string norm = WorkspaceConfig.NormalizeRoot(input.Path);
                sb.Append("{\"id\":");
                sb.Append(JsonUtil.Serialize(input.Id));
                sb.Append(",\"path\":");
                sb.Append(JsonUtil.Serialize(norm.Replace('\\', '/')));
                sb.Append(",\"writable\":");
                if (input.Writable)
                {
                    sb.Append("true");
                }
                else
                {
                    sb.Append("false");
                }
                if (input.Note.Length > 0)
                {
                    sb.Append(",\"note\":");
                    sb.Append(JsonUtil.Serialize(input.Note));
                }
                sb.Append("}");
            }
            sb.Append("],\"inject\":");
            sb.Append(injectJson);
            sb.Append("}");
            try
            {
                ConfigStore.AtomicWrite(wsPath, sb.ToString());
            }
            catch (Exception ex)
            {
                return Results.Json(new { ok = false, error = "落盘失败: " + ex.Message });
            }
            LogStore.Add("CatHome4", 1, "工作区已保存：" + rootsIn.Count.ToString() + " 个根（重启生效）", "CONFIG");
            return Results.Json(new { ok = true, roots = rootsIn.Count });
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
            // M4e 猫级白名单——配置变更后重建猫文件系统（启用根子集）
            ApplyCatRoots(key);
            if (key == "majordomo")
            {
                // 默认猫——静态面更新（session.new 重注入消费）
                _chatBridge.DefaultPersona = persona;
                _chatBridge.DefaultInjectList = injectList;
                _chatBridge.DefaultToolSpecs = specs;
                _chatBridge.DefaultQqBotId = ResolveQqBotId(cfg);
                _chatBridge.DefaultQqBotEnable = cfg.QqBotEnable;
                Guid newApi = ResolveApiConfigId(cfg);
                if (_defaultApiConfigId != newApi)
                {
                    _defaultApiConfigId = newApi;
                    _chatBridge.DefaultSession.SwapLlmRuntime(new DeepSeekLlmRuntime(_apiStore, newApi, _globalConfig));
                    LogStore.Add("CatHome4", 1, "majordomo 配置生效：LLM 端点切换为 " + newApi.ToString("D"), "CONFIG");
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
            cat.QqBotId = ResolveQqBotId(cfg);
            cat.QqBotEnable = cfg.QqBotEnable;
            // M4e 猫级白名单——运行时猫文件系统已在上方 ApplyCatRoots 重建（含本分支）
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
                LogStore.Add("CatHome4", 1, "猫「" + cat.DisplayName + "」配置生效：LLM 端点切换为 " + newApiId.ToString("D"), "CONFIG");
            }
            return "catcfg.apply | " + cat.DisplayName + " | 已生效（前文项新会话生效）";
        }

        /// <summary>
        /// 解析 cat.cfg 的 qqbot 配置身份——缺省 Guid.Empty=未绑定。
        /// </summary>
        /// <param name="cfg">配置数据</param>
        /// <returns>qqbot 配置身份</returns>
        private static Guid ResolveQqBotId(CatCfgData cfg)
        {
            Guid id = Guid.Empty;
            if (cfg != null && cfg.QqBotId != null && cfg.QqBotId.Length > 0)
            {
                Guid parsed;
                if (Guid.TryParse(cfg.QqBotId, out parsed) && parsed != Guid.Empty)
                {
                    id = parsed;
                }
            }
            return id;
        }

        /// <summary>
        /// 解析 cat.cfg 的 API 配置身份——缺省 Guid.Empty=默认端点语义（Cat 可选配置；未配置走默认端点）。
        /// </summary>
        /// <param name="cfg">配置数据</param>
        /// <returns>API 配置身份</returns>
        private static Guid ResolveApiConfigId(CatCfgData cfg)
        {
            Guid id = Guid.Empty;
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
        /// <param name="data">配置数据（用户配置面——运行态 running/port 不在此落盘）</param>
        private static void SaveCatCfgData(string id, CatCfgData data)
        {
            string dir = Path.Combine(_dataRoot, "Data", "sessions", id);
            string path = Path.Combine(dir, "cat.cfg");
            var payload = new
            {
                id = data.Id,
                displayName = data.DisplayName,
                apiConfigId = data.ApiConfigId,
                persona = data.Persona,
                // M2c 写时校验——序列化前比对内置清单过滤非法名（外部损坏防御：持久化面只落合法名）
                toolNames = ValidateToolNames(data.ToolNames),
                injectList = data.InjectList,
                qqbotId = data.QqBotId,
                qqbotEnable = data.QqBotEnable,
                enabledRoots = data.EnabledRoots,
                packs = data.Packs
            };
            try
            {
                ConfigStore.AtomicWrite(path, JsonUtil.Serialize(payload));
            }
            catch (Exception ex)
            {
                LogStore.Add("CatHome4", 2, "cat.cfg 写入失败：" + ex.Message, "CHAT");
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
        /// <summary>
        /// majordomo 默认猫 cat.cfg 补建——缺失时按全局默认模板创建（新用户无 cfg 是必然态；模板值 = 初次部署状态权威）。
        /// 已存在不覆盖——尊重用户已保存配置；模板缺失回退空默认（与运行时缺省回退语义一致）。
        /// </summary>
        internal static void EnsureMajordomoCfg()
        {
            string path = Path.Combine(_dataRoot, "Data", "sessions", "majordomo", "cat.cfg");
            if (File.Exists(path))
            {
                return;
            }

            CatDefaultCfgData tpl = LoadCatDefaultCfg();
            CatCfgData data = new CatCfgData();
            data.Id = "majordomo";
            data.DisplayName = "majordomo";
            data.Running = false;
            data.Port = 0;
            data.ApiConfigId = "";
            data.QqBotId = "";
            data.QqBotEnable = false;
            data.EnabledRoots = null;
            if (tpl != null)
            {
                data.Persona = tpl.DefaultPersona != null ? tpl.DefaultPersona : "";
                data.ToolNames = tpl.DefaultToolNames != null ? tpl.DefaultToolNames : "";
                data.InjectList = tpl.DefaultInjectList != null ? tpl.DefaultInjectList : new string[0];
            }
            else
            {
                data.Persona = "";
                data.ToolNames = "";
                data.InjectList = new string[0];
            }

            SaveCatCfgData("majordomo", data);
            LogStore.Add("CatHome4", 1, "majordomo cat.cfg 缺失——已按默认模板补建", "CONFIG");
        }
    }
}

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
                // [段1] 生效档案优先——密钥随档案（llm.apiKey.{profileId}）
                if (_activeProfileId.Length > 0)
                {
                    string profileKey = CredentialStore.Get("llm.apiKey." + _activeProfileId);
                    if (profileKey.Length > 0)
                    {
                        return profileKey;
                    }
                }
                // [段2] 回落：任意档案密钥——active 未设/无密钥时找第一个有密钥的档案（M3.4：重启无 Key 兜底）
                lock (_profiles)
                {
                    for (int i = 0; i < _profiles.Count; i = i + 1)
                    {
                        string anyKey = CredentialStore.Get("llm.apiKey." + _profiles[i].ProfileId);
                        if (anyKey.Length > 0)
                        {
                            return anyKey;
                        }
                    }
                }
                // [段3] 回落单配置——旧键兼容（llm.apiKey）
                return CredentialStore.Get("llm.apiKey");
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
            // 凭证隔离——API Key 不入 DataBox（Capture 全量快照不暴露；安全审查项 P0-6）
            CredentialStore.Set("llm.apiKey", apiKey == null ? "" : apiKey.Trim());
            CredentialStore.SavePersisted();
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
/// 配置档案表——当前全部 LLM API 档案（普通配置，不含密钥）
/// </summary>
private static readonly System.Collections.Generic.List<LlmProfile> _profiles = new System.Collections.Generic.List<LlmProfile>(); 
/// <summary>
/// 当前生效档案 Id——空=未指定（回落单配置模式）
/// </summary>
 private  static  string  _activeProfileId  =  "" ;  
/// <summary>
/// 档案持久化存储——宿主 ConfigureProfileStore 注入（null=不持久化）
/// </summary>
 private  static  ConfigStore ? _profileStore ;  
/// <summary>
/// 绑定档案持久化存储——宿主启动时调用（Load 已存档案）
/// </summary>
/// <param name = "store">ConfigStore 实例（可空=关闭持久化）</param>
 public  static  void  ConfigureProfileStore ( ConfigStore ? store ) {
            // 持久化存储绑定——null/空 = AppDataConfig 默认（%LOCALAPPDATA%/Mau_wls/CatHome4/llm.cfg）
            _profileStore = store != null ? store : AppDataConfig.Store;
            // 凭证内存加载——llm.apiKey.* 键（启动时同步持久化密钥）
            CredentialStore.LoadPersisted();
            string profilesJson = _profileStore.Get("profiles", "");
            if (profilesJson.Length > 0)
            {
                try
                {
                    System.Text.Json.JsonSerializerOptions options = new System.Text.Json.JsonSerializerOptions();
                    options.IncludeFields = true;
                    LlmProfile[]? loaded = System.Text.Json.JsonSerializer.Deserialize<LlmProfile[]>(profilesJson, options);
                    if (loaded != null)
                    {
                        _profiles.Clear();
                        for (int i = 0; i < loaded.Length; i = i + 1)
                        {
                            _profiles.Add(loaded[i]);
                        }
                    }
                }
                catch
                {
                    // 档案损坏——保持空表（防御式）
                }
            }
            _activeProfileId = _profileStore.Get("active", "");
            AuditStore.Default?.Record("LlmBridge", "cfg.load", -1, new AuditProp[] {
                new AuditProp("profiles", _profiles.Count.ToString()),
                new AuditProp("active", _activeProfileId)
            });
        }/// <summary>
/// 全部档案——只读拷贝（无密钥）
/// </summary>
/// <returns>档案数组</returns>
 public  static  LlmProfile [ ]  GetAllProfiles ( ) { lock  ( _profiles ) { LlmProfile [ ]  copy  =  new  LlmProfile [ _profiles . Count ] ;  for  ( int  i  =  0 ;  i < _profiles . Count ;  i  =  i + 1 ) { copy [ i ]  =  CopyProfile ( _profiles [ i ] ) ;  } return  copy ;  } } 
/// <summary>
/// 当前生效档案 Id
/// </summary>
 public  static  string  ActiveProfileId { get { return  _activeProfileId ;  } } 
/// <summary>
/// 按 Id 读取档案（无密钥）
/// </summary>
/// <param name = "profileId">档案 Id</param>
/// <returns>档案副本；不存在返回 null</returns>
 public  static  LlmProfile ? GetProfile ( string  profileId ) { lock  ( _profiles ) { for  ( int  i  =  0 ;  i < _profiles . Count ;  i  =  i + 1 ) { if  ( _profiles [ i ] . ProfileId == profileId ) { return  CopyProfile ( _profiles [ i ] ) ;  } } return  null ;  } } 
/// <summary>
/// 保存档案——新增或更新（普通配置落盘；密钥独立走 CredentialStore）
/// </summary>
/// <param name = "profile">档案（ProfileId 空=新建生成）</param>
/// <returns>最终档案 Id</returns>
 public  static  string  SaveProfile ( LlmProfile  profile ) {
            if (profile == null)
            {
                throw new ArgumentNullException("profile");
            }
            if (string.IsNullOrWhiteSpace(profile.ProfileId))
            {
                profile.ProfileId = Guid.NewGuid().ToString("N");
            }
            if (string.IsNullOrWhiteSpace(profile.DisplayName))
            {
                profile.DisplayName = "未命名";
            }
            string secret = profile.Secret != null ? profile.Secret.Trim() : "";
            profile.Secret = "";
            if (secret.Length > 0)
            {
                CredentialStore.Set("llm.apiKey." + profile.ProfileId, secret);
                CredentialStore.SavePersisted();
            }
            lock (_profiles)
            {
                bool replaced = false;
                for (int i = 0; i < _profiles.Count; i = i + 1)
                {
                    if (_profiles[i].ProfileId == profile.ProfileId)
                    {
                        _profiles[i] = CopyProfile(profile);
                        replaced = true;
                        break;
                    }
                }
                if (!replaced)
                {
                    _profiles.Add(CopyProfile(profile));
                }
                // 生效档案兜底——active 空时首个档案自动生效（M3.4：保存 Key 后重启无 Key——active 空导致 ApiKey 回落旧键）
                if (_activeProfileId.Length == 0)
                {
                    _activeProfileId = profile.ProfileId;
                }
                PersistProfilesLocked();
            }
            AuditStore.Default?.Record("LlmBridge", "cfg.change", -1, new AuditProp[] {
                new AuditProp("profileId", profile.ProfileId),
                new AuditProp("action", "save"),
                new AuditProp("secret", secret.Length > 0 ? "configured" : "missing")
            });
            return profile.ProfileId;
        }/// <summary>
/// 删除档案——同时清除其密钥
/// </summary>
/// <param name = "profileId">档案 Id</param>
/// <returns>true=删除成功</returns>
 public  static  bool  DeleteProfile ( string  profileId ) {
            lock (_profiles)
            {
                for (int i = 0; i < _profiles.Count; i = i + 1)
                {
                    if (_profiles[i].ProfileId == profileId)
                    {
                        _profiles.RemoveAt(i);
                        PersistProfilesLocked();
                        CredentialStore.Set("llm.apiKey." + profileId, "");
                        CredentialStore.SavePersisted();
                        if (_activeProfileId == profileId)
                        {
                            _activeProfileId = "";
                            PersistProfilesLocked();
                        }
                        return true;
                    }
                }
            }
            return false;
        }/// <summary>
/// 切换生效档案——应用其端点/模型到 DataBox scope "llm"（密钥随 ApiKey 属性切换）
/// </summary>
/// <param name = "profileId">档案 Id</param>
/// <returns>true=切换成功</returns>
 public  static  bool  SetActiveProfile ( string  profileId ) { lock  ( _profiles ) { for  ( int  i  =  0 ;  i < _profiles . Count ;  i  =  i + 1 ) { if  ( _profiles [ i ] . ProfileId == profileId ) { _activeProfileId  =  profileId ;  if  ( ! string . IsNullOrWhiteSpace ( _profiles [ i ] . Endpoint ) ) { DataBox . Set < string > ( "llm" ,  "endpoint" ,  _profiles [ i ] . Endpoint ) ;  } if  ( ! string . IsNullOrWhiteSpace ( _profiles [ i ] . Model ) ) { DataBox . Set < string > ( "llm" ,  "model" ,  _profiles [ i ] . Model ) ;  } PersistProfilesLocked ( ) ;  return  true ;  } } } return  false ;  } 
/// <summary>
/// 写入档案密钥——CredentialStore 隔离（键 llm.apiKey.{profileId}；空=清除）
/// </summary>
/// <param name = "profileId">档案 Id</param>
/// <param name = "key">API Key（空=清除）</param>
 public  static  void  SetProfileSecret ( string  profileId ,  string  key ) {
            CredentialStore.Set("llm.apiKey." + profileId, key == null ? "" : key.Trim());
            CredentialStore.SavePersisted();
            AuditStore.Default?.Record("LlmBridge", "cfg.change", -1, new AuditProp[] {
                new AuditProp("profileId", profileId),
                new AuditProp("action", "set_secret"),
                new AuditProp("secret", key != null && key.Trim().Length > 0 ? "configured" : "missing")
            });
        }/// <summary>
/// 读取档案密钥——CredentialStore 隔离（不存在返回空串）
/// </summary>
/// <param name = "profileId">档案 Id</param>
/// <returns>密钥或空串</returns>
 public  static  string  GetProfileSecret ( string  profileId ) { return  CredentialStore . Get ( "llm.apiKey." + profileId ) ;  } 
/// <summary>
/// 档案持久化——锁内调用（JSON 数组 + 生效 Id）
/// </summary>
 private  static  void  PersistProfilesLocked ( ) { if  ( _profileStore == null ) { return ;  } System . Text . Json . JsonSerializerOptions  options  =  new  System . Text . Json . JsonSerializerOptions ( ) ;  options . IncludeFields  =  true ;  string  json  =  System . Text . Json . JsonSerializer . Serialize ( _profiles ,  options ) ;  _profileStore . Set ( "profiles" ,  json ) ;  _profileStore . Set ( "active" ,  _activeProfileId ) ;  _profileStore . Save ( ) ;  } 
/// <summary>
/// 复制档案
/// </summary>
/// <param name = "source">源档案</param>
/// <returns>副本</returns>
 private  static  LlmProfile  CopyProfile ( LlmProfile  source ) { LlmProfile  copy  =  new  LlmProfile ( ) ;  copy . ProfileId  =  source . ProfileId ;  copy . DisplayName  =  source . DisplayName ;  copy . ApiType  =  source . ApiType ;  copy . Endpoint  =  source . Endpoint ;  copy . Model  =  source . Model ;  return  copy ;  }

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

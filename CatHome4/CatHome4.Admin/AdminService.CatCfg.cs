using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Mau.Runtime;
using CatHome4.Http;
using Mau.Providers;
using CH4;

namespace CatHome4.Admin
{
    /// <summary>
    /// Program 多猫持久化面分部——CatEntry/CatCfgData 实体 + cat.cfg 读写与文件清理（P9.3b 拆分自 Program.Cats.cs）。
    /// 线程模型：处理函数仅主线程调用（HTTP 线程经 P9.3c PumpCatQueues 入队泵消费）。
    /// </summary>
    internal static partial class AdminService
    {
        /// <summary>
        /// 猫实体——注册表条目（会话 + 外观层 + 持久化态）。
        /// </summary>
        private sealed class CatEntry
        {
            /// <summary>会话唯一 ID——创建时间戳注入（cat.cfg 持久化）</summary>
            public string Id;

            /// <summary>显示名——用户输入（cat.new 参数）</summary>
            public string DisplayName;

            /// <summary>运行态——true=HttpHost 监听中</summary>
            public bool Running;

            /// <summary>监听端口——静默态 0</summary>
            public int Port;

            /// <summary>会话实体——PumpSessions 轮转推进</summary>
            public ChatSession Session;

            /// <summary>HTTP 外观层——启动态非空</summary>
            public HttpHost Host;

            /// <summary>HTTP 线程 Chat 指令入队面——主线程泵消费（P9.3c PumpCatQueues）</summary>
            public ConcurrentQueue<string> PendingChat;

            /// <summary>HTTP 线程 Note 指令入队面——主线程泵消费（M4c note.add）</summary>
            public ConcurrentQueue<string> PendingNote;

            /// <summary>HTTP 线程会话指令入队面——主线程泵消费（P6b session.rollback/fork）</summary>
            public ConcurrentQueue<string> PendingSessionCmd;

            /// <summary>每猫配置存储——sessions/&lt;id&gt;/config.cfg（P9.4 per-cat 路由；ConfigStoreRegistry 注册）</summary>
            public ConfigStore Config;

            /// <summary>LLM API 配置身份——cat.cfg 持久化；缺省回退内置 DeepSeek（M1b）</summary>
            public Guid ApiConfigId;

            /// <summary>API 配置解析副本——M1c 每猫 Runtime 构造消费</summary>
            public CH_LlmApiConfig ApiConfig;

            /// <summary>角色段——cat.cfg 持久化（M2b；空=无角色段）</summary>
            public string Persona;

            /// <summary>工具名单原始串——cat.cfg 持久化（M2c；空=全量保底）</summary>
            public string ToolNames;

            /// <summary>前文注入清单——cat.cfg 持久化（M2d；空=不注入）</summary>
            public string[] InjectList;

            /// <summary>qqbot 配置身份——cat.cfg 持久化（R2.3；Guid.Empty=未绑定）</summary>
            public Guid QqBotId;

            /// <summary>qqbot 启用标志——cat.cfg 持久化（R2.3；false=不注入不转发）</summary>
            public bool QqBotEnable;

            /// <summary>工具声明面——M2c 裁剪后（session.new 重注入复用）</summary>
            public ToolSpec[] ToolSpecs;

            /// <summary>session.new 请求标志——HTTP 线程置位/主线程泵消费（M2d 按猫重注入）</summary>
            public bool SessionNewRequested;
        }

        /// <summary>
        /// cat.cfg 数据形态——sessions/&lt;id&gt;/cat.cfg（防御式读写；原子写落盘）。
        /// </summary>
        internal sealed class CatCfgData
        {
            /// <summary>会话 ID</summary>
            public string Id { get; set; }

            /// <summary>显示名</summary>
            public string DisplayName { get; set; }

            /// <summary>运行态——启动扫描拉起依据</summary>
            public bool Running { get; set; }

            /// <summary>监听端口——静默态 0</summary>
            public int Port { get; set; }

            /// <summary>LLM API 配置身份——缺省空串（回退内置 DeepSeek）</summary>
            public string ApiConfigId { get; set; }

            /// <summary>角色段——system prompt 注入后追加（M2b；空=无角色段）</summary>
            public string Persona { get; set; }

            /// <summary>工具名单——逗号清单（*=全部/空=全量保底；读时比对内置清单过滤非法名）</summary>
            public string ToolNames { get; set; }

            /// <summary>前文注入清单——文件寻址数组（id:相对路径；空=不注入）</summary>
            public string[] InjectList { get; set; }

            /// <summary>qqbot 配置身份——缺省空串（未绑定）</summary>
            public string QqBotId { get; set; }

            /// <summary>qqbot 启用标志——缺省 false</summary>
            public bool QqBotEnable { get; set; }

            /// <summary>启用根 id 清单——全局根池子集（null/空=全量；workspace 强制必选不可取消）</summary>
            public string[] EnabledRoots { get; set; }
        }

        /// <summary>
        /// 新猫默认模板数据形态——Data/config/cat-default.cfg（全局配置：基础角色段 + 新猫三字段默认值）。
        /// 独立于基座配置群——LLM/猫域配置不混入基座（莎拍板 2026-08-25）。
        /// </summary>
        internal sealed class CatDefaultCfgData
        {
            /// <summary>基础角色段——所有猫系统提示词首行（空=无基础角色行；模板缺失回退内置文案）</summary>
            public string BaseRole { get; set; }

            /// <summary>新猫默认 persona（空=无角色段）</summary>
            public string DefaultPersona { get; set; }

            /// <summary>新猫默认工具名单（空=全量保底）</summary>
            public string DefaultToolNames { get; set; }

            /// <summary>新猫默认前文注入清单（完整路径数组；空=不注入）</summary>
            public string[] DefaultInjectList { get; set; }
        }

        /// <summary>内置基础角色段——模板缺失时回退（行为不倒退）</summary>
        internal const string FallbackBaseRole = "你是 MajorDomoCat——CH4 自举宿主的管理员对话中枢（P8.5 会话配置化）。";

        /// <summary>
        /// 全局默认模板路径——Data/config/cat-default.cfg。
        /// </summary>
        /// <returns>模板文件绝对路径</returns>
        private static string GetCatDefaultPath()
        {
            return Path.Combine(_dataRoot, "Data", "config", "cat-default.cfg");
        }

        /// <summary>
        /// 全局默认模板读取——防御式解析（损坏/不存在返回 null）。
        /// </summary>
        /// <returns>模板数据；不存在/损坏 null</returns>
        internal static CatDefaultCfgData LoadCatDefaultCfg()
        {
            try
            {
                string path = GetCatDefaultPath();
                if (!File.Exists(path))
                {
                    return null;
                }
                string json = File.ReadAllText(path);
                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    JsonElement root = doc.RootElement;
                    CatDefaultCfgData data = new CatDefaultCfgData();
                    data.BaseRole = GetStringProp(root, "baseRole");
                    data.DefaultPersona = GetStringProp(root, "defaultPersona");
                    data.DefaultToolNames = GetStringProp(root, "defaultToolNames");
                    data.DefaultInjectList = GetStringArrayProp(root, "defaultInjectList");
                    return data;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// 全局默认模板原子写——ConfigStore.AtomicWrite（与 cat.cfg 同源）。
        /// </summary>
        /// <param name="data">模板数据</param>
        private static void SaveCatDefaultCfg(CatDefaultCfgData data)
        {
            var payload = new
            {
                baseRole = data.BaseRole,
                defaultPersona = data.DefaultPersona,
                defaultToolNames = ValidateToolNames(data.DefaultToolNames),
                defaultInjectList = data.DefaultInjectList
            };
            try
            {
                ConfigStore.AtomicWrite(GetCatDefaultPath(), JsonSerializer.Serialize(payload));
            }
            catch (Exception ex)
            {
                LogStore.Add("CatHome4", 2, "全局默认模板写入失败：" + ex.Message, "CONFIG");
            }
        }

        /// <summary>
        /// 注入路径合法性校验——只接受完整路径：绝对路径（盘符/UNC）或 id: 命名空间（id 为安全标识符）。
        /// 相对路径/空 id/非法字符 → false（不录入——莎拍板 2026-08-25）。
        /// </summary>
        /// <param name="path">候选路径</param>
        /// <returns>合法 true</returns>
        private static bool IsValidInjectPath(string path)
        {
            if (path == null || path.Length == 0)
            {
                return false;
            }
            if (Path.IsPathRooted(path))
            {
                return true;
            }
            int colon = path.IndexOf(':');
            if (colon <= 0 || colon >= path.Length - 1)
            {
                return false;
            }
            string id = path.Substring(0, colon);
            for (int i = 0; i < id.Length; i++)
            {
                char c = id[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_';
                if (!ok)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// cat.cfg 读取——防御式解析（损坏/缺字段回退默认；不存在返回 null）。
        /// </summary>
        /// <param name="path">cfg 路径</param>
        /// <returns>配置数据；损坏 null</returns>
        internal static CatCfgData LoadCatCfg(string path)
        {
            try
            {
                string json = File.ReadAllText(path);
                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    JsonElement root = doc.RootElement;
                    CatCfgData data = new CatCfgData();
                    data.Id = GetStringProp(root, "id");
                    data.DisplayName = GetStringProp(root, "displayName");
                    data.Running = GetBoolProp(root, "running");
                    data.Port = GetIntProp(root, "port");
                    data.ApiConfigId = GetStringProp(root, "apiConfigId");
                    data.Persona = GetStringProp(root, "persona");
                    data.ToolNames = GetStringProp(root, "toolNames");
                    data.InjectList = GetStringArrayProp(root, "injectList");
                    data.QqBotId = GetStringProp(root, "qqbotId");
                    data.QqBotEnable = GetBoolProp(root, "qqbotEnable");
                    data.EnabledRoots = GetStringArrayProp(root, "enabledRoots");
                    return data;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// cat.cfg 原子写——ConfigStore.AtomicWrite（临时文件 + 改名；CH2 模式移植）。
        /// </summary>
        /// <param name="cat">猫实体</param>
        private static void SaveCatCfg(CatEntry cat)
        {
            CatCfgData data = new CatCfgData();
            data.Id = cat.Id;
            data.DisplayName = cat.DisplayName;
            data.Running = cat.Running;
            data.Port = cat.Port;
            data.ApiConfigId = cat.ApiConfigId.ToString("D");
            data.Persona = cat.Persona;
            data.ToolNames = cat.ToolNames;
            data.InjectList = cat.InjectList;
            data.QqBotId = cat.QqBotId.ToString("D");
            data.QqBotEnable = cat.QqBotEnable;
            SaveCatCfgData(cat.Id, data);
        }

        /// <summary>
        /// 删除猫文件——sessions/&lt;id&gt;/ 目录 + 前文 json（异常不阻断删除流程）。
        /// </summary>
        /// <param name="id">会话 ID</param>
        private static void DeleteCatFiles(string id)
        {
            try
            {
                string dir = Path.Combine(_dataRoot, "Data", "sessions", id);
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                }
                string storePath = Path.Combine(_dataRoot, "Data", "sessions", id + ".json");
                if (File.Exists(storePath))
                {
                    File.Delete(storePath);
                }
            }
            catch (Exception ex)
            {
                LogStore.Add("CatHome4", 2, "销毁猫文件清理异常：" + ex.Message, "CHAT");
            }
        }

        /// <summary>
        /// 读取 JSON 对象布尔属性——防御式（缺字段返回 false）。
        /// </summary>
        /// <param name="obj">JSON 对象</param>
        /// <param name="prop">属性名</param>
        /// <returns>属性值</returns>
        private static bool GetBoolProp(JsonElement obj, string prop)
        {
            JsonElement value;
            if (obj.TryGetProperty(prop, out value) && value.ValueKind == JsonValueKind.True)
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// 读取 JSON 对象整数属性——防御式（缺字段/非整数返回 0）。
        /// </summary>
        /// <param name="obj">JSON 对象</param>
        /// <param name="prop">属性名</param>
        /// <returns>属性值</returns>
        private static int GetIntProp(JsonElement obj, string prop)
        {
            JsonElement value;
            if (obj.TryGetProperty(prop, out value) && value.ValueKind == JsonValueKind.Number)
            {
                int n;
                if (value.TryGetInt32(out n))
                {
                    return n;
                }
            }
            return 0;
        }

        /// <summary>
        /// 读取 JSON 对象字符串数组属性——防御式（缺字段/非数组返回空数组）。
        /// </summary>
        /// <param name="obj">JSON 对象</param>
        /// <param name="prop">属性名</param>
        /// <returns>字符串数组；缺字段空数组</returns>
        private static string[] GetStringArrayProp(JsonElement obj, string prop)
        {
            JsonElement value;
            if (obj.TryGetProperty(prop, out value) && value.ValueKind == JsonValueKind.Array)
            {
                List<string> list = new List<string>();
                for (int i = 0; i < value.GetArrayLength(); i++)
                {
                    JsonElement item = value[i];
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        string got = item.GetString();
                        if (got != null && got.Length > 0)
                        {
                            list.Add(got);
                        }
                    }
                }
                return list.ToArray();
            }
            return new string[0];
        }
    }
}

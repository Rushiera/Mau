using System;
using System.Collections.Generic;
using System.Text.Json;
using CatHome4.Contracts;
using CatHome4.Http;
using Mau.Runtime;
using CH4;

namespace CatHome4.Admin
{
    /// <summary>
    /// Admin 域依赖注入面 + 工具名单辅助——入口壳 Bootstrap 一次性注入（S4 程序集拆分：管理 API 处理器 + cat.* 指令族迁入）。
    /// 依赖：ChatBridge（Core）/OA（宿主）/工具执行器/快照构建/线程守卫/html 根；数据根沿用 CatEntry 域 _dataRoot 字段。
    /// </summary>
    internal static partial class AdminService
    {
        // [段1] 依赖注入字段——入口壳 Configure 一次性设置（命名与入口壳原静态成员一致——引用零改动）
        /// <summary>会话协调桥——默认猫/多猫会话注册（Core 域；入口壳注入）</summary>
        public static ChatBridge _chatBridge;

        /// <summary>OA 工单平台——ChatSession 构造（宿主注入）</summary>
        public static OA _oa;

        /// <summary>宿主主线程 ID——HTTP 线程分流判断（入口壳注入）</summary>
        public static int _mainThreadId;

        /// <summary>工具直执回调——ChatSession 构造（入口壳工具域注入）</summary>
        public static Func<string, string, string> ExecuteTool;

        /// <summary>环境信息构建回调——ChatSession info 工具注入（入口壳注入；M4e 猫级白名单 roots 视图）</summary>
        public static Func<string> BuildEnvInfoProvider;

        /// <summary>快照 JSON 构建——StartCatHost 注入（入口壳注入）</summary>
        public static Func<bool, string> BuildSnapshotJson;

        /// <summary>html 根解析——StartCatHost 注入（入口壳适配）</summary>
        public static IHtmlRootProvider HtmlRoot;

        /// <summary>
        /// 注入 Admin 域依赖——入口壳 Bootstrap 调用（S4：程序集拆分接线）。
        /// </summary>
        /// <param name="bridge">会话协调桥（Core）</param>
        /// <param name="oa">OA 工单平台（宿主）</param>
        /// <param name="dataRoot">数据根（CatEntry 域 _dataRoot）</param>
        /// <param name="mainThreadId">宿主主线程 ID</param>
        /// <param name="executeTool">工具直执回调（入口壳工具域）</param>
        /// <param name="snapshotBuilder">快照 JSON 构建</param>
        /// <param name="htmlRoot">html 根解析（入口壳适配）</param>
        /// <param name="apiStore">LLM API 配置池（catcfg.apply 重建 Runtime 消费）</param>
        /// <param name="qqBotStore">QQ Bot 配置池</param>
        /// <param name="globalConfig">全局配置存储（catcfg.apply 消费）</param>
        public static void Configure(ChatBridge bridge, OA oa, string dataRoot, int mainThreadId, Func<string, string, string> executeTool, Func<bool, string> snapshotBuilder, IHtmlRootProvider htmlRoot, CH_LlmApiConfigStore apiStore, CH_QqBotConfigStore qqBotStore, ConfigStore globalConfig)
        {
            _chatBridge = bridge;
            _oa = oa;
            _dataRoot = dataRoot;
            _mainThreadId = mainThreadId;
            ExecuteTool = executeTool;
            BuildSnapshotJson = snapshotBuilder;
            HtmlRoot = htmlRoot;
            _apiStore = apiStore;
            _qqBotStore = qqBotStore;
            _globalConfig = globalConfig;
        }

        // [段2] 工具名单处理——自持（原 Program.Tools.cs 静态面迁入；ToolRegistry 为 Core 静态注册表）
        /// <summary>内置全量工具名清单——GetAllToolNames 懒加载缓存</summary>
        private static string[] _allToolNames;

        /// <summary>
        /// 内置全量工具名清单——懒加载从 ToolRegistry.BuildSpecs 派生（声明表是唯一真相源）。
        /// </summary>
        /// <returns>全量工具名数组</returns>
        internal static string[] GetAllToolNames()
        {
            if (_allToolNames == null)
            {
                ToolSpec[] specs = ToolRegistry.BuildSpecs();
                _allToolNames = new string[specs.Length];
                for (int i = 0; i < specs.Length; i++)
                {
                    _allToolNames[i] = specs[i].Name;
                }
            }
            return _allToolNames;
        }

        /// <summary>
        /// 全量工具清单（含组别）——统一工具池派生（design-ch4-tools-pool §六：配置界面组别/名称自动生成统一走池）。
        /// </summary>
        /// <returns>工具名+组别数组（name/group）</returns>
        internal static object[] GetAllToolsWithGroup()
        {
            ToolDef[] defs = ToolPool.All();
            object[] result = new object[defs.Length];
            for (int i = 0; i < defs.Length; i = i + 1)
            {
                result[i] = new { name = defs[i].Name, group = defs[i].Group };
            }
            return result;
        }

        /// <summary>
        /// 工具名比对——线性扫描内置清单。
        /// </summary>
        /// <param name="names">清单数组</param>
        /// <param name="name">目标名</param>
        /// <returns>true=在清单内</returns>
        internal static bool ContainsToolName(string[] names, string name)
        {
            for (int i = 0; i < names.Length; i++)
            {
                if (string.Equals(names[i], name, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 工具名单解析——读时比对（M2c 外部损坏防御：空/缺省 → 全量保底；非法名过滤；过滤后全空 → 全量保底）。
        /// </summary>
        /// <param name="raw">cat.cfg toolNames 原始串（逗号/空白分隔）</param>
        /// <returns>合法工具名数组（保底全量）</returns>
        internal static string[] ResolveToolNames(string raw)
        {
            string[] all = GetAllToolNames();
            if (raw == null || raw.Trim().Length == 0)
            {
                return all;
            }
            string[] parts = raw.Split(new char[] { ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            List<string> names = new List<string>();
            for (int i = 0; i < parts.Length; i++)
            {
                string name = parts[i].Trim();
                if (name == "*")
                {
                    return all;
                }
                if (ContainsToolName(all, name))
                {
                    names.Add(name);
                }
            }
            if (names.Count == 0)
            {
                return all;
            }
            return names.ToArray();
        }

        /// <summary>
        /// 工具名单写时校验——序列化落盘前比对内置清单（M2c：非法名剔除，合法名逗号重拼）。
        /// </summary>
        /// <param name="raw">待写入工具名单原始串</param>
        /// <returns>过滤后逗号清单（空串=全量语义）</returns>
        internal static string ValidateToolNames(string raw)
        {
            if (raw == null || raw.Trim().Length == 0)
            {
                return "";
            }
            string[] all = GetAllToolNames();
            string[] parts = raw.Split(new char[] { ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            List<string> names = new List<string>();
            for (int i = 0; i < parts.Length; i++)
            {
                string name = parts[i].Trim();
                if (name == "*" || ContainsToolName(all, name))
                {
                    names.Add(name);
                }
            }
            return string.Join(",", names.ToArray());
        }

        /// <summary>
        /// 工具声明面裁剪——按名单从全量表取子集（M2c 每会话独立声明面；保序）。
        /// </summary>
        /// <param name="names">合法工具名数组（ResolveToolNames 产物）</param>
        /// <returns>裁剪后工具数组；名单空 → 全量</returns>
        internal static ToolSpec[] FilterToolSpecs(string[] names)
        {
            ToolSpec[] all = ToolRegistry.BuildSpecs();
            if (names == null || names.Length == 0)
            {
                return all;
            }
            List<ToolSpec> list = new List<ToolSpec>();
            for (int i = 0; i < all.Length; i++)
            {
                if (ContainsToolName(names, all[i].Name))
                {
                    list.Add(all[i]);
                }
            }
            if (list.Count == 0)
            {
                return all;
            }
            return list.ToArray();
        }

        // [段3] JSON 辅助——自持（原 Program.Chat.cs 静态面迁入）
        /// <summary>
        /// 读取 JSON 对象字符串属性——防御式（缺字段返回空串）
        /// </summary>
        /// <param name="obj">JSON 对象</param>
        /// <param name="prop">属性名</param>
        /// <returns>属性值</returns>
        internal static string GetStringProp(JsonElement obj, string prop)
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
    }
}

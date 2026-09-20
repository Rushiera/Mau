using System;
using System.Collections.Generic;
using Mau.Runtime;

namespace CH4
{
    /// <summary>
    /// 工具注册表条目——工具名 → 归属 + 声明 + 状态（R0.2 动态注册面；CH4 基座维护）
    /// </summary>
    public sealed class ToolRegistryEntry
    {
        /// <summary>工具名（text-read 等——唯一键）</summary>
        public string Name;

        /// <summary>工具声明（Name/Description/Schema——OpenAI 兼容 function 定义）</summary>
        public ToolSpec Spec;

        /// <summary>归属 Flow 名——OA 工具 = 工具组 Flow（TextCat 等）；内置 = ""（会话内/宿主直执）</summary>
        public string OwnerFlow;

        /// <summary>是否内置——true=不走 OA（Note/host-* 会话内或宿主直执）；false=走 OA 工单由工具组 Flow 认领</summary>
        public bool IsBuiltin;

        /// <summary>是否启用——热重载注销/停用标记（false=从声明面剔除）</summary>
        public bool Enabled;
        /// <summary>是否特权——true=仅主干会话（catKey=majordomo）可见可调（组级声明派生：工具定义 JSON 根级 privileged）</summary>
        public bool Privileged;
    }

    /// <summary>
    /// 运行时工具注册表——工具名 → 条目（CH4 基座单一真相源；内置 + OA 双轨）。
    /// 消费面：BuildSpecs/GetAllNames 派生（声明表唯一来源）→ M2c 名单/裁剪/注入六处跟随。
    /// 初始数据源：Program.BuildToolSpecs 静态表（Init 时灌入）；动态登记接口 Register/Unregister 预留——自举新工具组（R3）接入点。
    /// 分层铁律：内置（无需 OA——会话内直执）vs OA 工具（按工具组独立 Flow 认领）——双轨并存不走同一执行面。
    /// </summary>
    public static class ToolRegistry
    {
        /// <summary>注册表字典——工具名 → 条目（主线程初始化/查询；动态登记 R3 前仅在 Bootstrap）</summary>
        private static readonly Dictionary<string, ToolRegistryEntry> _entries = new Dictionary<string, ToolRegistryEntry>();

        /// <summary>
        /// 注册表初始化——静态表灌入（R0.2：声明单一真相源 = 注册表；静态表仅作初始数据）
        /// OwnerFlow 优先从 Flow 自曝元数据映射（design-ch4-flow-scan §3.3——宿主扫描 dll 后传入）；
        /// 映射缺失回退前缀映射 OwnerFlowFor；Note/host-* 标记内置（不走 OA）；
        /// 特权标记从组级声明映射灌入（工具定义 JSON 根级 privileged——ToolPool.BuildPrivilegedMap）
        /// </summary>
        /// <param name="specs">初始工具声明表（Program.BuildToolSpecs 产物）</param>
        /// <param name="ownerFlowMap">工具名 → 归属 Flow 名映射（null=回退前缀映射）</param>
        /// <param name="privilegedMap">工具名 → 特权标记映射（null=无特权项；显式传参——漏传即编译期报错，不静默失权）</param>
        public static void Init(ToolSpec[] specs, Dictionary<string, string> ownerFlowMap, Dictionary<string, bool> privilegedMap)
        {
            for (int i = 0; i < specs.Length; i = i + 1)
            {
                ToolSpec s = specs[i];
                bool builtin = s.Name == "Note" || s.Name == "time" || s.Name == "random" || s.Name == "info" || s.Name == "pack" || s.Name.StartsWith("host-", StringComparison.Ordinal);
                string ownerFlow = "";
                if (ownerFlowMap != null && ownerFlowMap.TryGetValue(s.Name, out ownerFlow))
                {
                    // 路由表数据化——Flow 自曝元数据（design-ch4-flow-scan §3.3）
                }
                else
                {
                    ownerFlow = OwnerFlowFor(s.Name);
                }
                bool privileged = false;
                if (privilegedMap != null && privilegedMap.TryGetValue(s.Name, out privileged))
                {
                    // 特权标记数据化——组级声明派生（工具定义 JSON 根级 privileged）
                }
                Register(s.Name, s, ownerFlow, builtin, privileged);
            }
        }

        /// <summary>
        /// 注册工具——动态登记接口（R0.2 预留：自举新工具组接入点；内置/OA 双轨统一入口；重复名覆盖）
        /// </summary>
        /// <param name="name">工具名</param>
        /// <param name="spec">工具声明</param>
        /// <param name="ownerFlow">归属 Flow 名（内置 = ""）</param>
        /// <param name="isBuiltin">是否内置（不走 OA）</param>
        /// <param name="privileged">是否特权（仅主干会话可见可调——组级声明派生）</param>
        public static void Register(string name, ToolSpec spec, string ownerFlow, bool isBuiltin, bool privileged)
        {
            ToolRegistryEntry entry = new ToolRegistryEntry();
            entry.Name = name;
            entry.Spec = spec;
            entry.OwnerFlow = ownerFlow;
            entry.IsBuiltin = isBuiltin;
            entry.Privileged = privileged;
            entry.Enabled = true;
            _entries[name] = entry;
        }

        /// <summary>
        /// 注销工具——热重载/退役同步（从声明面剔除；不存在静默）
        /// </summary>
        /// <param name="name">工具名</param>
        public static void Unregister(string name)
        {
            _entries.Remove(name);
        }

        /// <summary>
        /// 查询工具——按名
        /// </summary>
        /// <param name="name">工具名</param>
        /// <returns>条目（不存在 = null）</returns>
        public static ToolRegistryEntry Find(string name)
        {
            ToolRegistryEntry entry;
            if (_entries.TryGetValue(name, out entry))
            {
                return entry;
            }
            return null;
        }

        /// <summary>
        /// 全量声明表——按注册序构建（声明面唯一数据源；禁用的剔除）
        /// </summary>
        /// <returns>启用工具声明数组</returns>
        public static ToolSpec[] BuildSpecs()
        {
            List<ToolSpec> list = new List<ToolSpec>();
            foreach (KeyValuePair<string, ToolRegistryEntry> kv in _entries)
            {
                if (kv.Value.Enabled)
                {
                    list.Add(kv.Value.Spec);
                }
            }
            return list.ToArray();
        }

        /// <summary>
        /// 全量工具名——声明表派生（M2c 名单比对基准）
        /// </summary>
        /// <returns>启用工具名数组</returns>
        public static string[] GetAllNames()
        {
            ToolSpec[] specs = BuildSpecs();
            string[] names = new string[specs.Length];
            for (int i = 0; i < specs.Length; i = i + 1)
            {
                names[i] = specs[i].Name;
            }
            return names;
        }

        /// <summary>
        /// 工具名前缀 → 归属工具组 Flow 名映射（R0.2 工具组拆分同源；未知 = 内置空归属）
        /// </summary>
        /// <param name="name">工具名</param>
        /// <returns>Flow 名（内置 = ""）</returns>
        private static string OwnerFlowFor(string name)
        {
            if (name.StartsWith("text-", StringComparison.Ordinal))
            {
                return "TextCat";
            }
            if (name.StartsWith("mau-", StringComparison.Ordinal))
            {
                return "MauCat";
            }
            if (name.StartsWith("cs-", StringComparison.Ordinal))
            {
                return "CsCat";
            }
            if (name.StartsWith("config-", StringComparison.Ordinal))
            {
                return "ConfigCat";
            }
            if (name.StartsWith("web-", StringComparison.Ordinal))
            {
                return "SearchCat";
            }
            if (name.StartsWith("image-", StringComparison.Ordinal))
            {
                return "VisionCat";
            }
            if (name.StartsWith("temp-", StringComparison.Ordinal))
            {
                return "TempToolCat";
            }
            if (name == "powershell")
            {
                return "PsCat";
            }
            return "";
        }
    }
}
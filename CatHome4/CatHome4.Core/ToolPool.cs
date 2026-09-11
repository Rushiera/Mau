using System;
using System.Collections.Generic;
using System.Text.Json;
using Mau.Runtime;

namespace CH4
{
    /// <summary>
    /// 工具定义条目——统一工具池的原子形态（design-ch4-tools-pool）。
    /// 数据源：Flow.GetToolsJson() 自曝（tools.&lt;组&gt; 积木）或内置定义源——同一 JSON 格式，同一解析器。
    /// </summary>
    public sealed class ToolDef
    {
        /// <summary>工具名（text-read 等——唯一键）</summary>
        public string Name = "";

        /// <summary>语义化描述（LLM 面——OpenAI function description）</summary>
        public string Description = "";

        /// <summary>参数 JSON Schema（OpenAI 原样透传；空 = 无参工具）</summary>
        public string ParametersJson = "";

        /// <summary>归属组（TextCat 等；内置 = ""）</summary>
        public string Group = "";
    }

    /// <summary>
    /// 统一工具池——工具定义唯一真相源（消费面统一从此派生，不各自实现）。
    /// 聚合路径：Flow 自曝 GetToolsJson（工具组）+ 内置定义源（宿主内建——本质也是 BRIK，只是内置）。
    /// 消费面：ToolRegistry.Init（specs + ownerFlowMap）/ 配置界面名称/组别 / 快照 tools 段。
    /// </summary>
    public static class ToolPool
    {
        /// <summary>池条目——按名索引（重名覆盖：后注册优先）</summary>
        private static readonly Dictionary<string, ToolDef> _byName = new Dictionary<string, ToolDef>(StringComparer.Ordinal);

        /// <summary>池条目——插入序（All/BuildSpecs 确定性：注册序 = 扫描添加序）</summary>
        private static readonly List<ToolDef> _order = new List<ToolDef>();

        /// <summary>池条目——按组索引（配置界面组别/名称自动生成）</summary>
        private static readonly Dictionary<string, List<ToolDef>> _byGroup = new Dictionary<string, List<ToolDef>>(StringComparer.Ordinal);

        /// <summary>
        /// 解析工具定义 JSON——{"group":"X","tools":[{name,description,parameters}]}（与 BRIK GetToolsJson 返回值同构）。
        /// </summary>
        /// <param name="json">工具定义 JSON 文本</param>
        private static void AddFromJson(string json)
        {
            if (json == null || json.Length == 0)
            {
                return;
            }
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    JsonElement root = doc.RootElement;
                    string group = "";
                    JsonElement groupEl;
                    if (root.TryGetProperty("group", out groupEl) && groupEl.ValueKind == JsonValueKind.String)
                    {
                        group = groupEl.GetString() ?? "";
                    }
                    JsonElement tools;
                    if (!root.TryGetProperty("tools", out tools) || tools.ValueKind != JsonValueKind.Array)
                    {
                        return;
                    }
                    for (int i = 0; i < tools.GetArrayLength(); i = i + 1)
                    {
                        JsonElement item = tools[i];
                        if (item.ValueKind != JsonValueKind.Object)
                        {
                            continue;
                        }
                        ToolDef def = new ToolDef();
                        def.Group = group;
                        JsonElement nameEl;
                        if (item.TryGetProperty("name", out nameEl) && nameEl.ValueKind == JsonValueKind.String)
                        {
                            def.Name = nameEl.GetString() ?? "";
                        }
                        JsonElement descEl;
                        if (item.TryGetProperty("description", out descEl) && descEl.ValueKind == JsonValueKind.String)
                        {
                            def.Description = descEl.GetString() ?? "";
                        }
                        JsonElement paramsEl;
                        if (item.TryGetProperty("parameters", out paramsEl))
                        {
                            def.ParametersJson = paramsEl.GetRawText();
                        }
                        if (def.Name.Length == 0)
                        {
                            continue;
                        }
                        Add(def);
                    }
                }
            }
            catch (Exception)
            {
                // 定义 JSON 损坏——跳过该批（工具池缺条目；不阻断启动）
            }
        }

        /// <summary>
        /// 添加单条目——重名覆盖 + 组索引同步。
        /// </summary>
        /// <param name="def">工具定义</param>
        public static void Add(ToolDef def)
        {
            if (def == null || def.Name.Length == 0)
            {
                return;
            }
            // 重名覆盖——保持原插入序（确定性：注册序不因覆盖漂移）
            bool isNew = !_byName.ContainsKey(def.Name);
            _byName[def.Name] = def;
            if (isNew)
            {
                _order.Add(def);
            }
            string group = "";
            if (def.Group.Length > 0)
            {
                group = def.Group;
            }
            List<ToolDef> list;
            if (!_byGroup.TryGetValue(group, out list))
            {
                list = new List<ToolDef>();
                _byGroup[group] = list;
            }
            if (!list.Contains(def))
            {
                list.Add(def);
            }
        }

        /// <summary>
        /// 从 Flow 自曝工具定义加入池——IFlow.GetToolsJson()（工具组 Flow 调用面）。
        /// </summary>
        /// <param name="flow">已加载 Flow</param>
        public static void AddFromFlow(IFlow flow)
        {
            try
            {
                AddFromJson(flow.GetToolsJson());
            }
            catch (Exception)
            {
                // Flow 自曝失败——跳过（该组工具定义缺失）
            }
        }

        /// <summary>
        /// 从内置定义源加入池——宿主内建小表（本质也是 BRIK，只是内置：Note/time/random/info/host-*）。
        /// </summary>
        /// <param name="builtinJson">内置工具定义 JSON（同 GetToolsJson 格式）</param>
        public static void AddFromBuiltin(string builtinJson)
        {
            AddFromJson(builtinJson);
        }

        /// <summary>
        /// 全量工具定义——声明面/白名单比对基准（注册序 = 添加序）。
        /// </summary>
        /// <returns>工具定义数组</returns>
        public static ToolDef[] All()
        {
            ToolDef[] result = new ToolDef[_byName.Count];
            int i = 0;
            foreach (KeyValuePair<string, ToolDef> kv in _byName)
            {
                result[i] = kv.Value;
                i = i + 1;
            }
            return result;
        }

        /// <summary>
        /// 全量工具名——配置界面名称清单（M2c 比对基准）。
        /// </summary>
        /// <returns>工具名数组</returns>
        public static string[] AllNames()
        {
            ToolDef[] all = All();
            string[] names = new string[all.Length];
            for (int i = 0; i < all.Length; i = i + 1)
            {
                names[i] = all[i].Name;
            }
            return names;
        }

        /// <summary>
        /// 按组取工具——配置界面组别/名称自动生成（组空 = 内置）。
        /// </summary>
        /// <param name="group">组名（空 = 内置组）</param>
        /// <returns>组内工具定义数组（不存在空组 = 空数组）</returns>
        public static ToolDef[] ByGroup(string group)
        {
            string key = group ?? "";
            List<ToolDef> list;
            if (_byGroup.TryGetValue(key, out list))
            {
                return list.ToArray();
            }
            return new ToolDef[0];
        }

        /// <summary>
        /// 全部组名——配置界面分组展示（含内置空组；按首次出现序）。
        /// </summary>
        /// <returns>组名数组</returns>
        public static string[] AllGroups()
        {
            string[] result = new string[_byGroup.Count];
            int i = 0;
            foreach (KeyValuePair<string, List<ToolDef>> kv in _byGroup)
            {
                result[i] = kv.Key;
                i = i + 1;
            }
            return result;
        }

        /// <summary>
        /// 构建 ToolRegistry 数据——specs 数组（ToolSpec 投影）。
        /// </summary>
        /// <returns>工具规格数组</returns>
        public static ToolSpec[] BuildSpecs()
        {
            ToolDef[] all = All();
            ToolSpec[] specs = new ToolSpec[all.Length];
            for (int i = 0; i < all.Length; i = i + 1)
            {
                specs[i] = new ToolSpec(all[i].Name, all[i].Description, all[i].ParametersJson);
            }
            return specs;
        }

        /// <summary>
        /// 构建 OwnerFlow 映射——工具名 → 组名（ToolRegistry 路由表数据源）。
        /// </summary>
        /// <returns>工具名 → 组名映射</returns>
        public static Dictionary<string, string> BuildOwnerFlowMap()
        {
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, ToolDef> kv in _byName)
            {
                map[kv.Key] = kv.Value.Group;
            }
            return map;
        }

        /// <summary>
        /// 清空工具池——重载/重启重建面。
        /// </summary>
        public static void Clear()
        {
            _byName.Clear();
            _byGroup.Clear();
        }
    }
}

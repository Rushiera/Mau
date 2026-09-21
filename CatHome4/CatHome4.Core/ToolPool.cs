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
        /// <summary>组级特权声明——true=本组工具仅主干会话（catKey=majordomo）可见可调（工具定义 JSON 根级 privileged 字段；缺省 false）</summary>
        public bool Privileged = false;
    }

    /// <summary>
    /// 统一工具池——工具定义唯一真相源（消费面统一从此派生，不各自实现）。
    /// 聚合路径：Flow 自曝 GetToolsJson（工具组）+ 内置定义源（宿主内建——本质也是 BRIK，只是内置）。
    /// 消费面：ToolRegistry.Init（specs + ownerFlowMap）/ 配置界面名称/组别 / 快照 tools 段。
    /// </summary>
    public static class ToolPool
    {
        /// <summary>
        /// 池快照——三索引一次成型（写侧局部构建 → 整体换引用；读侧取局部引用即稳定视图，无半更新可见）。
        /// </summary>
        private sealed class PoolState
        {
            /// <summary>池条目——按名索引（重名覆盖：后注册优先）</summary>
            public Dictionary<string, ToolDef> ByName = new Dictionary<string, ToolDef>(StringComparer.Ordinal);

            /// <summary>池条目——插入序（All/BuildSpecs 确定性：注册序 = 扫描添加序）</summary>
            public List<ToolDef> Order = new List<ToolDef>();

            /// <summary>池条目——按组索引（配置界面组别/名称自动生成）</summary>
            public Dictionary<string, List<ToolDef>> ByGroup = new Dictionary<string, List<ToolDef>>(StringComparer.Ordinal);
        }

        /// <summary>当前池快照——唯一可变引用，写侧整体替换（RebuildAll）；读侧先取局部引用再遍历</summary>
        private static PoolState _state = new PoolState();

        /// <summary>
        /// 解析工具定义 JSON——{"group":"X","tools":[{name,description,parameters}]}（与 BRIK GetToolsJson 返回值同构）。
        /// </summary>
        /// <param name="json">工具定义 JSON 文本</param>
        private static void AddFromJson(PoolState target, string json)
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
                    // 组级特权声明——根级 privileged=true → 本组全部工具标记特权（缺省 false）
                    bool privileged = false;
                    JsonElement privEl;
                    if (root.TryGetProperty("privileged", out privEl) && privEl.ValueKind == JsonValueKind.True)
                    {
                        privileged = true;
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
                        def.Privileged = privileged;
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
                        AddTo(target, def);
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
        private static void AddTo(PoolState target, ToolDef def)
        {
            if (def == null || def.Name.Length == 0)
            {
                return;
            }
            // 重名覆盖——保持原插入序（确定性：注册序不因覆盖漂移）；内容跟随最新注册
            bool isNew = !target.ByName.ContainsKey(def.Name);
            target.ByName[def.Name] = def;
            if (isNew)
            {
                target.Order.Add(def);
            }
            else
            {
                for (int i = 0; i < target.Order.Count; i = i + 1)
                {
                    if (target.Order[i].Name == def.Name)
                    {
                        target.Order[i] = def;
                        break;
                    }
                }
            }
            string group = "";
            if (def.Group.Length > 0)
            {
                group = def.Group;
            }
            List<ToolDef> list;
            if (!target.ByGroup.TryGetValue(group, out list))
            {
                list = new List<ToolDef>();
                target.ByGroup[group] = list;
            }
            int exists = -1;
            for (int i = 0; i < list.Count; i = i + 1)
            {
                if (list[i].Name == def.Name)
                {
                    exists = i;
                    break;
                }
            }
            if (exists >= 0)
            {
                list[exists] = def;
            }
            else
            {
                list.Add(def);
            }
        }

        /// <summary>
        /// 全量工具定义——声明面/白名单比对基准（注册序 = 添加序）。
        /// </summary>
        /// <returns>工具定义数组</returns>
        public static ToolDef[] All()
        {
            // 读侧局部快照——写侧换引用不影响本次读取；注册序兑现（按插入序返回，不依赖 Dictionary 枚举序）
            PoolState snapshot = _state;
            ToolDef[] result = new ToolDef[snapshot.Order.Count];
            for (int i = 0; i < snapshot.Order.Count; i = i + 1)
            {
                result[i] = snapshot.Order[i];
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
            // 读侧局部快照——写侧换引用不影响本次读取
            PoolState snapshot = _state;
            string key = group ?? "";
            List<ToolDef> list;
            if (snapshot.ByGroup.TryGetValue(key, out list))
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
            // 读侧局部快照——写侧换引用不影响本次读取；首次出现序兑现（按注册序摄取组名）
            PoolState snapshot = _state;
            List<string> groups = new List<string>();
            for (int i = 0; i < snapshot.Order.Count; i = i + 1)
            {
                string key = "";
                if (snapshot.Order[i].Group.Length > 0)
                {
                    key = snapshot.Order[i].Group;
                }
                if (!groups.Contains(key))
                {
                    groups.Add(key);
                }
            }
            return groups.ToArray();
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
            // 读侧局部快照——写侧换引用不影响本次读取
            PoolState snapshot = _state;
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, ToolDef> kv in snapshot.ByName)
            {
                map[kv.Key] = kv.Value.Group;
            }
            return map;
        }
        /// <summary>
        /// 构建特权标记映射——工具名 → 是否特权（ToolRegistry 可见性判定数据源；组级声明派生）。
        /// </summary>
        /// <returns>工具名 → 特权标记映射</returns>
        public static Dictionary<string, bool> BuildPrivilegedMap()
        {
            // 读侧局部快照——写侧换引用不影响本次读取
            PoolState snapshot = _state;
            Dictionary<string, bool> map = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, ToolDef> kv in snapshot.ByName)
            {
                map[kv.Key] = kv.Value.Privileged;
            }
            return map;
        }
        /// <summary>
        /// 从 Flow 自曝工具定义加入目标快照——IFlow.GetToolsJson()（工具组 Flow 调用面；自曝失败跳过该组）。
        /// </summary>
        /// <param name="target">目标快照（构建期局部对象）</param>
        /// <param name="flow">已加载 Flow</param>
        private static void AddFromFlowTo(PoolState target, IFlow flow)
        {
            try
            {
                AddFromJson(target, flow.GetToolsJson());
            }
            catch (Exception)
            {
                // Flow 自曝失败——跳过（该组工具定义缺失）
            }
        }
        /// <summary>
        /// 全量重建工具池——唯一写入口（Bootstrap / 热重载同源）：局部构建新快照 → 原子换引用。
        /// 读者要么看到全旧、要么看到全新（无半更新窗口）；不在新表的工具随旧快照淘汰（无陈旧残留）。
        /// </summary>
        /// <param name="toolFlows">工具组 Flow 列表（GetToolsJson 自曝源）</param>
        /// <param name="quickFlow">QuickCat Flow（组级工单消费者——空组声明；null=未加载）</param>
        /// <param name="builtinJson">内置工具定义 JSON（Note/time/random/info/host-* 等）</param>
        public static void RebuildAll(List<IFlow> toolFlows, IFlow quickFlow, string builtinJson)
        {
            // [段1] 局部构建新快照——旧快照只读，读者不受构建期影响
            PoolState next = new PoolState();
            if (toolFlows != null)
            {
                for (int i = 0; i < toolFlows.Count; i = i + 1)
                {
                    AddFromFlowTo(next, toolFlows[i]);
                }
            }
            if (quickFlow != null)
            {
                AddFromFlowTo(next, quickFlow);
            }
            AddFromJson(next, builtinJson);
            // [段2] 原子换引用——读者要么全旧要么全新；被删工具随旧快照淘汰
            _state = next;
        }
    }
}

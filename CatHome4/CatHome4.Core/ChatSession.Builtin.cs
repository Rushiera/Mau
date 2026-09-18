using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Mau.Runtime;

namespace CH4
{
    /// <summary>
    /// 宿主会话实体——内置工具分部（R0.2/R1：会话内直执——无需 OA 的内置工具统一入口）。
    /// 分界铁律：内置（无需 OA——会话内/宿主直执）vs OA 工具（按工具组独立 Flow 认领）——双轨并存不走同一执行面。
    /// 内置工具：Note（M4a）+ time/random（R1.1）+ info（R1.2——读 ToolRegistry，agent 的眼睛）。
    /// </summary>
    internal sealed partial class ChatSession
    {
        /// <summary>
        /// 加载包数据面委托——pack 内置工具（catKey + packKey → 结果 JSON）；宿主启动期由组合根注入。
        /// 池定义与猫级挂载解析归 Admin 域——Core 不依赖配置面。
        /// </summary>
        internal static Func<string, string, string> PackPayloadProvider;

        /// <summary>
        /// 内置工具判定——读 ToolRegistry 注册面（内置/OA 双轨的单一真相源）；未注册 = 非内置。
        /// 分派处原为硬编码名单（Note/time/random/info）——新增内置工具会静默走 OA 工单（无人认领 → 超时）。
        /// </summary>
        /// <param name="name">工具名</param>
        /// <returns>true=内置（会话内直执，不进 OA）</returns>
        private static bool IsBuiltinTool(string name)
        {
            ToolRegistryEntry entry = ToolRegistry.Find(name);
            if (entry == null)
            {
                return false;
            }
            return entry.IsBuiltin;
        }

        /// <summary>
        /// 内置工具统一执行入口——EnterToolBatch 分派（Note/time/random/info/pack 会话内直执，不进 OA）。
        /// </summary>
        /// <param name="name">内置工具名</param>
        /// <param name="argsJson">参数 JSON</param>
        /// <returns>执行结果文本</returns>
        private string ExecuteBuiltin(string name, string argsJson)
        {
            if (name == "Note")
            {
                return ExecuteNote(argsJson);
            }
            if (name == "time")
            {
                string nowText = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                // 结构化返回体（design-ch4-tools 附录）——首行 JSON 元数据头 + 正文定界
                Dictionary<string, object> timeFields = new Dictionary<string, object>();
                timeFields["ts"] = nowText;
                return ToolMetaHead.With("time", true, timeFields, nowText);
            }
            if (name == "random")
            {
                return ExecuteRandom(argsJson);
            }
            if (name == "info")
            {
                return ExecuteInfo();
            }
            if (name == "pack")
            {
                return ExecutePack(argsJson);
            }
            return "ERR|UNKNOWN_BUILTIN|未知内置工具: " + name;
        }

        /// <summary>
        /// random 执行体——生成 [min, max) 范围内随机整数（min 含下限，max 不含上限；min &lt; max 校验）。
        /// </summary>
        /// <param name="argsJson">参数 JSON（min/max 整数）</param>
        /// <returns>随机整数文本</returns>
        private string ExecuteRandom(string argsJson)
        {
            int min = 0;
            int max = 0;
            bool hasMin = false;
            bool hasMax = false;
            if (argsJson != null && argsJson.Length > 0 && argsJson.StartsWith("{"))
            {
                try
                {
                    using (JsonDocument doc = JsonDocument.Parse(argsJson))
                    {
                        JsonElement root = doc.RootElement;
                        JsonElement mn;
                        if (root.TryGetProperty("min", out mn) && mn.ValueKind == JsonValueKind.Number)
                        {
                            min = mn.GetInt32();
                            hasMin = true;
                        }
                        JsonElement mx;
                        if (root.TryGetProperty("max", out mx) && mx.ValueKind == JsonValueKind.Number)
                        {
                            max = mx.GetInt32();
                            hasMax = true;
                        }
                    }
                }
                catch (Exception)
                {
                    return "ERR|BAD_ARGS|random 参数损坏";
                }
            }
            if (!hasMin || !hasMax || min >= max)
            {
                return "ERR|BAD_ARGS|random 需要 min < max 的整数参数";
            }
            Random rng = new Random();
            int picked = rng.Next(min, max);
            // 结构化返回体（design-ch4-tools 附录）——首行 JSON 元数据头 + 正文定界
            Dictionary<string, object> randomFields = new Dictionary<string, object>();
            randomFields["min"] = min;
            randomFields["max"] = max;
            randomFields["value"] = picked;
            return ToolMetaHead.With("random", true, randomFields, picked.ToString());
        }

        /// <summary>
        /// pack 执行体——加载包注入：池定义与猫级挂载经 PackPayloadProvider 取，路径解析与前文注入同源。
        /// 语义：任一件失败整包不注入（半个包比没有包更危险——部分成功与完全成功不可区分）。
        /// </summary>
        /// <param name="argsJson">参数 JSON（key 必填）</param>
        /// <returns>注入内容与加载实锤（失败侧 ERR| 前缀 + 逐件原因）</returns>
        private string ExecutePack(string argsJson)
        {
            string packKey = "";
            // [段0] 参数面——key 必填；未知参数拒绝（catId 宿主保留键放行）
            if (argsJson != null && argsJson.Length > 0)
            {
                try
                {
                    using (JsonDocument doc = JsonDocument.Parse(argsJson))
                    {
                        JsonElement root = doc.RootElement;
                        if (root.ValueKind != JsonValueKind.Object)
                        {
                            return "ERR|BAD_ARGS|参数必须是 JSON 对象";
                        }
                        foreach (JsonProperty property in root.EnumerateObject())
                        {
                            if (property.Name == "catId")
                            {
                                continue;
                            }
                            if (property.Name != "key")
                            {
                                return "ERR|BAD_ARGS|未知参数: " + property.Name + "（支持 key）";
                            }
                        }
                        JsonElement keyEl;
                        if (root.TryGetProperty("key", out keyEl) && keyEl.ValueKind == JsonValueKind.String)
                        {
                            string got = keyEl.GetString();
                            if (got != null)
                            {
                                packKey = got;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    return "ERR|BAD_ARGS|参数 JSON 解析失败: " + ex.Message;
                }
            }
            if (packKey.Length == 0)
            {
                return "ERR|BAD_ARGS|缺少参数 key（pack 需要包 key）";
            }
            if (PackPayloadProvider == null)
            {
                return "ERR|PACK_NO_PROVIDER|加载包数据面未注入（宿主未接线）";
            }
            // [段1] 数据面——池定义 + 猫级挂载（Admin 域解析；池外/未挂载在此拦下）
            string payload = PackPayloadProvider(_catKey, packKey);
            bool ok = false;
            string desc = "";
            string error = "";
            string[] paths = new string[0];
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(payload))
                {
                    JsonElement root = doc.RootElement;
                    JsonElement okEl;
                    if (root.TryGetProperty("ok", out okEl) && okEl.ValueKind == JsonValueKind.True)
                    {
                        ok = true;
                    }
                    JsonElement descEl;
                    if (root.TryGetProperty("desc", out descEl) && descEl.ValueKind == JsonValueKind.String)
                    {
                        string gotDesc = descEl.GetString();
                        if (gotDesc != null)
                        {
                            desc = gotDesc;
                        }
                    }
                    JsonElement errEl;
                    if (root.TryGetProperty("error", out errEl) && errEl.ValueKind == JsonValueKind.String)
                    {
                        string gotErr = errEl.GetString();
                        if (gotErr != null)
                        {
                            error = gotErr;
                        }
                    }
                    JsonElement pathsEl;
                    if (root.TryGetProperty("paths", out pathsEl) && pathsEl.ValueKind == JsonValueKind.Array)
                    {
                        List<string> collected = new List<string>();
                        for (int i = 0; i < pathsEl.GetArrayLength(); i = i + 1)
                        {
                            JsonElement item = pathsEl[i];
                            if (item.ValueKind == JsonValueKind.String)
                            {
                                string gotPath = item.GetString();
                                if (gotPath != null && gotPath.Length > 0)
                                {
                                    collected.Add(gotPath);
                                }
                            }
                        }
                        paths = collected.ToArray();
                    }
                }
            }
            catch (Exception ex)
            {
                return "ERR|PACK_BAD_PAYLOAD|包数据面返回解析失败: " + ex.Message;
            }
            if (!ok)
            {
                if (error.Length == 0)
                {
                    error = "ERR|PACK_RESOLVE|包解析失败: " + packKey;
                }
                return error;
            }
            // [段2] 路径展开——目录项一级 *.md 升序；文件项单条（与前文注入同语义）；未知根/缺失即记失败
            WorkspaceConfig ws = null;
            DataBox.TryResolve<WorkspaceConfig>(out ws);
            if (ws == null)
            {
                return "ERR|PACK_NO_WORKSPACE|工作区配置不可用（受控根解析面缺失）";
            }
            List<string> labels = new List<string>();
            List<string> targets = new List<string>();
            List<string> failures = new List<string>();
            for (int i = 0; i < paths.Length; i = i + 1)
            {
                string entryPath = paths[i];
                string resolved = "";
                try
                {
                    WorkspaceConfig.InjectEntry entry = new WorkspaceConfig.InjectEntry();
                    entry.File = entryPath;
                    entry.Optional = false;
                    entry.Label = entryPath;
                    resolved = ws.ResolveInjectFile(entry);
                }
                catch (Exception ex)
                {
                    failures.Add(entryPath + " | 根解析失败: " + ex.Message);
                    continue;
                }
                if (Directory.Exists(resolved))
                {
                    string[] found = Directory.GetFiles(resolved, "*.md", SearchOption.TopDirectoryOnly);
                    Array.Sort(found, StringComparer.OrdinalIgnoreCase);
                    string prefix = entryPath.TrimEnd('/', '\\');
                    for (int k = 0; k < found.Length; k = k + 1)
                    {
                        labels.Add(prefix + "/" + Path.GetFileName(found[k]));
                        targets.Add(found[k]);
                    }
                    if (found.Length == 0)
                    {
                        failures.Add(entryPath + " | 目录内无 .md 文件");
                    }
                }
                else
                {
                    if (!File.Exists(resolved))
                    {
                        failures.Add(entryPath + " | 文件不存在");
                        continue;
                    }
                    labels.Add(entryPath);
                    targets.Add(resolved);
                }
            }
            // [段3] 逐件读取——任一件失败整包不注入（原子拒绝）
            List<string> contents = new List<string>();
            int totalChars = 0;
            for (int i = 0; i < targets.Count; i = i + 1)
            {
                try
                {
                    string text = File.ReadAllText(targets[i]);
                    contents.Add(text);
                    totalChars = totalChars + text.Length;
                }
                catch (Exception ex)
                {
                    failures.Add(labels[i] + " | 读取失败: " + ex.Message);
                }
            }
            if (failures.Count > 0)
            {
                StringBuilder failMsg = new StringBuilder();
                failMsg.Append("ERR|PACK_LOAD_FAILED|");
                failMsg.Append(packKey);
                failMsg.Append(" 加载失败（整包未注入）:");
                for (int i = 0; i < failures.Count; i = i + 1)
                {
                    failMsg.Append("\n  - ");
                    failMsg.Append(failures[i]);
                }
                return failMsg.ToString();
            }
            // [段4] 组装——头部加载实锤（件数/字符数）+ 逐件标注（与前文注入同形）
            StringBuilder sb = new StringBuilder();
            sb.Append("✅ PACK ");
            sb.Append(packKey);
            if (desc.Length > 0)
            {
                sb.Append(" | ");
                sb.Append(desc);
            }
            sb.Append(" | ");
            sb.Append(targets.Count.ToString());
            sb.Append(" 件 / ");
            sb.Append(totalChars.ToString());
            sb.Append(" 字符\n");
            for (int i = 0; i < targets.Count; i = i + 1)
            {
                sb.Append("===== 注入文件: ");
                sb.Append(labels[i]);
                sb.Append(" =====");
                sb.Append("\n");
                sb.Append(contents[i]);
                sb.Append("\n");
            }
            LogStore.Add("CatHome4", 1, "pack 注入: cat=" + _catKey + " | key=" + packKey + " | " + targets.Count.ToString() + " 件 / " + totalChars.ToString() + " 字符", "INJECT");
            // 结构化返回体（design-ch4-tools 附录）——首行 JSON 元数据头 + 正文定界
            Dictionary<string, object> packFields = new Dictionary<string, object>();
            packFields["key"] = packKey;
            packFields["files"] = targets.Count;
            packFields["chars"] = totalChars;
            return ToolMetaHead.With("pack", true, packFields, sb.ToString());
        }

        /// <summary>
        /// info 执行体——环境信息（运行版本 + LLM 端点类型 + 当前时间 + 猫可见根；M4e：info 是猫自省目录范围的通道）。
        /// </summary>
        /// <returns>环境信息文本</returns>
        private string ExecuteInfo()
        {
            if (_envInfoProvider != null)
            {
                // M4e 猫级白名单——info 按当前会话猫输出（provider 读 ToolCatContext；内置工具直执不经 RunTool）
                string prev = ToolCatContext.CurrentCatKey;
                ToolCatContext.SetCat(_catKey);
                try
                {
                    string infoText = _envInfoProvider();
                    // 结构化返回体（design-ch4-tools 附录）——首行 JSON 元数据头 + 正文定界
                    Dictionary<string, object> infoFields = new Dictionary<string, object>();
                    infoFields["cat"] = _catKey;
                    return ToolMetaHead.With("info", true, infoFields, infoText);
                }
                finally
                {
                    ToolCatContext.SetCat(prev);
                }
            }
            return "CH4 | 环境信息不可用（未注入 provider）";
        }

        /// <summary>
        /// 环境信息文本公开面——QQ /info 指令直调工具函数（ExecuteInfo 同源；不触会话状态——跨线程安全）。
        /// </summary>
        /// <returns>环境信息文本</returns>
        public string GetInfoText()
        {
            return ExecuteInfo();
        }
    }
}
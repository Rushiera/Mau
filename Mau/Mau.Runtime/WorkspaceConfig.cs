using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Mau.Runtime
{
    /// <summary>
    /// 工作区配置——workspace.json 解析（受控根 + 注入清单）。design-ch4-workspace §三/§五——Mau 基座标准能力。
    /// 语义：工作区文件缺失 / roots 为空 = 默认单根 runtime 兜底（全局白名单语义——空=未配置白名单）；文件存在但 JSON 损坏 / 根不存在 → 显式失败（不静默降级到裸根）。
    /// </summary>
    public sealed class WorkspaceConfig
    {
        /// <summary>
        /// 受控根条目——id + 路径 + 可写标志（writable=false 拒绝写操作）
        /// </summary>
        public sealed class RootEntry
        {
            /// <summary>
            /// 根标识——命名空间寻址前缀（ccbp:L1/Tree.md 的 ccbp）
            /// </summary>
            public string Id = "";

            /// <summary>
            /// 规范绝对路径
            /// </summary>
            public string Path = "";

            /// <summary>
            /// 是否允许写入——false 时 file.write/append/replace/delete 拒绝
            /// </summary>
            public bool Writable;
        }

        /// <summary>
        /// 注入清单条目——文件寻址 + 可选标志 + 来源标注
        /// </summary>
        public sealed class InjectEntry
        {
            /// <summary>
            /// 文件寻址——id:relative（映射受控根）或纯路径
            /// </summary>
            public string File = "";

            /// <summary>
            /// 缺失是否可跳过——true 缺失仅警告（inject.missing 审计）
            /// </summary>
            public bool Optional;

            /// <summary>
            /// 来源标注——持久区可见（"系统前文来自哪"）
            /// </summary>
            public string Label = "";
        }

        /// <summary>
        /// 受控根列表——有序；首根为相对路径解析基准（FindOwningRoot 最长前缀匹配）
        /// </summary>
        public RootEntry[] Roots = new RootEntry[0];

        /// <summary>
        /// 注入清单——有序（按列表顺序拼接）
        /// </summary>
        public InjectEntry[] Inject = new InjectEntry[0];

        // [段1] 加载
        /// <summary>
        /// 从 workspace.json 加载——文件缺失或 roots 为空返回默认配置（单根 defaultRoot 可写 + 空注入）；存在则解析，损坏/根不存在抛异常
        /// </summary>
        /// <param name="path">workspace.json 路径</param>
        /// <param name="defaultRoot">缺文件时的兜底根（数据根，可写）</param>
        /// <returns>工作区配置</returns>
        public static WorkspaceConfig Load(string path, string defaultRoot)
        {
            WorkspaceConfig cfg = new WorkspaceConfig();
            if (path == null || path.Length == 0 || !File.Exists(path))
            {
                cfg.Roots = new RootEntry[] { new RootEntry() { Id = "runtime", Path = NormalizeRoot(defaultRoot), Writable = true } };
                cfg.Inject = new InjectEntry[0];
                return cfg;
            }
            using (JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path)))
            {
                JsonElement root = doc.RootElement;
                // [段1a] roots 解析
                List<RootEntry> roots = new List<RootEntry>();
                JsonElement rootsEl;
                if (root.TryGetProperty("roots", out rootsEl) && rootsEl.ValueKind == JsonValueKind.Array)
                {
                    for (int i = 0; i < rootsEl.GetArrayLength(); i++)
                    {
                        JsonElement item = rootsEl[i];
                        string id = GetProp(item, "id");
                        string raw = GetProp(item, "path");
                        bool writable = true;
                        JsonElement wEl;
                        if (item.TryGetProperty("writable", out wEl) && wEl.ValueKind == JsonValueKind.False)
                        {
                            writable = false;
                        }
                        if (id.Length == 0 || raw.Length == 0)
                        {
                            throw new InvalidDataException("workspace.roots[" + i.ToString() + "] 缺 id/path");
                        }
                        string norm = NormalizeRoot(raw);
                        if (!Directory.Exists(norm))
                        {
                            throw new InvalidDataException("workspace root 不存在: " + norm);
                        }
                        roots.Add(new RootEntry() { Id = id, Path = norm, Writable = writable });
                    }
                }
                if (roots.Count == 0)
                {
                    // 空 roots = 未配置白名单——默认单根 runtime 兜底（同文件缺失语义；FileSystemService 需至少一个根）
                    cfg.Roots = new RootEntry[] { new RootEntry() { Id = "runtime", Path = NormalizeRoot(defaultRoot), Writable = true } };
                    cfg.Inject = new InjectEntry[0];
                    return cfg;
                }
                cfg.Roots = roots.ToArray();
                // [段1b] inject 解析
                List<InjectEntry> inject = new List<InjectEntry>();
                JsonElement injectEl;
                if (root.TryGetProperty("inject", out injectEl) && injectEl.ValueKind == JsonValueKind.Array)
                {
                    for (int i = 0; i < injectEl.GetArrayLength(); i++)
                    {
                        JsonElement item = injectEl[i];
                        string file = GetProp(item, "file");
                        if (file.Length == 0)
                        {
                            continue;
                        }
                        bool optional = false;
                        JsonElement optEl;
                        if (item.TryGetProperty("optional", out optEl) && optEl.ValueKind == JsonValueKind.True)
                        {
                            optional = true;
                        }
                        string label = GetProp(item, "label");
                        inject.Add(new InjectEntry() { File = file, Optional = optional, Label = label });
                    }
                }
                cfg.Inject = inject.ToArray();
            }
            return cfg;
        }

        // [段2] 解析辅助
        /// <summary>
        /// 读取字符串属性——缺字段返回空串（防御式）
        /// </summary>
        /// <param name="obj">JSON 元素</param>
        /// <param name="prop">属性名</param>
        /// <returns>属性值</returns>
        private static string GetProp(JsonElement obj, string prop)
        {
            JsonElement value;
            if (obj.TryGetProperty(prop, out value) && value.ValueKind == JsonValueKind.String)
            {
                string? got = value.GetString();
                if (got != null)
                {
                    return got;
                }
            }
            return "";
        }

        /// <summary>
        /// 解析注入文件寻址——id:relative 映射受控根；纯路径 GetFullPath 直用（清单是管理员静态动作——不强制 roots 内，但约定 id: 命名空间）
        /// </summary>
        /// <param name="entry">注入条目</param>
        /// <returns>规范绝对路径</returns>
        public string ResolveInjectFile(InjectEntry entry)
        {
            string file = entry.File;
            int sep = file.IndexOf(':');
            if (sep > 0)
            {
                string id = file.Substring(0, sep);
                string rel = file.Substring(sep + 1);
                for (int i = 0; i < Roots.Length; i++)
                {
                    if (string.Equals(Roots[i].Id, id, StringComparison.Ordinal))
                    {
                        return Path.GetFullPath(Path.Combine(Roots[i].Path, rel));
                    }
                }
                throw new InvalidDataException("注入清单引用了未知根: " + id);
            }
            return Path.GetFullPath(file);
        }

        /// <summary>
        /// 规范化根路径——与 FileSystemService 同规（GetFullPath + 去掉尾部分隔符）
        /// </summary>
        /// <param name="root">原始根路径</param>
        /// <returns>规范绝对路径</returns>
        public static string NormalizeRoot(string root)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                throw new ArgumentException("Workspace root is empty.", "root");
            }
            string full = Path.GetFullPath(root);
            if (full.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) && full.Length > 3)
            {
                full = full.TrimEnd(Path.DirectorySeparatorChar);
            }
            return full;
        }
    }
}
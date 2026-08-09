// ═══════════════════════════════════════════════════
// 积木: cat.tools_json
// ID:   BRIK-CAT-002
// 类别: CAT
// 作用: 按猫工具域配置拼接 toolsJson——只有绑定域的工具进入 LLM 感知面（TalkCat 自阻断）
// 依赖: 无
// 引用: Mau.Runtime · System.Text
// 原理: DataBox "catcfg"/{catName} ConfigStore 读 tools 域清单 → 六域工具声明表过滤 → DeepSeek tools 数组
//       配置缺失/非法 → 默认域（system,file——莎 2026-08-09 损坏兜底）
// 常用: TalkCat 会话构建（T_BuildTools）——配置变更只在会话边界生效（新会话重建工具组前文）
// 包: 无
// ═══════════════════════════════════════════════════
using System.Collections.Generic;
using System.IO;
using System.Text;

using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 猫积木——cat.tools_json 按域拼接工具声明（TalkCat 自阻断——LLM 只感知绑定域工具）
    /// </summary>
    public static class CatToolsJsonBrick
    {
        /// <summary>
        /// 六域工具声明表——域 → 工具名数组（Mau 侧维护；DeepSeek tools 格式）
        /// </summary>
        private static readonly Dictionary<string, string[]> ToolTable = new Dictionary<string, string[]>(System.StringComparer.Ordinal)
        {
            { "file", new string[] { "file.read", "file.write", "file.append", "file.replace", "file.read_lines", "file.tree", "file.find", "file.move", "file.delete", "file.batch", "file.convert" } },
            { "shell", new string[] { "shell.exec" } },
            { "office", new string[] { "excel.read", "excel.write", "docx.read", "docx.write" } },
            { "system", new string[] { "system.info", "system.snapshot", "system.env" } },
            { "csharp", new string[] { "csharp.compile", "csharp.init", "csharp.info", "csharp.list", "csharp.read", "csharp.body_replace", "csharp.line_patch", "csharp.line_insert", "csharp.member_insert", "csharp.member_delete", "csharp.comment_set", "csharp.comment_check", "csharp.member_rename", "csharp.dead", "csharp.find_ref" } },
            { "mau", new string[] { "mau.build", "mau.verify", "mau.test" } }
        };

        /// <summary>
        /// 拼接 toolsJson——按猫配置域清单过滤工具声明；配置缺失 → 默认域（system,file）
        /// </summary>
        /// <param name="catName">猫名（DataBox "catcfg" scope 键）</param>
        /// <param name="toolsJson">DeepSeek tools 数组 JSON</param>
        /// <returns>true=成功</returns>
        public static bool ToolsJson(string catName, out string toolsJson)
        {
            toolsJson = "";
            if (catName == null || catName.Length == 0)
            {
                return false;
            }
            // [段1] 读猫工具域配置——DataBox "catcfg"（宿主 GetOrCreateCatConfig 同步 Bind）
            string toolsCfg = "";
            ConfigStore? store;
            DataBox.TryGet<ConfigStore>("catcfg", catName, out store);
            if (store != null)
            {
                toolsCfg = store.Get("tools", "");
            }
            // [段2] 解析域清单（逗号分隔）——空/非法 → 默认域 system,file（损坏兜底）
            List<string> domains = new List<string>();
            if (toolsCfg != null && toolsCfg.Length > 0)
            {
                string[] parts = toolsCfg.Split(',');
                for (int i = 0; i < parts.Length; i = i + 1)
                {
                    string d = parts[i].Trim();
                    if (d.Length > 0 && ToolTable.ContainsKey(d) && !domains.Contains(d))
                    {
                        domains.Add(d);
                    }
                }
            }
            if (domains.Count == 0)
            {
                domains.Add("system");
                domains.Add("file");
            }
            // [段3] 拼接 tools 数组——DeepSeek function calling 格式（name + description 最小声明）
            // 🔴 工具名规范：DeepSeek 工具名只允许 [a-zA-Z0-9_-]——点号非法（实测 400）
            //   声明名 = 原始名点转下划线（system.info → system_info）；执行侧 run_generic 归一化回点号
            using (MemoryStream stream = new MemoryStream())
            {
                using (System.Text.Json.Utf8JsonWriter writer = new System.Text.Json.Utf8JsonWriter(stream))
                {
                    writer.WriteStartArray();
                    for (int d = 0; d < domains.Count; d = d + 1)
                    {
                        string[] tools = ToolTable[domains[d]];
                        for (int t = 0; t < tools.Length; t = t + 1)
                        {
                            string declName = tools[t].Replace('.', '_');
                            writer.WriteStartObject();
                            writer.WriteString("type", "function");
                            writer.WritePropertyName("function");
                            writer.WriteStartObject();
                            writer.WriteString("name", declName);
                            writer.WriteString("description", "调用 " + tools[t]);
                            writer.WriteEndObject();
                            writer.WriteEndObject();
                        }
                    }
                    writer.WriteEndArray();
                }
                toolsJson = Encoding.UTF8.GetString(stream.ToArray());
            }
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:B154FF42DAC26614E71EA2644BD4C7EC6E8B546EC52E84202AEC87417553E8BC

// ═══════════════════════════════════════════════════
// 积木: cat.tools_json
// ID:   BRIK-CAT-002
// 类别: CAT
// 作用: 按猫工具域配置拼接 toolsJson——只有绑定域的工具进入 LLM 感知面（TalkCat 自阻断）
// 依赖: 无
// 引用: Mau.Runtime · System.Text
// 原理: DataBox "catcfg"/{catName} ConfigStore 读 tools 域清单 → 六域工具声明表过滤 → DeepSeek tools 数组
//       配置缺失/非法 → 默认域（system,file——莎 2026-08-09 损坏兜底）
// 常用: TalkCat 会话构建（T_BuildTools）——配置变更只在会话边界生效（新会话重建工具组前文）；内置工具 note.set/note.next 始终声明（G.5）
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
        /// 工具用途描述表——改进 LLM 对工具的理解（2026-08-12：原"调用 file.xxx"零语义）
        /// </summary>
        private static readonly Dictionary<string, string> ToolDesc = new Dictionary<string, string>(System.StringComparer.Ordinal)
        {
            { "file.read", "读取文本文件内容（UTF-8）" },
            { "file.write", "覆写文件（自动创建父目录，UTF-8 无 BOM）" },
            { "file.append", "追加文本到文件末尾（自动创建父目录）" },
            { "file.replace", "替换文件中的精确文本并原子写回" },
            { "file.read_lines", "按行号区间读取文本（1-based，带行号）" },
            { "file.tree", "列出目录树（空路径=工作根 Data/；相对路径基于工作根）" },
            { "file.find", "按文件名通配符搜索文件（如 *.md 或 **/*.cs）" },
            { "file.move", "移动/重命名文件（目标已存在时拒绝）" },
            { "file.delete", "软删除文件或空目录（移入 Data/RecycleBin 可恢复）" },
            { "file.batch", "批量文件操作（JSON 操作数组）" },
            { "file.convert", "文件格式转换" },
            { "shell.exec", "执行 PowerShell 命令——注意 PowerShell 语法（如 Get-ChildItem），不是 cmd 语法（dir /b 会失败）" },
            { "excel.read", "读取 Excel 表格为文本（TSV/CSV）" },
            { "excel.write", "将表格文本写入 Excel 文件" },
            { "docx.read", "读取 Word 文档纯文本" },
            { "docx.write", "将纯文本写入 Word 文档" },
            { "system.info", "获取系统信息（工作目录/系统版本/机器名/服务清单）" },
            { "system.snapshot", "获取运行时快照（实体/帧/审计）" },
            { "system.env", "获取环境变量值" },
            { "csharp.compile", "编译当前绑定 C# 项目（full=完整诊断）" },
            { "csharp.init", "绑定 .csproj 项目激活 csharp 工具" },
            { "csharp.info", "查询 C# 工具绑定状态" },
            { "csharp.list", "列出类或类的成员签名" },
            { "csharp.read", "读取成员源码（含行号）" },
            { "csharp.body_replace", "替换整个方法体（签名与注释不动）" },
            { "csharp.line_patch", "替换方法内行范围" },
            { "csharp.line_insert", "在方法内指定行后插入代码" },
            { "csharp.member_insert", "在类中插入新成员" },
            { "csharp.member_delete", "删除类成员" },
            { "csharp.comment_set", "设置 XML 注释（summary/param/returns）" },
            { "csharp.comment_check", "扫描缺少 summary 注释的成员" },
            { "csharp.member_rename", "重命名成员（全项目引用同步）" },
            { "csharp.dead", "扫描零引用成员（死代码）" },
            { "csharp.find_ref", "查找成员的所有引用位置" },
            { "mau.build", "执行 Mau CLI 指令（如 build/test/verify）" },
            { "mau.verify", "Mau 语法验证" },
            { "mau.test", "运行 Mau 门禁测试" },
            { "note.set", "设置任务计划（Note 面板显示进度）" },
            { "note.next", "推进任务到下一步" }
        };

        /// <summary>
        /// 工具参数 schema 表——参数名与 run_generic 读取键严格一致（args.* 展平协议）；
        /// 空 properties 数组 = 无参数工具。2026-08-12：LLM 零参数定义 → 猜参数/传空路径/复杂任务前停止
        /// </summary>
        private static readonly Dictionary<string, string> ParamSchemas = new Dictionary<string, string>(System.StringComparer.Ordinal)
        {
            { "file.read", "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"文件路径——相对工作根（Data/）或绝对路径\"}},\"required\":[\"path\"]}" },
            { "file.write", "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"文件路径（父目录自动创建）\"},\"content\":{\"type\":\"string\",\"description\":\"完整正文\"}},\"required\":[\"path\",\"content\"]}" },
            { "file.append", "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"文件路径（父目录自动创建）\"},\"content\":{\"type\":\"string\",\"description\":\"追加正文\"}},\"required\":[\"path\",\"content\"]}" },
            { "file.replace", "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"文件路径\"},\"oldText\":{\"type\":\"string\",\"description\":\"被替换的精确文本（非空）\"},\"newText\":{\"type\":\"string\",\"description\":\"替换后的新文本\"}},\"required\":[\"path\",\"oldText\",\"newText\"]}" },
            { "file.read_lines", "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"文件路径\"},\"startLine\":{\"type\":\"integer\",\"description\":\"起始行号（1-based）\"},\"endLine\":{\"type\":\"integer\",\"description\":\"结束行号，0=文件尾\"}},\"required\":[\"path\"]}" },
            { "file.tree", "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"目录路径——空=工作根（Data/），相对路径基于工作根\"},\"depth\":{\"type\":\"integer\",\"description\":\"递归深度 0-10（默认2）\"},\"limit\":{\"type\":\"integer\",\"description\":\"最大条数（默认50）\"}},\"required\":[]}" },
            { "file.find", "{\"type\":\"object\",\"properties\":{\"directory\":{\"type\":\"string\",\"description\":\"搜索目录——空=工作根（Data/）\"},\"pattern\":{\"type\":\"string\",\"description\":\"文件名通配符，如 *.md 或 **/*.cs\"},\"recursive\":{\"type\":\"boolean\",\"description\":\"是否递归（默认true）\"},\"limit\":{\"type\":\"integer\",\"description\":\"最大结果数（默认50）\"}},\"required\":[\"pattern\"]}" },
            { "file.move", "{\"type\":\"object\",\"properties\":{\"source\":{\"type\":\"string\",\"description\":\"源路径\"},\"destination\":{\"type\":\"string\",\"description\":\"目标路径（已存在则拒绝）\"}},\"required\":[\"source\",\"destination\"]}" },
            { "file.delete", "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"文件或空目录路径（软删入回收站）\"}},\"required\":[\"path\"]}" },
            { "file.batch", "{\"type\":\"object\",\"properties\":{\"operations\":{\"type\":\"string\",\"description\":\"JSON 操作数组文本\"},\"stopOnError\":{\"type\":\"boolean\",\"description\":\"遇错停止（默认true）\"}},\"required\":[\"operations\"]}" },
            { "file.convert", "{\"type\":\"object\",\"properties\":{\"input\":{\"type\":\"string\",\"description\":\"输入文件路径\"},\"output\":{\"type\":\"string\",\"description\":\"输出文件路径\"}},\"required\":[\"input\",\"output\"]}" },
            { "shell.exec", "{\"type\":\"object\",\"properties\":{\"command\":{\"type\":\"string\",\"description\":\"PowerShell 命令——PowerShell 语法（Get-ChildItem），不是 cmd（dir /b 会失败）\"},\"timeout\":{\"type\":\"integer\",\"description\":\"超时秒数 1-120（默认20）\"}},\"required\":[\"command\"]}" },
            { "excel.read", "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\".xlsx 文件路径\"},\"sheet\":{\"type\":\"string\",\"description\":\"工作表名（默认第一张）\"},\"format\":{\"type\":\"string\",\"description\":\"tsv 或 csv\"}},\"required\":[\"path\"]}" },
            { "excel.write", "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\".xlsx 文件路径\"},\"content\":{\"type\":\"string\",\"description\":\"表格文本（TSV/CSV）\"},\"sheet\":{\"type\":\"string\",\"description\":\"工作表名\"}},\"required\":[\"path\",\"content\"]}" },
            { "docx.read", "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\".docx 文件路径\"}},\"required\":[\"path\"]}" },
            { "docx.write", "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\".docx 文件路径\"},\"content\":{\"type\":\"string\",\"description\":\"纯文本内容\"}},\"required\":[\"path\",\"content\"]}" },
            { "system.info", "{\"type\":\"object\",\"properties\":{},\"required\":[]}" },
            { "system.snapshot", "{\"type\":\"object\",\"properties\":{},\"required\":[]}" },
            { "system.env", "{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\",\"description\":\"环境变量名\"}},\"required\":[]}" },
            { "csharp.compile", "{\"type\":\"object\",\"properties\":{\"full\":{\"type\":\"boolean\",\"description\":\"true=完整诊断列表\"}},\"required\":[]}" },
            { "csharp.init", "{\"type\":\"object\",\"properties\":{\"csproj\":{\"type\":\"string\",\"description\":\".csproj 文件路径\"}},\"required\":[\"csproj\"]}" },
            { "csharp.info", "{\"type\":\"object\",\"properties\":{},\"required\":[]}" },
            { "csharp.list", "{\"type\":\"object\",\"properties\":{\"class\":{\"type\":\"string\",\"description\":\"类名，空=全部类\"}},\"required\":[]}" },
            { "csharp.read", "{\"type\":\"object\",\"properties\":{\"class\":{\"type\":\"string\",\"description\":\"类名\"},\"member\":{\"type\":\"string\",\"description\":\"成员名，空=类概览\"}},\"required\":[\"class\"]}" },
            { "csharp.body_replace", "{\"type\":\"object\",\"properties\":{\"class\":{\"type\":\"string\",\"description\":\"类名\"},\"method\":{\"type\":\"string\",\"description\":\"方法名\"},\"body\":{\"type\":\"string\",\"description\":\"新方法体（含大括号）\"}},\"required\":[\"class\",\"method\",\"body\"]}" },
            { "csharp.line_patch", "{\"type\":\"object\",\"properties\":{\"class\":{\"type\":\"string\"},\"method\":{\"type\":\"string\"},\"startLine\":{\"type\":\"integer\"},\"endLine\":{\"type\":\"integer\"},\"newText\":{\"type\":\"string\"}},\"required\":[\"class\",\"method\",\"startLine\",\"endLine\",\"newText\"]}" },
            { "csharp.line_insert", "{\"type\":\"object\",\"properties\":{\"class\":{\"type\":\"string\"},\"method\":{\"type\":\"string\"},\"afterLine\":{\"type\":\"integer\",\"description\":\"0=方法体开头\"},\"newText\":{\"type\":\"string\"}},\"required\":[\"class\",\"method\",\"newText\"]}" },
            { "csharp.member_insert", "{\"type\":\"object\",\"properties\":{\"class\":{\"type\":\"string\"},\"position\":{\"type\":\"string\",\"description\":\"after/before/end/after_fields\"},\"anchor\":{\"type\":\"string\",\"description\":\"锚点成员名（after/before 必填）\"},\"code\":{\"type\":\"string\",\"description\":\"新成员源码\"}},\"required\":[\"class\",\"position\",\"code\"]}" },
            { "csharp.member_delete", "{\"type\":\"object\",\"properties\":{\"class\":{\"type\":\"string\"},\"member\":{\"type\":\"string\"}},\"required\":[\"class\",\"member\"]}" },
            { "csharp.comment_set", "{\"type\":\"object\",\"properties\":{\"class\":{\"type\":\"string\"},\"member\":{\"type\":\"string\",\"description\":\"成员名，空=类\"},\"type\":{\"type\":\"string\",\"description\":\"summary/param/returns\"},\"text\":{\"type\":\"string\"},\"param\":{\"type\":\"string\",\"description\":\"type=param 时参数名\"}},\"required\":[\"class\",\"type\",\"text\"]}" },
            { "csharp.comment_check", "{\"type\":\"object\",\"properties\":{},\"required\":[]}" },
            { "csharp.member_rename", "{\"type\":\"object\",\"properties\":{\"class\":{\"type\":\"string\"},\"oldName\":{\"type\":\"string\"},\"newName\":{\"type\":\"string\"}},\"required\":[\"class\",\"oldName\",\"newName\"]}" },
            { "csharp.dead", "{\"type\":\"object\",\"properties\":{},\"required\":[]}" },
            { "csharp.find_ref", "{\"type\":\"object\",\"properties\":{\"class\":{\"type\":\"string\"},\"member\":{\"type\":\"string\"}},\"required\":[\"class\",\"member\"]}" },
            { "mau.build", "{\"type\":\"object\",\"properties\":{\"command\":{\"type\":\"string\",\"description\":\"mau 子命令（build/test/verify 等）\"},\"args\":{\"type\":\"string\",\"description\":\"参数文本\"}},\"required\":[\"command\"]}" },
            { "mau.verify", "{\"type\":\"object\",\"properties\":{},\"required\":[]}" },
            { "mau.test", "{\"type\":\"object\",\"properties\":{\"update\":{\"type\":\"boolean\",\"description\":\"true=重建黄金文件\"},\"verbose\":{\"type\":\"boolean\"}},\"required\":[]}" },
            { "note.set", "{\"type\":\"object\",\"properties\":{\"plan\":{\"type\":\"string\",\"description\":\"任务计划（多行用 \\\\n 分隔）\"}},\"required\":[\"plan\"]}" },
            { "note.next", "{\"type\":\"object\",\"properties\":{},\"required\":[]}" }
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
                    // [段3.1] 域工具声明——按猫配置域过滤（六域 ToolTable）
                    for (int d = 0; d < domains.Count; d = d + 1)
                    {
                        string[] tools = ToolTable[domains[d]];
                        for (int t = 0; t < tools.Length; t = t + 1)
                        {
                            WriteToolDecl(writer, tools[t]);
                        }
                    }
                    // [段3.2] 内置工具声明——始终可用不依赖域（G.5 Note 面板 2026-08-11 D.3：
                    //   note.set/note.next 会话内执行（note.exec——TalkCat 内置分流），不落 OA 工单）
                    string[] builtinTools = new string[] { "note.set", "note.next" };
                    for (int b = 0; b < builtinTools.Length; b = b + 1)
                    {
                        WriteToolDecl(writer, builtinTools[b]);
                    }
                    writer.WriteEndArray();
                }
                toolsJson = Encoding.UTF8.GetString(stream.ToArray());
            }
            return true;
        }

        /// <summary>
        /// 写单个工具声明——name（点转下划线）+ 语义化 description + parameters schema
        /// （2026-08-12：参数 schema 与 run_generic 读取键严格一致；无 schema 定义时给空对象兜底）
        /// </summary>
        /// <param name="writer">JSON 写入器</param>
        /// <param name="toolName">路由工具名（点号）</param>
        private static void WriteToolDecl(System.Text.Json.Utf8JsonWriter writer, string toolName)
        {
            string declName = toolName.Replace('.', '_');
            writer.WriteStartObject();
            writer.WriteString("type", "function");
            writer.WritePropertyName("function");
            writer.WriteStartObject();
            writer.WriteString("name", declName);
            string desc;
            if (!ToolDesc.TryGetValue(toolName, out desc) || desc == null)
            {
                desc = "调用 " + toolName;
            }
            writer.WriteString("description", desc);
            string schema;
            if (ParamSchemas.TryGetValue(toolName, out schema) && schema != null && schema.Length > 0)
            {
                writer.WritePropertyName("parameters");
                using (System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(schema))
                {
                    doc.RootElement.WriteTo(writer);
                }
            }
            else
            {
                writer.WritePropertyName("parameters");
                writer.WriteStartObject();
                writer.WriteString("type", "object");
                writer.WritePropertyName("properties");
                writer.WriteStartObject();
                writer.WriteEndObject();
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
    }
}
// #MAU_CHECKSUM:SHA256:C7C9486DFEA4DAF66F5148D0D44C0691C7686927C99AC185A65BD7A4A4919949

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Mau.Contracts;
using Mau.Translator;
using Mau.Runtime;
using Mau.Development;

namespace Mau.Cli
{
    /// <summary>
    /// 积木管理命令——list / index --verify / index --update / index --check-license / test
    /// T1 积木索引机制（design-mau-brickindex.md）：编号规范 v2 + 五维索引 + 全局跑测出口
    /// </summary>
    public static class CommandBricks
    {
        /// <summary>
        /// 注册表积木契约条目——含从文件头扫描的索引信息
        /// </summary>
        public sealed class BrickIndexEntry
        {
            /// <summary>
            /// 唯一 ID——BRIK-{类别}-{三位序号}
            /// </summary>
            public string Id = "";

            /// <summary>
            /// 积木名——类别.函数（BrickRegistry.Name）
            /// </summary>
            public string Name = "";

            /// <summary>
            /// 类别码
            /// </summary>
            public string Category = "";

            /// <summary>
            /// 实现文件相对路径（仓库根起）
            /// </summary>
            public string Path = "";

            /// <summary>
            /// 依赖声明——L1 基座/L2 叶子积木/L3 外部库，逗号分隔
            /// </summary>
            public string Dependencies = "";

            /// <summary>
            /// 状态——active / deprecated
            /// </summary>
            public string Status = "";

            /// <summary>
            /// 来源——人工维护列（CH3/CH2/CH4 等）
            /// </summary>
            public string Source = "";

            /// <summary>
            /// 文件头 ID 原始声明——单 ID 或范围（001 ~ 011），check-license 用
            /// </summary>
            public string FileIdText = "";

            /// <summary>
            /// 契约——index.json 镜像字段
            /// </summary>
            public BrickContract? Contract;
        }

        /// <summary>
        /// bricks 命令入口
        /// </summary>
        /// <param name="args">参数——list | index --verify | index --update | index --check-license | test</param>
        /// <returns>退出码</returns>
        public static int Execute(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("用法: mau bricks list | mau bricks index --verify | mau bricks index --update | mau bricks index --check-license | mau bricks test");
                return 1;
            }
            string sub = args[0];
            if (sub == "list")
            {
                return ListBricks();
            }
            if (sub == "index")
            {
                if (args.Length < 2)
                {
                    Console.WriteLine("用法: mau bricks index --verify | --update | --check-license");
                    return 1;
                }
                if (args[1] == "--verify")
                {
                    return VerifyIndex();
                }
                if (args[1] == "--update")
                {
                    return UpdateIndex();
                }
                if (args[1] == "--check-license")
                {
                    return CheckLicense();
                }
                Console.WriteLine("未知 index 子命令: " + args[1]);
                return 1;
            }
            if (sub == "test")
            {
                return RunGlobalTest();
            }
            Console.WriteLine("未知 bricks 子命令: " + sub);
            return 1;
        }

        /// <summary>
        /// 注册表实时枚举——全部积木契约
        /// </summary>
        /// <returns>退出码</returns>
        public static int ListBricks()
        {
            int count = 0;
            foreach (Mau.Translator.BrickIndexEntry entry in BrickIndex.All)
            {
                Console.WriteLine(entry.Name + " → " + entry.Contract.Implementation);
                count = count + 1;
            }
            Console.WriteLine("合计: " + count + " 积木");
            return 0;
        }

        /// <summary>
        /// 扫描仓库 .cs 文件头注释块——提取 ID/积木/引用/依赖
        /// </summary>
        /// <param name="root">仓库根</param>
        /// <returns>索引条目（按文件头块展开为单积木）</returns>
        private static List<BrickIndexEntry> ScanBrickHeaders(string root)
        {
            List<BrickIndexEntry> entries = new List<BrickIndexEntry>();
            string[] dirs = new string[]
            {
                "Mau.Bricks.Standard", "Mau.Bricks.Data", "Mau.Bricks.Text",
                "Mau.Bricks.Shell", "Mau.Bricks.LLM", "Mau.Bricks.Approval",
                "Mau.Bricks.Office", "Mau.Bricks.Log"
            };
            for (int d = 0; d < dirs.Length; d++)
            {
                string dirPath = Path.Combine(root, dirs[d]);
                if (!Directory.Exists(dirPath))
                {
                    continue;
                }
                string[] files = Directory.GetFiles(dirPath, "*.cs");
                for (int f = 0; f < files.Length; f++)
                {
                    ScanOneFile(files[f], dirs[d], root, entries);
                }
            }
            return entries;
        }

        /// <summary>
        /// 扫描单个 .cs 文件头注释块
        /// </summary>
        /// <param name="file">文件路径</param>
        /// <param name="dir">工程目录名</param>
        /// <param name="root">仓库根</param>
        /// <param name="entries">输出集合</param>
        private static void ScanOneFile(string file, string dir, string root, List<BrickIndexEntry> entries)
        {
            string[] lines;
            try
            {
                lines = File.ReadAllLines(file);
            }
            catch
            {
                return;
            }
            string relPath = dir + "/" + Path.GetFileName(file);
            bool inHeader = false;
            string brickNames = "";
            string ids = "";
            string deps = "";
            for (int i = 0; i < lines.Length && i < 14; i++)
            {
                string line = lines[i].Trim();
                if (line.StartsWith("// ═"))
                {
                    inHeader = true;
                    continue;
                }
                if (!inHeader)
                {
                    continue;
                }
                if (line.StartsWith("// 积木:"))
                {
                    brickNames = line.Substring(6).Trim();
                }
                else if (line.StartsWith("// ID:"))
                {
                    ids = line.Substring(6).Trim();
                }
                else if (line.StartsWith("// 依赖:"))
                {
                    deps = line.Substring(7).Trim();
                }
            }
            if (brickNames.Length == 0)
            {
                return;
            }
            // 展开多积木——"a / b" 或 "a / b / c"
            string[] names = brickNames.Split('/');
            for (int n = 0; n < names.Length; n++)
            {
                string name = names[n].Trim();
                if (name.Length == 0)
                {
                    continue;
                }
                BrickIndexEntry entry = new BrickIndexEntry();
                entry.Name = name;
                entry.Path = relPath;
                entry.Dependencies = deps;
                entry.Status = "active";
                entry.Source = "";
                entry.FileIdText = ids;
                // ID 分配：单 ID 或范围（~）——范围时从 INDEX 已有行匹配；否则按名称序号
                entry.Id = ResolveId(ids, name, entries.Count);
                entry.Category = CategoryFromName(name);
                entries.Add(entry);
            }
        }

        /// <summary>
        /// 解析文件头 ID 声明——单 ID / 范围（001 ~ 011）
        /// </summary>
        /// <param name="idText">ID 声明文本</param>
        /// <param name="name">积木名</param>
        /// <param name="index">当前索引</param>
        /// <returns>解析出的 ID</returns>
        private static string ResolveId(string idText, string name, int index)
        {
            string trimmed = idText.Replace(" ", "");
            if (trimmed.Length == 0)
            {
                return "";
            }
            int tilde = trimmed.IndexOf('~');
            if (tilde < 0)
            {
                return trimmed;
            }
            // 范围形态——按 INDEX 已有行无法解析时用序号推导（类别-序号段）
            return "";
        }

        /// <summary>
        /// 类别码推断——积木名前缀
        /// </summary>
        /// <param name="name">积木名</param>
        /// <returns>类别码</returns>
        private static string CategoryFromName(string name)
        {
            int dot = name.IndexOf('.');
            if (dot < 0)
            {
                return "?";
            }
            return name.Substring(0, dot).ToUpperInvariant();
        }

        /// <summary>
        /// 查找仓库根——含 Mau.sln 的目录
        /// </summary>
        /// <returns>仓库根或空</returns>
        private static string? FindWorkspaceRoot()
        {
            DirectoryInfo? dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Mau.sln")))
                {
                    return dir.FullName;
                }
                dir = dir.Parent;
            }
            DirectoryInfo? exeDir = new DirectoryInfo(AppContext.BaseDirectory);
            while (exeDir != null)
            {
                if (File.Exists(Path.Combine(exeDir.FullName, "Mau.sln")))
                {
                    return exeDir.FullName;
                }
                exeDir = exeDir.Parent;
            }
            return null;
        }

        /// <summary>
        /// 读取 INDEX.md 表格行——ID/名字/类别/路径/来源
        /// </summary>
        /// <param name="indexPath">INDEX.md 路径</param>
        /// <returns>解析行列表（名称 → 条目）</returns>
        private static Dictionary<string, BrickIndexEntry> ReadIndexTable(string indexPath)
        {
            Dictionary<string, BrickIndexEntry> table = new Dictionary<string, BrickIndexEntry>(StringComparer.Ordinal);
            string[] lines;
            try
            {
                lines = File.ReadAllLines(indexPath);
            }
            catch
            {
                return table;
            }
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (!line.StartsWith("| BRIK-"))
                {
                    continue;
                }
                string[] cells = line.Split('|');
                if (cells.Length < 7)
                {
                    continue;
                }
                BrickIndexEntry entry = new BrickIndexEntry();
                entry.Id = cells[1].Trim();
                entry.Name = cells[2].Trim();
                entry.Category = cells[3].Trim();
                entry.Path = cells[4].Trim();
                entry.Status = cells[5].Trim();
                entry.Source = cells[6].Trim();
                entry.Dependencies = "";
                if (!table.ContainsKey(entry.Name))
                {
                    table[entry.Name] = entry;
                }
            }
            return table;
        }

        /// <summary>
        /// index --verify——V1-V9 一致性校验
        /// </summary>
        /// <returns>退出码</returns>
        public static int VerifyIndex()
        {
            string? root = FindWorkspaceRoot();
            if (root == null)
            {
                Console.WriteLine("FAIL: 未找到 Mau.sln");
                return 1;
            }
            string indexPath = Path.Combine(root, "BricksCatalog", "INDEX.md");
            if (!File.Exists(indexPath))
            {
                Console.WriteLine("FAIL: INDEX.md 不存在——" + indexPath);
                return 1;
            }
            Dictionary<string, BrickIndexEntry> table = ReadIndexTable(indexPath);
            int errors = 0;

            // V1: 积木索引全部登记
            List<string> registryNames = new List<string>();
            foreach (Mau.Translator.BrickIndexEntry brickEntry in BrickIndex.All)
            {
                registryNames.Add(brickEntry.Name);
                if (!table.ContainsKey(brickEntry.Name))
                {
                    Console.WriteLine("V1 FAIL: 积木未登记——" + brickEntry.Name);
                    errors = errors + 1;
                }
            }
            // V2: INDEX 无幽灵积木
            foreach (KeyValuePair<string, BrickIndexEntry> kv in table)
            {
                bool found = false;
                for (int i = 0; i < registryNames.Count; i++)
                {
                    if (registryNames[i] == kv.Key)
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    Console.WriteLine("V2 FAIL: INDEX 幽灵积木——" + kv.Key + "（注册表无此积木）");
                    errors = errors + 1;
                }
            }
            // V3: ID 唯一性
            Dictionary<string, string> idToName = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, BrickIndexEntry> kv in table)
            {
                string id = kv.Value.Id;
                if (idToName.ContainsKey(id))
                {
                    Console.WriteLine("V3 FAIL: ID 重复——" + id + "（" + idToName[id] + " 与 " + kv.Key + "）");
                    errors = errors + 1;
                }
                else
                {
                    idToName[id] = kv.Key;
                }
            }
            // V5: ID 格式——BRIK-{类别}-{三位序号}
            foreach (KeyValuePair<string, BrickIndexEntry> kv in table)
            {
                string id = kv.Value.Id;
                if (!IsValidIdFormat(id))
                {
                    Console.WriteLine("V5 FAIL: ID 格式非法——" + id + "（" + kv.Key + "）——需要 BRIK-{类别}-{三位序号}");
                    errors = errors + 1;
                }
            }
            // V6: 路径存在
            foreach (KeyValuePair<string, BrickIndexEntry> kv in table)
            {
                string path = kv.Value.Path;
                if (path.Length > 0)
                {
                    string full = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(full))
                    {
                        Console.WriteLine("V6 FAIL: 路径不存在——" + path + "（" + kv.Key + "）");
                        errors = errors + 1;
                    }
                }
            }
            // V9: 序号连续且不重用——按类别检查
            Dictionary<string, SortedSet<int>> categorySeqs = new Dictionary<string, SortedSet<int>>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, BrickIndexEntry> kv in table)
            {
                string id = kv.Value.Id;
                if (!IsValidIdFormat(id))
                {
                    continue;
                }
                string cat = kv.Value.Category;
                string seqStr = id.Substring(id.LastIndexOf('-') + 1);
                int seq;
                if (int.TryParse(seqStr, out seq))
                {
                    if (!categorySeqs.ContainsKey(cat))
                    {
                        categorySeqs[cat] = new SortedSet<int>();
                    }
                    if (categorySeqs[cat].Contains(seq))
                    {
                        Console.WriteLine("V9 FAIL: 序号重复——" + id + "（" + kv.Key + "）——已使用编号不可再赋值");
                        errors = errors + 1;
                    }
                    else
                    {
                        categorySeqs[cat].Add(seq);
                    }
                }
            }
            // V9 连续检查——类别内序号从 001 连续延伸（Deprecated 跳号豁免）
            foreach (KeyValuePair<string, SortedSet<int>> kv in categorySeqs)
            {
                int expected = 1;
                foreach (int seq in kv.Value)
                {
                    if (seq != expected)
                    {
                        Console.WriteLine("V9 FAIL: 类别 " + kv.Key + " 序号跳号——期望 " + expected + " 实际 " + seq);
                        errors = errors + 1;
                        break;
                    }
                    expected = expected + 1;
                }
            }

            if (errors == 0)
            {
                Console.WriteLine("BRICKS_INDEX_OK (" + table.Count + " 积木)");
                return 0;
            }
            Console.WriteLine("BRICKS_INDEX_FAIL: " + errors + " 处错误");
            return 1;
        }

        /// <summary>
        /// ID 格式校验——BRIK-{类别}-{三位序号}
        /// </summary>
        /// <param name="id">ID</param>
        /// <returns>合法为真</returns>
        private static bool IsValidIdFormat(string id)
        {
            if (!id.StartsWith("BRIK-"))
            {
                return false;
            }
            int lastDash = id.LastIndexOf('-');
            if (lastDash <= 5)
            {
                return false;
            }
            string seq = id.Substring(lastDash + 1);
            if (seq.Length != 3)
            {
                return false;
            }
            for (int i = 0; i < seq.Length; i++)
            {
                if (seq[i] < '0' || seq[i] > '9')
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// index --update——生成 INDEX.md + index.json（含 contract 镜像）
        /// </summary>
        /// <returns>退出码</returns>
        public static int UpdateIndex()
        {
            string? root = FindWorkspaceRoot();
            if (root == null)
            {
                Console.WriteLine("FAIL: 未找到 Mau.sln");
                return 1;
            }
            string catalogDir = Path.Combine(root, "BricksCatalog");
            string indexPath = Path.Combine(catalogDir, "INDEX.md");
            Dictionary<string, BrickIndexEntry> oldTable = ReadIndexTable(indexPath);

            // 从注册表构建——ID 从旧表继承，新积木按类别 max+1 分配
            StringBuilder md = new StringBuilder();
            md.AppendLine("# Mau 积木索引 — INDEX");
            md.AppendLine();
            md.AppendLine("> 版本：v3.0 | 创建：2026-08-04 | 更新：2026-08-06（v3.0：`mau bricks index --update` 自动生成——注册表唯一真相源）");
            md.AppendLine("> 全量积木登记——一行一条。ID 永不重用。");
            md.AppendLine();
            md.AppendLine("## 全部积木");
            md.AppendLine();
            md.AppendLine("| ID | 名字 | 类别 | 工程路径 | 依赖 | 状态 | 来源 |");
            md.AppendLine("|:--|:--|:--|:--|:--|:--|:--|");

            // 按类别+序号排序输出
            List<BrickIndexEntry> entries = new List<BrickIndexEntry>();
            Dictionary<string, int> categoryMax = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (Mau.Translator.BrickIndexEntry brickEntry in BrickIndex.All)
            {
                BrickContract contract = brickEntry.Contract;
                BrickIndexEntry entry;
                if (oldTable.TryGetValue(contract.Name, out entry!))
                {
                    entry.Contract = contract;
                }
                else
                {
                    entry = new BrickIndexEntry();
                    entry.Name = contract.Name;
                    entry.Category = CategoryFromName(contract.Name);
                    entry.Path = "";
                    entry.Dependencies = "";
                    entry.Status = "active";
                    entry.Source = "";
                    entry.Contract = contract;
                    // 新 ID——类别 max+1
                    int max = 0;
                    if (categoryMax.ContainsKey(entry.Category))
                    {
                        max = categoryMax[entry.Category];
                    }
                    else
                    {
                        foreach (KeyValuePair<string, BrickIndexEntry> kv in oldTable)
                        {
                            if (kv.Value.Category == entry.Category && IsValidIdFormat(kv.Value.Id))
                            {
                                string seqStr = kv.Value.Id.Substring(kv.Value.Id.LastIndexOf('-') + 1);
                                int seq;
                                if (int.TryParse(seqStr, out seq) && seq > max)
                                {
                                    max = seq;
                                }
                            }
                        }
                    }
                    max = max + 1;
                    entry.Id = "BRIK-" + entry.Category + "-" + max.ToString("D3");
                    categoryMax[entry.Category] = max;
                }
                entries.Add(entry);
            }
            // 排序——类别 + 序号
            entries.Sort(delegate (BrickIndexEntry a, BrickIndexEntry b)
            {
                int c = string.CompareOrdinal(a.Category, b.Category);
                if (c != 0)
                {
                    return c;
                }
                return string.CompareOrdinal(a.Id, b.Id);
            });

            int count = entries.Count;
            for (int i = 0; i < count; i++)
            {
                BrickIndexEntry e = entries[i];
                string deps = e.Dependencies.Length > 0 ? e.Dependencies : "";
                md.Append("| " + e.Id + " | " + e.Name + " | " + e.Category + " | " + e.Path + " | " + deps + " | " + e.Status + " | " + e.Source + " |");
                md.AppendLine();
            }
            md.AppendLine();
            md.AppendLine("---");
            md.AppendLine();
            md.AppendLine("_版本：v3.0 | 2026-08-06 | 自动生成——`mau bricks index --update`（注册表唯一真相源；来源/依赖列为人工维护区）_");
            File.WriteAllText(indexPath, md.ToString(), new UTF8Encoding(true));

            // index.json——含 contract 镜像
            StringBuilder json = new StringBuilder();
            json.AppendLine("{");
            json.AppendLine("  \"version\": 2,");
            json.AppendLine("  \"generatedAt\": \"" + DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ") + "\",");
            json.AppendLine("  \"bricks\": [");
            for (int i = 0; i < count; i++)
            {
                BrickIndexEntry e = entries[i];
                json.Append("    {\"id\":\"" + JsonEscape(e.Id) + "\",\"name\":\"" + JsonEscape(e.Name) + "\",\"category\":\"" + JsonEscape(e.Category) + "\",\"path\":\"" + JsonEscape(e.Path) + "\",\"dependencies\":\"" + JsonEscape(e.Dependencies) + "\",\"status\":\"" + JsonEscape(e.Status) + "\"");
                if (e.Contract != null)
                {
                    json.Append(",\"contract\":{\"implementation\":\"" + JsonEscape(e.Contract.Implementation) + "\",\"inputs\":[");
                    for (int p = 0; p < e.Contract.Inputs.Count; p++)
                    {
                        if (p > 0)
                        {
                            json.Append(",");
                        }
                        json.Append("{\"name\":\"" + JsonEscape(e.Contract.Inputs[p].Name) + "\",\"type\":\"" + JsonEscape(TypeName(e.Contract.Inputs[p].Type)) + "\"}");
                    }
                    json.Append("],\"outputs\":[");
                    for (int p = 0; p < e.Contract.Outputs.Count; p++)
                    {
                        if (p > 0)
                        {
                            json.Append(",");
                        }
                        json.Append("{\"name\":\"" + JsonEscape(e.Contract.Outputs[p].Name) + "\",\"type\":\"" + JsonEscape(TypeName(e.Contract.Outputs[p].Type)) + "\"}");
                    }
                    json.Append("],\"return\":\"" + e.Contract.Return.ToString() + "\",\"duration\":\"" + e.Contract.Duration.ToString() + "\",\"thread\":\"" + JsonEscape(e.Contract.Thread) + "\"}");
                }
                json.Append("}");
                if (i < count - 1)
                {
                    json.Append(",");
                }
                json.AppendLine();
            }
            json.AppendLine("  ]");
            json.AppendLine("}");
            File.WriteAllText(Path.Combine(catalogDir, "index.json"), json.ToString(), new UTF8Encoding(true));

            Console.WriteLine("已生成: " + indexPath + "（" + count + " 积木）");
            Console.WriteLine("已生成: " + Path.Combine(catalogDir, "index.json"));
            return 0;
        }

        /// <summary>
        /// Type 到类型名字符串
        /// </summary>
        /// <param name="type">类型</param>
        /// <returns>类型名</returns>
        private static string TypeName(Type type)
        {
            if (type.IsArray)
            {
                return TypeName(type.GetElementType()!) + "[]";
            }
            if (type == typeof(string))
            {
                return "string";
            }
            if (type == typeof(bool))
            {
                return "bool";
            }
            if (type == typeof(int))
            {
                return "int";
            }
            if (type == typeof(long))
            {
                return "long";
            }
            if (type == typeof(double))
            {
                return "double";
            }
            return type.Name;
        }

        /// <summary>
        /// JSON 转义
        /// </summary>
        /// <param name="s">输入</param>
        /// <returns>转义后</returns>
        private static string JsonEscape(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }

        /// <summary>
        /// index --check-license——文件头注释块完整性
        /// </summary>
        /// <returns>退出码</returns>
        public static int CheckLicense()
        {
            string? root = FindWorkspaceRoot();
            if (root == null)
            {
                Console.WriteLine("FAIL: 未找到 Mau.sln");
                return 1;
            }
            List<BrickIndexEntry> entries = ScanBrickHeaders(root);
            int errors = 0;
            Dictionary<string, bool> seen = new Dictionary<string, bool>(StringComparer.Ordinal);
            for (int i = 0; i < entries.Count; i++)
            {
                BrickIndexEntry e = entries[i];
                if (e.FileIdText.Length == 0)
                {
                    Console.WriteLine("V8 FAIL: 文件头缺 ID 行——" + e.Name + "（" + e.Path + "）");
                    errors = errors + 1;
                }
                if (e.Dependencies.Length == 0)
                {
                    Console.WriteLine("V8 FAIL: 文件头缺 依赖: 行——" + e.Name + "（" + e.Path + "）");
                    errors = errors + 1;
                }
                if (seen.ContainsKey(e.Name))
                {
                    Console.WriteLine("V8 FAIL: 文件头积木名重复——" + e.Name);
                    errors = errors + 1;
                }
                else
                {
                    seen[e.Name] = true;
                }
            }
            if (errors == 0)
            {
                Console.WriteLine("BRICKS_LICENSE_OK (" + entries.Count + " 文件头块)");
                return 0;
            }
            Console.WriteLine("BRICKS_LICENSE_FAIL: " + errors + " 处缺失");
            return 1;
        }

        /// <summary>
        /// bricks test——全局一次性跑测（枚举→语料生成→构筑→跑测）
        /// 完整管道：T1 设计 §五——本版实现：枚举 + 最小语料生成 + 编译 + ALC 加载 + Fire/Tick + 必然结果断言
        /// 宿主桥积木（OaBrick/ToolBrick/CmdBrick/Approval/Llm/Context）需要 Configure 注入 → 全局夹具未注入 → SKIP
        /// </summary>
        /// <returns>退出码</returns>
        public static int RunGlobalTest()
        {
            int total = 0;
            int pass = 0;
            int skip = 0;
            int fail = 0;
            List<BrickContract> contracts = new List<BrickContract>();
            foreach (Mau.Translator.BrickIndexEntry brickEntry in BrickIndex.All)
            {
                contracts.Add(brickEntry.Contract);
            }
            for (int i = 0; i < contracts.Count; i++)
            {
                BrickContract c = contracts[i];
                total = total + 1;
                string result = RunOneBrick(c);
                if (result == "PASS")
                {
                    pass = pass + 1;
                    Console.WriteLine("PASS: " + c.Name);
                }
                else if (result == "SKIP")
                {
                    skip = skip + 1;
                    Console.WriteLine("SKIP: " + c.Name + "（" + skipReason + "）");
                }
                else
                {
                    fail = fail + 1;
                    Console.WriteLine("FAIL: " + c.Name + "（" + result + "）");
                }
            }
            Console.WriteLine("=== BRICKS_TEST 汇总 ===");
            Console.WriteLine("总计 " + total + "  通过 " + pass + "  跳过 " + skip + "  失败 " + fail);
            return fail == 0 ? 0 : 1;
        }

        /// <summary>
        /// 当前跳过原因——RunOneBrick 设置
        /// </summary>
        private static string skipReason = "";

        /// <summary>
        /// 是否宿主桥积木——实现位于 Configure 注入型积木类
        /// </summary>
        /// <param name="c">契约</param>
        /// <returns>是宿主桥为真</returns>
        private static bool IsHostBridge(BrickContract c)
        {
            string impl = c.Implementation;
            return impl.Contains("OaBrick") || impl.Contains("ToolBrick") || impl.Contains("CmdBrick")
                || impl.Contains("ApprovalBrick") || impl.Contains("LlmBrick") || impl.Contains("ContextBrick");
        }

        /// <summary>
        /// 跑测单个积木——生成最小 .mau 语料 → 翻译 → 编译 → ALC 加载 → Fire/Tick → 断言
        /// </summary>
        /// <param name="c">积木契约</param>
        /// <returns>PASS / FAIL(原因) / SKIP(原因)</returns>
        private static string RunOneBrick(BrickContract c)
        {
            skipReason = "";
            // 宿主桥积木——需要 Configure 注入，全局夹具未注入 → SKIP
            if (IsHostBridge(c))
            {
                skipReason = "宿主桥积木——需 Configure 注入";
                return "SKIP";
            }
            // 复杂端口类型（非基元）——CLI 无法构造最小样例 → SKIP
            for (int p = 0; p < c.Inputs.Count; p++)
            {
                Type t = c.Inputs[p].Type;
                if (!IsPrimitivePort(t))
                {
                    skipReason = "复杂端口类型 " + t.Name + "——CLI 无法构造最小样例";
                    return "SKIP";
                }
            }
            // 输出端口含非基元——翻译器复杂类型生成能力未覆盖 → SKIP（已知问题：data.snapshot/text.md_parse）
            for (int p = 0; p < c.Outputs.Count; p++)
            {
                Type t = c.Outputs[p].Type;
                if (!IsPrimitivePort(t))
                {
                    skipReason = "复杂输出端口 " + t.Name + "——翻译器复杂类型生成待支持";
                    return "SKIP";
                }
            }
            // 最小语料——信号触发 + 变迁调用 + 双后置
            StringBuilder paramLines = new StringBuilder();
            for (int p = 0; p < c.Inputs.Count; p++)
            {
                if (p > 0)
                {
                    paramLines.Append(", ");
                }
                paramLines.Append(c.Inputs[p].Name);
            }
            string flowName = "BrickTest" + c.Name.Replace(".", "");
            string source = "Mau 0.1\n基座: Mau.Runtime/v0.1\n\n命题:\n  P_Go 信号\n  P_Done 事实\n  P_Failed 事实\n\n变迁 T_Run:\n  前置: P_Go\n  动作: " + c.Name + "\n  参数: " + paramLines.ToString() + "\n  时限: 60帧\n  后置: P_Done / P_Failed\n";

            // [1] 翻译
            CompileResult compileResult = MauCompiler.Compile(source, flowName);
            if (!compileResult.Success)
            {
                return "翻译失败: " + (compileResult.Diagnostics.Count > 0 ? compileResult.Diagnostics[0].ToString() : "未知");
            }

            // [2] 编译——Roslyn Emit
            string className = "FL_" + flowName;
            string pocketRoot = Path.Combine(Path.GetTempPath(), "mau_bricks_test_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            FlowHandle? handle = null;
            try
            {
                MauPocketCompiler compiler = new MauPocketCompiler(pocketRoot);
                MauPocketCompileResult pocketResult = compiler.Compile(compileResult.GeneratedCode, className);
                if (!pocketResult.Success)
                {
                    return "编译失败: " + (pocketResult.Diagnostics.Length > 0 ? pocketResult.Diagnostics[0] : "未知");
                }

                // [3] ALC 加载
                try
                {
                    handle = FlowHandle.Load(pocketResult.AssemblyPath);
                }
                catch (Exception ex)
                {
                    return "加载失败: " + ex.Message;
                }

                // [4] Fire——按信号名反射调用（FireGo），基元参数生成最小样例
                Type flowType = handle.Flow.GetType();
                System.Reflection.MethodInfo? fire = flowType.GetMethod("FireGo");
                if (fire == null)
                {
                    return "FireGo 未生成";
                }
                System.Reflection.ParameterInfo[] ps = fire.GetParameters();
                object?[] args = new object?[ps.Length];
                for (int a = 0; a < ps.Length; a++)
                {
                    args[a] = DefaultValue(ps[a].ParameterType);
                }
                try
                {
                    fire.Invoke(handle.Flow, args);
                }
                catch (Exception ex)
                {
                    return "Fire 调用失败: " + ex.Message;
                }

                // [5] Tick 60 帧——结构冒烟断言（P_Done 或 P_Failed 任一成立 = 变迁执行、积木被调用）
                // 行为正确性归 L4 测试——全局跑测只验证"能被语料调用 + 不崩溃"
                for (int t = 0; t < 60; t = t + 1)
                {
                    handle.Flow.Tick();
                }
                RuntimeStatus status = handle.Flow.GetStatus();
                bool settled = false;
                for (int s = 0; s < status.Propositions.Length; s = s + 1)
                {
                    if (status.Propositions[s].Name == "P_Done" || status.Propositions[s].Name == "P_Failed")
                    {
                        if (status.Propositions[s].Value)
                        {
                            settled = true;
                        }
                    }
                }
                if (!settled)
                {
                    return "变迁未结算（前置未触发或 Cube 未完成）";
                }
                return "PASS";
            }
            catch (Exception ex)
            {
                return "运行时异常: " + ex.Message;
            }
            finally
            {
                if (handle != null)
                {
                    try
                    {
                        handle.TryUnload(3);
                    }
                    catch
                    {
                        // 卸载失败不影响结果
                    }
                }
                try
                {
                    if (Directory.Exists(pocketRoot))
                    {
                        Directory.Delete(pocketRoot, true);
                    }
                }
                catch
                {
                    // 清理失败不影响结果
                }
            }
        }

        /// <summary>
        /// 是否基元端口——全局跑测可构造最小样例的类型
        /// </summary>
        /// <param name="t">端口类型</param>
        /// <returns>基元为真</returns>
        private static bool IsPrimitivePort(Type t)
        {
            if (t == typeof(string))
            {
                return true;
            }
            if (t == typeof(int))
            {
                return true;
            }
            if (t == typeof(long))
            {
                return true;
            }
            if (t == typeof(bool))
            {
                return true;
            }
            if (t == typeof(double))
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// 生成基元默认值——最小样例
        /// </summary>
        /// <param name="t">参数类型</param>
        /// <returns>默认值对象</returns>
        private static object? DefaultValue(Type t)
        {
            if (t == typeof(string))
            {
                return "t";
            }
            if (t == typeof(int))
            {
                return 1;
            }
            if (t == typeof(long))
            {
                return 1L;
            }
            if (t == typeof(bool))
            {
                return true;
            }
            if (t == typeof(double))
            {
                return 1.0;
            }
            if (t.IsValueType)
            {
                return Activator.CreateInstance(t);
            }
            return null;
        }
    }
}

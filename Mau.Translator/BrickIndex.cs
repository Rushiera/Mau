using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Mau.Contracts;
using Mau.Runtime;

namespace Mau.Translator
{
    /// <summary>
    /// 积木索引条目——index.json 反序列化结果（契约 + 文本库定位 + 闭包）
    /// </summary>
    public sealed class BrickIndexEntry
    {
        /// <summary>
        /// 唯一 ID——BRIK-{类别}-{三位序号}
        /// </summary>
        public string Id = "";

        /// <summary>
        /// 积木名——语料动作调用名
        /// </summary>
        public string Name = "";

        /// <summary>
        /// 类别码——FILE/MATH/... 
        /// </summary>
        public string Category = "";

        /// <summary>
        /// 文本相对路径——Bricks/ 根下
        /// </summary>
        public string Path = "";

        /// <summary>
        /// 闭包依赖——积木名（积木间调用）或 _support/ 文件（过渡期）
        /// </summary>
        public List<string> Dependencies = new List<string>();

        /// <summary>
        /// SHA256 校验尾——文件验证
        /// </summary>
        public string Checksum = "";

        /// <summary>
        /// 积木契约——端口/时长/线程（翻译器生成调用形态）
        /// </summary>
        public BrickContract Contract = null!;
    }

    /// <summary>
    /// 积木索引——Bricks/index.json 查询器。翻译器构筑期的积木契约/文本定位/闭包唯一入口。
    /// 替代运行时 BrickRegistry（注册表退役）——查询 = 文件索引，验证 = 文件头 + 校验尾。
    /// </summary>
    public static class BrickIndex
    {
        /// <summary>
        /// 按积木名索引
        /// </summary>
        private static readonly Dictionary<string, BrickIndexEntry> _byName =
            new Dictionary<string, BrickIndexEntry>(StringComparer.Ordinal);

        /// <summary>
        /// 按 BRIK-ID 索引
        /// </summary>
        private static readonly Dictionary<string, BrickIndexEntry> _byId =
            new Dictionary<string, BrickIndexEntry>(StringComparer.Ordinal);

        /// <summary>
        /// Bricks/ 根目录——已加载则为非空
        /// </summary>
        private static string _root = "";

        /// <summary>
        /// Bricks/ 根目录（空=未加载）
        /// </summary>
        public static string Root
        {
            get { return _root; }
        }

        /// <summary>
        /// 已加载积木数量
        /// </summary>
        public static int Count
        {
            get { return _byName.Count; }
        }

        /// <summary>
        /// 全部条目——按积木名（枚举/生成用）
        /// </summary>
        public static IReadOnlyCollection<BrickIndexEntry> All
        {
            get { return _byName.Values; }
        }

        /// <summary>
        /// 从 Bricks/ 根目录加载索引——读 index.json + 全量校验文件头/校验尾
        /// </summary>
        /// <param name="bricksRoot">Bricks/ 目录</param>
        /// <returns>加载成功（含全量校验通过）</returns>
        public static bool Load(string bricksRoot)
        {
            _byName.Clear();
            _byId.Clear();
            _root = "";
            if (string.IsNullOrWhiteSpace(bricksRoot))
            {
                return false;
            }
            string indexFile = Path.Combine(bricksRoot, "index.json");
            if (!File.Exists(indexFile))
            {
                return false;
            }
            _root = Path.GetFullPath(bricksRoot);
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(File.ReadAllText(indexFile)))
                {
                    JsonElement root = doc.RootElement;
                    JsonElement bricks;
                    if (!root.TryGetProperty("bricks", out bricks)
                        || bricks.ValueKind != JsonValueKind.Array)
                    {
                        return false;
                    }
                    foreach (JsonElement b in bricks.EnumerateArray())
                    {
                        BrickIndexEntry? entry = ParseEntry(b);
                        if (entry == null || entry.Name.Length == 0)
                        {
                            return false;
                        }
                        // 依赖补齐——积木文件头 // 依赖: 行为权威（index.json 依赖字段可能缺失/过期）
                        MergeHeaderDependencies(entry);
                        _byName[entry.Name] = entry;
                        _byId[entry.Id] = entry;
                    }
                }
            }
            catch
            {
                return false;
            }
            return _byName.Count > 0;
        }

        /// <summary>
        /// 解析单条积木索引
        /// </summary>
        /// <param name="b">bricks 数组元素</param>
        /// <returns>条目（解析失败返回 null）</returns>
        private static BrickIndexEntry? ParseEntry(JsonElement b)
        {
            string id = ReadString(b, "id");
            string name = ReadString(b, "name");
            string category = ReadString(b, "category");
            string path = ReadString(b, "path");
            if (id.Length == 0 || name.Length == 0 || path.Length == 0)
            {
                return null;
            }
            BrickIndexEntry entry = new BrickIndexEntry();
            entry.Id = id;
            entry.Name = name;
            entry.Category = category;
            entry.Path = path;
            entry.Dependencies = new List<string>();
            JsonElement deps;
            if (b.TryGetProperty("dependencies", out deps)
                && deps.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement d in deps.EnumerateArray())
                {
                    if (d.ValueKind == JsonValueKind.String)
                    {
                        entry.Dependencies.Add(d.GetString() ?? "");
                    }
                }
            }
            else if (b.TryGetProperty("dependencies", out deps)
                && deps.ValueKind == JsonValueKind.String)
            {
                // 兼容字符串形态（逗号分隔）——旧 UpdateIndex 曾输出字符串；数组为标准形态
                string depText = deps.GetString() ?? "";
                string[] parts = depText.Split(',');
                for (int p = 0; p < parts.Length; p++)
                {
                    string part = parts[p].Trim();
                    if (part.Length > 0)
                    {
                        entry.Dependencies.Add(part);
                    }
                }
            }
            entry.Checksum = ReadString(b, "checksum");
            JsonElement contract;
            if (b.TryGetProperty("contract", out contract)
                && contract.ValueKind == JsonValueKind.Object)
            {
                entry.Contract = ParseContract(contract, name);
            }
            else
            {
                entry.Contract = new BrickContract(name, "Mau.Bricks.BRIK." + name);
            }
            return entry;
        }

        /// <summary>
        /// 解析契约
        /// </summary>
        /// <param name="c">contract 对象</param>
        /// <param name="name">积木名</param>
        /// <returns>契约</returns>
        private static BrickContract ParseContract(JsonElement c, string name)
        {
            string implementation = ReadString(c, "implementation");
            BrickContract contract = new BrickContract(name, implementation);
            JsonElement inputs;
            if (c.TryGetProperty("inputs", out inputs)
                && inputs.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement p in inputs.EnumerateArray())
                {
                    contract.Inputs.Add(new BrickPort(
                        ReadString(p, "name"), MapType(ReadString(p, "type"))));
                }
            }
            JsonElement outputs;
            if (c.TryGetProperty("outputs", out outputs)
                && outputs.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement p in outputs.EnumerateArray())
                {
                    contract.Outputs.Add(new BrickPort(
                        ReadString(p, "name"), MapType(ReadString(p, "type"))));
                }
            }
            string ret = ReadString(c, "return");
            if (ret == "Void")
            {
                contract.Return = BrickReturnKind.Void;
            }
            else
            {
                contract.Return = BrickReturnKind.Bool;
            }
            string duration = ReadString(c, "duration");
            if (duration == "Streaming")
            {
                contract.Duration = BrickDuration.Streaming;
            }
            else if (duration == "Async")
            {
                contract.Duration = BrickDuration.Async;
            }
            else
            {
                contract.Duration = BrickDuration.Sync;
            }
            contract.Thread = ReadString(c, "thread");
            if (contract.Thread.Length == 0)
            {
                contract.Thread = "main";
            }
            return contract;
        }

        /// <summary>
        /// 类型名 → Type 映射——覆盖当前 82 积木契约全量类型
        /// </summary>
        /// <param name="typeName">JSON 类型名</param>
        /// <returns>Type（未知类型回退 string）</returns>
        private static Type MapType(string typeName)
        {
            switch (typeName)
            {
                case "string":
                    return typeof(string);
                case "long":
                    return typeof(long);
                case "int":
                    return typeof(int);
                case "bool":
                    return typeof(bool);
                case "string[]":
                    return typeof(string[]);
                case "long[]":
                    return typeof(long[]);
                case "int[]":
                    return typeof(int[]);
                case "bool[]":
                    return typeof(bool[]);
                case "Office[]":
                    return typeof(Office[]);
                case "Office":
                    return typeof(Office);
                case "OfficeData":
                    return typeof(OfficeData);
                case "ApprovalResult":
                    return typeof(ApprovalResult);
                case "MarkdownPart":
                    return typeof(MarkdownPart);
                case "LlmMessage":
                    return typeof(LlmMessage);
                case "List`1":
                    return typeof(List<MarkdownPart>);
                case "Dictionary`2":
                    return typeof(Dictionary<string, List<string>>);
                default:
                    return typeof(string);
            }
        }

        /// <summary>
        /// 按积木名查询
        /// </summary>
        /// <param name="name">积木名</param>
        /// <param name="entry">命中条目</param>
        /// <returns>是否命中</returns>
        public static bool TryGet(string name, out BrickIndexEntry entry)
        {
            return _byName.TryGetValue(name, out entry!);
        }

        /// <summary>
        /// 按 BRIK-ID 查询
        /// </summary>
        /// <param name="id">BRIK-ID</param>
        /// <param name="entry">命中条目</param>
        /// <returns>是否命中</returns>
        public static bool TryGetById(string id, out BrickIndexEntry entry)
        {
            return _byId.TryGetValue(id, out entry!);
        }

        /// <summary>
        /// BRIK-ID → 合法 C# 类名（BRIK-FILE-002 → BRIK_FILE_002）
        /// </summary>
        /// <param name="id">BRIK-ID</param>
        /// <returns>类名</returns>
        public static string IdClassName(string id)
        {
            return id.Replace('-', '_');
        }

        /// <summary>
        /// 解析条目源码绝对路径
        /// </summary>
        /// <param name="entry">条目</param>
        /// <returns>绝对路径</returns>
        public static string ResolveSourcePath(BrickIndexEntry entry)
        {
            return Path.Combine(_root, entry.Path);
        }

        /// <summary>
        /// 读取对象内字符串属性
        /// </summary>
        /// <param name="e">JSON 元素</param>
        /// <param name="name">属性名</param>
        /// <returns>字符串或空串</returns>
        private static string ReadString(JsonElement e, string name)
        {
            JsonElement value;
            if (e.TryGetProperty(name, out value)
                && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? "";
            }
            return "";
        }
/// <summary>
/// 依赖补齐——积木文件头 // 依赖: 行为权威（index.json 依赖字段可能缺失/过期）
/// 与文件头依赖行合并（index.json 已有依赖保留，文件头新增补充；"无" 跳过）
/// </summary>
/// <param name = "entry">条目（Path 相对 Bricks 根）</param>
private static void MergeHeaderDependencies(BrickIndexEntry entry)
{
    try
    {
        string file = Path.Combine(_root, entry.Path);
        if (!File.Exists(file))
        {
            return;
        }

        string[] lines = File.ReadAllLines(file);
        for (int i = 0; i < lines.Length && i < 14; i++)
        {
            string line = lines[i].Trim();
            if (line.StartsWith("// 依赖:", StringComparison.Ordinal))
            {
                string depText = line.Substring(6).Trim();
                string[] parts = depText.Split(',');
                for (int p = 0; p < parts.Length; p++)
                {
                    string part = parts[p].Trim();
                    if (part.Length == 0 || part == "无")
                    {
                        continue;
                    }

                    bool exists = false;
                    for (int e = 0; e < entry.Dependencies.Count; e++)
                    {
                        if (string.Equals(entry.Dependencies[e], part, StringComparison.Ordinal))
                        {
                            exists = true;
                            break;
                        }
                    }

                    if (!exists)
                    {
                        entry.Dependencies.Add(part);
                    }
                }

                return;
            }
        }
    }
    catch
    {
    // 文件头读取失败不影响索引加载
    }
}    }
}

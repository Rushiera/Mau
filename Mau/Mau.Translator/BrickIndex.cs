#nullable disable warnings
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Mau.Translator
{
    /// <summary>
    /// 积木索引条目——契约轻量镜像（输入类型字符串序列，E4xx 类别比较用）
    /// </summary>
    public sealed class BrickIndexEntry
    {
        /// <summary>
        /// 积木名——Mau 文本调用名（probe.sink）
        /// </summary>
        public string Name = "";

        /// <summary>
        /// 实现签名——命名空间.类.方法（生成器内嵌用）
        /// </summary>
        public string Implementation = "";

        /// <summary>
        /// 输入端口类型序列（契约声明顺序）
        /// </summary>
        public List<string> InputTypes = new List<string>();

        /// <summary>
        /// 源文件相对路径（Bricks/ 下——生成器内嵌读取）
        /// </summary>
        public string Path = "";
        /// <summary>
        /// 输出端口数量（生成器 out _ 占位用）
        /// </summary>
        public int OutputCount;

        /// <summary>
        /// 输出端口类型序列（契约声明顺序——值传感器类型推导用）
        /// </summary>
        public List<string> OutputTypes = new List<string>();
    }

    /// <summary>
    /// 积木索引——Bricks/index.json 静态查询器（翻译器构筑期唯一积木寻路）。
    /// 数据源：进程目录向上找 Mau.sln → Bricks/index.json（与 CLI/FixtureBuilder 同模式）。
    /// 会话内静态缓存；索引缺失（非仓库环境）→ 查询按未命中处理。
    /// </summary>
    public static class BrickIndex
    {
        /// <summary>
        /// 索引缓存（会话内一次加载）
        /// </summary>
        private static Dictionary<string, BrickIndexEntry>? BrickIndex_Cache;

        /// <summary>
        /// 查找积木条目
        /// </summary>
        /// <param name="name">积木名</param>
        /// <param name="entry">输出条目（未命中时为空条目）</param>
        /// <returns>true=在索引中</returns>
        public static bool TryFind(string name, out BrickIndexEntry entry)
        {
            Dictionary<string, BrickIndexEntry> index = Load();
            if (index.TryGetValue(name, out entry) && entry != null)
            {
                return true;
            }
            entry = new BrickIndexEntry();
            return false;
        }
        /// <summary>
        /// 全量索引——bricks list 枚举用
        /// </summary>
        /// <returns>名字 → 条目</returns>
        public static Dictionary<string, BrickIndexEntry> All()
        {
            // 防御性拷贝——避免调用方变异内部缓存（R2-P3）
            return new Dictionary<string, BrickIndexEntry>(Load(), StringComparer.Ordinal);
        }
        /// <summary>
        /// 加载索引——向上找 Mau.sln → Bricks/index.json（防御式解析，失败返回空）
        /// </summary>
        /// <returns>名字 → 条目</returns>
        private static Dictionary<string, BrickIndexEntry> Load()
        {
            if (BrickIndex_Cache != null)
            {
                return BrickIndex_Cache;
            }
            Dictionary<string, BrickIndexEntry> result = new Dictionary<string, BrickIndexEntry>(StringComparer.Ordinal);
            string indexPath = FindIndexPath();
            if (!File.Exists(indexPath))
            {
                BrickIndex_Cache = result;
                return result;
            }
            try
            {
                string json = File.ReadAllText(indexPath);
                using (JsonDocument document = JsonDocument.Parse(json))
                {
                    JsonElement root = document.RootElement;
                    JsonElement bricks;
                    if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("bricks", out bricks) && bricks.ValueKind == JsonValueKind.Array)
                    {
                        for (int i = 0; i < bricks.GetArrayLength(); i++)
                        {
                            BrickIndexEntry entry = ParseEntry(bricks[i]);
                            if (entry.Name.Length > 0)
                            {
                                result[entry.Name] = entry;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // 索引损坏——按未命中处理（空索引）
                Console.Error.WriteLine("[BrickIndex] 索引解析失败，按空索引处理: " + ex.Message);
            }
            BrickIndex_Cache = result;
            return result;
        }
        /// <summary>
        /// 单条目解析——防御式（字段缺失跳过）
        /// </summary>
        /// <param name="element">bricks 数组元素</param>
        /// <returns>条目（解析失败返回空名）</returns>
        private static BrickIndexEntry ParseEntry(JsonElement element)
        {
            BrickIndexEntry entry = new BrickIndexEntry();
            if (element.ValueKind != JsonValueKind.Object)
            {
                return entry;
            }
            JsonElement name;
            if (element.TryGetProperty("name", out name) && name.ValueKind == JsonValueKind.String)
            {
                string nameText = name.GetString();
                if (nameText != null)
                {
                    entry.Name = nameText;
                }
            }
            JsonElement path;
            if (element.TryGetProperty("path", out path) && path.ValueKind == JsonValueKind.String)
            {
                string pathText = path.GetString();
                if (pathText != null)
                {
                    entry.Path = pathText;
                }
            }
            JsonElement contract;
            if (element.TryGetProperty("contract", out contract) && contract.ValueKind == JsonValueKind.Object)
            {
                JsonElement impl;
                if (contract.TryGetProperty("implementation", out impl) && impl.ValueKind == JsonValueKind.String)
                {
                    string implText = impl.GetString();
                    if (implText != null)
                    {
                        entry.Implementation = implText;
                    }
                }
                JsonElement inputs;
                if (contract.TryGetProperty("inputs", out inputs) && inputs.ValueKind == JsonValueKind.Array)
                {
                    for (int i = 0; i < inputs.GetArrayLength(); i++)
                    {
                        JsonElement type;
                        if (inputs[i].ValueKind == JsonValueKind.Object && inputs[i].TryGetProperty("type", out type) && type.ValueKind == JsonValueKind.String)
                        {
                            string typeText = type.GetString();
                            if (typeText != null)
                            {
                                entry.InputTypes.Add(typeText);
                            }
                        }
                    }
                }
                JsonElement outputs;
                if (contract.TryGetProperty("outputs", out outputs) && outputs.ValueKind == JsonValueKind.Array)
                {
                    entry.OutputCount = (int)outputs.GetArrayLength();
                    for (int o = 0; o < entry.OutputCount; o = o + 1)
                    {
                        JsonElement outType;
                        if (outputs[o].ValueKind == JsonValueKind.Object && outputs[o].TryGetProperty("type", out outType) && outType.ValueKind == JsonValueKind.String)
                        {
                            string outTypeText = outType.GetString();
                            if (outTypeText != null)
                            {
                                entry.OutputTypes.Add(outTypeText);
                            }
                            else
                            {
                                entry.OutputTypes.Add("string");
                            }
                        }
                        else
                        {
                            entry.OutputTypes.Add("string");
                        }
                    }
                }
            }
            return entry;
        }

        /// <summary>
        /// 索引文件定位——进程目录向上找 Mau.sln → Bricks/index.json
        /// </summary>
        /// <returns>路径（未找到返回空串）</returns>
        private static string FindIndexPath()
        {
            string root = FindRepoRoot();
            if (root.Length == 0)
            {
                return "";
            }
            return Path.Combine(root, "Bricks", "index.json");
        }        /// <summary>
                 /// 静态仓库根注入——宿主 Bootstrap 设置（受控根 id=mau）；优先于 env/程序集探测
                 /// </summary>
        private static string? _injectedRoot;

        /// <summary>
        /// 注入仓库根——宿主启动时调用（受控根 id=mau）；空/不含 Mau.sln 忽略
        /// </summary>
        /// <param name="root">仓库根绝对路径</param>
        public static void SetWorkspaceRoot(string root)
        {
            if (root == null || root.Length == 0)
            {
                return;
            }
            _injectedRoot = root;
        }

        /// <summary>
        /// 仓库根定位——注入根 → env MAU_ROOT → 程序集目录向上找 Mau.sln（生成器内嵌积木源读取共用）
        /// </summary>
        /// <returns>仓库根（未找到返回空串）</returns>
        public static string FindRepoRoot()
        {
            if (_injectedRoot != null && _injectedRoot.Length > 0 && File.Exists(Path.Combine(_injectedRoot, "Mau.sln")))
            {
                return _injectedRoot;
            }
            string? envRoot = Environment.GetEnvironmentVariable("MAU_ROOT");
            if (envRoot != null && envRoot.Length > 0 && File.Exists(Path.Combine(envRoot, "Mau.sln")))
            {
                return envRoot;
            }
            DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Mau.sln")))
            {
                dir = dir.Parent;
            }

            if (dir == null)
            {
                return "";
            }

            return dir.FullName;
        }
    }
}

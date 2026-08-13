using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Mau.Translator
{
    /// <summary>
    /// 积木索引视图——实例化索引（产品积木目录的独立索引；基座由静态 BrickIndex 承担）。
    /// 编译上下文（BrickContext）持有 0..N 个本类实例——产品积木契约/源文件定位/闭包收集。
    /// 产品积木机制（design-mau-boundary.md §五）：项目自持积木目录 + index.json，只调用 Mau 工具链。
    /// </summary>
    public sealed class BrickIndexView
    {
        /// <summary>
        /// Bricks/ 根目录——加载成功非空
        /// </summary>
        public string Root = "";

        /// <summary>
        /// 按积木名索引
        /// </summary>
        private readonly Dictionary<string, BrickIndexEntry> _byName =
            new Dictionary<string, BrickIndexEntry>(StringComparer.Ordinal);

        /// <summary>
        /// 按 BRIK-ID 索引
        /// </summary>
        private readonly Dictionary<string, BrickIndexEntry> _byId =
            new Dictionary<string, BrickIndexEntry>(StringComparer.Ordinal);

        /// <summary>
        /// 已加载积木数量
        /// </summary>
        public int Count
        {
            get { return _byName.Count; }
        }

        /// <summary>
        /// 全部条目——产品积木谱枚举用
        /// </summary>
        public IReadOnlyCollection<BrickIndexEntry> All
        {
            get { return _byName.Values; }
        }

        /// <summary>
        /// 从产品积木目录加载索引——读 index.json + 依赖补齐（复用基座解析逻辑，索引来源 = 产品侧自持）
        /// </summary>
        /// <param name="bricksRoot">产品 Bricks/ 目录（含 index.json）</param>
        /// <returns>加载成功</returns>
        public bool Load(string bricksRoot)
        {
            if (string.IsNullOrWhiteSpace(bricksRoot))
            {
                return false;
            }
            string indexFile = Path.Combine(bricksRoot, "index.json");
            if (!File.Exists(indexFile))
            {
                return false;
            }
            Dictionary<string, BrickIndexEntry> newByName = new Dictionary<string, BrickIndexEntry>(StringComparer.Ordinal);
            Dictionary<string, BrickIndexEntry> newById = new Dictionary<string, BrickIndexEntry>(StringComparer.Ordinal);
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
                        BrickIndexEntry? entry = BrickIndex.ParseEntry(b);
                        if (entry == null || entry.Name.Length == 0)
                        {
                            return false;
                        }
                        // 依赖补齐——文件头行为权威（index.json 依赖字段可能缺失/过期）
                        BrickIndex.MergeHeaderDependencies(entry, bricksRoot);
                        newByName[entry.Name] = entry;
                        newById[entry.Id] = entry;
                    }
                }
            }
            catch
            {
                return false;
            }
            if (newByName.Count == 0)
            {
                return false;
            }
            _byName.Clear();
            foreach (KeyValuePair<string, BrickIndexEntry> kv in newByName)
            {
                _byName[kv.Key] = kv.Value;
            }
            _byId.Clear();
            foreach (KeyValuePair<string, BrickIndexEntry> kv in newById)
            {
                _byId[kv.Key] = kv.Value;
            }
            Root = Path.GetFullPath(bricksRoot);
            return true;
        }

        /// <summary>
        /// 按积木名查询
        /// </summary>
        /// <param name="name">积木名</param>
        /// <param name="entry">命中条目</param>
        /// <returns>是否命中</returns>
        public bool TryGet(string name, out BrickIndexEntry entry)
        {
            return _byName.TryGetValue(name, out entry!);
        }

        /// <summary>
        /// 解析条目源码绝对路径
        /// </summary>
        /// <param name="entry">条目（Path 相对本视图根）</param>
        /// <returns>绝对路径</returns>
        public string ResolveSourcePath(BrickIndexEntry entry)
        {
            return Path.Combine(Root, entry.Path);
        }
    }
}

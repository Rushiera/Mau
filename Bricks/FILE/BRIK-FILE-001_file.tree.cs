// ═══════════════════════════════════════════════════
// 积木: file.tree
// ID:   BRIK-FILE-001
// 类别: FILE
// 作用: 目录树——列目录结构（depth 层级 / limit 条数上限；稳定排序）——LLM 工具 file-tree 语料执行面
// 依赖: 无
// 引用: Mau.Runtime（FileSystemService / FsTreeFacts / DataBox）
// 原理: DataBox.TryResolve<FileSystemService> → TreeFacts(path, depth, limit)；条目与提示行拼装（[git] / [skip] / [截断] 文案）归积木层（A228 档 2 · B3）
// 常用: FileCat 认领线——'file.tree'[@args] > @result
// ═══════════════════════════════════════════════════
using System;
using System.Text.Json;
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 文件积木——file-tree 目录树（LLM 工具执行面：参数整包 argsJson）
    /// </summary>
    public static class FileTreeBrick
    {
        /// <summary>
        /// 目录树
        /// </summary>
        /// <param name="argsJson">工具参数 JSON（path/depth/limit）</param>
        /// <param name="result">相对路径列表（\n 分隔）或 ERR| 错误文本</param>
        /// <returns>true=执行成功</returns>
        public static bool Tree(string argsJson, out string result)
        {
            result = "";
            // [参数面] 声明面口径零容忍——未知 / 缺值一律 ERR|BAD_ARGS（catId 保留键放行）
            string badArgs = JsonArgs.Validate(argsJson, "path depth limit", "path", "", "");
            if (badArgs.Length > 0)
            {
                result = badArgs;
                return false;
            }
            string path = JsonArgs.Get(argsJson, "path");
            if (path == "§PARSE_FAIL§")
            {
                result = "ERR|BAD_ARGS|工具参数 JSON 解析失败（LLM 生成参数可能被截断——超长内容请分段写入）";
                return false;
            }
            if (path.Length == 0)
            {
                result = "ERR|BAD_ARGS|缺少参数 path";
                return false;
            }
            int depth = 2;
            int limit = 500;
            string depthRaw = JsonArgs.Get(argsJson, "depth");
            string limitRaw = JsonArgs.Get(argsJson, "limit");
            if (depthRaw.Length > 0 && !int.TryParse(depthRaw, out depth))
            {
                result = "ERR|BAD_ARGS|参数 depth 非整数: " + depthRaw;
                return false;
            }
            if (limitRaw.Length > 0 && !int.TryParse(limitRaw, out limit))
            {
                result = "ERR|BAD_ARGS|参数 limit 非整数: " + limitRaw;
                return false;
            }
            try
            {
                FileSystemService? fs = FileSystemRegistry.ResolveScoped(JsonArgs.Get(argsJson, "catId"));
                if (fs == null)
                {
                    DataBox.TryResolve<FileSystemService>(out fs);
                }
                if (fs == null)
                {
                    result = "ERR|FS_NO_SERVICE|宿主未注入 FileSystemService";
                    return false;
                }
                FsTreeFacts facts = fs.TreeFacts(path, depth, limit);
                System.Collections.Generic.List<string> lines = new System.Collections.Generic.List<string>();
                // [段1] 条目行——目录加尾标（显示约定归积木层）
                for (int i = 0; i < facts.Entries.Length; i = i + 1)
                {
                    FsEntry entry = facts.Entries[i];
                    lines.Add(entry.IsDir ? entry.RelativePath + "/" : entry.RelativePath);
                }
                // [段2] git 特征行——limit 有余量时补位
                for (int i = 0; i < facts.GitFacts.Length; i = i + 1)
                {
                    if (lines.Count >= limit)
                    {
                        break;
                    }
                    lines.Add("[git] 存在 .git（" + facts.GitFacts[i].RelativePath + "——HEAD: " + facts.GitFacts[i].Head + "）");
                }
                // [段3] 提示行——不占 limit 配额
                if (facts.IgnoredDirs > 0)
                {
                    lines.Add("[skip] " + facts.IgnoredDirs.ToString() + " 个忽略目录（.git/bin/obj/node_modules 等）未扫描——条目在其内已跳过");
                }
                if (facts.Total > facts.Entries.Length)
                {
                    lines.Add("[截断] 共 " + facts.Total.ToString() + " 条，已列 " + facts.Entries.Length.ToString() + " 条——提高 limit 至 ≥" + facts.Total.ToString() + "，或收窄 path / 降 depth 可看全");
                }
                int shown = lines.Count;
                if (shown == 0)
                {
                    result = MetaHead("file-tree", true, path, 0) + "\n" + path + " | 0 条";
                    return true;
                }
                result = MetaHead("file-tree", true, path, shown) + "\n" + path + " | " + shown.ToString() + " 条" + "\n" + string.Join("\n", lines.ToArray());
                return true;
            }
            catch (Exception ex)
            {
                result = "ERR|" + ex.GetType().Name + "|" + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 结构化元数据头（统一口径·A214）——恒定 ok / tool + 主来源 target + 主计数 items（空串 / 负值 = 省略）。
        /// </summary>
        /// <param name="tool">工具名</param>
        /// <param name="ok">成败</param>
        /// <param name="target">主来源（空串 = 省略）</param>
        /// <param name="items">主计数（负值 = 省略）</param>
        /// <returns>单行 JSON</returns>
        private static string MetaHead(string tool, bool ok, string target, int items)
        {
            System.Collections.Generic.Dictionary<string, object> head = new System.Collections.Generic.Dictionary<string, object>();
            head["ok"] = ok;
            head["tool"] = tool;
            if (target.Length > 0)
            {
                head["target"] = target;
            }
            if (items >= 0)
            {
                head["items"] = items;
            }
            return JsonSerializer.Serialize(head);
        }
    }
}
// #MAU_CHECKSUM:SHA256:71C9CE79E363EC680508BB7556FA043DFA67AEB2BE492073C80542E715D69CEA

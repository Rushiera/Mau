// ═══════════════════════════════════════════════════
// 积木: text.grep
// ID:   BRIK-TEXT-009
// 类别: TEXT
// 作用: 内容关键词搜索——目录内递归扫文本文件，返回 相对路径:行号:上下文（前后 ≤10 字符）——LLM 工具 text-grep 语料执行面
// 依赖: 无
// 引用: Mau.Runtime（FileSystemService/DataBox）
// 原理: DataBox.TryResolve<FileSystemService> → Grep(dir, keyword, pattern, limit)；argsJson 内解析 dir/keyword/pattern/limit
// 常用: TextCat 认领线——'text.grep'[@args] > @result
// ═══════════════════════════════════════════════════
using System;
using System.Text.Json;
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 文本积木——text-grep 内容关键词搜索（LLM 工具执行面：参数整包 argsJson）
    /// </summary>
    public static class TextGrepBrick
    {
        /// <summary>
        /// 内容关键词搜索
        /// </summary>
        /// <param name="argsJson">工具参数 JSON（dir/keyword/pattern/limit）</param>
        /// <param name="result">匹配行列表（\n 分隔）或 ERR| 错误文本</param>
        /// <returns>true=执行成功</returns>
        public static bool Grep(string argsJson, out string result)
        {
            result = "";
            // [参数面] 声明面口径零容忍——未知 / 缺值一律 ERR|BAD_ARGS（catId 保留键放行）
            string badArgs = JsonArgs.Validate(argsJson, "dir keyword pattern limit", "dir keyword", "", "");
            if (badArgs.Length > 0)
            {
                result = badArgs;
                return false;
            }
            string dir = JsonArgs.Get(argsJson, "dir");
            string keyword = JsonArgs.Get(argsJson, "keyword");
            if (dir == "§PARSE_FAIL§" || keyword == "§PARSE_FAIL§")
            {
                result = "ERR|BAD_ARGS|工具参数 JSON 解析失败（LLM 生成参数可能被截断——超长内容请分段写入）";
                return false;
            }
            if (dir.Length == 0 || keyword.Length == 0)
            {
                result = "ERR|BAD_ARGS|缺少参数 dir 或 keyword";
                return false;
            }
            string pattern = JsonArgs.Get(argsJson, "pattern");
            if (pattern.Length == 0)
            {
                pattern = "*";
            }
            int limit = 200;
            string limitRaw = JsonArgs.Get(argsJson, "limit");
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
                FsGrepFacts facts = fs.GrepFacts(dir, keyword, pattern, limit);
                System.Collections.Generic.List<string> lines = new System.Collections.Generic.List<string>();
                // [段1] 命中行——相对路径:行号:上下文（展示格式归积木层）
                for (int i = 0; i < facts.Hits.Length; i = i + 1)
                {
                    lines.Add(facts.Hits[i].RelativePath + ":" + facts.Hits[i].Line.ToString() + ":" + facts.Hits[i].Context);
                }
                // [段2] 忽略目录提示——不静默（与其他提示行同序：忽略 → 截断 → 跳过文件）
                if (facts.IgnoredDirs > 0)
                {
                    lines.Add("[skip] " + facts.IgnoredDirs.ToString() + " 个忽略目录（.git/bin/obj/node_modules 等）未扫描——匹配条目在其内已跳过");
                }
                if (facts.HitTotal > facts.Hits.Length)
                {
                    lines.Add("[截断] 共 " + facts.HitTotal.ToString() + " 条，已列 " + facts.Hits.Length.ToString() + " 条——提高 limit 至 ≥" + facts.HitTotal.ToString() + "，或收窄 dir / keyword 可看全");
                }
                for (int i = 0; i < facts.Skipped.Length; i = i + 1)
                {
                    lines.Add("[skip-file] " + facts.Skipped[i].RelativePath + " | " + facts.Skipped[i].Reason);
                }
                int hits = lines.Count;
                string head = MetaHead("text-grep", true, dir, hits) + "\n" + dir + " | 命中 " + hits.ToString();
                if (hits == 0)
                {
                    result = head;
                    return true;
                }
                result = head + "\n" + string.Join("\n", lines.ToArray());
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
// #MAU_CHECKSUM:SHA256:19441CEC807975CD71A4A69C8C9AF97465CE8A156C16906AD985603011E62ED1

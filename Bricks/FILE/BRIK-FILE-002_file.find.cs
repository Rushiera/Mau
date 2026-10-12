// ═══════════════════════════════════════════════════
// 积木: file.find
// ID:   BRIK-FILE-002
// 类别: FILE
// 作用: 文件名 glob 搜索——按文件名模式找文件（pattern 如 *.md / **/*.cs；recursive 默认 true）——LLM 工具 file-find 语料执行面
// 依赖: 无
// 引用: Mau.Runtime（FileSystemService / FsFindFacts / DataBox）
// 原理: DataBox.TryResolve<FileSystemService> → FindFacts(dir, pattern, recursive, limit)；提示行拼装（[skip] / [截断] 文案）归积木层（A228 档 2 · B3）
// 常用: FileCat 认领线——'file.find'[@args] > @result
// ═══════════════════════════════════════════════════
using System;
using System.Text.Json;
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 文件积木——file-find 文件名搜索（LLM 工具执行面：参数整包 argsJson）
    /// </summary>
    public static class FileFindBrick
    {
        /// <summary>
        /// 文件名 glob 搜索
        /// </summary>
        /// <param name="argsJson">工具参数 JSON（dir/pattern/recursive/limit）</param>
        /// <param name="result">相对路径列表（\n 分隔）或 ERR| 错误文本</param>
        /// <returns>true=执行成功</returns>
        public static bool Find(string argsJson, out string result)
        {
            result = "";
            // [参数面] 声明面口径零容忍——未知 / 缺值一律 ERR|BAD_ARGS（catId 保留键放行）
            string badArgs = JsonArgs.Validate(argsJson, "dir pattern recursive limit", "dir", "", "");
            if (badArgs.Length > 0)
            {
                result = badArgs;
                return false;
            }
            string dir = JsonArgs.Get(argsJson, "dir");
            if (dir == "§PARSE_FAIL§")
            {
                result = "ERR|BAD_ARGS|工具参数 JSON 解析失败（LLM 生成参数可能被截断——超长内容请分段写入）";
                return false;
            }
            if (dir.Length == 0)
            {
                result = "ERR|BAD_ARGS|缺少参数 dir";
                return false;
            }
            string pattern = JsonArgs.Get(argsJson, "pattern");
            if (pattern == "§PARSE_FAIL§")
            {
                result = "ERR|BAD_ARGS|工具参数 JSON 解析失败（LLM 生成参数可能被截断——超长内容请分段写入）";
                return false;
            }
            if (pattern.Length == 0)
            {
                pattern = "*";
            }
            bool recursive = true;
            string recRaw = JsonArgs.Get(argsJson, "recursive");
            if (recRaw.Length > 0 && (recRaw == "false" || recRaw == "0"))
            {
                recursive = false;
            }
            int limit = 500;
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
                FsFindFacts facts = fs.FindFacts(dir, pattern, recursive, limit);
                System.Collections.Generic.List<string> lines = new System.Collections.Generic.List<string>();
                for (int i = 0; i < facts.RelativePaths.Length; i = i + 1)
                {
                    lines.Add(facts.RelativePaths[i]);
                }
                if (facts.IgnoredDirs > 0)
                {
                    lines.Add("[skip] " + facts.IgnoredDirs.ToString() + " 个忽略目录（.git/bin/obj/node_modules 等）未扫描——条目在其内已跳过");
                }
                if (facts.Matched > facts.RelativePaths.Length)
                {
                    lines.Add("[截断] 共 " + facts.Matched.ToString() + " 条，已列 " + facts.RelativePaths.Length.ToString() + " 条——提高 limit 至 ≥" + facts.Matched.ToString() + "，或收窄 dir / pattern 可看全");
                }
                if (lines.Count == 0)
                {
                    result = MetaHead("file-find", true, dir, 0) + "\n" + dir + " | 0 条";
                    return true;
                }
                result = MetaHead("file-find", true, dir, lines.Count) + "\n" + dir + " | " + lines.Count.ToString() + " 条" + "\n" + string.Join("\n", lines.ToArray());
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
// #MAU_CHECKSUM:SHA256:B14F3B19D43A4B924F87451952C4FAC91438D35647F3BC1958BE8CA205A1C737

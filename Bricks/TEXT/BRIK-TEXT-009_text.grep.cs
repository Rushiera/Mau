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
                string[] rows = fs.Grep(dir, keyword, pattern, limit);
                if (rows == null || rows.Length == 0)
                {
                    result = "（无匹配）";
                    return true;
                }
                result = string.Join("\n", rows);
                return true;
            }
            catch (Exception ex)
            {
                result = "ERR|" + ex.GetType().Name + "|" + ex.Message;
                return false;
            }
        }
    }
}
// #MAU_CHECKSUM:SHA256:BDDBDA41EECF60ABE4F6689AE58CF8C17A5C7CD98BD2C6161AFCBB24BF8D30D8

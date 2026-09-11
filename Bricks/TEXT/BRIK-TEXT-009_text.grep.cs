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
            string dir = ExtractArg(argsJson, "dir");
            string keyword = ExtractArg(argsJson, "keyword");
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
            string pattern = ExtractArg(argsJson, "pattern");
            if (pattern.Length == 0)
            {
                pattern = "*";
            }
            int limit = 200;
            string limitRaw = ExtractArg(argsJson, "limit");
            if (limitRaw.Length > 0 && !int.TryParse(limitRaw, out limit))
            {
                result = "ERR|BAD_ARGS|参数 limit 非整数: " + limitRaw;
                return false;
            }
            try
            {
                FileSystemService? fs = FileSystemRegistry.ResolveScoped(ExtractArg(argsJson, "catId"));
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

        /// <summary>
        /// 展平参数提取——argsJson 中取字符串值（不存在返回空串）
        /// </summary>
        /// <param name="argumentsJson">参数 JSON</param>
        /// <param name="key">参数名</param>
        /// <returns>参数值</returns>
        private static string ExtractArg(string argumentsJson, string key)
        {
            if (argumentsJson == null || argumentsJson.Length == 0)
            {
                return "§PARSE_FAIL§";
            }
            try
            {
                JsonDocument doc = JsonDocument.Parse(argumentsJson);
                try
                {
                    if (doc.RootElement.TryGetProperty(key, out JsonElement el))
                    {
                        if (el.ValueKind == JsonValueKind.String)
                        {
                            return el.GetString() ?? "";
                        }
                        return el.GetRawText();
                    }
                }
                finally
                {
                    doc.Dispose();
                }
            }
            catch (Exception)
            {
                return "§PARSE_FAIL§";
            }
            return "";
        }
    }
}
// #MAU_CHECKSUM:SHA256:3A49D60B9D0DDD99DF0C82E0179AB8DBF0B014614B12C3E97BF5692C34EEBB48

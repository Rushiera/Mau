// ═══════════════════════════════════════════════════
// 积木: text.find
// ID:   BRIK-TEXT-008
// 类别: TEXT
// 作用: 文件名 glob 搜索——按文件名模式找文件（pattern 如 *.md / **/*.cs；recursive 默认 true）——LLM 工具 text-find 语料执行面
// 依赖: 无
// 引用: Mau.Runtime（FileSystemService/DataBox）
// 原理: DataBox.TryResolve<FileSystemService> → Find(dir, pattern, recursive, limit)；argsJson 内解析 dir/pattern/recursive/limit
// 常用: TextCat 认领线——'text.find'[@args] > @result
// ═══════════════════════════════════════════════════
using System;
using System.Text.Json;
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 文本积木——text-find 文件名搜索（LLM 工具执行面：参数整包 argsJson）
    /// </summary>
    public static class TextFindBrick
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
            string dir = ExtractArg(argsJson, "dir");
            if (dir.Length == 0)
            {
                result = "ERR|BAD_ARGS|缺少参数 dir";
                return false;
            }
            string pattern = ExtractArg(argsJson, "pattern");
            if (pattern.Length == 0)
            {
                pattern = "*";
            }
            bool recursive = true;
            string recRaw = ExtractArg(argsJson, "recursive");
            if (recRaw.Length > 0 && (recRaw == "false" || recRaw == "0"))
            {
                recursive = false;
            }
            int limit = 500;
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
                string[] rows = fs.Find(dir, pattern, recursive, limit);
                if (rows == null || rows.Length == 0)
                {
                    result = "（未找到匹配文件）";
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
            }
            return "";
        }
    }
}
// #MAU_CHECKSUM:SHA256:55EA2FC452991E8C6C77BE3C34D792B3BE9C061105BBDC818DD8806952CD1773

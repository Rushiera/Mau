// ═══════════════════════════════════════════════════
// 积木: text.read_lines
// ID:   BRIK-TEXT-005
// 类别: TEXT
// 作用: 按行号区间读取文本（start 起 / end 止，1 起；end 省略读至文件尾；编码自动探测）——LLM 工具 text-read_lines 语料执行面
// 依赖: 无
// 引用: Mau.Runtime（FileSystemService/DataBox）
// 原理: DataBox.TryResolve<FileSystemService> → ReadLinesAuto(path, start, end)；argsJson 内解析 path/start/end
// 常用: TextCat 认领线——'text.read_lines'[@args] > @result
// ═══════════════════════════════════════════════════
using System;
using System.Text.Json;
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 文本积木——text-read_lines 行号区间读取（LLM 工具执行面：参数整包 argsJson）
    /// </summary>
    public static class TextReadLinesBrick
    {
        /// <summary>
        /// 按行号区间读取
        /// </summary>
        /// <param name="argsJson">工具参数 JSON（path/start/end）</param>
        /// <param name="result">带行号文本或 ERR| 错误文本</param>
        /// <returns>true=执行成功</returns>
        public static bool ReadLines(string argsJson, out string result)
        {
            result = "";
            string path = ExtractArg(argsJson, "path");
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
            int start = 1;
            int end = 0;
            string startRaw = ExtractArg(argsJson, "start");
            string endRaw = ExtractArg(argsJson, "end");
            if (startRaw.Length > 0 && !int.TryParse(startRaw, out start))
            {
                result = "ERR|BAD_ARGS|参数 start 非整数: " + startRaw;
                return false;
            }
            if (endRaw.Length > 0 && !int.TryParse(endRaw, out end))
            {
                result = "ERR|BAD_ARGS|参数 end 非整数: " + endRaw;
                return false;
            }
            if (start < 1)
            {
                result = "ERR|BAD_ARGS|参数 start 必须 ≥1";
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
                result = fs.ReadLinesAuto(path, start, end);
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
// #MAU_CHECKSUM:SHA256:3EEE0CB7BB428989C6ED69C11B0D41C553F0EF8D7A9FB676237419C4CC12282A

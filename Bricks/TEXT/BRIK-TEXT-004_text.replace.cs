// ═══════════════════════════════════════════════════
// 积木: text.replace
// ID:   BRIK-TEXT-004
// 类别: TEXT
// 作用: 锚点替换——exact/ignore_case 唯一锚点替换，all/regex 全部匹配替换并原子写回（exact/ignore_case/all/regex；NotFound 带差异字节定位，Ambiguous 带候选行）——LLM 工具 text-replace 语料执行面
// 依赖: 无
// 引用: Mau.Runtime（FileSystemService/DataBox/TextReplaceOutcome）
// 原理: DataBox.TryResolve<FileSystemService> → ReplaceTextAuto(path, old, new, mode)；argsJson 内解析 path/old/new/mode
// 常用: dev_cat.mau 认领线——'text.replace'[@args] > @result
// ═══════════════════════════════════════════════════
using System;
using System.Text.Json;
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 文本积木——text-replace 替换文本（LLM 工具执行面：参数整包 argsJson）
    /// </summary>
    public static class TextReplaceBrick
    {
        /// <summary>
        /// 锚点替换——exact/ignore_case 唯一命中替换，all/regex 全部匹配；编码内建 + 换行保真
        /// </summary>
        /// <param name="argsJson">工具参数 JSON（path/old/new/mode）</param>
        /// <param name="result">三态确认文本或 ERR| 错误文本</param>
        /// <returns>true=执行成功</returns>
        public static bool Replace(string argsJson, out string result)
        {
            result = "";
            string path = ExtractArg(argsJson, "path");
            string oldText = ExtractArg(argsJson, "old");
            string newText = ExtractArg(argsJson, "new");
            if (path == "§PARSE_FAIL§" || oldText == "§PARSE_FAIL§" || newText == "§PARSE_FAIL§")
            {
                result = "ERR|BAD_ARGS|工具参数 JSON 解析失败（LLM 生成参数可能被截断——超长内容请分段写入）";
                return false;
            }
            if (path.Length == 0 || oldText.Length == 0)
            {
                result = "ERR|BAD_ARGS|缺少参数 path 或 old";
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
                string mode = ExtractArg(argsJson, "mode");
                if (mode.Length == 0)
                {
                    mode = "exact";
                }
                TextReplaceOutcome outcome = fs.ReplaceTextAuto(path, oldText, newText, mode);
                if (outcome.Status == TextReplaceStatus.NotFound)
                {
                    result = "ERR|ANCHOR_NOT_FOUND|第 " + outcome.DiffByteIndex.ToString() + " 字节 期望「" + outcome.Expected + "」实际「" + outcome.Actual + "」";
                    return false;
                }
                if (outcome.Status == TextReplaceStatus.Ambiguous)
                {
                    result = "ERR|ANCHOR_AMBIGUOUS|锚点出现 " + outcome.Count.ToString() + " 次，候选行: " + string.Join(", ", outcome.CandidateLines);
                    return false;
                }
                result = "OK 替换完成: " + outcome.Count.ToString() + " 处（" + path + "）--目标段--" + outcome.Snippet;
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
// #MAU_CHECKSUM:SHA256:F25DB65ECC89B12BB8F291FBA81DA4B1CBA84095395C7E819AD56251107F6581

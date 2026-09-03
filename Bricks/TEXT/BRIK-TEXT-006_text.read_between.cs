// ═══════════════════════════════════════════════════
// 积木: text.read_between
// ID:   BRIK-TEXT-006
// 类别: TEXT
// 作用: 锚点区间读取——str1 与 str2 之间内容（str1 空=文件头 / str2 空=文件尾；锚点须全文唯一；编码自动探测）——LLM 工具 text-read_between 语料执行面
// 依赖: 无
// 引用: Mau.Runtime（FileSystemService/DataBox）
// 原理: DataBox.TryResolve<FileSystemService> → ReadBetweenAuto(path, str1, str2)；argsJson 内解析 path/str1/str2
// 常用: TextCat 认领线——'text.read_between'[@args] > @result
// ═══════════════════════════════════════════════════
using System;
using System.Text.Json;
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 文本积木——text-read_between 锚点区间读取（LLM 工具执行面：参数整包 argsJson）
    /// </summary>
    public static class TextReadBetweenBrick
    {
        /// <summary>
        /// 锚点区间读取
        /// </summary>
        /// <param name="argsJson">工具参数 JSON（path/str1/str2）</param>
        /// <param name="result">区间内容或 ERR| 错误文本</param>
        /// <returns>true=执行成功</returns>
        public static bool ReadBetween(string argsJson, out string result)
        {
            result = "";
            string path = ExtractArg(argsJson, "path");
            if (path.Length == 0)
            {
                result = "ERR|BAD_ARGS|缺少参数 path";
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
                result = fs.ReadBetweenAuto(path, ExtractArg(argsJson, "str1"), ExtractArg(argsJson, "str2"));
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
// #MAU_CHECKSUM:SHA256:DB645A540F113E552044B5237F07D0BF83F4B5D335C6FB178DB73F80063751B0

// ═══════════════════════════════════════════════════
// 积木: text.append
// ID:   BRIK-TEXT-003
// 类别: TEXT
// 作用: 追加文本到文件末尾（文件不存在则新建；自动创建父目录）——LLM 工具 text-append 语料执行面
// 依赖: 无
// 引用: Mau.Runtime（FileSystemService/DataBox）
// 原理: DataBox.TryResolve<FileSystemService> → AppendText(path, content)；argsJson 内解析 path/content
// 常用: dev_cat.mau 认领线——'text.append'[@args] > @result
// ═══════════════════════════════════════════════════
using System;
using System.Text.Json;
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 文本积木——text-append 追加文本（LLM 工具执行面：参数整包 argsJson）
    /// </summary>
    public static class TextAppendBrick
    {
        /// <summary>
        /// 追加文本到文件末尾
        /// </summary>
        /// <param name="argsJson">工具参数 JSON（path/content）</param>
        /// <param name="result">确认文本或 ERR| 错误文本</param>
        /// <returns>true=执行成功</returns>
        public static bool Append(string argsJson, out string result)
        {
            result = "";
            string path = ExtractArg(argsJson, "path");
            string content = ExtractArg(argsJson, "content");
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
                fs.AppendText(path, content);
                result = "OK 已追加: " + path + "（+" + content.Length.ToString() + " 字符）";
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
// #MAU_CHECKSUM:SHA256:343A7FA17D32EF28F3E48C5DBF713F7DC4E66CE9D6B041678DF8BCB5C85E8298

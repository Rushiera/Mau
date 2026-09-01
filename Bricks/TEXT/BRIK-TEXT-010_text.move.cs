// ═══════════════════════════════════════════════════
// 积木: text.move
// ID:   BRIK-TEXT-010
// 类别: TEXT
// 作用: 移动/重命名——文件与目录均支持（目录=整棵子树移动）；自动创建目标父目录；目标已存在拒绝——LLM 工具 text-move 语料执行面
// 依赖: 无
// 引用: Mau.Runtime（FileSystemService/DataBox）
// 原理: DataBox.TryResolve<FileSystemService> → Move(src, dest)；argsJson 内解析 src/dest
// 常用: TextCat 认领线——'text.move'[@args] > @result
// ═══════════════════════════════════════════════════
using System;
using System.Text.Json;
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 文本积木——text-move 移动/重命名（LLM 工具执行面：参数整包 argsJson）
    /// </summary>
    public static class TextMoveBrick
    {
        /// <summary>
        /// 移动/重命名
        /// </summary>
        /// <param name="argsJson">工具参数 JSON（src/dest）</param>
        /// <param name="result">确认文本或 ERR| 错误文本</param>
        /// <returns>true=执行成功</returns>
        public static bool Move(string argsJson, out string result)
        {
            result = "";
            string src = ExtractArg(argsJson, "src");
            string dest = ExtractArg(argsJson, "dest");
            if (src.Length == 0 || dest.Length == 0)
            {
                result = "ERR|BAD_ARGS|缺少参数 src 或 dest";
                return false;
            }
            try
            {
                FileSystemService? fs;
                DataBox.TryResolve<FileSystemService>(out fs);
                if (fs == null)
                {
                    result = "ERR|FS_NO_SERVICE|宿主未注入 FileSystemService";
                    return false;
                }
                fs.Move(src, dest);
                result = "OK 已移动: " + src + " → " + dest;
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
// #MAU_CHECKSUM:SHA256:976BC8937EB8F3356FCDE223329DA7CF29C9E8EBB812349C3B77E119F24AA45C

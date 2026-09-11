// ═══════════════════════════════════════════════════
// 积木: text.delete
// ID:   BRIK-TEXT-011
// 类别: TEXT
// 作用: 软删除——移入受控回收站（可恢复）；支持文件与空目录——LLM 工具 text-delete 语料执行面
// 依赖: 无
// 引用: Mau.Runtime（FileSystemService/DataBox）
// 原理: DataBox.TryResolve<FileSystemService> → Recycle(path)；argsJson 内解析 path
// 常用: TextCat 认领线——'text.delete'[@args] > @result
// ═══════════════════════════════════════════════════
using System;
using System.Text.Json;
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 文本积木——text-delete 软删除（LLM 工具执行面：参数整包 argsJson）
    /// </summary>
    public static class TextDeleteBrick
    {
        /// <summary>
        /// 软删除到回收站
        /// </summary>
        /// <param name="argsJson">工具参数 JSON（path）</param>
        /// <param name="result">回收站路径确认或 ERR| 错误文本</param>
        /// <returns>true=执行成功</returns>
        public static bool Delete(string argsJson, out string result)
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
                string target = fs.Recycle(path);
                result = "OK 已软删除 → " + target;
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
// #MAU_CHECKSUM:SHA256:3C5AC539AA7D116F6F4462E260B0DDFDCEF1B003917110B8F6DBCDA41DFF0FBE

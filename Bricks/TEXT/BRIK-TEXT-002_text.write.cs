// ═══════════════════════════════════════════════════
// 积木: text.write
// ID:   BRIK-TEXT-002
// 类别: TEXT
// 作用: 覆写文件（含新建）——整文件替换为 content（受控根内路径）——LLM 工具 text-write 语料执行面
// 依赖: 无
// 引用: Mau.Runtime（FileSystemService/DataBox）
// 原理: DataBox.TryResolve<FileSystemService> → WriteTextAuto(path, content)；argsJson 内解析 path/content
// 常用: dev_cat.mau 认领线——'text.write'[@args] > @result
// ═══════════════════════════════════════════════════
using System;
using System.Text.Json;
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 文本积木——text-write 覆写文件（LLM 工具执行面：参数整包 argsJson）
    /// </summary>
    public static class TextWriteBrick
    {
        /// <summary>
        /// 覆写文件（含新建）
        /// </summary>
        /// <param name="argsJson">工具参数 JSON（path/content）</param>
        /// <param name="result">确认文本或 ERR| 错误文本</param>
        /// <returns>true=执行成功</returns>
        public static bool Write(string argsJson, out string result)
        {
            result = "";
            string path = ExtractArg(argsJson, "path");
            string content = ExtractArg(argsJson, "content");
            if (path == "§PARSE_FAIL§" || content == "§PARSE_FAIL§")
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
                fs.WriteTextAuto(path, content);
                result = "OK 已覆写: " + path + "（" + content.Length.ToString() + " 字符）";
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
// #MAU_CHECKSUM:SHA256:546878D22E0D4B8170239D1672CAD5530589311C1C3580EA62CC2CADAD751717

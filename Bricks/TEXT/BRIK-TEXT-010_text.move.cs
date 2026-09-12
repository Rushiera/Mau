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
            // [参数面] 声明面口径零容忍——未知 / 缺值一律 ERR|BAD_ARGS（catId 保留键放行）
            string badArgs = ValidateArgs(argsJson, "src dest", "src dest", "", "");
            if (badArgs.Length > 0)
            {
                result = badArgs;
                return false;
            }
            string src = ExtractArg(argsJson, "src");
            string dest = ExtractArg(argsJson, "dest");
            if (src == "§PARSE_FAIL§" || dest == "§PARSE_FAIL§")
            {
                result = "ERR|BAD_ARGS|工具参数 JSON 解析失败（LLM 生成参数可能被截断——超长内容请分段写入）";
                return false;
            }
            if (src.Length == 0 || dest.Length == 0)
            {
                result = "ERR|BAD_ARGS|缺少参数 src 或 dest";
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
        /// 参数面校验——声明面口径零容忍：未知参数 / 必填缺值 / 非法枚举值一律 ERR|BAD_ARGS（宿主注入保留键 catId 放行）。
        /// </summary>
        /// <param name="argsJson">工具参数 JSON</param>
        /// <param name="allowed">允许键（空格分隔）</param>
        /// <param name="required">必填键（空格分隔）</param>
        /// <param name="enumName">枚举参数名（空=无）</param>
        /// <param name="enumValues">枚举合法值（| 分隔）</param>
        /// <returns>错误文本（空=通过）</returns>
        private static string ValidateArgs(string argsJson, string allowed, string required, string enumName, string enumValues)
        {
            if (argsJson == null || argsJson.Length == 0)
            {
                return "ERR|BAD_ARGS|工具参数 JSON 解析失败（LLM 生成参数可能被截断——超长内容请分段写入）";
            }
            try
            {
                JsonDocument doc = JsonDocument.Parse(argsJson);
                try
                {
                    JsonElement root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object)
                    {
                        return "ERR|BAD_ARGS|参数必须是 JSON 对象";
                    }
                    foreach (JsonProperty property in root.EnumerateObject())
                    {
                        if (property.Name == "catId")
                        {
                            continue;
                        }
                        if ((" " + allowed + " ").IndexOf(" " + property.Name + " ", StringComparison.Ordinal) < 0)
                        {
                            return "ERR|BAD_ARGS|未知参数: " + property.Name + "（支持 " + allowed + "）";
                        }
                    }
                    string[] must = required.Split(' ');
                    for (int i = 0; i < must.Length; i = i + 1)
                    {
                        JsonElement mustValue;
                        if (!root.TryGetProperty(must[i], out mustValue) ||
                            (mustValue.ValueKind == JsonValueKind.String && (mustValue.GetString() ?? "").Length == 0))
                        {
                            return "ERR|BAD_ARGS|缺参数 " + must[i] + "（必填：" + required + "）";
                        }
                    }
                    if (enumName.Length > 0)
                    {
                        JsonElement enumValue;
                        if (root.TryGetProperty(enumName, out enumValue) && enumValue.ValueKind == JsonValueKind.String)
                        {
                            string value = enumValue.GetString() ?? "";
                            if (value.Length > 0 && ("|" + enumValues + "|").IndexOf("|" + value + "|", StringComparison.Ordinal) < 0)
                            {
                                return "ERR|BAD_ARGS|" + enumName + " 非法值: " + value + "（" + enumValues + "）";
                            }
                        }
                    }
                    return "";
                }
                finally
                {
                    doc.Dispose();
                }
            }
            catch (Exception ex)
            {
                return "ERR|BAD_ARGS|参数 JSON 解析失败: " + ex.Message;
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
// #MAU_CHECKSUM:SHA256:13FB130C29F40DEC51DA170609FF0AE375B3A93C0292ED11B876E959DDF54D0D

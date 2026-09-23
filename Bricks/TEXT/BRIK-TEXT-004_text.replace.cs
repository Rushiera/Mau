// ═══════════════════════════════════════════════════
// 积木: text.replace
// ID:   BRIK-TEXT-004
// 类别: TEXT
// 作用: 锚点替换——exact/ignore_case 唯一锚点替换，all/regex 全部匹配替换并原子写回（exact/ignore_case/all/regex；NotFound 带差异字节定位，Ambiguous 带候选行）；new 传删除标记「黑暗剑+22」按空文本落盘（等价删除，四模式通用），空 new 一律拒绝——LLM 工具 text-replace 语料执行面
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
        /// <summary>删除标记——单一真相源：Mau.Runtime.TextReplaceSpec（积木与宿主摘要投影共用）</summary>
        private const string DeleteKey = TextReplaceSpec.DeleteKey;

        /// <summary>
        /// 锚点替换——exact/ignore_case 唯一命中替换，all/regex 全部匹配；编码内建 + 换行保真；
        /// new 空值一律拒绝（错误文本提示删除标记），new 等于 DeleteKey 时按空文本落盘（等价删除），成功返回文本注明本次按空 New 删除
        /// </summary>
        /// <param name="argsJson">工具参数 JSON（path/old/new/mode）</param>
        /// <param name="result">三态确认文本或 ERR| 错误文本</param>
        /// <returns>true=执行成功</returns>
        public static bool Replace(string argsJson, out string result)
        {
            result = "";
            // [参数面] 声明面口径零容忍——未知 / 缺值 / 非法 mode 一律 ERR|BAD_ARGS（catId 保留键放行）
            string badArgs = ValidateArgs(argsJson, "path old new mode", "path old new", "mode", "exact|ignore_case|all|regex");
            if (badArgs.Length > 0)
            {
                result = badArgs;
                return false;
            }
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
            // [删除语义] new 面——空值 / 缺值一律拒绝（区分显式删除与漏传）；删除标记按空文本落盘（A88）
            if (newText.Length == 0)
            {
                result = "ERR|BAD_ARGS|缺参数 new——替换文本不可为空；删除匹配文本请传删除标记「" + DeleteKey + "」";
                return false;
            }
            bool deleteMode = newText == DeleteKey;
            if (deleteMode)
            {
                newText = "";
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
                string deleteNote = deleteMode ? "——本次 New 为空（删除标记「" + DeleteKey + "」）：删除匹配的 Old 串" : "";
                result = "OK 替换完成: " + outcome.Count.ToString() + " 处（" + path + "）" + deleteNote + "--目标段--" + outcome.Snippet;
                return true;
            }
            catch (Exception ex)
            {
                result = "ERR|" + ex.GetType().Name + "|" + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 参数面校验——声明面口径零容忍：未知参数 / 必填缺键 / 非字符串值 / 非法枚举值一律 ERR|BAD_ARGS（宿主注入保留键 catId 放行）；
        /// 空值与删除标记不在本层判定——结构性校验（键存在 + 类型）与内容性校验（空值语义）分离，后者归调用方。
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
                        if (!root.TryGetProperty(must[i], out mustValue))
                        {
                            return "ERR|BAD_ARGS|缺参数 " + must[i] + "（必填：" + required + "）";
                        }
                        if (mustValue.ValueKind != JsonValueKind.String)
                        {
                            return "ERR|BAD_ARGS|参数 " + must[i] + " 必须是字符串（当前 " + mustValue.ValueKind.ToString() + "）";
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
// #MAU_CHECKSUM:SHA256:972CDA2C964442D9B70722602E936DC1E461787DEE770420D00E6BD89E58D816

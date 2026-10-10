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
            string badArgs = JsonArgs.Validate(argsJson, "path old new mode", "path old new", "mode", "exact|ignore_case|all|regex");
            if (badArgs.Length > 0)
            {
                result = badArgs;
                return false;
            }
            string path = JsonArgs.Get(argsJson, "path");
            string oldText = JsonArgs.Get(argsJson, "old");
            string newText = JsonArgs.Get(argsJson, "new");
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
                FileSystemService? fs = FileSystemRegistry.ResolveScoped(JsonArgs.Get(argsJson, "catId"));
                if (fs == null)
                {
                    DataBox.TryResolve<FileSystemService>(out fs);
                }
                if (fs == null)
                {
                    result = "ERR|FS_NO_SERVICE|宿主未注入 FileSystemService";
                    return false;
                }
                string mode = JsonArgs.Get(argsJson, "mode");
                if (mode.Length == 0)
                {
                    mode = "exact";
                }
                TextReplaceOutcome outcome = fs.ReplaceTextAuto(path, oldText, newText, mode);
                if (outcome.Status == TextReplaceStatus.NotFound)
                {
                    result = "ERR|ANCHOR_NOT_FOUND|第 " + outcome.DiffByteIndex.ToString() + " 字节 期望「" + outcome.Expected + "」实际「" + outcome.Actual + "」";
                    if (outcome.EntitySuspect)
                    {
                        result = result + "｜提示：疑似实体写法——锚点直接写裸符号 < > &；若文件本身是实体文本，锚点写双写形态 &amp;lt;";
                    }
                    return false;
                }
                if (outcome.Status == TextReplaceStatus.Ambiguous)
                {
                    result = "ERR|ANCHOR_AMBIGUOUS|锚点出现 " + outcome.Count.ToString() + " 次，候选行: " + string.Join(", ", outcome.CandidateLines);
                    return false;
                }
                string deleteNote = deleteMode ? "——本次 New 为空（删除标记「" + DeleteKey + "」）：删除匹配的 Old 串" : "";
                string spanNote = "";
                if (mode == "regex")
                {
                    spanNote = " · 匹配跨度 最长 " + outcome.MaxSpanLines.ToString() + " 行 / " + outcome.MaxSpanChars.ToString() + " 字符 · 命中合计 " + outcome.TotalSpanChars.ToString() + " 字符";
                    if (outcome.SpanWarned)
                    {
                        spanNote = spanNote + " ⚠ 单次匹配跨度过大——请核对是否吞入相邻内容";
                    }
                }
                string lineNote = "";
                if (mode == "all" && outcome.CandidateLines.Length > 0)
                {
                    lineNote = " · 命中行 " + string.Join(", ", outcome.CandidateLines);
                    if (outcome.CandidateLines.Length < outcome.Count)
                    {
                        lineNote = lineNote + "…（前 " + outcome.CandidateLines.Length.ToString() + " / 共 " + outcome.Count.ToString() + "）";
                    }
                }
                // 结构化返回体（design-ch4-tools 附录 · A214）——首行 JSON 头（target/items）+ 正文摘要行
                result = MetaHead("text-replace", true, path, outcome.Count) + "\n" + path + " | 替换 " + outcome.Count.ToString() + " 处" + spanNote + lineNote + deleteNote + "\n" + outcome.Snippet;
                return true;
            }
            catch (Exception ex)
            {
                result = "ERR|" + ex.GetType().Name + "|" + ex.Message;
                return false;
            }
        }
        /// <summary>
        /// 结构化元数据头（统一口径·A214）——恒定 ok / tool + 主来源 target + 主计数 items（空串 / 负值 = 省略）。
        /// </summary>
        /// <param name="tool">工具名</param>
        /// <param name="ok">成败</param>
        /// <param name="target">主来源（空串 = 省略）</param>
        /// <param name="items">主计数（负值 = 省略）</param>
        /// <returns>单行 JSON</returns>
        private static string MetaHead(string tool, bool ok, string target, int items)
        {
            System.Collections.Generic.Dictionary<string, object> head = new System.Collections.Generic.Dictionary<string, object>();
            head["ok"] = ok;
            head["tool"] = tool;
            if (target.Length > 0)
            {
                head["target"] = target;
            }
            if (items >= 0)
            {
                head["items"] = items;
            }
            return JsonSerializer.Serialize(head);
        }
    }
}
// #MAU_CHECKSUM:SHA256:A67F253C06536C11041B2C26C362A76CA49216CC4AF77DB72336284879D6A4E9

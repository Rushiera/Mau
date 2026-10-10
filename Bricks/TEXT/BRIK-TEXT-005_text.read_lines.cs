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
            // [参数面] 声明面口径零容忍——未知 / 缺值一律 ERR|BAD_ARGS（catId 保留键放行）
            string badArgs = JsonArgs.Validate(argsJson, "path start end", "path", "", "");
            if (badArgs.Length > 0)
            {
                result = badArgs;
                return false;
            }
            string path = JsonArgs.Get(argsJson, "path");
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
            string startRaw = JsonArgs.Get(argsJson, "start");
            string endRaw = JsonArgs.Get(argsJson, "end");
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
                string body = fs.ReadLinesAuto(path, start, end);
                int lines = 1;
                for (int i = 0; i < body.Length; i = i + 1)
                {
                    if (body[i] == '\n')
                    {
                        lines = lines + 1;
                    }
                }
                // 结构化返回体（design-ch4-tools 附录 · A214）——首行 JSON 头（target/items）+ 正文摘要行
                result = MetaHead("text-read_lines", true, path, lines) + "\n" + path + " | " + lines.ToString() + " 行" + "\n" + body;
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
// #MAU_CHECKSUM:SHA256:6AB7A0E91B435C543564B7301CF4570EEE7CAE6904615C8F5AEA1A81D60B5BAC

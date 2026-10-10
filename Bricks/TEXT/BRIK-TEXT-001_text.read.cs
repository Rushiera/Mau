// ═══════════════════════════════════════════════════
// 积木: text.read
// ID:   BRIK-TEXT-001
// 类别: TEXT
// 作用: 自动编码读取文本文件（受控根内路径）——LLM 工具 text-read 的语料执行面（P8 二期 dev_cat）
// 依赖: 无
// 引用: Mau.Runtime（FileSystemService/DataBox）
// 原理: DataBox.TryResolve<FileSystemService> → ReadTextAuto(path)；argsJson 内解析 path（语料零 JSON 解析）
// 常用: dev_cat.mau 认领线——'text.read'[@args] > @result
// ═══════════════════════════════════════════════════
using System;
using System.Text.Json;
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 文本积木——text-read 读取文本（LLM 工具执行面：参数整包 argsJson）
    /// </summary>
    public static class TextReadBrick
    {
        /// <summary>
        /// 自动编码读取文本文件
        /// </summary>
        /// <param name="argsJson">工具参数 JSON（path）</param>
        /// <param name="result">文件内容（截断 20K）或 ERR| 错误文本</param>
        /// <returns>true=执行成功</returns>
        public static bool Read(string argsJson, out string result)
        {
            result = "";
            // [参数面] 声明面口径零容忍——未知 / 缺值一律 ERR|BAD_ARGS（catId 保留键放行）
            string badArgs = JsonArgs.Validate(argsJson, "path", "path", "", "");
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
                string content = fs.ReadTextAuto(path);
                int lines = 1;
                for (int i = 0; i < content.Length; i = i + 1)
                {
                    if (content[i] == '\n')
                    {
                        lines = lines + 1;
                    }
                }
                // 结构化返回体（design-ch4-tools 附录 · A214）——首行 JSON 头（target/items）+ 正文摘要行
                result = MetaHead("text-read", true, path, lines) + "\n" + path + " | " + lines.ToString() + " 行" + "\n" + TrimResult(content, 20000);
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
        /// <summary>
        /// 结果截断——超长文本保留头部 + 截断提示（上下文防爆）
        /// </summary>
        /// <param name="text">原文</param>
        /// <param name="max">上限字符数</param>
        /// <returns>截断文本</returns>
        private static string TrimResult(string text, int max)
        {
            if (text == null || text.Length <= max)
            {
                return text ?? "";
            }
            return text.Substring(0, max) + "\n…[截断: 共 " + text.Length.ToString() + " 字符，仅保留前 " + max.ToString() + "]";
        }
    }
}
// #MAU_CHECKSUM:SHA256:2313B0BBE573674D1BE425E952430728E9F8DCE84FDF48952A38E4DA7DFD98FA

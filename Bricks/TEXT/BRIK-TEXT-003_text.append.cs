// ═══════════════════════════════════════════════════
// 积木: text.append
// ID:   BRIK-TEXT-003
// 类别: TEXT
// 作用: 追加文本到文件末尾（文件不存在则新建；自动创建父目录）——LLM 工具 text-append 语料执行面
// 依赖: 无
// 引用: Mau.Runtime（FileSystemService/DataBox）
// 原理: DataBox.TryResolve<FileSystemService> → AppendTextAuto(path, content)；argsJson 内解析 path/content
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
            // [参数面] 声明面口径零容忍——未知 / 缺值一律 ERR|BAD_ARGS（catId 保留键放行）
            string badArgs = JsonArgs.Validate(argsJson, "path content", "path content", "", "");
            if (badArgs.Length > 0)
            {
                result = badArgs;
                return false;
            }
            string path = JsonArgs.Get(argsJson, "path");
            string content = JsonArgs.Get(argsJson, "content");
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
                fs.AppendTextAuto(path, content);
                result = "OK 已追加: " + path + "（+" + content.Length.ToString() + " 字符）";
                return true;
            }
            catch (Exception ex)
            {
                result = "ERR|" + ex.GetType().Name + "|" + ex.Message;
                return false;
            }
        }
    }
}
// #MAU_CHECKSUM:SHA256:0D10C8F87A5BCEEBE0D4FC9DEEEEF3353643D226B5EFE68F2529EC1F24A6733D

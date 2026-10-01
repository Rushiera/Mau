// ═══════════════════════════════════════════════════
// 积木: text.read_between
// ID:   BRIK-TEXT-006
// 类别: TEXT
// 作用: 锚点区间读取——str1 与 str2 之间内容（str1 空=文件头 / str2 空=文件尾；锚点须全文唯一；编码自动探测）——LLM 工具 text-read_between 语料执行面
// 依赖: 无
// 引用: Mau.Runtime（FileSystemService/DataBox）
// 原理: DataBox.TryResolve<FileSystemService> → ReadBetweenAuto(path, str1, str2)；argsJson 内解析 path/str1/str2
// 常用: TextCat 认领线——'text.read_between'[@args] > @result
// ═══════════════════════════════════════════════════
using System;
using System.Text.Json;
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 文本积木——text-read_between 锚点区间读取（LLM 工具执行面：参数整包 argsJson）
    /// </summary>
    public static class TextReadBetweenBrick
    {
        /// <summary>
        /// 锚点区间读取
        /// </summary>
        /// <param name="argsJson">工具参数 JSON（path/str1/str2）</param>
        /// <param name="result">区间内容或 ERR| 错误文本</param>
        /// <returns>true=执行成功</returns>
        public static bool ReadBetween(string argsJson, out string result)
        {
            result = "";
            // [参数面] 声明面口径零容忍——未知 / 缺值一律 ERR|BAD_ARGS（catId 保留键放行）
            string badArgs = JsonArgs.Validate(argsJson, "path str1 str2", "path", "", "");
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
                result = fs.ReadBetweenAuto(path, JsonArgs.Get(argsJson, "str1"), JsonArgs.Get(argsJson, "str2"));
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
// #MAU_CHECKSUM:SHA256:5D5A6AA648BF4A486CE95BAFF17522390E3F99AC8DF6DF2F01A49093336FD577

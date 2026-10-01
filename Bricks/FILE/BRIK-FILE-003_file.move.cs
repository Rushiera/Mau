// ═══════════════════════════════════════════════════
// 积木: file.move
// ID:   BRIK-FILE-003
// 类别: FILE
// 作用: 移动/重命名——文件与目录均支持（目录=整棵子树移动）；自动创建目标父目录；目标已存在拒绝——LLM 工具 file-move 语料执行面
// 依赖: 无
// 引用: Mau.Runtime（FileSystemService/DataBox）
// 原理: DataBox.TryResolve<FileSystemService> → Move(src, dest)；argsJson 内解析 src/dest
// 常用: FileCat 认领线——'file.move'[@args] > @result
// ═══════════════════════════════════════════════════
using System;
using System.Text.Json;
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 文件积木——file-move 移动/重命名（LLM 工具执行面：参数整包 argsJson）
    /// </summary>
    public static class FileMoveBrick
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
            string badArgs = JsonArgs.Validate(argsJson, "src dest", "src dest", "", "");
            if (badArgs.Length > 0)
            {
                result = badArgs;
                return false;
            }
            string src = JsonArgs.Get(argsJson, "src");
            string dest = JsonArgs.Get(argsJson, "dest");
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
    }
}
// #MAU_CHECKSUM:SHA256:63E178F4953D5BBD959200E207CB2917CE9F793ED9E0BA5B9FD255206FF1D9FA

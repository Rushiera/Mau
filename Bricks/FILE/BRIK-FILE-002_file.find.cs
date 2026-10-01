// ═══════════════════════════════════════════════════
// 积木: file.find
// ID:   BRIK-FILE-002
// 类别: FILE
// 作用: 文件名 glob 搜索——按文件名模式找文件（pattern 如 *.md / **/*.cs；recursive 默认 true）——LLM 工具 file-find 语料执行面
// 依赖: 无
// 引用: Mau.Runtime（FileSystemService/DataBox）
// 原理: DataBox.TryResolve<FileSystemService> → Find(dir, pattern, recursive, limit)；argsJson 内解析 dir/pattern/recursive/limit
// 常用: FileCat 认领线——'file.find'[@args] > @result
// ═══════════════════════════════════════════════════
using System;
using System.Text.Json;
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 文件积木——file-find 文件名搜索（LLM 工具执行面：参数整包 argsJson）
    /// </summary>
    public static class FileFindBrick
    {
        /// <summary>
        /// 文件名 glob 搜索
        /// </summary>
        /// <param name="argsJson">工具参数 JSON（dir/pattern/recursive/limit）</param>
        /// <param name="result">相对路径列表（\n 分隔）或 ERR| 错误文本</param>
        /// <returns>true=执行成功</returns>
        public static bool Find(string argsJson, out string result)
        {
            result = "";
            // [参数面] 声明面口径零容忍——未知 / 缺值一律 ERR|BAD_ARGS（catId 保留键放行）
            string badArgs = JsonArgs.Validate(argsJson, "dir pattern recursive limit", "dir", "", "");
            if (badArgs.Length > 0)
            {
                result = badArgs;
                return false;
            }
            string dir = JsonArgs.Get(argsJson, "dir");
            if (dir == "§PARSE_FAIL§")
            {
                result = "ERR|BAD_ARGS|工具参数 JSON 解析失败（LLM 生成参数可能被截断——超长内容请分段写入）";
                return false;
            }
            if (dir.Length == 0)
            {
                result = "ERR|BAD_ARGS|缺少参数 dir";
                return false;
            }
            string pattern = JsonArgs.Get(argsJson, "pattern");
            if (pattern == "§PARSE_FAIL§")
            {
                result = "ERR|BAD_ARGS|工具参数 JSON 解析失败（LLM 生成参数可能被截断——超长内容请分段写入）";
                return false;
            }
            if (pattern.Length == 0)
            {
                pattern = "*";
            }
            bool recursive = true;
            string recRaw = JsonArgs.Get(argsJson, "recursive");
            if (recRaw.Length > 0 && (recRaw == "false" || recRaw == "0"))
            {
                recursive = false;
            }
            int limit = 500;
            string limitRaw = JsonArgs.Get(argsJson, "limit");
            if (limitRaw.Length > 0 && !int.TryParse(limitRaw, out limit))
            {
                result = "ERR|BAD_ARGS|参数 limit 非整数: " + limitRaw;
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
                string[] rows = fs.Find(dir, pattern, recursive, limit);
                if (rows == null || rows.Length == 0)
                {
                    result = "（未找到匹配文件）";
                    return true;
                }
                result = string.Join("\n", rows);
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
// #MAU_CHECKSUM:SHA256:5681C792503508149A91E63ED8B252B505E7593B4EA45FE11C6E50E364D5E1C3

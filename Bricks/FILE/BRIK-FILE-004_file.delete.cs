// ═══════════════════════════════════════════════════
// 积木: file.delete
// ID:   BRIK-FILE-004
// 类别: FILE
// 作用: 软删除——移入受控回收站（可恢复）；支持文件与目录（含非空目录=整棵子树整体迁移，附内容统计）——LLM 工具 file-delete 语料执行面
// 依赖: 无
// 引用: Mau.Runtime（FileSystemService/DataBox）
// 原理: DataBox.TryResolve<FileSystemService> → Recycle(path)；argsJson 内解析 path
// 常用: FileCat 认领线——'file.delete'[@args] > @result
// ═══════════════════════════════════════════════════
using System;
using System.Text.Json;
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 文件积木——file-delete 软删除（LLM 工具执行面：参数整包 argsJson）
    /// </summary>
    public static class FileDeleteBrick
    {
        /// <summary>
        /// 软删除到回收站
        /// </summary>
        /// <param name="argsJson">工具参数 JSON（path）</param>
        /// <param name="result">回收站路径确认或 ERR| 错误文本</param>
        /// <returns>true=执行成功</returns>
        public static bool Delete(string argsJson, out string result)
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
                RecycleOutcome outcome = fs.Recycle(path);
                result = "OK 已软删除 → " + outcome.Target + FormatStats(outcome);
                return true;
            }
            catch (Exception ex)
            {
                result = "ERR|" + ex.GetType().Name + "|" + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 内容统计文本——文件 = 体积；目录 = 文件数 + 子目录数 + 合计体积
        /// </summary>
        /// <param name="outcome">回收结果</param>
        /// <returns>统计文本</returns>
        private static string FormatStats(RecycleOutcome outcome)
        {
            if (!outcome.IsDirectory)
            {
                return "（文件 · " + SizeText(outcome.TotalBytes) + "）";
            }
            return "（目录 · 文件 " + outcome.FileCount + " / 子目录 " + outcome.DirectoryCount + " / 合计 " + SizeText(outcome.TotalBytes) + "）";
        }

        /// <summary>
        /// 体积自适应文本（B / KB / MB / GB——整数运算，无小数文化差异）
        /// </summary>
        /// <param name="bytes">字节数</param>
        /// <returns>可读体积</returns>
        private static string SizeText(long bytes)
        {
            if (bytes < 1024)
            {
                return bytes + " B";
            }
            if (bytes < 1024 * 1024)
            {
                return ((bytes + 512) / 1024) + " KB";
            }
            if (bytes < 1024L * 1024 * 1024)
            {
                return ((bytes + (512 * 1024)) / (1024 * 1024)) + " MB";
            }
            return ((bytes + (512L * 1024 * 1024)) / (1024L * 1024 * 1024)) + " GB";
        }
    }
}
// #MAU_CHECKSUM:SHA256:49FC1FD5CCE990EEABB02FF7D59321D6BD6BA4FE1EFA96372A85A508D915FCE3

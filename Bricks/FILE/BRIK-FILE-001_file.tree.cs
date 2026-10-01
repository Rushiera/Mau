// ═══════════════════════════════════════════════════
// 积木: file.tree
// ID:   BRIK-FILE-001
// 类别: FILE
// 作用: 目录树——列目录结构（depth 层级 / limit 条数上限；稳定排序）——LLM 工具 file-tree 语料执行面
// 依赖: 无
// 引用: Mau.Runtime（FileSystemService/DataBox）
// 原理: DataBox.TryResolve<FileSystemService> → Tree(path, depth, limit)；argsJson 内解析 path/depth/limit
// 常用: FileCat 认领线——'file.tree'[@args] > @result
// ═══════════════════════════════════════════════════
using System;
using System.Text.Json;
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 文件积木——file-tree 目录树（LLM 工具执行面：参数整包 argsJson）
    /// </summary>
    public static class FileTreeBrick
    {
        /// <summary>
        /// 目录树
        /// </summary>
        /// <param name="argsJson">工具参数 JSON（path/depth/limit）</param>
        /// <param name="result">相对路径列表（\n 分隔）或 ERR| 错误文本</param>
        /// <returns>true=执行成功</returns>
        public static bool Tree(string argsJson, out string result)
        {
            result = "";
            // [参数面] 声明面口径零容忍——未知 / 缺值一律 ERR|BAD_ARGS（catId 保留键放行）
            string badArgs = JsonArgs.Validate(argsJson, "path depth limit", "path", "", "");
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
            int depth = 2;
            int limit = 500;
            string depthRaw = JsonArgs.Get(argsJson, "depth");
            string limitRaw = JsonArgs.Get(argsJson, "limit");
            if (depthRaw.Length > 0 && !int.TryParse(depthRaw, out depth))
            {
                result = "ERR|BAD_ARGS|参数 depth 非整数: " + depthRaw;
                return false;
            }
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
                string[] rows = fs.Tree(path, depth, limit);
                if (rows == null || rows.Length == 0)
                {
                    result = "（空目录）";
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
// #MAU_CHECKSUM:SHA256:FDA61D5ECC73F87103C3A882646B80C2229BA7D9E2B8032B1F8BDE91C65C927B

// ═══════════════════════════════════════════════════
// 积木: text.tree
// ID:   BRIK-TEXT-007
// 类别: TEXT
// 作用: 目录树——列目录结构（depth 层级 / limit 条数上限；稳定排序）——LLM 工具 text-tree 语料执行面
// 依赖: 无
// 引用: Mau.Runtime（FileSystemService/DataBox）
// 原理: DataBox.TryResolve<FileSystemService> → Tree(path, depth, limit)；argsJson 内解析 path/depth/limit
// 常用: TextCat 认领线——'text.tree'[@args] > @result
// ═══════════════════════════════════════════════════
using System;
using System.Text.Json;
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 文本积木——text-tree 目录树（LLM 工具执行面：参数整包 argsJson）
    /// </summary>
    public static class TextTreeBrick
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
            string path = ExtractArg(argsJson, "path");
            if (path.Length == 0)
            {
                result = "ERR|BAD_ARGS|缺少参数 path";
                return false;
            }
            int depth = 2;
            int limit = 500;
            string depthRaw = ExtractArg(argsJson, "depth");
            string limitRaw = ExtractArg(argsJson, "limit");
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
                FileSystemService? fs;
                DataBox.TryResolve<FileSystemService>(out fs);
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

        /// <summary>
        /// 展平参数提取——argsJson 中取字符串值（不存在返回空串）
        /// </summary>
        /// <param name="argumentsJson">参数 JSON</param>
        /// <param name="key">参数名</param>
        /// <returns>参数值</returns>
        private static string ExtractArg(string argumentsJson, string key)
        {
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
            }
            return "";
        }
    }
}
// #MAU_CHECKSUM:SHA256:027F9B83635BDBA79CB83DCB43968CADD1D61032BAD8134BCC5C524051EFA02A

// ═══════════════════════════════════════════════════
// 积木: file.batch
// ID:   BRIK-FILE-011
// 类别: FILE
// 作用: 顺序批量执行最多一百项写操作
// 依赖: 无
// 引用: System · System.Text · System.Text.Json
// 原理: JSON 操作数组解析 → 逐项执行（仅变更工具）→ 逐项摘要
// 常用: 批量文件变更
// ═══════════════════════════════════════════════════
using System;
using System.Text;
using System.Text.Json;

using Mau.Runtime;
namespace Mau.Bricks
{
    /// <summary>
    /// 文件积木——file.batch 批量执行（依赖 FileBridge）
    /// </summary>
    public static class FileBatchBrick
    {
        /// <summary>
        /// 顺序批量执行最多一百项写操作
        /// </summary>
        /// <param name="operations">操作数组 JSON（[{tool,path,...}]）</param>
        /// <param name="stopOnError">遇错是否停止</param>
        /// <param name="summary">逐项摘要行——成功时填充</param>
        /// <returns>true=全部成功</returns>
        public static bool Batch(string operations, bool stopOnError, out string summary)
        {
            StringBuilder results = new StringBuilder();
            try
            {
                using JsonDocument doc = JsonDocument.Parse(operations);
                JsonElement root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() > 100)
                {
                    summary = "ERR|INVALID_OPERATIONS|Operations array is invalid.";
                    return false;
                }
                bool allOk = true;
                int index = 0;
                foreach (JsonElement operation in root.EnumerateArray())
                {
                    string tool = ReadRequiredText(operation, "tool");
                    try
                    {
                        string result = ExecuteBatchItem(tool, operation);
                        if (results.Length > 0)
                        {
                            results.Append('\n');
                        }
                        results.Append(index.ToString()).Append(":ok:").Append(result);
                    }
                    catch (Exception ex)
                    {
                        allOk = false;
                        if (results.Length > 0)
                        {
                            results.Append('\n');
                        }
                        results.Append(index.ToString()).Append(":failed:").Append(ex.GetType().Name);
                        if (stopOnError)
                        {
                            break;
                        }
                    }
                    index = index + 1;
                }
                summary = results.ToString();
                return allOk;
            }
            catch (JsonException)
            {
                summary = "ERR|TOOL_ARGUMENTS_INVALID|Batch operations JSON is invalid.";
                return false;
            }
        }

        /// <summary>
        /// 执行批量条目
        /// </summary>
        /// <param name="tool">工具名</param>
        /// <param name="op">操作 JSON 元素</param>
        /// <returns>结果文本</returns>
        private static string ExecuteBatchItem(string tool, JsonElement op)
        {
            FileBridge.CurrentFileSystem();
            if (tool == "file_read" || tool == "file_tree" || tool == "file_find"
                || tool == "file_read_lines" || tool == "file_batch")
            {
                throw new InvalidOperationException("Batch accepts mutation tools only.");
            }
            if (tool == "file_write")
            {
                FileBridge.CurrentFileSystem().WriteText(ReadRequiredText(op, "path"), ReadRequiredText(op, "content"));
                return "written";
            }
            if (tool == "file_append")
            {
                FileBridge.CurrentFileSystem().AppendText(ReadRequiredText(op, "path"), ReadRequiredText(op, "content"));
                return "appended";
            }
            if (tool == "file_replace")
            {
                int count = FileBridge.CurrentFileSystem().ReplaceText(ReadRequiredText(op, "path"),
                    ReadRequiredText(op, "oldText"), ReadRequiredText(op, "newText"));
                return "replacements=" + count.ToString();
            }
            if (tool == "file_move")
            {
                FileBridge.CurrentFileSystem().Move(ReadRequiredText(op, "source"), ReadRequiredText(op, "destination"));
                return "moved";
            }
            if (tool == "file_delete")
            {
                return "recycled=" + FileBridge.CurrentFileSystem().Recycle(ReadRequiredText(op, "path"));
            }
            throw new InvalidOperationException("Unsupported batch tool: " + tool);
        }

        /// <summary>
        /// 读取必需非空字符串参数
        /// </summary>
        /// <param name="root">参数对象</param>
        /// <param name="name">字段名</param>
        /// <returns>字符串</returns>
        private static string ReadRequiredText(JsonElement root, string name)
        {
            JsonElement value;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty(name, out value)
                || value.ValueKind != JsonValueKind.String)
            {
                throw new JsonException("Required text is missing: " + name);
            }
            string? text = value.GetString();
            if (text == null)
            {
                return "";
            }
            return text;
        }
    }
}
// #MAU_CHECKSUM:SHA256:28A826097BE5F6CC74928CE2D4EA2EF3A85E297FB55F63E4F9B184DFC1ECEBD7

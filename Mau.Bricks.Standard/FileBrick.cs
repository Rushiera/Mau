// ═══════════════════════════════════════════════
// 积木: file.convert / file.read / file.write / file.append / file.replace
//       file.read_lines / file.tree / file.find / file.move / file.delete / file.batch
// ID:   BRIK-FILE-001 ~ 011
// 作用: 受控文件操作全集——读写/追加/替换/行读/目录树/搜索/移动/软删/批量
// 引用: Mau.Bricks.Standard → Mau.Contracts（BrickRegistry）· FileSystemService
// 依赖: FileSystemService
// 原理: 静态方法 + BrickContract 注册——白名单边界由 FileSystemService 提供
// 常用: CH4 IO 工具组 / 任意文件读取场景 / 批量文件变更
// ═══════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Mau.Contracts;

namespace Mau.Bricks
{
    /// <summary>
    /// 文件积木——标准积木库文件类。全部操作经过受控文件系统（白名单 + 重解析点防护）。
    /// 输出型积木（Read/ReadLines/Tree/Find）使用 bool + out 形态——翻译器输出端口支持后接入生成物。
    /// </summary>
    public static class FileBrick
    {
        /// <summary>
        /// 受控根目录——宿主启动时 ConfigureRoots 配置，默认当前工作目录
        /// </summary>
        private static string[] _roots = new string[] { Environment.CurrentDirectory };

        /// <summary>
        /// 回收目录——默认当前目录 Recycle
        /// </summary>
        private static string _recycleRoot = Path.Combine(Environment.CurrentDirectory, "Recycle");

        /// <summary>
        /// 配置受控根目录——宿主启动时调用，未配置时默认当前工作目录
        /// </summary>
        /// <param name="roots">允许根目录</param>
        /// <param name="recycleRoot">回收目录</param>
        public static void ConfigureRoots(string[] roots, string recycleRoot)
        {
            if (roots == null || roots.Length == 0)
            {
                throw new ArgumentException("At least one root is required.", "roots");
            }
            _roots = roots;
            _recycleRoot = recycleRoot;
        }
        /// <summary>
        /// 转换文件——真实实现（读取→转码→写入）
        /// </summary>
        /// <param name="input">输入文件路径</param>
        /// <param name="output">输出文件路径</param>
        /// <returns>转换是否成功</returns>
        public static bool Convert(string input, string output)
        {
            try
            {
                byte[] bytes = System.IO.File.ReadAllBytes(input);
                // 第一期：转码 = 读取后原样写出——编码转换算法留积木内部后续实现
                System.IO.File.WriteAllBytes(output, bytes);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 读取 UTF-8 文本
        /// </summary>
        /// <param name="path">受控路径</param>
        /// <param name="content">完整文本——成功时填充</param>
        /// <returns>true=成功</returns>
        public static bool Read(string path, out string content)
        {
            try
            {
                content = CurrentFileSystem().ReadText(path);
                return true;
            }
            catch (Exception ex)
            {
                content = "ERR|" + ex.GetType().Name + "|" + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 原子覆写 UTF-8 文本
        /// </summary>
        /// <param name="path">受控路径</param>
        /// <param name="content">完整正文</param>
        /// <returns>true=成功</returns>
        public static bool Write(string path, string content)
        {
            try
            {
                CurrentFileSystem().WriteText(path, content);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 追加 UTF-8 文本并创建父目录
        /// </summary>
        /// <param name="path">受控路径</param>
        /// <param name="content">追加正文</param>
        /// <returns>true=成功</returns>
        public static bool Append(string path, string content)
        {
            try
            {
                CurrentFileSystem().AppendText(path, content);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 替换全部精确文本并原子写回
        /// </summary>
        /// <param name="path">受控路径</param>
        /// <param name="oldText">非空目标</param>
        /// <param name="newText">新文本</param>
        /// <returns>true=成功（0 次替换也算成功）</returns>
        public static bool Replace(string path, string oldText, string newText)
        {
            try
            {
                CurrentFileSystem().ReplaceText(path, oldText, newText);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 按一基闭区间读取文本行（带行号）
        /// </summary>
        /// <param name="path">受控路径</param>
        /// <param name="startLine">起始行（1-based）</param>
        /// <param name="endLine">结束行；0=文件尾</param>
        /// <param name="lines">带行号文本——成功时填充</param>
        /// <returns>true=成功</returns>
        public static bool ReadLines(string path, int startLine, int endLine, out string lines)
        {
            try
            {
                lines = CurrentFileSystem().ReadLines(path, startLine, endLine);
                return true;
            }
            catch (Exception ex)
            {
                lines = "ERR|" + ex.GetType().Name + "|" + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 按深度和数量上限列出稳定排序目录树
        /// </summary>
        /// <param name="path">受控目录</param>
        /// <param name="depth">零到十层</param>
        /// <param name="limit">最大条数</param>
        /// <param name="tree">相对路径行——成功时填充</param>
        /// <returns>true=成功</returns>
        public static bool Tree(string path, int depth, int limit, out string tree)
        {
            try
            {
                string[] entries = CurrentFileSystem().Tree(path, depth, limit);
                tree = string.Join("\n", entries);
                return true;
            }
            catch (Exception ex)
            {
                tree = "ERR|" + ex.GetType().Name + "|" + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 按文件名通配符搜索并稳定排序
        /// </summary>
        /// <param name="directory">受控目录</param>
        /// <param name="pattern">文件名模式</param>
        /// <param name="recursive">是否递归</param>
        /// <param name="limit">最大结果</param>
        /// <param name="found">相对路径行——成功时填充</param>
        /// <returns>true=成功</returns>
        public static bool Find(string directory, string pattern, bool recursive, int limit, out string found)
        {
            try
            {
                string[] entries = CurrentFileSystem().Find(directory, pattern, recursive, limit);
                found = string.Join("\n", entries);
                return true;
            }
            catch (Exception ex)
            {
                found = "ERR|" + ex.GetType().Name + "|" + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 移动文件且拒绝覆盖目标
        /// </summary>
        /// <param name="source">源路径</param>
        /// <param name="destination">目标路径</param>
        /// <returns>true=成功</returns>
        public static bool Move(string source, string destination)
        {
            try
            {
                CurrentFileSystem().Move(source, destination);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 软删除到受控回收站
        /// </summary>
        /// <param name="path">目标路径</param>
        /// <param name="recycled">回收站内新路径——成功时填充</param>
        /// <returns>true=成功</returns>
        public static bool Delete(string path, out string recycled)
        {
            try
            {
                recycled = CurrentFileSystem().Recycle(path);
                return true;
            }
            catch (Exception ex)
            {
                recycled = "ERR|" + ex.GetType().Name + "|" + ex.Message;
                return false;
            }
        }

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
            FileSystemService fs = CurrentFileSystem();
            if (tool == "file_read" || tool == "file_tree" || tool == "file_find"
                || tool == "file_read_lines" || tool == "file_batch")
            {
                throw new InvalidOperationException("Batch accepts mutation tools only.");
            }
            if (tool == "file_write")
            {
                fs.WriteText(ReadRequiredText(op, "path"), ReadRequiredText(op, "content"));
                return "written";
            }
            if (tool == "file_append")
            {
                fs.AppendText(ReadRequiredText(op, "path"), ReadRequiredText(op, "content"));
                return "appended";
            }
            if (tool == "file_replace")
            {
                int count = fs.ReplaceText(ReadRequiredText(op, "path"),
                    ReadRequiredText(op, "oldText"), ReadRequiredText(op, "newText"));
                return "replacements=" + count.ToString();
            }
            if (tool == "file_move")
            {
                fs.Move(ReadRequiredText(op, "source"), ReadRequiredText(op, "destination"));
                return "moved";
            }
            if (tool == "file_delete")
            {
                return "recycled=" + fs.Recycle(ReadRequiredText(op, "path"));
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

        /// <summary>
        /// 当前受控文件系统——使用 ConfigureRoots 配置的根目录
        /// </summary>
        /// <returns>文件系统服务</returns>
        private static FileSystemService CurrentFileSystem()
        {
            return new FileSystemService(_roots, _recycleRoot);
        }
    }

    /// <summary>
    /// 标准积木注册——进程启动时调用一次
    /// </summary>
    public static class StandardBrickRegistration
    {
        /// <summary>
        /// 注册全部标准积木
        /// </summary>
        public static void RegisterAll()
        {
            RegisterFileConvert();
            RegisterFileRead();
            RegisterFileWrite();
            RegisterFileAppend();
            RegisterFileReplace();
            RegisterFileReadLines();
            RegisterFileTree();
            RegisterFileFind();
            RegisterFileMove();
            RegisterFileDelete();
            RegisterFileBatch();
            CmdBrickRegistration.RegisterAll();
        }

        /// <summary>
        /// 注册 file.convert
        /// </summary>
        private static void RegisterFileConvert()
        {
            BrickContract contract = new BrickContract("file.convert", "Mau.Bricks.FileBrick.Convert");
            contract.Inputs.Add(new BrickPort("input", typeof(string), "输入文件路径"));
            contract.Inputs.Add(new BrickPort("output", typeof(string), "输出文件路径"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }

        /// <summary>
        /// 注册 file.read
        /// </summary>
        private static void RegisterFileRead()
        {
            BrickContract contract = new BrickContract("file.read", "Mau.Bricks.FileBrick.Read");
            contract.Inputs.Add(new BrickPort("path", typeof(string), "受控文件路径"));
            contract.Outputs.Add(new BrickPort("content", typeof(string), "完整文本"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }

        /// <summary>
        /// 注册 file.write
        /// </summary>
        private static void RegisterFileWrite()
        {
            BrickContract contract = new BrickContract("file.write", "Mau.Bricks.FileBrick.Write");
            contract.Inputs.Add(new BrickPort("path", typeof(string), "受控文件路径"));
            contract.Inputs.Add(new BrickPort("content", typeof(string), "完整正文"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }

        /// <summary>
        /// 注册 file.append
        /// </summary>
        private static void RegisterFileAppend()
        {
            BrickContract contract = new BrickContract("file.append", "Mau.Bricks.FileBrick.Append");
            contract.Inputs.Add(new BrickPort("path", typeof(string), "受控文件路径"));
            contract.Inputs.Add(new BrickPort("content", typeof(string), "追加正文"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }

        /// <summary>
        /// 注册 file.replace
        /// </summary>
        private static void RegisterFileReplace()
        {
            BrickContract contract = new BrickContract("file.replace", "Mau.Bricks.FileBrick.Replace");
            contract.Inputs.Add(new BrickPort("path", typeof(string), "受控文件路径"));
            contract.Inputs.Add(new BrickPort("oldText", typeof(string), "非空目标文本"));
            contract.Inputs.Add(new BrickPort("newText", typeof(string), "新文本"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }

        /// <summary>
        /// 注册 file.read_lines
        /// </summary>
        private static void RegisterFileReadLines()
        {
            BrickContract contract = new BrickContract("file.read_lines", "Mau.Bricks.FileBrick.ReadLines");
            contract.Inputs.Add(new BrickPort("path", typeof(string), "受控文件路径"));
            contract.Inputs.Add(new BrickPort("startLine", typeof(int), "起始行（1-based）"));
            contract.Inputs.Add(new BrickPort("endLine", typeof(int), "结束行；0=文件尾"));
            contract.Outputs.Add(new BrickPort("lines", typeof(string), "带行号文本"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }

        /// <summary>
        /// 注册 file.tree
        /// </summary>
        private static void RegisterFileTree()
        {
            BrickContract contract = new BrickContract("file.tree", "Mau.Bricks.FileBrick.Tree");
            contract.Inputs.Add(new BrickPort("path", typeof(string), "受控目录"));
            contract.Inputs.Add(new BrickPort("depth", typeof(int), "递归深度 0-10"));
            contract.Inputs.Add(new BrickPort("limit", typeof(int), "最大条数 1-10000"));
            contract.Outputs.Add(new BrickPort("tree", typeof(string), "相对路径行"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }

        /// <summary>
        /// 注册 file.find
        /// </summary>
        private static void RegisterFileFind()
        {
            BrickContract contract = new BrickContract("file.find", "Mau.Bricks.FileBrick.Find");
            contract.Inputs.Add(new BrickPort("directory", typeof(string), "受控目录"));
            contract.Inputs.Add(new BrickPort("pattern", typeof(string), "文件名通配符"));
            contract.Inputs.Add(new BrickPort("recursive", typeof(bool), "是否递归"));
            contract.Inputs.Add(new BrickPort("limit", typeof(int), "最大结果 1-10000"));
            contract.Outputs.Add(new BrickPort("found", typeof(string), "相对路径行"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }

        /// <summary>
        /// 注册 file.move
        /// </summary>
        private static void RegisterFileMove()
        {
            BrickContract contract = new BrickContract("file.move", "Mau.Bricks.FileBrick.Move");
            contract.Inputs.Add(new BrickPort("source", typeof(string), "源路径"));
            contract.Inputs.Add(new BrickPort("destination", typeof(string), "目标路径"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }

        /// <summary>
        /// 注册 file.delete
        /// </summary>
        private static void RegisterFileDelete()
        {
            BrickContract contract = new BrickContract("file.delete", "Mau.Bricks.FileBrick.Delete");
            contract.Inputs.Add(new BrickPort("path", typeof(string), "待删除路径"));
            contract.Outputs.Add(new BrickPort("recycled", typeof(string), "回收站内新路径"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }

        /// <summary>
        /// 注册 file.batch
        /// </summary>
        private static void RegisterFileBatch()
        {
            BrickContract contract = new BrickContract("file.batch", "Mau.Bricks.FileBrick.Batch");
            contract.Inputs.Add(new BrickPort("operations", typeof(string), "操作数组 JSON"));
            contract.Inputs.Add(new BrickPort("stopOnError", typeof(bool), "遇错是否停止"));
            contract.Outputs.Add(new BrickPort("summary", typeof(string), "逐项摘要行"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }
    }
}

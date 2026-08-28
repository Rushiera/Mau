using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Mau.Runtime
{
    /// <summary>
    /// 受控文件系统服务——file.* 积木的底层安全边界。
    /// 将文件能力限制在显式根目录集合内，所有操作拒绝穿过现有重解析点，
    /// 避免符号链接绕过字符串前缀检查。
    /// </summary>
    public sealed class FileSystemService
    {
        /// <summary>
        /// 规范化允许根目录
        /// </summary>
        private readonly string[] _roots;

        /// <summary>
        /// 软删除回收目录
        /// </summary>
        private readonly string _recycleRoot;

        /// <summary>
        /// 串行保护变更文件的复合操作
        /// </summary>
        private readonly object _writeGate;

        /// <summary>
        /// 只读根标志——与 _roots 对齐；true 的根拒绝写操作（writable=false 语义）
        /// </summary>
        private readonly bool[] _readOnly;

        /// <summary>
        /// 根标识数组——与 _roots 对齐（命名空间寻址 id:relative——P8.5b）
        /// </summary>
        private readonly string[] _rootIds;

        /// <summary>
        /// 建立明确根目录和位于其中的回收站
        /// </summary>
        /// <param name="roots">允许根目录</param>
        /// <param name="recycleRoot">回收目录</param>
        public FileSystemService(string[] roots, string recycleRoot)
        {
            if (roots == null || roots.Length == 0)
            {
                throw new ArgumentException("At least one filesystem root is required.", "roots");
            }
            _roots = new string[roots.Length];
            _readOnly = new bool[roots.Length];
            _rootIds = new string[roots.Length];
            for (int i = 0; i < roots.Length; i = i + 1)
            {
                if (string.IsNullOrWhiteSpace(roots[i]))
                {
                    throw new ArgumentException("Filesystem root is empty.", "roots");
                }
                _roots[i] = PathBoundary.NormalizeRoot(roots[i]);
                _rootIds[i] = "root" + i.ToString();
            }
            _recycleRoot = Resolve(recycleRoot, true);
            Directory.CreateDirectory(_recycleRoot);
            _writeGate = new object();
        }

        /// <summary>
        /// 读取 UTF-8 文本
        /// </summary>
        /// <param name="path">受控路径</param>
        /// <returns>完整文本</returns>
        public string ReadText(string path)
        {
            string resolved = Resolve(path, false);
            return File.ReadAllText(resolved, Encoding.UTF8);
        }

        /// <summary>
        /// 原子覆写 UTF-8 无 BOM 文本
        /// </summary>
        /// <param name="path">受控路径</param>
        /// <param name="content">完整正文</param>
        public void WriteText(string path, string content)
        {
            string resolved = Resolve(path, true);
            lock (_writeGate)
            {
                WriteAtomic(resolved, SafeText(content));
            }
        }

        /// <summary>
        /// 追加 UTF-8 文本并创建父目录
        /// </summary>
        /// <param name="path">受控路径</param>
        /// <param name="content">追加正文</param>
        public void AppendText(string path, string content)
        {
            string resolved = Resolve(path, true);
            lock (_writeGate)
            {
                EnsureParentDirectory(resolved);
                File.AppendAllText(resolved, SafeText(content), new UTF8Encoding(false));
            }
        }

        /// <summary>
        /// 替换全部精确文本并原子写回
        /// </summary>
        /// <param name="path">受控路径</param>
        /// <param name="oldText">非空目标</param>
        /// <param name="newText">新文本</param>
        /// <returns>替换数量</returns>
        public int ReplaceText(string path, string oldText, string newText)
        {
            if (string.IsNullOrEmpty(oldText))
            {
                throw new ArgumentException("Replacement target is empty.", "oldText");
            }
            string resolved = Resolve(path, true);
            lock (_writeGate)
            {
                string content = File.ReadAllText(resolved, Encoding.UTF8);
                int count = CountOccurrences(content, oldText);
                if (count > 0)
                {
                    WriteAtomic(resolved, content.Replace(oldText, SafeText(newText), StringComparison.Ordinal));
                }
                return count;
            }
        }

        /// <summary>
        /// 按一基闭区间读取文本行
        /// </summary>
        /// <param name="path">受控路径</param>
        /// <param name="startLine">起始行</param>
        /// <param name="endLine">结束行；零表示文件尾</param>
        /// <returns>带行号文本</returns>
        public string ReadLines(string path, int startLine, int endLine)
        {
            if (startLine < 1 || endLine < 0)
            {
                throw new ArgumentOutOfRangeException("startLine");
            }
            string[] lines = File.ReadAllLines(Resolve(path, false), Encoding.UTF8);
            int end = endLine;
            if (end == 0 || end > lines.Length)
            {
                end = lines.Length;
            }
            if (startLine > end && lines.Length > 0)
            {
                throw new ArgumentOutOfRangeException("startLine");
            }
            StringBuilder builder = new StringBuilder();
            for (int line = startLine; line <= end; line = line + 1)
            {
                if (builder.Length > 0)
                {
                    builder.Append('\n');
                }
                builder.Append(line.ToString());
                builder.Append(": ");
                builder.Append(lines[line - 1]);
            }
            return builder.ToString();
        }

        /// <summary>
        /// 按深度和数量上限列出稳定排序目录树
        /// </summary>
        /// <param name="path">受控目录</param>
        /// <param name="depth">零到十层</param>
        /// <param name="limit">最大条数</param>
        /// <returns>相对目录路径</returns>
        public string[] Tree(string path, int depth, int limit)
        {
            if (depth < 0 || depth > 10 || limit < 1 || limit > 10000)
            {
                throw new ArgumentOutOfRangeException("depth");
            }
            string root = Resolve(path, false);
            if (!Directory.Exists(root))
            {
                throw new DirectoryNotFoundException("Tree root was not found.");
            }
            List<string> output = new List<string>();
            AppendTree(root, root, depth, limit, output);
            return output.ToArray();
        }

        /// <summary>
        /// 按文件名通配符搜索并稳定排序
        /// </summary>
        /// <param name="directory">受控目录</param>
        /// <param name="pattern">文件名模式</param>
        /// <param name="recursive">是否递归</param>
        /// <param name="limit">最大结果</param>
        /// <returns>相对搜索根的路径</returns>
        public string[] Find(string directory, string pattern, bool recursive, int limit)
        {
            if (string.IsNullOrWhiteSpace(pattern) || limit < 1 || limit > 10000)
            {
                throw new ArgumentException("Find parameters are invalid.", "pattern");
            }
            string root = Resolve(directory, false);
            List<string> files = new List<string>();
            AppendFind(root, root, pattern, recursive, limit, files);
            files.Sort(StringComparer.OrdinalIgnoreCase);
            string[] result = new string[files.Count];
            for (int i = 0; i < files.Count; i = i + 1)
            {
                result[i] = Path.GetRelativePath(root, files[i]);
            }
            return result;
        }

        /// <summary>
        /// 移动文件且拒绝覆盖目标
        /// </summary>
        /// <param name="source">源路径</param>
        /// <param name="destination">目标路径</param>
        public void Move(string source, string destination)
        {
            string resolvedSource = Resolve(source, true);
            string resolvedDestination = Resolve(destination, true);
            lock (_writeGate)
            {
                if (File.Exists(resolvedDestination) || Directory.Exists(resolvedDestination))
                {
                    throw new IOException("Move destination already exists.");
                }
                EnsureParentDirectory(resolvedDestination);
                File.Move(resolvedSource, resolvedDestination, false);
            }
        }

        /// <summary>
        /// 将文件或空目录移动到受控回收站
        /// </summary>
        /// <param name="path">目标路径</param>
        /// <returns>回收站内新路径</returns>
        public string Recycle(string path)
        {
            string resolved = Resolve(path, true);
            lock (_writeGate)
            {
                string leaf = Path.GetFileName(resolved);
                string target = Path.Combine(_recycleRoot,
                    DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ") + "-"
                    + Guid.NewGuid().ToString("N") + "-" + leaf);
                if (File.Exists(resolved))
                {
                    File.Move(resolved, target, false);
                    return target;
                }
                if (Directory.Exists(resolved))
                {
                    if (Directory.GetFileSystemEntries(resolved).Length > 0)
                    {
                        throw new IOException("Only empty directories can be recycled.");
                    }
                    Directory.Move(resolved, target);
                    return target;
                }
                throw new FileNotFoundException("Recycle target was not found.", resolved);
            }
        }

        /// <summary>
        /// 解析路径并验证归属和重解析点边界
        /// </summary>
        /// <param name="path">用户路径</param>
        /// <param name="forWrite">是否为写入</param>
        /// <returns>规范绝对路径</returns>
        public string Resolve(string path, bool forWrite)
{
            if (string.IsNullOrWhiteSpace(path))
            {
                // v3 机制纯净——空路径是调用方错误（参数校验，不做产品级回落；LLM 适配归自举层语料）
                throw new ArgumentException("Path is empty.", "path");
            }
            // P8.5b 命名空间寻址——id:relative（受控根 id 前缀；LLM 工具路径语义——同 WorkspaceConfig.ResolveInjectFile；Windows 盘符如 C:\ 前缀不匹配则跳过）
            int nsSep = path.IndexOf(':');
            if (nsSep > 0)
            {
                string nsId = path.Substring(0, nsSep);
                string nsRel = path.Substring(nsSep + 1);
                for (int i = 0; i < _roots.Length; i = i + 1)
                {
                    if (string.Equals(_rootIds[i], nsId, StringComparison.Ordinal))
                    {
                        path = Path.Combine(_roots[i], nsRel);
                        break;
                    }
                }
            }
            string resolved;
            if (Path.IsPathFullyQualified(path))
            {
                resolved = Path.GetFullPath(path);
            }
            else
            {
                resolved = Path.GetFullPath(Path.Combine(_roots[0], path));
            }
            string? owningRoot = FindOwningRoot(resolved);
            if (owningRoot == null)
            {
                throw new UnauthorizedAccessException("Path is outside allowed roots.");
            }
            // P8.5 只读根校验——forWrite 且归属根只读 → 拒绝（ccbp 等知识根默认只读）
            if (forWrite && IsReadOnlyRoot(owningRoot))
            {
                throw new UnauthorizedAccessException("Path is in a read-only root: " + owningRoot);
            }
            return PathBoundary.ResolveOwnedPath(owningRoot, resolved);
        }        /// <summary>
        /// 显式遍历搜索目录，并拒绝进入或返回重解析点。
        /// </summary>
        /// <param name="root">搜索相对根</param>
        /// <param name="current">当前目录</param>
        /// <param name="pattern">文件名模式</param>
        /// <param name="recursive">是否递归</param>
        /// <param name="limit">结果上限</param>
        /// <param name="output">绝对文件路径</param>
        private void AppendFind(string root, string current, string pattern,
            bool recursive, int limit, List<string> output)
        {
            if (output.Count >= limit)
            {
                return;
            }
            // [段1] 当前目录文件按稳定顺序加入，文件链接本身也不暴露
            string[] files = Directory.GetFiles(current, pattern, SearchOption.TopDirectoryOnly);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < files.Length && output.Count < limit; i = i + 1)
            {
                if (!PathBoundary.IsReparsePoint(files[i]))
                {
                    output.Add(files[i]);
                }
            }
            if (!recursive || output.Count >= limit)
            {
                return;
            }
            // [段2] 逐目录验证后递归，避免 AllDirectories 隐式穿过链接
            string[] directories = Directory.GetDirectories(current, "*", SearchOption.TopDirectoryOnly);
            Array.Sort(directories, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < directories.Length && output.Count < limit; i = i + 1)
            {
                if (PathBoundary.IsReparsePoint(directories[i]))
                {
                    continue;
                }
                PathBoundary.ResolveOwnedPath(root, directories[i]);
                AppendFind(root, directories[i], pattern, true, limit, output);
            }
        }

        /// <summary>
        /// 递归追加目录树
        /// </summary>
        /// <param name="root">输出相对根</param>
        /// <param name="current">当前目录</param>
        /// <param name="depth">剩余深度</param>
        /// <param name="limit">条数上限</param>
        /// <param name="output">输出列表</param>
        private void AppendTree(string root, string current, int depth, int limit, List<string> output)
        {
            if (output.Count >= limit)
            {
                return;
            }
            string[] entries = Directory.GetFileSystemEntries(current);
            Array.Sort(entries, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < entries.Length && output.Count < limit; i = i + 1)
            {
                output.Add(Path.GetRelativePath(root, entries[i]));
                if (depth > 0 && Directory.Exists(entries[i])
                    && !PathBoundary.IsReparsePoint(entries[i]))
                {
                    AppendTree(root, entries[i], depth - 1, limit, output);
                }
            }
        }

        /// <summary>
        /// 寻找包含目标的最长允许根
        /// </summary>
        /// <param name="path">绝对路径</param>
        /// <returns>允许根或 null</returns>
        private string? FindOwningRoot(string path)
        {
            string? owner = null;
            for (int i = 0; i < _roots.Length; i = i + 1)
            {
                string root = _roots[i];
                if (string.Equals(path, root, StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith(root + Path.DirectorySeparatorChar.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    if (owner == null || root.Length > owner.Length)
                    {
                        owner = root;
                    }
                }
            }
            return owner;
        }

        /// <summary>
        /// 统计非重叠精确子串数量
        /// </summary>
        /// <param name="content">正文</param>
        /// <param name="target">目标</param>
        /// <returns>数量</returns>
        private int CountOccurrences(string content, string target)
        {
            int count = 0;
            int index = 0;
            while (index <= content.Length - target.Length)
            {
                int found = content.IndexOf(target, index, StringComparison.Ordinal);
                if (found < 0)
                {
                    return count;
                }
                count = count + 1;
                index = found + target.Length;
            }
            return count;
        }

        /// <summary>
        /// 同目录临时文件原子替换
        /// </summary>
        /// <param name="path">目标路径</param>
        /// <param name="content">正文</param>
        private void WriteAtomic(string path, string content)
{
            EnsureParentDirectory(path);
            // 原子写——统一实现 ConfigStore.AtomicWrite（审查修复轮 2026-08-11 决策3）
            ConfigStore.AtomicWrite(path, content);
        }
        /// <summary>
        /// 创建目标父目录
        /// </summary>
        /// <param name="path">文件路径</param>
        private void EnsureParentDirectory(string path)
        {
            string? directory = Path.GetDirectoryName(path);
            if (directory == null)
            {
                throw new InvalidOperationException("Parent directory is unavailable.");
            }
            Directory.CreateDirectory(directory);
        }

        /// <summary>
        /// 把可空文本规范为空字符串
        /// </summary>
        /// <param name="value">输入</param>
        /// <returns>非空文本</returns>
        private static string SafeText(string? value)
        {
            if (value == null)
            {
                return "";
            }
            return value;
        }

        /// <summary>
        /// 建立受控根条目版构造——含只读标志（writable=false 根拒绝写操作；design-ch4-workspace §三）
        /// </summary>
        /// <param name="rootEntries">受控根条目（id + 路径 + 可写标志）</param>
        /// <param name="recycleRoot">回收目录</param>
        public FileSystemService(WorkspaceConfig.RootEntry[] rootEntries, string recycleRoot)
        {
            if (rootEntries == null || rootEntries.Length == 0)
            {
                throw new ArgumentException("At least one filesystem root is required.", "rootEntries");
            }
            _roots = new string[rootEntries.Length];
            _readOnly = new bool[rootEntries.Length];
            _rootIds = new string[rootEntries.Length];
            for (int i = 0; i < rootEntries.Length; i = i + 1)
            {
                WorkspaceConfig.RootEntry entry = rootEntries[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.Path))
                {
                    throw new ArgumentException("Filesystem root is empty.", "rootEntries");
                }
                _roots[i] = PathBoundary.NormalizeRoot(entry.Path);
                _readOnly[i] = !entry.Writable;
                string entryId = entry.Id;
                if (entryId == null || entryId.Length == 0)
                {
                    entryId = "root" + i.ToString();
                }
                _rootIds[i] = entryId;
            }
            _recycleRoot = Resolve(recycleRoot, true);
            Directory.CreateDirectory(_recycleRoot);
            _writeGate = new object();
        }

        /// <summary>
        /// 查询根是否只读——按归属根路径匹配（Resolve forWrite 校验用）
        /// </summary>
        /// <param name="rootPath">归属根路径</param>
        /// <returns>true=只读（拒绝写操作）</returns>
        private bool IsReadOnlyRoot(string rootPath)
        {
            for (int i = 0; i < _roots.Length; i = i + 1)
            {
                if (string.Equals(_roots[i], rootPath, StringComparison.OrdinalIgnoreCase))
                {
                    return _readOnly[i];
                }
            }
            return false;
        }
    }

    /// <summary>
    /// 路径边界证明——根归属 + 重解析点防护
    /// </summary>
    internal static class PathBoundary
    {
        /// <summary>
        /// 规范并创建受控根，同时拒绝根本身为重解析点。
        /// </summary>
        /// <param name="root">受控根路径</param>
        /// <returns>不带多余末尾分隔符的绝对路径</returns>
        internal static string NormalizeRoot(string root)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                throw new ArgumentException("Controlled root is empty.", "root");
            }
            string normalized = TrimEndingSeparator(Path.GetFullPath(root));
            Directory.CreateDirectory(normalized);
            if (IsReparsePoint(normalized))
            {
                throw new UnauthorizedAccessException("Controlled root cannot be a reparse point.");
            }
            return normalized;
        }

        /// <summary>
        /// 证明目标仍在受控根内且现存路径没有穿过重解析点。
        /// </summary>
        /// <param name="root">已规范的受控根</param>
        /// <param name="path">目标绝对或相对路径</param>
        /// <returns>规范目标绝对路径</returns>
        internal static string ResolveOwnedPath(string root, string path)
        {
            string normalized = Path.GetFullPath(path);
            string prefix = root + Path.DirectorySeparatorChar.ToString();
            if (!string.Equals(normalized, root, StringComparison.OrdinalIgnoreCase)
                && !normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException("Controlled path escaped its owned root.");
            }
            RejectExistingReparsePoints(root, normalized);
            return normalized;
        }

        /// <summary>
        /// 判断现存文件或目录是否为重解析点。
        /// </summary>
        /// <param name="path">现存路径</param>
        /// <returns>是否为重解析点</returns>
        internal static bool IsReparsePoint(string path)
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
        }

        /// <summary>
        /// 拒绝从受控根到目标的任一现存重解析点。
        /// </summary>
        /// <param name="root">受控根</param>
        /// <param name="path">根内目标</param>
        private static void RejectExistingReparsePoints(string root, string path)
        {
            if (IsReparsePoint(root))
            {
                throw new UnauthorizedAccessException("Controlled root cannot be a reparse point.");
            }
            string relative = Path.GetRelativePath(root, path);
            string[] segments = relative.Split(new char[]
            {
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar
            }, StringSplitOptions.RemoveEmptyEntries);
            string current = root;
            for (int i = 0; i < segments.Length; i = i + 1)
            {
                current = Path.Combine(current, segments[i]);
                if ((File.Exists(current) || Directory.Exists(current)) && IsReparsePoint(current))
                {
                    throw new UnauthorizedAccessException("Controlled path crosses a reparse point.");
                }
            }
        }

        /// <summary>
        /// 移除普通目录末尾分隔符但保留卷根。
        /// </summary>
        /// <param name="path">绝对目录</param>
        /// <returns>规范目录</returns>
        private static string TrimEndingSeparator(string path)
        {
            string? volumeRoot = Path.GetPathRoot(path);
            if (volumeRoot != null && string.Equals(path, volumeRoot, StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
// #MAU_CHECKSUM:SHA256:B4F53316ED88B6C245CC66C358B19B2AE3094E131A267FE42B678830DB35C2C3

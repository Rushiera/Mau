using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Mau.Cli
{
    /// <summary>
    /// Mau.Cli 公共支撑（v3 最小面）——仓库根探测 / SHA256 / 输出分级 / 尾部截断 / 文件锁探测。
    /// v2 的积木谱语料构造（BuildMinimalCorpus*）随积木体系退役（P5 重生后再立）。
    /// </summary>
    public static class CliSupport
    {
        /// <summary>
        /// 查找 workspace 根——含 Mau.sln 的目录；当前目录向上优先，程序集位置兜底
        /// </summary>
        /// <returns>workspace 根或空</returns>
        public static string? FindWorkspaceRoot()
        {
            return FindRepoRoot(Directory.GetCurrentDirectory(), new string[] { "Mau.sln" }, AppContext.BaseDirectory);
        }

        /// <summary>
        /// 统一仓库根探针——从起始目录向上查找含任一标记的目录；fallbackDir 向上兜底
        /// </summary>
        /// <param name="startDir">起始目录（优先查找链）</param>
        /// <param name="markers">标记集合——文件或目录名（如 Mau.sln）</param>
        /// <param name="fallbackDir">兜底目录（如程序集位置）；空=不兜底</param>
        /// <returns>仓库根或空</returns>
        public static string? FindRepoRoot(string startDir, string[] markers, string? fallbackDir = null)
        {
            string? found = Probe(startDir, markers);
            if (found != null)
            {
                return found;
            }
            if (fallbackDir != null && fallbackDir.Length > 0)
            {
                return Probe(fallbackDir, markers);
            }
            return null;
        }

        /// <summary>
        /// 向上探测标记
        /// </summary>
        /// <param name="from">起始目录</param>
        /// <param name="markers">标记集合</param>
        /// <returns>命中目录或空</returns>
        private static string? Probe(string? from, string[] markers)
        {
            string? dir = from == null ? null : new DirectoryInfo(from).FullName;
            while (dir != null)
            {
                for (int i = 0; i < markers.Length; i = i + 1)
                {
                    string candidate = Path.Combine(dir, markers[i]);
                    if (File.Exists(candidate) || Directory.Exists(candidate))
                    {
                        return dir;
                    }
                }
                dir = Directory.GetParent(dir)?.FullName;
            }
            return null;
        }

        /// <summary>
        /// 计算字符串的 SHA256 哈希——UTF-8 字节转 64 位十六进制大写
        /// </summary>
        /// <param name="text">输入文本</param>
        /// <returns>64 位十六进制哈希（大写）</returns>
        public static string ComputeSha256(string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            byte[] hash = SHA256.HashData(bytes);
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < hash.Length; i++)
            {
                sb.Append(hash[i].ToString("X2"));
            }
            return sb.ToString();
        }

        /// <summary>
        /// 详细输出开关——--verbose 全局生效
        /// </summary>
        public static bool Verbose = false;

        /// <summary>
        /// 默认流——关键节点/结果/错误/摘要
        /// </summary>
        /// <param name="message">输出行</param>
        public static void Info(string message)
        {
            Console.WriteLine(message);
        }

        /// <summary>
        /// 详细流——过程细节（--verbose 才输出）
        /// </summary>
        /// <param name="message">输出行</param>
        public static void Detail(string message)
        {
            if (Verbose)
            {
                Console.WriteLine(message);
            }
        }

        /// <summary>
        /// 解析 verbose 开关——命令参数含 --verbose 时启用
        /// </summary>
        /// <param name="args">命令参数</param>
        public static void ParseVerbose(string[] args)
        {
            for (int i = 0; i < args.Length; i = i + 1)
            {
                if (args[i] == "--verbose")
                {
                    Verbose = true;
                    return;
                }
            }
        }

        /// <summary>
        /// 尾部保留截断——从尾部保留最后 N 行（错误/结论在尾部）
        /// </summary>
        /// <param name="text">原始文本</param>
        /// <param name="maxLines">保留行数</param>
        /// <returns>截断后文本——空输入返回（无输出）</returns>
        public static string TailLines(string text, int maxLines)
        {
            if (text == null || text.Trim().Length == 0)
            {
                return "（无输出）";
            }
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            int start = lines.Length > maxLines ? lines.Length - maxLines : 0;
            StringBuilder sb = new StringBuilder();
            for (int i = start; i < lines.Length; i = i + 1)
            {
                sb.AppendLine(lines[i]);
            }
            return sb.ToString();
        }

        /// <summary>
        /// 探测文件是否被进程占用——以写方式独占打开（成功=未锁；IOException=被锁）
        /// </summary>
        /// <param name="paths">待探测路径</param>
        /// <returns>第一个被锁路径；全部未锁返回空串</returns>
        public static string FindLockedFile(string[] paths)
        {
            for (int i = 0; i < paths.Length; i = i + 1)
            {
                if (!File.Exists(paths[i]))
                {
                    continue;
                }
                try
                {
                    using (FileStream fs = new FileStream(paths[i], FileMode.Open, FileAccess.Write, FileShare.None))
                    {
                        // 探测成功——未锁
                    }
                }
                catch (IOException)
                {
                    return paths[i];
                }
                catch (UnauthorizedAccessException)
                {
                    return paths[i];
                }
            }
            return "";
        }

        /// <summary>
        /// 取子参数数组——args[1..]（命令名之后；统一分发样板）
        /// </summary>
        /// <param name="args">完整命令行参数</param>
        /// <returns>命令名之后的子数组（无则空数组）</returns>
        public static string[] Tail(string[] args)
        {
            if (args == null || args.Length <= 1)
            {
                return Array.Empty<string>();
            }
            string[] tail = new string[args.Length - 1];
            for (int i = 1; i < args.Length; i = i + 1)
            {
                tail[i - 1] = args[i];
            }
            return tail;
        }
    }
}

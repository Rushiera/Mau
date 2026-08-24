using System;
using System.IO;
using System.Text;

namespace Mau.Runtime
{
    /// <summary>
    /// Secret 专用明文原子替换器。它允许提交期间存在一个临时文件，
    /// 但不保留上一版、坏原文或其他可恢复的历史明文。
    /// </summary>
    internal static class CH_SecretTextFile
    {
        /// <summary>Secret 临时提交文件后缀。</summary>
        private const string CH_SecretTextFile_TemporarySuffix = ".secret-tmp";

        /// <summary>读取当前 Secret 正文并清理旧实现留下的历史明文。</summary>
        /// <param name="path">Secret 正式文件路径</param>
        /// <returns>当前正文</returns>
        public static string Read(string path)
        {
            string fullPath = Path.GetFullPath(path);
            DeleteHistory(fullPath);
            return File.ReadAllText(fullPath, Encoding.UTF8);
        }

        /// <summary>以同目录临时文件耐久写入并替换 Secret 正式文件。</summary>
        /// <param name="path">Secret 正式文件路径</param>
        /// <param name="content">Secret 明文正文</param>
        public static void Write(string path, string content)
        {
            // [段1] 提交前先清除旧版本可能遗留的历史明文
            string fullPath = Path.GetFullPath(path);
            string? directory = Path.GetDirectoryName(fullPath);
            if (directory == null)
            {
                throw new InvalidOperationException(
                    "Secret file directory is unavailable.");
            }
            Directory.CreateDirectory(directory);
            DeleteHistory(fullPath);

            // [段2] 新正文只经过一次同目录耐久临时提交
            string temporary = fullPath + "."
                + Guid.NewGuid().ToString("N")
                + CH_SecretTextFile_TemporarySuffix;
            try
            {
                byte[] bytes = new UTF8Encoding(false).GetBytes(content);
                using (FileStream stream = new FileStream(temporary,
                    FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    4096, FileOptions.WriteThrough))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                File.Move(temporary, fullPath, true);
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
            DeleteHistory(fullPath);
        }

        /// <summary>删除通用原子文件旧实现可能留下的 Secret 历史。</summary>
        /// <param name="path">Secret 正式文件路径</param>
        public static void DeleteHistory(string path)
        {
            string fullPath = Path.GetFullPath(path);
            string? directory = Path.GetDirectoryName(fullPath);
            string fileName = Path.GetFileName(fullPath);
            if (directory == null || !Directory.Exists(directory))
            {
                return;
            }
            string[] files = Directory.GetFiles(directory, fileName + ".*",
                SearchOption.TopDirectoryOnly);
            for (int i = 0; i < files.Length; i = i + 1)
            {
                string candidateName = Path.GetFileName(files[i]);
                if (IsHistoryName(fileName, candidateName))
                {
                    File.Delete(files[i]);
                }
            }
        }

        /// <summary>判断文件名是否属于已知 Secret 历史或临时提交。</summary>
        /// <param name="fileName">正式文件名</param>
        /// <param name="candidateName">候选文件名</param>
        /// <returns>是否应清理</returns>
        private static bool IsHistoryName(string fileName,
            string candidateName)
        {
            string suffix = candidateName.Substring(fileName.Length);
            if (string.Equals(suffix, CH_AtomicTextFile.LastSuffix,
                StringComparison.Ordinal))
            {
                return true;
            }
            if (suffix.StartsWith(".broken-", StringComparison.Ordinal))
            {
                return true;
            }
            if (suffix.EndsWith(".tmp", StringComparison.Ordinal)
                || suffix.EndsWith(".recover", StringComparison.Ordinal)
                || suffix.EndsWith(CH_SecretTextFile_TemporarySuffix,
                    StringComparison.Ordinal))
            {
                return true;
            }
            return false;
        }
    }
}

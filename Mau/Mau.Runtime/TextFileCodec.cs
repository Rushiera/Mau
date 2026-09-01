using System;
using System.IO;
using System.Text;

namespace Mau.Runtime
{
    /// <summary>
    /// 文本文件编码内建层——text-* v2 的 P1/P2 核心（design-ch4-text-tools.md §五）。
    /// 按文件类型自动处理编码/BOM/换行，模型零感知。文件类型契约：
    ///   .md/.ps1 → UTF-8 带 BOM；.bat → GBK(936) + CRLF；.cs/.mau/.json/.txt/未知 → UTF-8 无 BOM。
    /// 换行保真：写侧探测目标文件已有换行风格（\r\n / \n），新内容跟随；新文件按类型契约。
    /// </summary>
    public static class TextFileCodec
    {
        /// <summary>
        /// 静态初始化——注册代码页编码提供者（GBK(936) 依赖；.NET Core 共享框架内置 System.Text.Encoding.CodePages）
        /// </summary>
        static TextFileCodec()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        /// <summary>
        /// 编码契约表——按扩展名路由（未知类型按 .txt 兜底——莎拍板 2026-09-01）
        /// </summary>
        public static Encoding ProfileFor(string path)
        {
            if (path == null)
            {
                return new UTF8Encoding(false);
            }
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".md" || ext == ".ps1")
            {
                return new UTF8Encoding(true);
            }
            if (ext == ".bat")
            {
                return Encoding.GetEncoding(936);
            }
            return new UTF8Encoding(false);
        }

        /// <summary>
        /// 写入 BOM 判定——当前仅 .md/.ps1 带 BOM（GBK 无 BOM 语义）
        /// </summary>
        /// <param name="path">文件路径</param>
        /// <returns>true=写入 BOM</returns>
        public static bool ShouldWriteBom(string path)
        {
            if (path == null)
            {
                return false;
            }
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext == ".md" || ext == ".ps1";
        }

        /// <summary>
        /// 默认换行风格——.bat 强制 CRLF；其余探测（新文件默认 \n）
        /// </summary>
        /// <param name="path">文件路径</param>
        /// <returns>换行串</returns>
        public static string DefaultNewline(string path)
        {
            if (path != null && Path.GetExtension(path).ToLowerInvariant() == ".bat")
            {
                return "\r\n";
            }
            return "\n";
        }

        /// <summary>
        /// 从原始字节探测实际换行风格——首个 \r\n 出现早于独立 \n 判 CRLF，否则 LF
        /// </summary>
        /// <param name="raw">原始字节</param>
        /// <returns>\r\n 或 \n</returns>
        public static string DetectNewline(byte[] raw)
        {
            if (raw == null || raw.Length < 2)
            {
                return "\n";
            }
            int firstLf = -1;
            int firstCrLf = -1;
            for (int i = 0; i < raw.Length - 1; i = i + 1)
            {
                if (raw[i] == (byte)'\r' && raw[i + 1] == (byte)'\n')
                {
                    firstCrLf = i;
                    break;
                }
            }
            for (int i = 0; i < raw.Length; i = i + 1)
            {
                if (raw[i] == (byte)'\n')
                {
                    if (i > 0 && raw[i - 1] == (byte)'\r')
                    {
                        continue;
                    }
                    firstLf = i;
                    break;
                }
            }
            if (firstCrLf >= 0 && (firstLf < 0 || firstCrLf <= firstLf))
            {
                return "\r\n";
            }
            return "\n";
        }

        /// <summary>
        /// 统一文本换行风格——\r\n 与 \n 归一为目标风格
        /// </summary>
        /// <param name="content">原文</param>
        /// <param name="newline">目标换行</param>
        /// <returns>统一后文本</returns>
        public static string NormalizeNewlines(string content, string newline)
        {
            if (content == null)
            {
                return "";
            }
            string unified = content.Replace("\r\n", "\n");
            if (newline == "\r\n")
            {
                return unified.Replace("\n", "\r\n");
            }
            return unified;
        }

        /// <summary>
        /// 读取侧编码探测——BOM 优先（EF BB BF=UTF-8），无 BOM 按类型契约
        /// </summary>
        /// <param name="path">文件路径</param>
        /// <param name="raw">原始字节</param>
        /// <returns>解码用编码</returns>
        public static Encoding DetectReadEncoding(string path, byte[] raw)
        {
            if (raw != null && raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF)
            {
                return new UTF8Encoding(true);
            }
            return ProfileFor(path);
        }
    }
}

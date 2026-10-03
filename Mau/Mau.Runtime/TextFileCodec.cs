using System;
using System.IO;
using System.Text;

namespace Mau.Runtime
{
    /// <summary>文本文件编码内建层——text-* v2 的 P1/P2 核心（design-ch4-text-tools.md §四）。按类型自动处理编码/BOM/换行，模型零感知。两态语义：既有文件 → 探测实际 BOM/换行并保真；新建文件 → 类型契约。契约表（新建/工具产物）：.md/.ps1 → UTF-8 带 BOM + LF；.bat → GBK(936) + CRLF；.cs/.csproj/.mau/.mauproj（工程与语料族）→ UTF-8 带 BOM + CRLF；.html/.js/.css（Web 资产族）→ 无 BOM UTF-8 + CRLF；.txt/未知 → 无 BOM UTF-8 + LF。工程与语料族的写侧同源约束：mau bricks index --update（校验尾重算）与 text-* 走同一契约。</summary>
    public static class TextFileCodec
    {
        /// <summary>
        /// 静态初始化——注册代码页编码提供者（GBK(936) 依赖；.NET Core 共享框架内置 System.Text.Encoding.CodePages）
        /// </summary>
        static TextFileCodec()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        /// <summary>基础编码表——只定"用什么编码族"（.bat=GBK(936)，其余 UTF-8）；BOM 与换行由 DefaultBom/DefaultNewline（新建）或保真探测（既有文件）决定。</summary>
        public static Encoding ProfileFor(string path)
        {
            if (path != null && Path.GetExtension(path).ToLowerInvariant() == ".bat")
            {
                return Encoding.GetEncoding(936);
            }
            return new UTF8Encoding(false);
        }
        /// <summary>
        /// 工程与语料族判定——.cs/.csproj/.mau/.mauproj 统一契约：UTF-8 带 BOM + CRLF。
        /// 依据：工程区换行 CRLF 全量、BOM 多数；工具产物（积木校验尾重算/生成物）同族。
        /// </summary>
        /// <param name="path">文件路径</param>
        /// <returns>true=工程与语料族</returns>
        private static bool IsCodeFamily(string path)
        {
            if (path == null)
            {
                return false;
            }
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext == ".cs" || ext == ".csproj" || ext == ".mau" || ext == ".mauproj";
        }
        /// <summary>
        /// Web 资产族判定——.html/.js/.css 新建换行走 CRLF（仓库实测全量 CRLF）；BOM 不写（无约定，存量多数无 BOM）。
        /// </summary>
        /// <param name="path">文件路径</param>
        /// <returns>true=Web 资产族</returns>
        private static bool IsWebAssetFamily(string path)
        {
            if (path == null)
            {
                return false;
            }
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext == ".html" || ext == ".js" || ext == ".css";
        }

        /// <summary>新建文件默认换行——工程与语料族（.cs/.csproj/.mau/.mauproj）、Web 资产族（.html/.js/.css）与 .bat 走 CRLF；文档族（.md/.txt/未知）走 LF。</summary>
        /// <param name="path">文件路径</param>
        /// <returns>换行串</returns>
        public static string DefaultNewline(string path)
        {
            if (path == null)
            {
                return "\n";
            }
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (IsCodeFamily(path) || IsWebAssetFamily(path) || ext == ".bat")
            {
                return "\r\n";
            }
            return "\n";
        }
        /// <summary>
        /// 新建文件默认 BOM——文档族（.md/.ps1）与工程语料族（.cs/.csproj/.mau/.mauproj）写 BOM；.bat（GBK）/其余不写。
        /// 既有文件的 BOM 走保真探测（FileSystemService.ResolveWriteStyle），不套用本节。
        /// </summary>
        /// <param name="path">文件路径</param>
        /// <returns>true=写入 BOM</returns>
        public static bool DefaultBom(string path)
        {
            if (path == null)
            {
                return false;
            }
            if (IsCodeFamily(path))
            {
                return true;
            }
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext == ".md" || ext == ".ps1";
        }
        /// <summary>
        /// 写侧编码构造——UTF-8 家族按 BOM 开关构造；非 UTF-8 家族（.bat=GBK）原样返回（BOM 语义不适用）。
        /// </summary>
        /// <param name="path">文件路径</param>
        /// <param name="bom">是否写 BOM（既有文件=保真探测值 / 新建=DefaultBom）</param>
        /// <returns>写盘用编码</returns>
        public static Encoding WriteEncoding(string path, bool bom)
        {
            Encoding baseEncoding = ProfileFor(path);
            if (baseEncoding is UTF8Encoding)
            {
                return new UTF8Encoding(bom);
            }
            return baseEncoding;
        }
        /// <summary>
        /// BOM 探测——首三字节 EF BB BF（保真判据，写侧共用）。
        /// </summary>
        /// <param name="raw">原始字节</param>
        /// <returns>true=带 UTF-8 BOM</returns>
        public static bool DetectBom(byte[] raw)
        {
            return raw != null && raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF;
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
            if (DetectBom(raw))
            {
                return new UTF8Encoding(true);
            }
            return ProfileFor(path);
        }
        /// <summary>
        /// 写入风格描述——「编码 / 换行」可读串（回执声明用——A148）；非 UTF-8 族（.bat=GBK(936)）不适用 BOM 语义。
        /// </summary>
        /// <param name="path">文件路径（编码族判定用）</param>
        /// <param name="bom">写入是否带 BOM</param>
        /// <param name="newline">写入换行串</param>
        /// <returns>风格描述（如 UTF-8 BOM / CRLF）</returns>
        public static string DescribeStyle(string path, bool bom, string newline)
        {
            string enc = "GBK(936)";
            if (ProfileFor(path) is UTF8Encoding)
            {
                if (bom)
                {
                    enc = "UTF-8 BOM";
                }
                else
                {
                    enc = "UTF-8";
                }
            }
            string nl = "LF";
            if (newline == "\r\n")
            {
                nl = "CRLF";
            }
            return enc + " / " + nl;
        }
    }
}

using System.Security.Cryptography;
using System.Text;

namespace Mau.Runtime
{
    /// <summary>
    /// 哈希工具——SHA256 统一实现（黄金校验尾/指纹/积木块尾共用）。
    /// 收敛历史：CliSupport.ComputeSha256（Mau.Cli）与 BrickEmbedder.ComputeSha256（Mau.Translator）原为两份独立实现——
    /// Translator 无法引用 Cli，下沉到 Runtime（Translator/Cli 均引用）统一（2026-08-11 审查修复轮）。
    /// </summary>
    public static class HashUtil
    {
        /// <summary>
        /// 黄金/积木校验尾前缀——MAU_CHECKSUM:SHA256 统一常量（原 6 处硬编码，审查修复轮收拢）
        /// </summary>
        public const string ChecksumPrefix = "// #MAU_CHECKSUM:SHA256:";

        /// <summary>
        /// 计算字符串的 SHA256 哈希——UTF-8 字节转 64 位十六进制大写
        /// </summary>
        /// <param name="text">输入文本</param>
        /// <returns>64 位十六进制哈希（大写）</returns>
        public static string ComputeSha256(string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            byte[] hash = SHA256.HashData(bytes);
            StringBuilder hex = new StringBuilder();
            for (int i = 0; i < hash.Length; i++)
            {
                hex.Append(hash[i].ToString("X2"));
            }
            return hex.ToString();
        }
    }
}

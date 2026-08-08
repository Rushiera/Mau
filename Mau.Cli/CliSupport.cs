using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Mau.Cli
{
    /// <summary>
    /// Mau.Cli 公共支撑——FindWorkspaceRoot / ComputeSha256 唯一实现（收敛原四份重复）
    /// </summary>
    public static class CliSupport
    {
        /// <summary>
        /// 查找 workspace 根——含 Mau.sln 的目录；当前目录向上优先，程序集位置兜底
        /// </summary>
        /// <returns>workspace 根或空</returns>
        public static string? FindWorkspaceRoot()
        {
            DirectoryInfo? dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Mau.sln")))
                {
                    return dir.FullName;
                }
                dir = dir.Parent;
            }
            DirectoryInfo? exeDir = new DirectoryInfo(AppContext.BaseDirectory);
            while (exeDir != null)
            {
                if (File.Exists(Path.Combine(exeDir.FullName, "Mau.sln")))
                {
                    return exeDir.FullName;
                }
                exeDir = exeDir.Parent;
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
            StringBuilder hex = new StringBuilder();
            for (int i = 0; i < hash.Length; i++)
            {
                hex.Append(hash[i].ToString("X2"));
            }
            return hex.ToString();
        }
    }
}

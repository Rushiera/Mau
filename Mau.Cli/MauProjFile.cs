using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Mau.Cli
{
    /// <summary>
    /// mauproj 工程文件——.mau lib 组声明（组名/依赖/引用/文件列表），行指令式格式
    /// </summary>
    public sealed class MauProjFile
    {
        /// <summary>
        /// 组名——输出 DLL 名 FL_&lt;组名&gt;.dll
        /// </summary>
        public string Name;

        /// <summary>
        /// 组版本
        /// </summary>
        public string Version;

        /// <summary>
        /// 积木依赖——BRIK-ID 列表（import 时查可用性）
        /// </summary>
        public List<string> Dependencies;

        /// <summary>
        /// 对外接口依赖——程序集/工程名列表
        /// </summary>
        public List<string> References;

        /// <summary>
        /// 成员 .mau 文件——相对组目录；空 = 递归收集全部子目录 *.mau
        /// </summary>
        public List<string> Files;

        /// <summary>
        /// 组目录——mauproj 所在目录（文件收集基准）
        /// </summary>
        public string DirectoryPath;

        /// <summary>
        /// 校验尾前缀——文件尾完整性标记
        /// </summary>
        public const string ChecksumPrefix = "// #MAUPROJ_CHECKSUM:SHA256:";

        /// <summary>
        /// 构造空 mauproj
        /// </summary>
        public MauProjFile()
        {
            Name = "";
            Version = "";
            Dependencies = new List<string>();
            References = new List<string>();
            Files = new List<string>();
            DirectoryPath = "";
        }

        /// <summary>
        /// 从文件加载并解析 mauproj——校验尾存在则必须匹配，缺失仅警告
        /// </summary>
        /// <param name="path">mauproj 文件路径</param>
        /// <returns>解析结果（File 非空 = 成功）</returns>
        public static MauProjParseResult Load(string path)
        {
            MauProjParseResult result = new MauProjParseResult();
            if (!File.Exists(path))
            {
                result.Error = "文件不存在: " + path;
                return result;
            }

            string content = File.ReadAllText(path).Replace("\r\n", "\n");

            // [段1] 校验尾检查——存在则必须匹配
            string body = content;
            string[] lines = content.Split('\n');
            int bodyEnd = lines.Length;
            bool hasChecksum = false;
            string actualChecksum = "";
            while (bodyEnd > 0)
            {
                string last = lines[bodyEnd - 1].Trim();
                if (last.Length == 0)
                {
                    bodyEnd = bodyEnd - 1;
                    continue;
                }
                if (last.StartsWith(ChecksumPrefix))
                {
                    hasChecksum = true;
                    actualChecksum = last.Substring(ChecksumPrefix.Length).Trim();
                    bodyEnd = bodyEnd - 1;
                    continue;
                }
                break;
            }
            if (hasChecksum)
            {
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < bodyEnd; i++)
                {
                    if (i > 0)
                    {
                        sb.Append('\n');
                    }
                    sb.Append(lines[i]);
                }
                string expected = ComputeSha256(sb.ToString());
                if (actualChecksum != expected)
                {
                    result.Error = "校验尾不匹配——mauproj 已被篡改或损坏";
                    return result;
                }
            }
            else
            {
                result.Warning = "校验尾缺失（首次创建可忽略）";
            }

            // [段2] 行指令解析
            MauProjFile file = new MauProjFile();
            file.DirectoryPath = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
            bool nameSeen = false;
            string[] bodyLines = body.Split('\n');
            for (int i = 0; i < bodyLines.Length; i++)
            {
                string trimmed = bodyLines[i].Trim();
                int lineNumber = i + 1;
                if (trimmed.Length == 0 || trimmed.StartsWith("//"))
                {
                    continue;
                }
                int colon = trimmed.IndexOf(':');
                if (colon <= 0)
                {
                    result.Error = "第 " + lineNumber + " 行格式错误——缺少 '关键字: 值' 分隔";
                    return result;
                }
                string key = trimmed.Substring(0, colon).Trim();
                string value = trimmed.Substring(colon + 1).Trim();
                if (key == "组")
                {
                    if (nameSeen)
                    {
                        result.Error = "第 " + lineNumber + " 行——组名重复声明";
                        return result;
                    }
                    nameSeen = true;
                    file.Name = value;
                }
                else if (key == "版本")
                {
                    if (file.Version.Length > 0)
                    {
                        result.Error = "第 " + lineNumber + " 行——版本重复声明";
                        return result;
                    }
                    file.Version = value;
                }
                else if (key == "依赖")
                {
                    AddList(file.Dependencies, value);
                }
                else if (key == "引用")
                {
                    AddList(file.References, value);
                }
                else if (key == "文件")
                {
                    file.Files.Add(value);
                }
                else
                {
                    result.Error = "第 " + lineNumber + " 行——未知指令: " + key;
                    return result;
                }
            }
            if (!nameSeen)
            {
                result.Error = "缺少必填指令: 组";
                return result;
            }
            if (file.Name.Length == 0)
            {
                result.Error = "组名为空——组: 指令值不能为空";
                return result;
            }

            // [段3] 转义解码——所有值统一 Unescape
            file.Name = Unescape(file.Name);
            file.Version = Unescape(file.Version);
            for (int i = 0; i < file.Dependencies.Count; i++)
            {
                file.Dependencies[i] = Unescape(file.Dependencies[i]);
            }
            for (int i = 0; i < file.References.Count; i++)
            {
                file.References[i] = Unescape(file.References[i]);
            }
            for (int i = 0; i < file.Files.Count; i++)
            {
                file.Files[i] = Unescape(file.Files[i]);
            }

            result.File = file;
            return result;
        }

        /// <summary>
        /// 收集成员文件——Files 非空按列表（相对组目录，跳过缺失），空则递归收集全部 *.mau
        /// </summary>
        /// <returns>成员文件绝对路径列表</returns>
        public List<string> CollectFiles()
        {
            List<string> collected = new List<string>();
            if (Files.Count > 0)
            {
                for (int i = 0; i < Files.Count; i++)
                {
                    string abs = Path.Combine(DirectoryPath, Files[i]);
                    if (File.Exists(abs))
                    {
                        collected.Add(abs);
                    }
                }
            }
            else
            {
                string[] all = Directory.GetFiles(DirectoryPath, "*.mau", SearchOption.AllDirectories);
                Array.Sort(all, StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < all.Length; i++)
                {
                    collected.Add(all[i]);
                }
            }
            return collected;
        }

        /// <summary>
        /// 计算 SHA256——UTF-8 字节转 64 位十六进制大写
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

        /// <summary>
        /// 计算文件 SHA256——读取文本后按 UTF-8 字节计算
        /// </summary>
        /// <param name="path">文件路径</param>
        /// <returns>64 位十六进制哈希（大写）</returns>
        public static string ComputeFileSha256(string path)
        {
            string text = File.ReadAllText(path).Replace("\r\n", "\n");
            return ComputeSha256(text);
        }

        /// <summary>
        /// 值转义解码——\, 转义字面逗号，\\ 转义字面反斜杠
        /// </summary>
        /// <param name="value">原始值</param>
        /// <returns>解码后的值</returns>
        private static string Unescape(string value)
        {
            if (value.IndexOf('\\') < 0)
            {
                return value;
            }
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '\\' && i + 1 < value.Length)
                {
                    char next = value[i + 1];
                    if (next == ',' || next == '\\')
                    {
                        sb.Append(next);
                        i = i + 1;
                        continue;
                    }
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>
        /// 追加列表值——按未转义逗号拆分（\, 保留为字面逗号，解码延后）
        /// </summary>
        /// <param name="list">目标列表</param>
        /// <param name="value">逗号分隔文本</param>
        private static void AddList(List<string> list, string value)
        {
            StringBuilder current = new StringBuilder();
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '\\' && i + 1 < value.Length && (value[i + 1] == ',' || value[i + 1] == '\\'))
                {
                    current.Append(c);
                    current.Append(value[i + 1]);
                    i = i + 1;
                    continue;
                }
                if (c == ',')
                {
                    string part = current.ToString().Trim();
                    if (part.Length > 0)
                    {
                        list.Add(part);
                    }
                    current.Clear();
                    continue;
                }
                current.Append(c);
            }
            string lastPart = current.ToString().Trim();
            if (lastPart.Length > 0)
            {
                list.Add(lastPart);
            }
        }
    }

    /// <summary>
    /// mauproj 解析结果
    /// </summary>
    public sealed class MauProjParseResult
    {
        /// <summary>
        /// 解析出的工程文件（成功时非空）
        /// </summary>
        public MauProjFile? File;

        /// <summary>
        /// 错误消息（空 = 成功）
        /// </summary>
        public string Error;

        /// <summary>
        /// 警告消息（空 = 无）
        /// </summary>
        public string Warning;

        /// <summary>
        /// 构造解析结果
        /// </summary>
        public MauProjParseResult()
        {
            File = null;
            Error = "";
            Warning = "";
        }
    }
}

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
        /// 产品积木目录——bricks: 声明（相对 mauproj 所在目录；分号分隔多目录）
        /// 产品积木机制（design-mau-boundary.md §五）：项目自持积木目录 + index.json
        /// </summary>
        public List<string> Bricks;

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
            Bricks = new List<string>();
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
                string expected = CliSupport.ComputeSha256(sb.ToString());
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
                    // 逗号分隔多文件——与 依赖/引用 同规（\, 转义字面逗号）
                    AddList(file.Files, value);
                }
                else if (key == "bricks")
                {
                    // 产品积木目录——分号分隔多目录（相对 mauproj 所在目录）
                    AddList(file.Bricks, value, ';');
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
            if (!IsSafeName(file.Name))
            {
                result.Error = "组名非法——仅允许字母/数字/下划线（防路径逃逸）: " + file.Name;
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
                    // 路径逃逸防护——GetFullPath 后必须仍位于组目录内（../ 或绝对路径拒绝；安全审查项 P0-7）
                    string full = Path.GetFullPath(abs);
                    string rootFull = Path.GetFullPath(DirectoryPath);
                    if (!full.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    if (File.Exists(full))
                    {
                        collected.Add(full);
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
/// 解析引用清单——把 引用: 声明解析为绝对 dll 路径（D25 引用确定性）。
/// 形态判定：含路径分隔符或以 .dll 结尾 = 相对路径（相对组目录解析）；
/// 纯程序集名 = 宿主目录查找 {name}.dll。
/// </summary>
/// <param name = "hostDir">宿主目录（Mau.exe 所在目录——基座/契约/积木 dll 所在地）</param>
/// <returns>解析后的 dll 绝对路径列表；解析失败项写入 errors</returns>
public List<string> ResolveReferences(string hostDir, out List<string> errors)
{
    errors = new List<string>();
    List<string> resolved = new List<string>();
    for (int i = 0; i < References.Count; i++)
    {
        string raw = References[i];
        if (raw.Length == 0)
        {
            continue;
        }

        bool isPath = raw.IndexOf('/') >= 0 || raw.IndexOf('\\') >= 0 || raw.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
        string full = "";
        if (isPath)
        {
            // 相对路径——相对组目录解析 + 根内校验（复用 CollectFiles 安全模式）
            full = Path.GetFullPath(Path.Combine(DirectoryPath, raw));
            string rootFull = Path.GetFullPath(DirectoryPath);
            if (!full.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("引用 " + raw + " 越出组目录——拒绝");
                continue;
            }
        }
        else
        {
            // 纯程序集名——宿主目录查找
            full = Path.Combine(hostDir, raw + ".dll");
        }

        if (!File.Exists(full))
        {
            errors.Add("引用 " + raw + " 未找到——" + full);
            continue;
        }

        if (!resolved.Contains(full))
        {
            resolved.Add(full);
        }
    }

    return resolved;
}
        /// <summary>
        /// 计算文件 SHA256——读取文本后按 UTF-8 字节计算
        /// </summary>
        /// <param name="path">文件路径</param>
        /// <returns>64 位十六进制哈希（大写）</returns>
        public static string ComputeFileSha256(string path)
        {
            string text = File.ReadAllText(path).Replace("\r\n", "\n");
            return CliSupport.ComputeSha256(text);
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
        /// 追加列表值——按未转义分隔符拆分（\, 保留为字面逗号，解码延后）
        /// </summary>
        /// <param name="list">目标列表</param>
        /// <param name="value">分隔文本</param>
        /// <param name="separator">分隔符（默认逗号）</param>
        private static void AddList(List<string> list, string value, char separator = ',')
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
                if (c == separator)
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
/// <summary>
/// 安全逻辑名——仅字母/数字/下划线（组名类名防路径逃逸；安全审查项 P0-7）
/// </summary>
/// <param name = "name">名称</param>
/// <returns>安全为真</returns>
internal static bool IsSafeName(string name)
{
    if (name.Length == 0)
    {
        return false;
    }

    for (int i = 0; i < name.Length; i++)
    {
        char c = name[i];
        if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_'))
        {
            return false;
        }
    }

    return true;
}    }

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

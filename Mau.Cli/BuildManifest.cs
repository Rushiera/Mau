using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Mau.Cli
{
    /// <summary>
    /// 构建清单——工程声明"从源码到完全可用"的环节链（design-mau-rebuild.md §一）。
    /// 行指令格式（mauproj 的上一级）：构建/版本/环节（输入 glob/前置/命令）。
    /// 定义权在声明，执行权在聚合器（CommandUp）——与 Mau 哲学一致（定义权转移）。
    /// </summary>
    public sealed class BuildManifest
    {
        /// <summary>
        /// 单环节声明——输入 glob 列表 + 前置环节 + 执行命令
        /// </summary>
        public sealed class BuildStep
        {
            /// <summary>
            /// 环节名——清单内唯一
            /// </summary>
            public string Name = "";

            /// <summary>
            /// 输入 glob 列表——相对清单目录；** 递归
            /// </summary>
            public List<string> Inputs = new List<string>();

            /// <summary>
            /// 前置环节名列表——拓扑依赖
            /// </summary>
            public List<string> Prereqs = new List<string>();

            /// <summary>
            /// 执行命令——首词决定执行形态（mau=进程内 / 其他=子进程）
            /// </summary>
            public string Command = "";
        }

        /// <summary>
        /// 工程名——清单身份
        /// </summary>
        public string Name = "";

        /// <summary>
        /// 清单版本
        /// </summary>
        public string Version = "";

        /// <summary>
        /// 环节表——按声明顺序
        /// </summary>
        public List<BuildStep> Steps = new List<BuildStep>();

        /// <summary>
        /// 清单文件绝对路径
        /// </summary>
        public string ManifestPath = "";

        /// <summary>
        /// 清单所在目录（命令工作目录基准）
        /// </summary>
        public string DirectoryPath = "";

        /// <summary>
        /// 输入基准目录——仓库根（输入 glob 相对仓库根解析；清单位于 CH4.Corpus/ 等次级时自动向上找）
        /// </summary>
        public string InputBaseDir = "";

        /// <summary>
        /// 解析错误——空=成功
        /// </summary>
        public string Error = "";

        /// <summary>
        /// 按名查环节
        /// </summary>
        /// <param name="name">环节名</param>
        /// <returns>环节或空</returns>
        public BuildStep? FindStep(string name)
        {
            for (int i = 0; i < Steps.Count; i++)
            {
                if (Steps[i].Name == name)
                {
                    return Steps[i];
                }
            }
            return null;
        }

        /// <summary>
        /// 从文件加载并解析构建清单——行指令格式：
        /// 构建: 名 / 版本: 1.0 / 环节 名:（子行 输入: glob,glob / 前置: 名 / 命令: cmd）
        /// # 注释行；值内 \, 转义逗号（与 mauproj 同规）
        /// </summary>
        /// <param name="path">清单文件路径</param>
        /// <returns>解析结果（Name 非空=成功）</returns>
        public static BuildManifest Load(string path)
        {
            BuildManifest manifest = new BuildManifest();
            if (!File.Exists(path))
            {
                manifest.Error = "清单文件不存在: " + path;
                return manifest;
            }
            manifest.ManifestPath = Path.GetFullPath(path);
            manifest.DirectoryPath = Path.GetDirectoryName(manifest.ManifestPath) ?? ".";
            // 输入基准 = 仓库根——向上找含 .sln 或 .git 的目录（输入 glob 相对仓库根解析；P2：清单放 CH4.Corpus/ 次级）
            string? repoProbe = manifest.DirectoryPath;
            while (repoProbe != null)
            {
                if (File.Exists(Path.Combine(repoProbe, ".git"))
                    || Directory.Exists(Path.Combine(repoProbe, ".git"))
                    || File.Exists(Path.Combine(repoProbe, "Mau.sln"))
                    || File.Exists(Path.Combine(repoProbe, "CH4.sln")))
                {
                    manifest.InputBaseDir = repoProbe;
                    break;
                }
                repoProbe = Directory.GetParent(repoProbe)?.FullName;
            }
            if (manifest.InputBaseDir.Length == 0)
            {
                manifest.InputBaseDir = manifest.DirectoryPath;
            }

            string content = File.ReadAllText(path).Replace("\r\n", "\n");
            string[] lines = content.Split('\n');
            bool nameSeen = false;
            bool versionSeen = false;
            BuildStep? current = null;
            int lineNumber = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                lineNumber = i + 1;
                string trimmed = lines[i].Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("#") || trimmed.StartsWith("//"))
                {
                    continue;
                }
                int colon = trimmed.IndexOf(':');
                if (colon <= 0)
                {
                    manifest.Error = "第 " + lineNumber + " 行格式错误——缺少 '关键字: 值' 分隔";
                    return manifest;
                }
                string key = trimmed.Substring(0, colon).Trim();
                string value = trimmed.Substring(colon + 1).Trim();

                if (key == "构建")
                {
                    if (nameSeen)
                    {
                        manifest.Error = "第 " + lineNumber + " 行——工程名重复声明";
                        return manifest;
                    }
                    nameSeen = true;
                    manifest.Name = Unescape(value);
                }
                else if (key == "版本")
                {
                    if (versionSeen)
                    {
                        manifest.Error = "第 " + lineNumber + " 行——版本重复声明";
                        return manifest;
                    }
                    versionSeen = true;
                    manifest.Version = Unescape(value);
                }
                else if (key.StartsWith("环节", StringComparison.Ordinal))
                {
                    // 环节 名: ——key 为 "环节 名"
                    string stepName = key.Substring(2).Trim();
                    if (stepName.Length == 0)
                    {
                        manifest.Error = "第 " + lineNumber + " 行——环节名为空";
                        return manifest;
                    }
                    BuildStep step = new BuildStep();
                    step.Name = Unescape(stepName);
                    manifest.Steps.Add(step);
                    current = step;
                }
                else if (key == "输入")
                {
                    if (current == null)
                    {
                        manifest.Error = "第 " + lineNumber + " 行——输入 需在 环节 块内";
                        return manifest;
                    }
                    AddList(current.Inputs, value);
                }
                else if (key == "前置")
                {
                    if (current == null)
                    {
                        manifest.Error = "第 " + lineNumber + " 行——前置 需在 环节 块内";
                        return manifest;
                    }
                    AddList(current.Prereqs, value);
                }
                else if (key == "命令")
                {
                    if (current == null)
                    {
                        manifest.Error = "第 " + lineNumber + " 行——命令 需在 环节 块内";
                        return manifest;
                    }
                    current.Command = Unescape(value);
                }
                else
                {
                    manifest.Error = "第 " + lineNumber + " 行——未知指令: " + key;
                    return manifest;
                }
            }
            if (!nameSeen)
            {
                manifest.Error = "缺少必填指令: 构建";
                return manifest;
            }
            // 校验：环节必须有命令；前置必须存在；拓扑无环
            for (int i = 0; i < manifest.Steps.Count; i++)
            {
                BuildStep step = manifest.Steps[i];
                if (step.Command.Length == 0)
                {
                    manifest.Error = "环节 " + step.Name + " 缺少命令";
                    return manifest;
                }
                for (int p = 0; p < step.Prereqs.Count; p++)
                {
                    if (manifest.FindStep(step.Prereqs[p]) == null)
                    {
                        manifest.Error = "环节 " + step.Name + " 前置不存在: " + step.Prereqs[p];
                        return manifest;
                    }
                }
            }
            // 环检测——DFS 三色
            Dictionary<string, int> color = new Dictionary<string, int>();
            for (int i = 0; i < manifest.Steps.Count; i++)
            {
                color[manifest.Steps[i].Name] = 0;
            }
            for (int i = 0; i < manifest.Steps.Count; i++)
            {
                if (HasCycle(manifest, manifest.Steps[i].Name, color))
                {
                    manifest.Error = "环节依赖成环——" + manifest.Steps[i].Name;
                    return manifest;
                }
            }
            return manifest;
        }

        /// <summary>
        /// 拓扑排序——前置在前，按声明序稳定
        /// </summary>
        /// <returns>拓扑序环节名列表</returns>
        public List<string> TopologicalOrder()
        {
            List<string> order = new List<string>();
            Dictionary<string, bool> visited = new Dictionary<string, bool>();
            for (int i = 0; i < Steps.Count; i++)
            {
                visited[Steps[i].Name] = false;
            }
            for (int i = 0; i < Steps.Count; i++)
            {
                VisitOrder(Steps[i].Name, visited, order);
            }
            return order;
        }

        /// <summary>
        /// DFS 后序访问——前置先入序
        /// </summary>
        /// <param name="name">环节名</param>
        /// <param name="visited">访问标记</param>
        /// <param name="order">结果序</param>
        private void VisitOrder(string name, Dictionary<string, bool> visited, List<string> order)
        {
            if (visited[name])
            {
                return;
            }
            visited[name] = true;
            BuildStep? step = FindStep(name);
            if (step == null)
            {
                return;
            }
            for (int i = 0; i < step.Prereqs.Count; i++)
            {
                BuildStep? pre = FindStep(step.Prereqs[i]);
                if (pre != null && !visited[pre.Name])
                {
                    VisitOrder(pre.Name, visited, order);
                }
            }
            order.Add(name);
        }

        /// <summary>
        /// DFS 三色环检测——0 未访问 1 访问中 2 完成
        /// </summary>
        /// <param name="manifest">清单</param>
        /// <param name="name">当前环节</param>
        /// <param name="color">颜色表</param>
        /// <returns>有环</returns>
        private static bool HasCycle(BuildManifest manifest, string name, Dictionary<string, int> color)
        {
            if (color[name] == 1)
            {
                return true;
            }
            if (color[name] == 2)
            {
                return false;
            }
            color[name] = 1;
            BuildStep? step = manifest.FindStep(name);
            if (step != null)
            {
                for (int i = 0; i < step.Prereqs.Count; i++)
                {
                    BuildStep? pre = manifest.FindStep(step.Prereqs[i]);
                    if (pre != null && HasCycle(manifest, pre.Name, color))
                    {
                        return true;
                    }
                }
            }
            color[name] = 2;
            return false;
        }

        /// <summary>
        /// 展开输入 glob——相对输入基准（仓库根）；支持 ** 递归 + 单段 *；返回绝对路径列表（排序去重）
        /// </summary>
        /// <param name="step">环节</param>
        /// <returns>文件绝对路径列表</returns>
        public List<string> ExpandInputs(BuildStep step)
        {
            List<string> files = new List<string>();
            string baseDir = InputBaseDir.Length > 0 ? InputBaseDir : DirectoryPath;
            for (int g = 0; g < step.Inputs.Count; g++)
            {
                string pattern = step.Inputs[g].Replace('\\', '/');
                string fullPattern = pattern;
                bool recursive = false;
                int dstar = pattern.IndexOf("**", StringComparison.Ordinal);
                if (dstar >= 0)
                {
                    recursive = true;
                    string head = pattern.Substring(0, dstar);
                    string tail = pattern.Substring(dstar + 2);
                    string headDir = Path.Combine(baseDir, head.Replace('/', Path.DirectorySeparatorChar));
                    string searchPattern = tail.TrimStart('/');
                    if (searchPattern.Length == 0)
                    {
                        searchPattern = "*";
                    }
                    if (Directory.Exists(headDir))
                    {
                        string[] all = Directory.GetFiles(headDir, searchPattern,
                            recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
                        for (int i = 0; i < all.Length; i++)
                        {
                            string full = Path.GetFullPath(all[i]);
                            // 排除编译产物目录（bin/obj/Debug/Release）——产物不参与指纹，否则 dotnet build 每次漂移
                            if (IsArtifactPath(full))
                            {
                                continue;
                            }
                            files.Add(full);
                        }
                    }
                    continue;
                }
                string dirPart = pattern.IndexOf('/') >= 0
                    ? pattern.Substring(0, pattern.LastIndexOf('/'))
                    : "";
                string filePart = pattern.IndexOf('/') >= 0
                    ? pattern.Substring(pattern.LastIndexOf('/') + 1)
                    : pattern;
                string fullDir = Path.Combine(baseDir, dirPart.Replace('/', Path.DirectorySeparatorChar));
                if (filePart.Contains("*"))
                {
                    if (Directory.Exists(fullDir))
                    {
                        string[] matched = Directory.GetFiles(fullDir, filePart, SearchOption.TopDirectoryOnly);
                        for (int i = 0; i < matched.Length; i++)
                        {
                            string full = Path.GetFullPath(matched[i]);
                            if (IsArtifactPath(full))
                            {
                                continue;
                            }
                            files.Add(full);
                        }
                    }
                }
                else
                {
                    string fullFile = Path.Combine(baseDir, pattern.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(fullFile))
                    {
                        string full = Path.GetFullPath(fullFile);
                        if (!IsArtifactPath(full))
                        {
                            files.Add(full);
                        }
                    }
                }
            }
            files.Sort(StringComparer.OrdinalIgnoreCase);
            return files;
        }

        /// <summary>
        /// 编译产物路径判定——路径段含 bin/obj/Debug/Release（大小写不敏感）
        /// </summary>
        /// <param name="fullPath">文件绝对路径</param>
        /// <returns>是产物路径</returns>
        private static bool IsArtifactPath(string fullPath)
        {
            string normalized = fullPath.Replace('/', Path.DirectorySeparatorChar);
            string[] segments = normalized.Split(Path.DirectorySeparatorChar);
            for (int i = 0; i < segments.Length; i++)
            {
                string seg = segments[i];
                if (seg.Length == 0)
                {
                    continue;
                }
                if (string.Equals(seg, "bin", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(seg, "obj", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(seg, "Debug", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(seg, "Release", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 计算文件列表指纹——相对路径 + 大小 + 最后修改时间的 SHA256（结构变化检测，D2）
        /// </summary>
        /// <param name="files">文件绝对路径列表</param>
        /// <returns>64 位十六进制指纹</returns>
        public static string ComputeListFingerprint(List<string> files)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < files.Count; i++)
            {
                try
                {
                    FileInfo info = new FileInfo(files[i]);
                    sb.Append(files[i]);
                    sb.Append('|');
                    sb.Append(info.Length.ToString());
                    sb.Append('|');
                    sb.Append(info.LastWriteTimeUtc.Ticks.ToString());
                    sb.Append('\n');
                }
                catch
                {
                    // 文件不可读——跳过（指纹缺项仍可对比）
                }
            }
            return CliSupport.ComputeSha256(sb.ToString());
        }

        /// <summary>
        /// 扫描 Bricks 块尾失配——输入含 Bricks/** 时校验 // #MAU_CHECKSUM:SHA256: 尾
        /// </summary>
        /// <param name="files">文件列表</param>
        /// <returns>失配文件数（0=全匹配或非积木输入）</returns>
        public static int CountBrickChecksumMismatch(List<string> files)
        {
            int mismatch = 0;
            for (int i = 0; i < files.Count; i++)
            {
                string file = files[i];
                if (!file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                string? dir = Path.GetDirectoryName(file);
                if (dir == null)
                {
                    continue;
                }
                string dirName = Path.GetFileName(dir);
                // 仅校验 Bricks 类别目录下的积木文件（含 Bricks 路径）
                if (file.IndexOf("Bricks" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }
                try
                {
                    string content = File.ReadAllText(file).Replace("\r\n", "\n");
                    string[] lines = content.Split('\n');
                    int bodyEnd = lines.Length;
                    string actualChecksum = "";
                    while (bodyEnd > 0)
                    {
                        string last = lines[bodyEnd - 1].Trim();
                        if (last.Length == 0)
                        {
                            bodyEnd = bodyEnd - 1;
                            continue;
                        }
                        if (last.StartsWith("// #MAU_CHECKSUM:SHA256:", StringComparison.Ordinal))
                        {
                            actualChecksum = last.Substring("// #MAU_CHECKSUM:SHA256:".Length).Trim();
                            bodyEnd = bodyEnd - 1;
                            continue;
                        }
                        break;
                    }
                    if (actualChecksum.Length == 0)
                    {
                        continue; // 无校验尾的 .cs（非积木文本）——跳过
                    }
                    StringBuilder sb = new StringBuilder();
                    for (int l = 0; l < bodyEnd; l++)
                    {
                        if (l > 0)
                        {
                            sb.Append('\n');
                        }
                        sb.Append(lines[l]);
                    }
                    string expected = CliSupport.ComputeSha256(sb.ToString());
                    if (expected != actualChecksum)
                    {
                        mismatch = mismatch + 1;
                    }
                }
                catch
                {
                    mismatch = mismatch + 1;
                }
            }
            return mismatch;
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

        /// <summary>
        /// 值转义解码——\, 转义字面逗号，\\ 转义字面反斜杠
        /// </summary>
        /// <param name="value">原始值</param>
        /// <returns>解码值</returns>
        private static string Unescape(string value)
        {
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
    }
}

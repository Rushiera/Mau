using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Mau.Contracts;
using Mau.Development;
using Mau.Translator;

namespace Mau.Cli
{
    /// <summary>
    /// mauproj 组工程命令——组模式构筑 / 导出组包 / 导入组包
    /// </summary>
    public static class CommandMauProj
    {
        /// <summary>
        /// 组模式构筑——全组 .mau 验证 + 生成 + 一次 Emit 编译为 FL_&lt;组名&gt;.dll
        /// </summary>
        /// <param name="mauprojPath">mauproj 文件路径</param>
        /// <param name="outDir">输出目录</param>
        /// <param name="useSdk">true=走环境 dotnet build（--sdk），false=Roslyn Emit</param>
        /// <returns>退出码——0 成功</returns>
        public static int Build(string mauprojPath, string outDir, bool useSdk)
        {
            // [段1] 解析 mauproj
            MauProjParseResult parsed = MauProjFile.Load(mauprojPath);
            if (parsed.Error.Length > 0)
            {
                Console.WriteLine("FAIL: " + parsed.Error);
                return 1;
            }
            if (parsed.Warning.Length > 0)
            {
                Console.WriteLine("WARN: " + parsed.Warning);
            }
            MauProjFile proj = parsed.File!;
            string assemblyName = "FL_" + proj.Name;

            // [段2] 收集成员文件
            List<string> files = proj.CollectFiles();
            if (files.Count == 0)
            {
                Console.WriteLine("FAIL: 组 " + proj.Name + " 无成员 .mau 文件");
                return 1;
            }
            Console.WriteLine("组 " + proj.Name + "：成员 " + files.Count + " 个");

            // [段3] 逐个验证 + 生成——任何失败整组拒绝（组编译：骨架数组 + 共享 BRIKGROUP）
            string[] texts = new string[files.Count];
            string[] names = new string[files.Count];
            for (int i = 0; i < files.Count; i++)
            {
                texts[i] = File.ReadAllText(files[i]);
                names[i] = Program.FlowNameFromPath(Path.GetFileNameWithoutExtension(files[i]));
            }
            GroupCompileResult groupResult = MauCompiler.CompileGroup(texts, names);
            List<string> sources = new List<string>();
            List<string> classNames = new List<string>();
            for (int i = 0; i < groupResult.Results.Length; i++)
            {
                CompileResult result = groupResult.Results[i];
                if (!result.Success)
                {
                    Program.PrintDiagnostics(files[i], result.Diagnostics);
                    Console.WriteLine("构建失败: " + Path.GetFileName(files[i]) + " 验证未通过——整组拒绝");
                    return 1;
                }
                sources.Add(result.GeneratedCode);
                classNames.Add("FL_" + names[i]);
                Console.WriteLine("  验证通过: " + Path.GetFileName(files[i]) + " → FL_" + names[i]);
            }
            if (groupResult.BrickGroupSource.Length > 0)
            {
                sources.Add(groupResult.BrickGroupSource);
                classNames.Add("FL_BRIKGROUP");
                Console.WriteLine("  内嵌闭包: " + (sources.Count - 1) + " 个 BRIK（共享 BRIKGROUP）");
            }

            // [段4] 输出目录
            if (!Directory.Exists(outDir))
            {
                Directory.CreateDirectory(outDir);
            }
            string dllPath = Path.Combine(outDir, assemblyName + ".dll");

            // [段5] 编译——默认 Roslyn Emit（CompileMany 组模式）；--sdk 走临时项目 dotnet build
            if (useSdk)
            {
                if (!BuildSdkGroup(sources, classNames, assemblyName, dllPath))
                {
                    return 2;
                }
            }
            else
            {
                string pocketRoot = Path.Combine(Path.GetTempPath(), "mau_build_group_" + Guid.NewGuid().ToString("N").Substring(0, 8));
                MauPocketCompiler compiler = new MauPocketCompiler(pocketRoot);
                MauPocketCompileResult compileResult = compiler.CompileMany(sources.ToArray(), classNames.ToArray(), assemblyName);
                if (!compileResult.Success)
                {
                    Console.WriteLine("构建失败: Roslyn 编译错误");
                    for (int i = 0; i < compileResult.Diagnostics.Length; i++)
                    {
                        Console.WriteLine("  " + compileResult.Diagnostics[i]);
                    }
                    return 2;
                }
                File.Copy(compileResult.AssemblyPath, dllPath, true);
                try
                {
                    if (Directory.Exists(pocketRoot))
                    {
                        Directory.Delete(pocketRoot, true);
                    }
                }
                catch
                {
                    // 临时目录清理失败不影响结果
                }
            }

            Console.WriteLine("构建成功: " + dllPath + "（组模式 " + (useSdk ? "dotnet build" : "Roslyn Emit") + "）");
            return 0;
        }

        /// <summary>
        /// 导出组包——源 + mauproj（补校验尾）+ 黄金基准 + manifest.json 到规范中介目录
        /// </summary>
        /// <param name="mauprojPath">mauproj 文件路径</param>
        /// <param name="outDir">导出目录——缺省 = 仓库根 CatTemp/mau-export/&lt;组名&gt;/</param>
        /// <returns>退出码——0 成功</returns>
        public static int Export(string mauprojPath, string? outDir)
        {
            // [段1] 解析 mauproj
            MauProjParseResult parsed = MauProjFile.Load(mauprojPath);
            if (parsed.Error.Length > 0)
            {
                Console.WriteLine("FAIL: " + parsed.Error);
                return 1;
            }
            if (parsed.Warning.Length > 0)
            {
                Console.WriteLine("WARN: " + parsed.Warning);
            }
            MauProjFile proj = parsed.File!;

            // [段2] 目标目录——缺省规范中介目录（仓库根 CatTemp/mau-export/）
            string targetDir;
            if (outDir != null && outDir.Length > 0)
            {
                targetDir = outDir;
            }
            else
            {
                string? root = FindWorkspaceRootFrom(mauprojPath);
                if (root == null)
                {
                    Console.WriteLine("FAIL: 未找到 Mau.sln——请用 -o 指定导出目录");
                    return 1;
                }
                targetDir = Path.Combine(root, "CatTemp", "mau-export", proj.Name);
            }
            if (Directory.Exists(targetDir))
            {
                Console.WriteLine("FAIL: 导出目录已存在——" + targetDir + "（先删除或换 -o）");
                return 1;
            }

            // [段3] 收集成员文件
            List<string> files = proj.CollectFiles();
            if (files.Count == 0)
            {
                Console.WriteLine("FAIL: 组 " + proj.Name + " 无成员 .mau 文件");
                return 1;
            }

            // [段4] 复制 mauproj + 成员 .mau（保持相对路径）+ 黄金基准 expected/
            Directory.CreateDirectory(targetDir);
            List<string> exportedFiles = new List<string>();

            string projFileName = Path.GetFileName(mauprojPath);
            string projContent = File.ReadAllText(mauprojPath).Replace("\r\n", "\n").TrimEnd();
            if (projContent.IndexOf(MauProjFile.ChecksumPrefix) < 0)
            {
                projContent = projContent + "\n" + MauProjFile.ChecksumPrefix + MauProjFile.ComputeSha256(projContent);
            }
            File.WriteAllText(Path.Combine(targetDir, projFileName), projContent + "\n", Encoding.UTF8);
            exportedFiles.Add(projFileName);

            for (int i = 0; i < files.Count; i++)
            {
                string rel = Path.GetRelativePath(proj.DirectoryPath, files[i]);
                string dest = Path.Combine(targetDir, rel);
                string? destDir = Path.GetDirectoryName(dest);
                if (destDir != null && !Directory.Exists(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }
                File.Copy(files[i], dest, true);
                exportedFiles.Add(rel);
            }

            string expectedDir = Path.Combine(proj.DirectoryPath, "expected");
            if (Directory.Exists(expectedDir))
            {
                string[] goldens = Directory.GetFiles(expectedDir, "*.cs", SearchOption.TopDirectoryOnly);
                Array.Sort(goldens, StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < goldens.Length; i++)
                {
                    string dest = Path.Combine(targetDir, "expected", Path.GetFileName(goldens[i]));
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    File.Copy(goldens[i], dest, true);
                    exportedFiles.Add("expected/" + Path.GetFileName(goldens[i]));
                }
            }

            // [段5] 生成 manifest.json——机器可读导入验证依据
            string mauVersion = ReadMauVersion(files[0]);
            string baseVersion = ReadBaseVersion();
            StringBuilder json = new StringBuilder();
            json.AppendLine("{");
            json.AppendLine("  \"name\": \"" + JsonEscape(proj.Name) + "\",");
            json.AppendLine("  \"version\": \"" + JsonEscape(proj.Version) + "\",");
            json.AppendLine("  \"exportedAt\": \"" + DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ") + "\",");
            json.AppendLine("  \"mauVersion\": \"" + JsonEscape(mauVersion) + "\",");
            json.AppendLine("  \"baseVersion\": \"" + JsonEscape(baseVersion) + "\",");
            json.AppendLine("  \"files\": [");
            for (int i = 0; i < exportedFiles.Count; i++)
            {
                string rel = exportedFiles[i];
                string hash = MauProjFile.ComputeFileSha256(Path.Combine(targetDir, rel));
                json.Append("    {\"path\": \"" + JsonEscape(rel) + "\", \"sha256\": \"" + hash + "\"}");
                if (i < exportedFiles.Count - 1)
                {
                    json.Append(",");
                }
                json.AppendLine();
            }
            json.AppendLine("  ],");
            json.AppendLine("  \"references\": {");
            json.AppendLine("    \"bricks\": [");
            for (int i = 0; i < proj.Dependencies.Count; i++)
            {
                json.Append("      \"" + JsonEscape(proj.Dependencies[i]) + "\"");
                if (i < proj.Dependencies.Count - 1)
                {
                    json.Append(",");
                }
                json.AppendLine();
            }
            json.AppendLine("    ],");
            json.AppendLine("    \"projects\": [");
            for (int i = 0; i < proj.References.Count; i++)
            {
                json.Append("      \"" + JsonEscape(proj.References[i]) + "\"");
                if (i < proj.References.Count - 1)
                {
                    json.Append(",");
                }
                json.AppendLine();
            }
            json.AppendLine("    ]");
            json.AppendLine("  }");
            json.AppendLine("}");
            File.WriteAllText(Path.Combine(targetDir, "manifest.json"), json.ToString(), new UTF8Encoding(true));

            Console.WriteLine("导出成功: " + proj.Name + " → " + targetDir + "（" + exportedFiles.Count + " 文件 + manifest）");
            return 0;
        }

        /// <summary>
        /// 导入组包——manifest 校验（SHA256 + 积木可用性）后落盘，不编译
        /// </summary>
        /// <param name="packageDir">组包目录（含 manifest.json）</param>
        /// <param name="targetDir">目标语料目录</param>
        /// <param name="force">true=覆盖已存在文件</param>
        /// <returns>退出码——0 成功</returns>
        public static int Import(string packageDir, string targetDir, bool force)
        {
            // [段1] 读 manifest.json
            string manifestPath = Path.Combine(packageDir, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                Console.WriteLine("FAIL: 组包缺少 manifest.json——" + manifestPath);
                return 1;
            }
            string manifestText = File.ReadAllText(manifestPath);
            string name = "";
            string version = "";
            List<string> brickDeps = new List<string>();
            List<string> projectRefs = new List<string>();
            List<ManifestEntry> entries = new List<ManifestEntry>();
            try
            {
                using (System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(manifestText))
                {
                    System.Text.Json.JsonElement root = doc.RootElement;
                    if (root.TryGetProperty("name", out System.Text.Json.JsonElement nameEl))
                    {
                        name = nameEl.GetString() ?? "";
                    }
                    if (root.TryGetProperty("version", out System.Text.Json.JsonElement verEl))
                    {
                        version = verEl.GetString() ?? "";
                    }
                    if (root.TryGetProperty("references", out System.Text.Json.JsonElement refsEl)
                        && refsEl.ValueKind == System.Text.Json.JsonValueKind.Object)
                    {
                        if (refsEl.TryGetProperty("bricks", out System.Text.Json.JsonElement bricksEl))
                        {
                            for (int i = 0; i < bricksEl.GetArrayLength(); i++)
                            {
                                brickDeps.Add(bricksEl[i].GetString() ?? "");
                            }
                        }
                        if (refsEl.TryGetProperty("projects", out System.Text.Json.JsonElement projectsEl))
                        {
                            for (int i = 0; i < projectsEl.GetArrayLength(); i++)
                            {
                                projectRefs.Add(projectsEl[i].GetString() ?? "");
                            }
                        }
                    }
                    if (root.TryGetProperty("files", out System.Text.Json.JsonElement filesEl))
                    {
                        for (int i = 0; i < filesEl.GetArrayLength(); i++)
                        {
                            System.Text.Json.JsonElement fileEl = filesEl[i];
                            ManifestEntry entry = new ManifestEntry();
                            if (fileEl.TryGetProperty("path", out System.Text.Json.JsonElement pathEl))
                            {
                                entry.Path = pathEl.GetString() ?? "";
                            }
                            if (fileEl.TryGetProperty("sha256", out System.Text.Json.JsonElement hashEl))
                            {
                                entry.Sha256 = hashEl.GetString() ?? "";
                            }
                            entries.Add(entry);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL: manifest.json 解析失败——" + ex.Message);
                return 1;
            }
            if (name.Length == 0)
            {
                Console.WriteLine("FAIL: manifest.json 缺少 name");
                return 1;
            }

            // [段2] SHA256 完整性校验
            for (int i = 0; i < entries.Count; i++)
            {
                ManifestEntry entry = entries[i];
                string absPath = Path.Combine(packageDir, entry.Path);
                if (!File.Exists(absPath))
                {
                    Console.WriteLine("FAIL: 包内文件缺失——" + entry.Path);
                    return 1;
                }
                string actual = MauProjFile.ComputeFileSha256(absPath);
                if (actual != entry.Sha256)
                {
                    Console.WriteLine("FAIL: 文件校验失败——" + entry.Path + "（哈希不匹配，包可能被篡改）");
                    return 1;
                }
            }
            Console.WriteLine("完整性校验通过: " + entries.Count + " 个文件");

            // [段3] 积木依赖可用性检查
            for (int i = 0; i < brickDeps.Count; i++)
            {
                string dep = brickDeps[i];
                if (dep.Length > 0 && !BrickIndex.TryGet(dep, out BrickIndexEntry? _))
                {
                    Console.WriteLine("FAIL: 依赖积木不可用——" + dep + "（本环境未注册）");
                    return 1;
                }
            }
            if (brickDeps.Count > 0)
            {
                Console.WriteLine("积木依赖检查通过: " + brickDeps.Count + " 个");
            }

            // [段4] 落盘——目标 &lt;targetDir&gt;/&lt;组名&gt;/...
            string groupDir = Path.Combine(targetDir, name);
            if (Directory.Exists(groupDir) && !force)
            {
                Console.WriteLine("FAIL: 目标组目录已存在——" + groupDir + "（用 --force 覆盖）");
                return 1;
            }
            Directory.CreateDirectory(groupDir);
            for (int i = 0; i < entries.Count; i++)
            {
                ManifestEntry entry = entries[i];
                string src = Path.Combine(packageDir, entry.Path);
                string dest = Path.Combine(groupDir, entry.Path);
                string? destDir = Path.GetDirectoryName(dest);
                if (destDir != null && !Directory.Exists(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }
                File.Copy(src, dest, force);
            }

            Console.WriteLine("导入成功: " + name + (version.Length > 0 ? " v" + version : "") + " → " + groupDir);
            return 0;
        }

        /// <summary>
        /// --sdk 组模式——临时项目 + dotnet build（引用宿主输出目录全部 dll）
        /// </summary>
        /// <param name="sources">C# 源码数组</param>
        /// <param name="classNames">类名数组</param>
        /// <param name="assemblyName">程序集名</param>
        /// <param name="dllPath">输出 DLL 路径</param>
        /// <returns>true=成功</returns>
        private static bool BuildSdkGroup(List<string> sources, List<string> classNames, string assemblyName, string dllPath)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "mau_build_group_sdk_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                Directory.CreateDirectory(tempDir);

                // [段1] 写全部 .cs 文件
                for (int i = 0; i < sources.Count; i++)
                {
                    File.WriteAllText(Path.Combine(tempDir, classNames[i] + ".cs"), sources[i], Encoding.UTF8);
                }

                // [段2] 写 .csproj——引用宿主输出目录全部 dll（与 Roslyn 引用集同语义）
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("<Project Sdk=\"Microsoft.NET.Sdk\">");
                sb.AppendLine("  <PropertyGroup>");
                sb.AppendLine("    <TargetFramework>net8.0</TargetFramework>");
                sb.AppendLine("    <OutputType>Library</OutputType>");
                sb.AppendLine("    <Nullable>enable</Nullable>");
                sb.AppendLine("    <ImplicitUsings>disable</ImplicitUsings>");
                sb.AppendLine("    <AssemblyName>" + assemblyName + "</AssemblyName>");
                sb.AppendLine("    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>");
                sb.AppendLine("  </PropertyGroup>");
                sb.AppendLine("  <ItemGroup>");
                for (int i = 0; i < classNames.Count; i++)
                {
                    sb.AppendLine("    <Compile Include=\"" + classNames[i] + ".cs\" />");
                }
                sb.AppendLine("  </ItemGroup>");
                sb.AppendLine("  <ItemGroup>");
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                if (Directory.Exists(baseDir))
                {
                    string[] dlls = Directory.GetFiles(baseDir, "*.dll", SearchOption.TopDirectoryOnly);
                    Array.Sort(dlls, StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < dlls.Length; i++)
                    {
                        string name = Path.GetFileNameWithoutExtension(dlls[i]);
                        sb.AppendLine("    <Reference Include=\"" + name + "\">");
                        sb.AppendLine("      <HintPath>" + Path.GetFullPath(dlls[i]) + "</HintPath>");
                        sb.AppendLine("    </Reference>");
                    }
                }
                sb.AppendLine("  </ItemGroup>");
                sb.AppendLine("</Project>");
                string csprojPath = Path.Combine(tempDir, assemblyName + ".csproj");
                File.WriteAllText(csprojPath, sb.ToString(), Encoding.UTF8);

                // [段3] dotnet build
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = "build \"" + csprojPath + "\" -c Release -o \"" + tempDir + "\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                Process? process = null;
                string stdout = "";
                string stderr = "";
                try
                {
                    process = Process.Start(psi);
                    if (process == null)
                    {
                        Console.WriteLine("构建失败: 无法启动 dotnet build 进程");
                        return false;
                    }
                    stdout = process.StandardOutput.ReadToEnd();
                    stderr = process.StandardError.ReadToEnd();
                    process.WaitForExit(60000);
                    if (process.ExitCode != 0)
                    {
                        Console.WriteLine("构建失败: C# 编译错误");
                        Console.WriteLine(stdout);
                        Console.WriteLine(stderr);
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("构建失败: dotnet build 执行失败——" + ex.Message);
                    return false;
                }
                finally
                {
                    if (process != null)
                    {
                        process.Dispose();
                    }
                }

                // [段4] 复制产物
                string builtDll = Path.Combine(tempDir, assemblyName + ".dll");
                if (!File.Exists(builtDll))
                {
                    Console.WriteLine("构建失败: 未找到输出 DLL——" + builtDll);
                    return false;
                }
                File.Copy(builtDll, dllPath, true);
                return true;
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempDir))
                    {
                        Directory.Delete(tempDir, true);
                    }
                }
                catch
                {
                    // 临时目录清理失败不影响结果
                }
            }
        }

        /// <summary>
        /// 从 mauproj 所在目录向上查找仓库根——含 Mau.sln 的目录（不依赖 CWD）
        /// </summary>
        /// <param name="mauprojPath">mauproj 文件路径</param>
        /// <returns>仓库根或空</returns>
        private static string? FindWorkspaceRootFrom(string mauprojPath)
        {
            DirectoryInfo? dir = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(mauprojPath)) ?? ".");
            while (dir != null)
            {
                string sln = Path.Combine(dir.FullName, "Mau.sln");
                if (File.Exists(sln))
                {
                    return dir.FullName;
                }
                dir = dir.Parent;
            }
            return null;
        }

        /// <summary>
        /// 读取 .mau 文件头 Mau 版本——首行 Mau &lt;版本&gt;
        /// </summary>
        /// <param name="mauPath">.mau 文件路径</param>
        /// <returns>版本号或 unknown</returns>
        private static string ReadMauVersion(string mauPath)
        {
            try
            {
                string[] lines = File.ReadAllLines(mauPath);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.StartsWith("Mau "))
                    {
                        return line.Substring(4).Trim();
                    }
                }
            }
            catch
            {
                // 读取失败不阻塞导出
            }
            return "unknown";
        }

        /// <summary>
        /// 读取 Mau.Runtime 程序集版本——manifest baseVersion 字段
        /// </summary>
        /// <returns>版本号或 unknown</returns>
        private static string ReadBaseVersion()
        {
            try
            {
                string runtimeDll = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Mau.Runtime.dll");
                if (File.Exists(runtimeDll))
                {
                    System.Reflection.AssemblyName name = System.Reflection.AssemblyName.GetAssemblyName(runtimeDll);
                    if (name.Version != null)
                    {
                        return name.Version.ToString();
                    }
                }
            }
            catch
            {
                // 读取失败不阻塞导出
            }
            return "unknown";
        }

        /// <summary>
        /// JSON 字符串转义——引号/反斜杠/控制字符
        /// </summary>
        /// <param name="value">原始字符串</param>
        /// <returns>转义后文本</returns>
        private static string JsonEscape(string value)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '"')
                {
                    sb.Append("\\\"");
                }
                else if (c == '\\')
                {
                    sb.Append("\\\\");
                }
                else if (c == '\n')
                {
                    sb.Append("\\n");
                }
                else if (c == '\r')
                {
                    sb.Append("\\r");
                }
                else if (c == '\t')
                {
                    sb.Append("\\t");
                }
                else if (c < 0x20)
                {
                    sb.Append("\\u" + ((int)c).ToString("X4"));
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// manifest 文件条目——导入校验依据
    /// </summary>
    public sealed class ManifestEntry
    {
        /// <summary>
        /// 包内相对路径
        /// </summary>
        public string Path;

        /// <summary>
        /// SHA256 哈希（大写 64 位）
        /// </summary>
        public string Sha256;

        /// <summary>
        /// 构造文件条目
        /// </summary>
        public ManifestEntry()
        {
            Path = "";
            Sha256 = "";
        }
    }
}

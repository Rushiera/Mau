using System;
using System.IO;
using System.Text;
using Mau.Runtime;
using Mau.Development;
using Mau.Translator;

namespace CH4
{
    /// <summary>
    /// Program 的工具执行器分部——text-* 4 件 + mau-* 3 件直执执行器与路径/文件系统辅助（拆分自 Program.Tools.cs）。
    /// 执行入口：ExecuteTool 路由（声明表在 Program.Tools.cs）；OA 走单经 dev_cat.mau 认领线。
    /// </summary>
    public static partial class Program
    {
        // [段1] 文本工具执行器——FileSystemService 直执（Bootstrap 已 Bind；受控根 = 数据根）

        /// <summary>
        /// text-read——读取 UTF-8 文本文件
        /// </summary>
        /// <param name="argsJson">参数 JSON</param>
        /// <returns>文件内容（超长截断）</returns>
        private static string ExecTextRead(string argsJson)
        {
            string path = ExtractArg(argsJson, "path");
            if (path.Length == 0)
            {
                return "ERR|BAD_ARGS|缺少参数 path";
            }
            try
            {
                FileSystemService fs = ResolveFileSystem();
                if (fs == null)
                {
                    return "ERR|FS_UNAVAILABLE|FileSystemService 未注入";
                }
                return TrimResult(fs.ReadText(path), MaxToolResultChars);
            }
            catch (Exception ex)
            {
                return "ERR|" + ex.GetType().Name + "|" + ex.Message;
            }
        }

        /// <summary>
        /// text-write——覆写文件（含新建；原子写，UTF-8 无 BOM）
        /// </summary>
        /// <param name="argsJson">参数 JSON</param>
        /// <returns>确认文本</returns>
        private static string ExecTextWrite(string argsJson)
        {
            string path = ExtractArg(argsJson, "path");
            string content = ExtractArg(argsJson, "content");
            if (path.Length == 0)
            {
                return "ERR|BAD_ARGS|缺少参数 path";
            }
            try
            {
                FileSystemService fs = ResolveFileSystem();
                if (fs == null)
                {
                    return "ERR|FS_UNAVAILABLE|FileSystemService 未注入";
                }
                fs.WriteText(path, content);
                return "OK 已覆写: " + path + "（" + content.Length.ToString() + " 字符）";
            }
            catch (Exception ex)
            {
                return "ERR|" + ex.GetType().Name + "|" + ex.Message;
            }
        }

        /// <summary>
        /// text-append——追加文本到文件末尾（自动创建父目录）
        /// </summary>
        /// <param name="argsJson">参数 JSON</param>
        /// <returns>确认文本</returns>
        private static string ExecTextAppend(string argsJson)
        {
            string path = ExtractArg(argsJson, "path");
            string content = ExtractArg(argsJson, "content");
            if (path.Length == 0)
            {
                return "ERR|BAD_ARGS|缺少参数 path";
            }
            try
            {
                FileSystemService fs = ResolveFileSystem();
                if (fs == null)
                {
                    return "ERR|FS_UNAVAILABLE|FileSystemService 未注入";
                }
                fs.AppendText(path, content);
                return "OK 已追加: " + path + "（+" + content.Length.ToString() + " 字符）";
            }
            catch (Exception ex)
            {
                return "ERR|" + ex.GetType().Name + "|" + ex.Message;
            }
        }

        /// <summary>
        /// text-replace——替换全部出现处并原子写回（old 未找到报错）
        /// </summary>
        /// <param name="argsJson">参数 JSON</param>
        /// <returns>替换数量确认</returns>
        private static string ExecTextReplace(string argsJson)
        {
            string path = ExtractArg(argsJson, "path");
            string oldText = ExtractArg(argsJson, "old");
            string newText = ExtractArg(argsJson, "new");
            if (path.Length == 0 || oldText.Length == 0)
            {
                return "ERR|BAD_ARGS|缺少参数 path 或 old";
            }
            try
            {
                FileSystemService fs = ResolveFileSystem();
                if (fs == null)
                {
                    return "ERR|FS_UNAVAILABLE|FileSystemService 未注入";
                }
                int count = fs.ReplaceText(path, oldText, newText);
                if (count == 0)
                {
                    return "ERR|NOT_FOUND|文件 " + path + " 中未找到目标文本";
                }
                return "OK 替换完成: " + count.ToString() + " 处（" + path + "）";
            }
            catch (Exception ex)
            {
                return "ERR|" + ex.GetType().Name + "|" + ex.Message;
            }
        }

        // [段2] Mau 自查执行器——B3 落地（mau-verify 直调 MauCompilerV3；mau-gen/proj 走 MauGroupBuilder 共享服务）

        /// <summary>
        /// mau-verify——Mau 语料全链检查（B3 实装——MauCompilerV3 进程内直调，零产出）
        /// </summary>
        /// <param name="argsJson">参数 JSON</param>
        /// <returns>诊断文本（文件:行:错误码:消息）</returns>
        private static string ExecMauVerify(string argsJson)
        {
            string file = ExtractArg(argsJson, "file");
            if (file.Length == 0)
            {
                return "ERR|BAD_ARGS|缺少参数 file";
            }
            string abs = ResolveRepoPath(file);
            if (abs.Length == 0)
            {
                return "ERR|PATH_ESCAPE|路径越界（仅允许仓库根内）: " + file;
            }
            if (!File.Exists(abs))
            {
                return "ERR|NOT_FOUND|文件不存在: " + abs;
            }
            try
            {
                string source = File.ReadAllText(abs);
                string flowName = MauGroupBuilder.FlowNameFromPath(abs);
                CompileResultV3 result = MauCompilerV3.Compile(source, flowName);
                if (result.Success)
                {
                    StringBuilder sb = new StringBuilder();
                    sb.Append("OK 验证通过: " + file + " → FL_" + flowName);
                    for (int i = 0; i < result.Reports.Count; i++)
                    {
                        sb.Append(Environment.NewLine);
                        sb.Append("报告: " + result.Reports[i]);
                    }
                    return sb.ToString();
                }
                StringBuilder err = new StringBuilder();
                err.Append("FAIL|VALIDATE|" + file);
                for (int i = 0; i < result.Diagnostics.Count; i++)
                {
                    MauDiagnostic d = result.Diagnostics[i];
                    err.Append(Environment.NewLine);
                    err.Append(file + ":" + d.Line + ": " + d.Code + ": " + d.Message);
                }
                return err.ToString();
            }
            catch (Exception ex)
            {
                return "ERR|" + ex.GetType().Name + "|" + ex.Message;
            }
        }

        /// <summary>
        /// mau-gen——组翻译中间产物（B3 实装——MauGroupBuilder 共享服务，不编译）
        /// </summary>
        /// <param name="argsJson">参数 JSON</param>
        /// <returns>结果文本（步骤日志 + 失败诊断）</returns>
        private static string ExecMauGen(string argsJson)
        {
            string proj = ExtractArg(argsJson, "proj");
            if (proj.Length == 0)
            {
                return "ERR|BAD_ARGS|缺少参数 proj";
            }
            return RunGroupBuild(proj, false);
        }

        /// <summary>
        /// mau-proj——组翻译 + 编译（B3 实装——MauGroupBuilder 共享服务 + dotnet build）
        /// </summary>
        /// <param name="argsJson">参数 JSON</param>
        /// <returns>结果文本（步骤日志 + 失败诊断）</returns>
        private static string ExecMauProj(string argsJson)
        {
            string proj = ExtractArg(argsJson, "proj");
            if (proj.Length == 0)
            {
                return "ERR|BAD_ARGS|缺少参数 proj";
            }
            string buildRaw = ExtractArg(argsJson, "build");
            bool doBuild = buildRaw == "true" || buildRaw == "True" || buildRaw == "1";
            return RunGroupBuild(proj, doBuild);
        }

        /// <summary>
        /// 组翻译共享执行——路径解析 → MauGroupBuilder.Build → 结果文本
        /// </summary>
        /// <param name="projParam">.mauproj 相对仓库根路径</param>
        /// <param name="doBuild">true=翻译后 dotnet build</param>
        /// <returns>结果文本（步骤日志 + 失败诊断）</returns>
        private static string RunGroupBuild(string projParam, bool doBuild)
        {
            string abs = ResolveRepoPath(projParam);
            if (abs.Length == 0)
            {
                return "ERR|PATH_ESCAPE|路径越界（仅允许仓库根内）: " + projParam;
            }
            if (!File.Exists(abs))
            {
                return "ERR|NOT_FOUND|文件不存在: " + abs;
            }
            try
            {
                string root = ResolveDataRoot();
                MauProjParseResult parsed = MauProjFile.Load(abs);
                if (parsed.Error.Length > 0)
                {
                    return "FAIL|MAUPROJ|" + parsed.Error;
                }
                MauProjFile proj = parsed.File!;
                string srcDir = Path.Combine(root, "public", "src", proj.Name);
                string dllDir = Path.Combine(root, "public", "app", "Flows");
                MauGroupBuildResult result = MauGroupBuilder.Build(abs, srcDir, dllDir, doBuild);
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < result.Steps.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(Environment.NewLine);
                    }
                    sb.Append(result.Steps[i]);
                }
                if (!result.Success)
                {
                    for (int i = 0; i < result.FailDiagnostics.Count; i++)
                    {
                        sb.Append(Environment.NewLine);
                        sb.Append(result.FailDiagnostics[i]);
                    }
                    if (result.Error.Length > 0)
                    {
                        sb.Append(Environment.NewLine);
                        sb.Append("FAIL|GEN|" + result.Error);
                    }
                    return sb.ToString();
                }
                return sb.ToString();
            }
            catch (Exception ex)
            {
                return "ERR|" + ex.GetType().Name + "|" + ex.Message;
            }
        }

        /// <summary>
        /// 相对仓库根路径解析——拼接数据根并防越界（GetFullPath 后必须仍在仓库根内）
        /// </summary>
        /// <param name="relPath">相对仓库根路径</param>
        /// <returns>绝对路径（越界返回空串）</returns>
        private static string ResolveRepoPath(string relPath)
        {
            // P8.5b 命名空间前缀兼容——mau: 映射到仓库根（与 text.* id: 语义一致；mau.* 工具默认基准=仓库根）
            if (relPath.StartsWith("mau:", StringComparison.Ordinal))
            {
                relPath = relPath.Substring(4);
            }
            string root = ResolveDataRoot();
            string full = Path.GetFullPath(Path.Combine(root, relPath));
            if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return "";
            }
            return full;
        }

        /// <summary>
        /// 解析 FileSystemService——DataBox 服务解析（未注入返回 null）
        /// </summary>
        /// <returns>文件系统服务或 null</returns>
        private static FileSystemService ResolveFileSystem()
        {
            FileSystemService fs;
            if (DataBox.TryResolve<FileSystemService>(out fs))
            {
                return fs;
            }
            return null;
        }

        /// <summary>
        /// 工具结果截断——超长文本保留头部 + 截断提示（上下文防爆）
        /// </summary>
        /// <param name="text">原文</param>
        /// <param name="max">上限字符数</param>
        /// <returns>截断文本</returns>
        private static string TrimResult(string text, int max)
        {
            if (text == null || text.Length == 0)
            {
                return "";
            }
            if (text.Length <= max)
            {
                return text;
            }
            return text.Substring(0, max) + System.Environment.NewLine + "…[截断: 共 " + text.Length.ToString() + " 字符，仅保留前 " + max.ToString() + "]";
        }
/// <summary>
/// 前端 html 根解析——源码区优先（仓库根/CatHome4/html——唯一事实源，改即生效）；回退部署区（AppContext.BaseDirectory/html——发布包）。
/// </summary>
/// <returns>html 根目录</returns>
internal static string ResolveHtmlRoot()
{
    string root = FindRepoRoot(AppContext.BaseDirectory);
    if (root.Length > 0)
    {
        string src = System.IO.Path.Combine(root, "CatHome4", "html");
        if (System.IO.Directory.Exists(src))
        {
            return src;
        }
    }

    return System.IO.Path.Combine(AppContext.BaseDirectory, "html");
}    }
}

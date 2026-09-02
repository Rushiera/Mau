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
                return TrimResult(fs.ReadTextAuto(path), MaxToolResultChars);
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
                fs.WriteTextAuto(path, content);
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
                fs.AppendTextAuto(path, content);
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
            string mode = ExtractArg(argsJson, "mode");
            if (mode.Length == 0)
            {
                mode = "exact";
            }
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
                TextReplaceOutcome outcome = fs.ReplaceTextAuto(path, oldText, newText, mode);
                if (outcome.Status == TextReplaceStatus.NotFound)
                {
                    return "ERR|ANCHOR_NOT_FOUND|第 " + outcome.DiffByteIndex.ToString() + " 字节 期望「" + outcome.Expected + "」实际「" + outcome.Actual + "」";
                }
                if (outcome.Status == TextReplaceStatus.Ambiguous)
                {
                    return "ERR|ANCHOR_AMBIGUOUS|锚点出现 " + outcome.CandidateLines.Length.ToString() + " 次以上，候选行: " + string.Join(",", outcome.CandidateLines);
                }
                return "OK 替换完成: " + outcome.Count.ToString() + " 处（" + path + "）\n--目标段--\n" + outcome.Snippet;
            }
            catch (Exception ex)
            {
                return "ERR|" + ex.GetType().Name + "|" + ex.Message;
            }
        }
        // [段1b] text-* v2 扩展执行器（design-ch4-text-tools.md C4——检索面/区间读/文件管理）

        /// <summary>
        /// text-read_lines——按行号区间读取（1 起；end=0 读至文件尾；编码自动探测）
        /// </summary>
        /// <param name="argsJson">参数 JSON（path/start/end）</param>
        /// <returns>带行号文本</returns>
        private static string ExecTextReadLines(string argsJson)
        {
            string path = ExtractArg(argsJson, "path");
            string startRaw = ExtractArg(argsJson, "start");
            string endRaw = ExtractArg(argsJson, "end");
            if (path.Length == 0)
            {
                return "ERR|BAD_ARGS|缺少参数 path";
            }
            int start = 1;
            int end = 0;
            if (startRaw.Length > 0 && !int.TryParse(startRaw, out start))
            {
                return "ERR|BAD_ARGS|参数 start 非整数: " + startRaw;
            }
            if (endRaw.Length > 0 && !int.TryParse(endRaw, out end))
            {
                return "ERR|BAD_ARGS|参数 end 非整数: " + endRaw;
            }
            if (start < 1)
            {
                return "ERR|BAD_ARGS|参数 start 必须 ≥1";
            }
            try
            {
                FileSystemService fs = ResolveFileSystem();
                if (fs == null)
                {
                    return "ERR|FS_UNAVAILABLE|FileSystemService 未注入";
                }
                return fs.ReadLines(path, start, end);
            }
            catch (Exception ex)
            {
                return "ERR|" + ex.GetType().Name + "|" + ex.Message;
            }
        }

        /// <summary>
        /// text-read_between——锚点区间读取（str1 空=文件头 / str2 空=文件尾；锚点唯一契约）
        /// </summary>
        /// <param name="argsJson">参数 JSON（path/str1/str2）</param>
        /// <returns>区间内容（锚点歧义/缺失返回 ERR| 前缀）</returns>
        private static string ExecTextReadBetween(string argsJson)
        {
            string path = ExtractArg(argsJson, "path");
            string str1 = ExtractArg(argsJson, "str1");
            string str2 = ExtractArg(argsJson, "str2");
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
                return TrimResult(fs.ReadBetweenAuto(path, str1, str2), MaxToolResultChars);
            }
            catch (Exception ex)
            {
                return "ERR|" + ex.GetType().Name + "|" + ex.Message;
            }
        }

        /// <summary>
        /// text-tree——目录树（depth ≤10；limit ≤10000；稳定排序）
        /// </summary>
        /// <param name="argsJson">参数 JSON（path/depth/limit）</param>
        /// <returns>相对路径列表</returns>
        private static string ExecTextTree(string argsJson)
        {
            string path = ExtractArg(argsJson, "path");
            if (path.Length == 0)
            {
                return "ERR|BAD_ARGS|缺少参数 path";
            }
            int depth = 2;
            int limit = 500;
            string depthRaw = ExtractArg(argsJson, "depth");
            if (depthRaw.Length > 0 && !int.TryParse(depthRaw, out depth))
            {
                return "ERR|BAD_ARGS|参数 depth 非整数: " + depthRaw;
            }
            string limitRaw = ExtractArg(argsJson, "limit");
            if (limitRaw.Length > 0 && !int.TryParse(limitRaw, out limit))
            {
                return "ERR|BAD_ARGS|参数 limit 非整数: " + limitRaw;
            }
            try
            {
                FileSystemService fs = ResolveFileSystem();
                if (fs == null)
                {
                    return "ERR|FS_UNAVAILABLE|FileSystemService 未注入";
                }
                string[] rows = fs.Tree(path, depth, limit);
                if (rows == null || rows.Length == 0)
                {
                    return "（空目录）";
                }
                return string.Join(System.Environment.NewLine, rows);
            }
            catch (Exception ex)
            {
                return "ERR|" + ex.GetType().Name + "|" + ex.Message;
            }
        }

        /// <summary>
        /// text-find——文件名 glob 搜索（pattern 默认 *；recursive 默认 true）
        /// </summary>
        /// <param name="argsJson">参数 JSON（dir/pattern/recursive/limit）</param>
        /// <returns>相对路径列表</returns>
        private static string ExecTextFind(string argsJson)
        {
            string dir = ExtractArg(argsJson, "dir");
            if (dir.Length == 0)
            {
                return "ERR|BAD_ARGS|缺少参数 dir";
            }
            string pattern = ExtractArg(argsJson, "pattern");
            if (pattern.Length == 0)
            {
                pattern = "*";
            }
            string recRaw = ExtractArg(argsJson, "recursive");
            bool recursive = recRaw.Length == 0 || recRaw == "true" || recRaw == "1";
            int limit = 500;
            string limitRaw = ExtractArg(argsJson, "limit");
            if (limitRaw.Length > 0 && !int.TryParse(limitRaw, out limit))
            {
                return "ERR|BAD_ARGS|参数 limit 非整数: " + limitRaw;
            }
            try
            {
                FileSystemService fs = ResolveFileSystem();
                if (fs == null)
                {
                    return "ERR|FS_UNAVAILABLE|FileSystemService 未注入";
                }
                string[] rows = fs.Find(dir, pattern, recursive, limit);
                if (rows == null || rows.Length == 0)
                {
                    return "（未找到匹配文件）";
                }
                return string.Join(System.Environment.NewLine, rows);
            }
            catch (Exception ex)
            {
                return "ERR|" + ex.GetType().Name + "|" + ex.Message;
            }
        }

        /// <summary>
        /// text-grep——内容关键词搜索（受控根内；返回 相对路径:行号:上下文）
        /// </summary>
        /// <param name="argsJson">参数 JSON（dir/keyword/pattern/limit）</param>
        /// <returns>匹配行列表</returns>
        private static string ExecTextGrep(string argsJson)
        {
            string dir = ExtractArg(argsJson, "dir");
            string keyword = ExtractArg(argsJson, "keyword");
            if (dir.Length == 0 || keyword.Length == 0)
            {
                return "ERR|BAD_ARGS|缺少参数 dir 或 keyword";
            }
            string pattern = ExtractArg(argsJson, "pattern");
            if (pattern.Length == 0)
            {
                pattern = "*";
            }
            int limit = 200;
            string limitRaw = ExtractArg(argsJson, "limit");
            if (limitRaw.Length > 0 && !int.TryParse(limitRaw, out limit))
            {
                return "ERR|BAD_ARGS|参数 limit 非整数: " + limitRaw;
            }
            try
            {
                FileSystemService fs = ResolveFileSystem();
                if (fs == null)
                {
                    return "ERR|FS_UNAVAILABLE|FileSystemService 未注入";
                }
                string[] rows = fs.Grep(dir, keyword, pattern, limit);
                if (rows == null || rows.Length == 0)
                {
                    return "（无匹配）";
                }
                return string.Join(System.Environment.NewLine, rows);
            }
            catch (Exception ex)
            {
                return "ERR|" + ex.GetType().Name + "|" + ex.Message;
            }
        }

        /// <summary>
        /// text-move——移动/重命名（文件与目录均支持；自动建父目录；目标存在拒绝）
        /// </summary>
        /// <param name="argsJson">参数 JSON（src/dest）</param>
        /// <returns>确认文本</returns>
        private static string ExecTextMove(string argsJson)
        {
            string src = ExtractArg(argsJson, "src");
            string dest = ExtractArg(argsJson, "dest");
            if (src.Length == 0 || dest.Length == 0)
            {
                return "ERR|BAD_ARGS|缺少参数 src 或 dest";
            }
            try
            {
                FileSystemService fs = ResolveFileSystem();
                if (fs == null)
                {
                    return "ERR|FS_UNAVAILABLE|FileSystemService 未注入";
                }
                fs.Move(src, dest);
                return "OK 已移动: " + src + " → " + dest;
            }
            catch (Exception ex)
            {
                return "ERR|" + ex.GetType().Name + "|" + ex.Message;
            }
        }

        /// <summary>
        /// text-delete——软删除（移入受控回收站；文件与空目录；可恢复）
        /// </summary>
        /// <param name="argsJson">参数 JSON（path）</param>
        /// <returns>回收站路径确认</returns>
        private static string ExecTextDelete(string argsJson)
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
                string target = fs.Recycle(path);
                return "OK 已软删除 → " + target;
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
                string root = ResolveMauRoot();
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
        /// 相对仓库根路径解析——受控根索引优先（workspace.json id=mau 根），防越界（GetFullPath 后必须仍在根内）。
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
            string root = ResolveMauRoot();
            string full = Path.GetFullPath(Path.Combine(root, relPath));
            if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return "";
            }
            return full;
        }

        /// <summary>
        /// Mau 仓库根解析——受控根索引优先（workspace.json 中 id=mau 的根，部署实例也配置 → 工具始终可用）；
        /// 回退仓库根探测（就地开发）；再回退数据根。
        /// </summary>
        /// <returns>Mau 仓库根目录</returns>
        private static string ResolveMauRoot()
        {
            // [段1] 受控根索引——workspace.json roots 中 id=mau（部署实例配置 mau 根 → mau-* 工具可用）
            WorkspaceConfig ws = null;
            if (DataBox.TryResolve<WorkspaceConfig>(out ws) && ws != null)
            {
                for (int i = 0; i < ws.Roots.Length; i++)
                {
                    if (string.Equals(ws.Roots[i].Id, "mau", StringComparison.Ordinal))
                    {
                        return ws.Roots[i].Path;
                    }
                }
            }
            // [段2] 仓库根探测——就地开发环境兜底（FindRepoRoot 从 exe 所在目录向上找 Mau.sln）
            string root = FindRepoRoot(AppContext.BaseDirectory);
            if (root.Length > 0)
            {
                return root;
            }
            // [段3] 数据根兜底——未配置 mau 根且无仓库（退化行为）
            return ResolveDataRoot();
        }

        /// <summary>
        /// 解析 FileSystemService——DataBox 服务解析（未注入返回 null）
        /// </summary>
        /// <returns>文件系统服务或 null</returns>
        private static FileSystemService ResolveFileSystem()
        {
            // M4e 猫级白名单——当前猫上下文优先（工具执行链设置；无猫上下文回退全局）
            FileSystemService catFs = ToolCatContext.ResolveCatFileSystem();
            if (catFs != null)
            {
                return catFs;
            }
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

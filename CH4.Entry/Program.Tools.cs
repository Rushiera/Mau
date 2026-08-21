using System;
using System.IO;
using System.Text;
using Mau.Runtime;
using Mau.Development;
using Mau.Translator;

namespace CH4
{
    /// <summary>
    /// Program 的工具执行面分部——P8 工具组（一期：宿主直执，无 OA）。
    /// 归属：agent 循环基建（宿主侧）——工具声明表 + 执行器路由 + 文本/Mau 执行器。
    /// 二期：执行器不变，入口换 OA 工单（dev_cat.mau 认领）；三期：Roslyn cs.* 域落本面。
    /// </summary>
    public static partial class Program
    {
        /// <summary>
        /// 工具结果截断上限——回传 LLM 上下文防爆（text.read 大文件场景）
        /// </summary>
        private const int MaxToolResultChars = 20000;

        /// <summary>
        /// 构建工具声明表——P8 一期 7 件（文本 4 + Mau 自查 3；ask/read_file 退役——二期待 OA 恢复）
        /// </summary>
        /// <returns>工具数组</returns>
        private static ToolSpec[] BuildToolSpecs()
        {
            ToolSpec[] specs = new ToolSpec[]
            {
                new ToolSpec("text.read", "读取 UTF-8 文本文件（受控根内；路径支持 id:相对路径——mau:corpus/...=仓库根 / ccbp:...=知识库 / runtime:...=数据根，或绝对路径），返回完整内容", "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"要读取的文件路径（支持 mau:/ccbp: 前缀）\"}},\"required\":[\"path\"]}"),
                new ToolSpec("text.write", "覆写文件（含新建）——整文件替换为 content（路径支持 id: 前缀同 text.read）", "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"目标文件路径\"},\"content\":{\"type\":\"string\",\"description\":\"完整新内容\"}},\"required\":[\"path\",\"content\"]}"),
                new ToolSpec("text.append", "追加文本到文件末尾（文件不存在则新建；路径支持 id: 前缀同 text.read）", "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"目标文件路径\"},\"content\":{\"type\":\"string\",\"description\":\"要追加的文本\"}},\"required\":[\"path\",\"content\"]}"),
                new ToolSpec("text.replace", "替换文本——old 全部出现处替换为 new，返回替换数量；未找到报错（路径支持 id: 前缀同 text.read）", "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"目标文件路径\"},\"old\":{\"type\":\"string\",\"description\":\"要查找的旧文本\"},\"new\":{\"type\":\"string\",\"description\":\"替换后的新文本\"}},\"required\":[\"path\",\"old\",\"new\"]}"),
                new ToolSpec("mau.verify", "Mau 语料全链检查（词法→解析→验证→分析），返回诊断（文件:行:错误码:消息）；零产出", "{\"type\":\"object\",\"properties\":{\"file\":{\"type\":\"string\",\"description\":\".mau 文件路径\"}},\"required\":[\"file\"]}"),
                new ToolSpec("mau.gen", "组翻译——.mauproj 组声明 → 中间产物（验证全组 + BRIKGROUP.cs + FL_*.cs）；不编译", "{\"type\":\"object\",\"properties\":{\"proj\":{\"type\":\"string\",\"description\":\".mauproj 文件路径\"}},\"required\":[\"proj\"]}"),
                new ToolSpec("mau.proj", "组翻译 + 编译——.mauproj → Flows/FL_<组>.dll（长耗时；产物可在宿主热重载）", "{\"type\":\"object\",\"properties\":{\"proj\":{\"type\":\"string\",\"description\":\".mauproj 文件路径\"},\"build\":{\"type\":\"boolean\",\"description\":\"true=翻译后执行 dotnet build\"}},\"required\":[\"proj\"]}"),
                new ToolSpec("host.reload", "热重载语料 dll（宿主级）——在 mau.proj 编译成功后单独调用（建议下一轮）；事务三段式：加载失败保留旧版本；cat=quick|dev", "{\"type\":\"object\",\"properties\":{\"cat\":{\"type\":\"string\",\"description\":\"quick|dev\"}},\"required\":[\"cat\"]}"),
                // P8 三期——Roslyn cs.* 编码工具域（9 件——经 ICSharpBridge / MauRoslynBridge 调度；path=受控根内 csproj 或项目目录）
                new ToolSpec("cs.check", "C# 语义快查——项目语法树诊断（增量/毫秒级）；full=true 含警告；实机裁决走 cs.build", "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"csproj 路径或项目目录（受控根内）\"},\"full\":{\"type\":\"boolean\",\"description\":\"true=输出全部警告\"}},\"required\":[\"path\"]}"),
                new ToolSpec("cs.build", "C# 实机编译——dotnet build 子进程（唯一权威裁决；成功后引用集自动刷新）", "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"csproj 路径或项目目录\"}},\"required\":[\"path\"]}"),
                new ToolSpec("cs.list", "类/成员签名清单（语法层；class 空=全项目类清单）", "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"csproj 路径或项目目录\"},\"class\":{\"type\":\"string\",\"description\":\"类名（空=全项目）\"}},\"required\":[\"path\"]}"),
                new ToolSpec("cs.read", "成员源码 + 方法内行号标注（补丁锚点依据；member 空=类概览）", "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"csproj 路径或项目目录\"},\"class\":{\"type\":\"string\",\"description\":\"类名\"},\"member\":{\"type\":\"string\",\"description\":\"成员名（空=类概览）\"}},\"required\":[\"path\",\"class\"]}"),
                new ToolSpec("cs.find_ref", "成员全引用（含重载全匹配；语义级）", "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"csproj 路径或项目目录\"},\"class\":{\"type\":\"string\",\"description\":\"类名\"},\"member\":{\"type\":\"string\",\"description\":\"成员名\"}},\"required\":[\"path\",\"class\",\"member\"]}"),
                new ToolSpec("cs.patch", "方法体级替换（锚点=类+方法名；body 完整含大括号）——三态：OK 落盘 / ROLLED_BACK 未落盘+诊断 / ERR", "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"csproj 路径或项目目录\"},\"class\":{\"type\":\"string\",\"description\":\"类名\"},\"method\":{\"type\":\"string\",\"description\":\"方法名\"},\"body\":{\"type\":\"string\",\"description\":\"新方法体（含大括号）\"}},\"required\":[\"path\",\"class\",\"method\",\"body\"]}"),
                new ToolSpec("cs.member", "成员增删改——op=insert(增)/delete(删)/rename(改名 全项目引用同步)", "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"csproj 路径或项目目录\"},\"class\":{\"type\":\"string\",\"description\":\"类名\"},\"op\":{\"type\":\"string\",\"description\":\"insert|delete|rename\"},\"position\":{\"type\":\"string\",\"description\":\"insert 用：end|before|after|after_fields\"},\"anchor\":{\"type\":\"string\",\"description\":\"before/after 用：锚点成员名\"},\"code\":{\"type\":\"string\",\"description\":\"insert 用：完整成员声明源码\"},\"oldName\":{\"type\":\"string\",\"description\":\"rename 用：旧成员名\"},\"newName\":{\"type\":\"string\",\"description\":\"rename 用：新成员名\"}},\"required\":[\"path\",\"class\",\"op\"]}"),
                new ToolSpec("cs.comment", "XML 注释增改——type=summary/param/returns（param 需 param=参数名）", "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"csproj 路径或项目目录\"},\"class\":{\"type\":\"string\",\"description\":\"类名\"},\"member\":{\"type\":\"string\",\"description\":\"成员名（空=类）\"},\"type\":{\"type\":\"string\",\"description\":\"summary|param|returns\"},\"text\":{\"type\":\"string\",\"description\":\"注释文本\"},\"param\":{\"type\":\"string\",\"description\":\"type=param 时的参数名\"}},\"required\":[\"path\",\"class\",\"type\",\"text\"]}"),
new ToolSpec("cs.dead", "零引用成员扫描（private/internal；public/override 跳过）", "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"csproj 路径或项目目录\"}},\"required\":[\"path\"]}"),
                // P8.5d——config.* 配置自改工具组（4 件——schema 白名单写：config.set/reset 仅 writable 项；唯一校验实现 ConfigStore.SetChecked）
                new ToolSpec("config.list", "配置全览——schema 全部条目（键/当前值/来源/schema 默认/敏感/可写/值域/描述）；敏感键掩码", "{\"type\":\"object\",\"properties\":{}}"),
                new ToolSpec("config.get", "配置单项查询——按 schema 键返回（含默认/敏感/可写/值域/描述）；敏感键掩码", "{\"type\":\"object\",\"properties\":{\"key\":{\"type\":\"string\",\"description\":\"配置键（如 ui.chat_font_size）\"}},\"required\":[\"key\"]}"),
                new ToolSpec("config.set", "配置写入——仅 schema 声明且 writable=true 的项（白名单+值域校验+原子写+失败回滚）；llm.* 私密环境变量只读", "{\"type\":\"object\",\"properties\":{\"key\":{\"type\":\"string\",\"description\":\"配置键\"},\"value\":{\"type\":\"string\",\"description\":\"新值（掩码值拒绝）\"}},\"required\":[\"key\",\"value\"]}"),
                new ToolSpec("config.reset", "配置还原默认——key 空=全群 writable 项还原 schema default；key 非空=单项", "{\"type\":\"object\",\"properties\":{\"key\":{\"type\":\"string\",\"description\":\"配置键（空=全群）\"}},\"required\":[]}")
            };
            return specs;
        }

        /// <summary>
        /// 工具执行器路由——按工具名调度（一期直执；未知工具 ERR）
        /// </summary>
        /// <param name="name">工具名</param>
        /// <param name="argsJson">参数 JSON（展平）</param>
        /// <returns>执行结果（失败 ERR| 前缀——错误可见性）</returns>
        private static string ExecuteTool(string name, string argsJson)
        {
            if (name == "text.read")
            {
                return ExecTextRead(argsJson);
            }
            if (name == "text.write")
            {
                return ExecTextWrite(argsJson);
            }
            if (name == "text.append")
            {
                return ExecTextAppend(argsJson);
            }
            if (name == "text.replace")
            {
                return ExecTextReplace(argsJson);
            }
            if (name == "mau.verify")
            {
                return ExecMauVerify(argsJson);
            }
            if (name == "mau.gen")
            {
                return ExecMauGen(argsJson);
            }
            if (name == "mau.proj")
            {
                return ExecMauProj(argsJson);
            }
            if (name == "host.reload")
            {
                return ExecHostReload(argsJson);
            }
            if (name.StartsWith("cs.", StringComparison.Ordinal))
            {
                return ExecCSharpTool(name, argsJson);
            }
            return "ERR|UNKNOWN_TOOL|未知工具: " + name;
        }

        // [段3] C# 工具桥执行器——P8 三期（cs.* 9 件经 ICSharpBridge/MauRoslynBridge 调度：磁盘权威 + 三态缓存 + 回滚保护）

        /// <summary>
        /// cs.* 统一执行——桥内分派（method = 工具名去 cs. 前缀）；结果截断防爆
        /// </summary>
        /// <param name="name">工具名（cs.check 等）</param>
        /// <param name="argsJson">参数整包 JSON</param>
        /// <returns>桥结果文本（OK/ROLLED_BACK/ERR| 语义）</returns>
        private static string ExecCSharpTool(string name, string argsJson)
        {
            Mau.Runtime.ICSharpBridge bridge;
            if (!DataBox.TryResolve<Mau.Runtime.ICSharpBridge>(out bridge))
            {
                return "ERR|CSHARP_NO_BRIDGE|宿主未注入 ICSharpBridge（Bootstrap 需 Bind MauRoslynBridge）";
            }
            string method = name.Substring(3);
            string result;
            bool ok = bridge.Invoke(method, argsJson, out result);
            if (!ok || result == null || result.Length == 0)
            {
                return "ERR|BRIDGE_FAIL|cs." + method + " 调用失败（" + (result ?? "空结果") + "）";
            }
            return TrimResult(result, MaxToolResultChars);
        }

        // [段1] 文本工具执行器——FileSystemService 直执（Bootstrap 已 Bind；受控根 = 数据根）

        /// <summary>
        /// text.read——读取 UTF-8 文本文件
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
        /// text.write——覆写文件（含新建；原子写，UTF-8 无 BOM）
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
        /// text.append——追加文本到文件末尾（自动创建父目录）
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
        /// text.replace——替换全部出现处并原子写回（old 未找到报错）
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

        // [段2] Mau 自查执行器——B3 落地（mau.verify 直调 MauCompilerV3；mau.gen/proj 需 MauProjFile 下沉共享库）

        /// <summary>
        /// mau.verify——Mau 语料全链检查（B3 实装——MauCompilerV3 进程内直调，零产出）
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
        /// mau.gen——组翻译中间产物（B3 实装——MauGroupBuilder 共享服务，不编译）
        /// </summary>
        /// <param name="argsJson">参数 JSON</param>
        /// <returns>落盘确认文本</returns>
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
        /// mau.proj——组翻译 + 编译（B3 实装——MauGroupBuilder 共享服务 + dotnet build）
        /// </summary>
        /// <param name="argsJson">参数 JSON</param>
        /// <returns>产物确认文本</returns>
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
    }
}
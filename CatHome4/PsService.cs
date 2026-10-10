using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading;
using Mau.Runtime;

namespace CH4
{
    /// <summary>
    /// PowerShell 执行服务——powershell / powershell7 工具实现（PsCat 工具组）。
    /// 解释器线（shell 参数）：powershell=默认解释器（Windows PowerShell 5.1）；powershell7=PowerShell 7
    ///   （pwsh.exe 路径读配置 ps.pwsh_path——未配置/路径不存在即明示 ERR，不静默回落默认线）。
    /// 设计动机（CH2 shell_exec 痛点反推）：
    ///   1) 编码/转义不可控 → 整段命令 UTF-16LE → Base64 走 -EncodedCommand（PS 官方免转义通道）
    ///   2) 输出乱码 → 命令前缀注入 [Console]::OutputEncoding=UTF8 + chcp 65001，宿主按 UTF-8 解码
    ///   3) 写文件损坏数据 → 词法拦截写文件语义（强制走 text-* 读写工具）；git 命令豁免
    ///   4) 进程挂死 → 禁 Start-Process / ReadKey / ReadLine；超时杀进程树（taskkill /T /F）
    ///   5) 输出截断 → stdout 16KB / stderr 8KB + truncated 标记
    /// 线程模型：同步执行（工具循环等待语义）；进程异步读流防死锁。
    /// </summary>
    public sealed class PsService : Mau.Runtime.IPsService
    {
        /// <summary>stdout 截断上限——回传 LLM 上下文防爆</summary>
        private const int MaxStdoutChars = 16384;

        /// <summary>stderr 截断上限——排错入口保留较短</summary>
        private const int MaxStderrChars = 8192;

        /// <summary>默认超时毫秒——无 timeout_ms 参数时</summary>
        private const long DefaultTimeoutMs = 30000;

        /// <summary>超时上限毫秒——与工单/后台执行层统一 120 秒口径（A48 同步）</summary>
        private const long MaxTimeoutMs = 120000;

        /// <summary>
        /// 执行 PowerShell 命令——拦截检查 → EncodedCommand 启动 → 超时/输出处理 → JSON 回执
        /// </summary>
        /// <param name="argsJson">工具参数 JSON（command/cwd/timeout_ms）</param>
        /// <param name="shell">解释器线标识（powershell=默认解释器 / powershell7=PowerShell 7）</param>
        /// <returns>结果 JSON 或 ERR| 错误文本</returns>
        public string Exec(string argsJson, string shell)
        {
            string command = ExtractArg(argsJson, "command");
            if (command.Length == 0)
            {
                return "ERR|PS_BAD_ARGS|缺少参数 command";
            }
            // [段0] 解释器解析——默认线 powershell.exe；powershell7 线读配置 ps.pwsh_path（未配置/路径不存在 → 明示 ERR，不静默回落默认线）
            string exePath = "powershell.exe";
            if (string.Equals(shell, "powershell7", StringComparison.Ordinal))
            {
                string shellError = "";
                exePath = ResolvePwshPath(out shellError);
                if (exePath.Length == 0)
                {
                    return shellError;
                }
            }
            string cwd = ExtractArg(argsJson, "cwd");
            long timeoutMs = ParseTimeout(ExtractArg(argsJson, "timeout_ms"));
            // [段1] 拦截检查——写文件语义 / Start-Process / ReadKey-ReadLine（技术错误预防，非安全拦截）
            string block = DetectForbidden(command);
            if (block.Length > 0)
            {
                return block;
            }
            // [段2] 命令前缀——UTF-8 输出内建（PS 5.1 默认 ANSI/GBK——乱码根因；chcp 65001 + OutputEncoding 双保险）+ 进度流静默（首载模块 CLIXML 噪音）
            //        powershell7 线追加 $PSStyle.OutputRendering='PlainText'（pwsh 7 专有——默认 Host 渲染给格式化输出与错误流加 ANSI 色码，LLM 消费面需纯文本）
            string prefix = "[Console]::OutputEncoding=[Text.Encoding]::UTF8; $ProgressPreference='SilentlyContinue'; chcp 65001 > $null; ";
            if (string.Equals(shell, "powershell7", StringComparison.Ordinal))
            {
                prefix = prefix + "$PSStyle.OutputRendering='PlainText'; ";
            }
            // [段2b] 根寻址前缀——猫可见受控根注册为本会话 PS 驱动器（命令内可直接写 ccbp:L1/Tree.md）
            string rootPrefix = BuildRootDrivePrefix(argsJson);
            string prefixed = prefix + rootPrefix + command;
            // [段3] EncodedCommand——UTF-16LE → Base64（免转义：引号/反斜杠/JSON 原样直达 PS 解析器）
            string base64 = Convert.ToBase64String(Encoding.Unicode.GetBytes(prefixed));
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = exePath;
            psi.Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + base64;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;
            if (cwd.Length > 0)
            {
                try
                {
                    psi.WorkingDirectory = cwd;
                }
                catch (Exception ex)
                {
                    // 工作目录无效——回退默认（不阻断执行）
                    LogStore.Add("CatHome4", 2, "PS 工作目录无效，回退默认: " + ex.Message, "SYS");
                }
            }
            // [段4] 启动 + 异步读流（异步读防管道缓冲区死锁——同步 ReadToEnd 大输出会卡）
            Process proc;
            try
            {
                proc = new Process();
                proc.StartInfo = psi;
                proc.Start();
            }
            catch (Exception ex)
            {
                return "ERR|PS_LAUNCH|" + ex.GetType().Name + "|" + ex.Message;
            }
            StringBuilder stdout = new StringBuilder();
            StringBuilder stderr = new StringBuilder();
            proc.OutputDataReceived += delegate (object sender, DataReceivedEventArgs e)
            {
                if (e.Data != null)
                {
                    AppendCapped(stdout, e.Data, MaxStdoutChars);
                }
            };
            proc.ErrorDataReceived += delegate (object sender, DataReceivedEventArgs e)
            {
                if (e.Data != null)
                {
                    AppendCapped(stderr, e.Data, MaxStderrChars);
                }
            };
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            // [段5] 超时等待——超时杀进程树（taskkill /T /F——不留僵尸）
            bool timeout = false;
            try
            {
                if (!proc.WaitForExit((int)timeoutMs))
                {
                    timeout = true;
                    KillTree(proc);
                }
            }
            catch (Exception ex)
            {
                // 等待异常（进程已退出等）——按已退出处理
                LogStore.Add("CatHome4", 2, "PS 等待进程异常，按已退出处理: " + ex.Message, "SYS");
            }
            string outText = stdout.ToString();
            string errText = CleanErrorText(stderr.ToString());
            bool outTrunc = outText.Length >= MaxStdoutChars;
            bool errTrunc = errText.Length >= MaxStderrChars;
            // [段6] 结构化回执（design-ch4-tools 附录）——首行 JSON 元数据头 + 正文定界
            //   正文 = stdout（+ 换行 + stderr）；行数在头里给出，前端按行数切分（不依赖内容分隔符——零撞车）
            int stdoutLines = CountLinesOf(outText);
            int stderrLines = CountLinesOf(errText);
            string shellName = string.Equals(shell, "powershell7", StringComparison.Ordinal) ? "powershell7" : "powershell";
            Dictionary<string, object> fields = new Dictionary<string, object>();
            int exitCode = timeout ? -1 : proc.ExitCode;
            fields["exit"] = exitCode;
            fields["truncated"] = outTrunc || errTrunc;
            fields["timeout"] = timeout;
            fields["stdoutLines"] = stdoutLines;
            fields["stderrLines"] = stderrLines;
            string bodyText = errText.Length > 0 ? (outText + "\n" + errText) : outText;
            return ToolMetaHead.With(shellName, true, shellName, -1, fields, shellName + " | 退出 " + exitCode.ToString() + " · " + stdoutLines.ToString() + " 行" + "\n" + bodyText);
        }

        /// <summary>
        /// 行数统计——空串 0；否则 \n 计数 + 1（与前端 body.split('\n').length 同口径）
        /// </summary>
        /// <param name="text">源文本</param>
        /// <returns>行数</returns>
        private static int CountLinesOf(string text)
        {
            if (text == null || text.Length == 0)
            {
                return 0;
            }
            int n = 1;
            for (int i = 0; i < text.Length; i = i + 1)
            {
                if (text[i] == '\n')
                {
                    n = n + 1;
                }
            }
            return n;
        }
        /// <summary>
        /// 根寻址前缀——猫可见受控根注册为本次 PS 会话的驱动器（New-PSDrive）。
        /// 注入后命令内可直接写 ccbp:L1/Tree.md / mau:README.md（驱动器相对路径——正反斜杠均可）。
        /// 失败出声不中断：注册异常写入错误流（重名等由管理员面黑名单规避，此处不静默、不改名）。
        /// </summary>
        /// <param name="argsJson">工具参数 JSON（catId 取猫级根集）</param>
        /// <returns>PS 前缀片段（无可注入根时空串）</returns>
        private static string BuildRootDrivePrefix(string argsJson)
        {
            string catId = ExtractArg(argsJson, "catId");
            if (catId.Length == 0)
            {
                return "";
            }
            FileSystemService fs = ToolCatContext.ResolveCatFileSystem(catId);
            if (fs == null)
            {
                return "";
            }
            // [段1] 逐根生成注册片段——try/catch 出声（注册失败不阻断命令本身）
            Dictionary<string, string> roots = fs.DescribeRoots();
            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<string, string> pair in roots)
            {
                string safeRoot = pair.Value.Replace("'", "''");
                sb.Append("try { New-PSDrive -Name ");
                sb.Append(pair.Key);
                sb.Append(" -PSProvider FileSystem -Root '");
                sb.Append(safeRoot);
                sb.Append("' -ErrorAction Stop | Out-Null } catch { Write-Error ('根寻址注册失败: ");
                sb.Append(pair.Key);
                sb.Append(" — ' + $_.Exception.Message) }; ");
            }
            return sb.ToString();
        }

        /// <summary>
        /// 解析 PowerShell 7 可执行路径——读配置 ps.pwsh_path（未配置 / 路径不存在 → error 明示，不静默回落默认解释器）
        /// </summary>
        /// <param name="error">错误文本（空=解析成功）</param>
        /// <returns>pwsh.exe 全路径（解析失败返回空串）</returns>
        private static string ResolvePwshPath(out string error)
        {
            error = "";
            Mau.Runtime.ConfigStore cfg;
            Mau.Runtime.DataBox.TryResolve<Mau.Runtime.ConfigStore>(out cfg);
            string path = "";
            if (cfg != null)
            {
                path = cfg.Get("ps.pwsh_path", "");
            }
            if (path.Length == 0)
            {
                error = "ERR|PS_PWSH_MISSING|PowerShell 7 未配置——请在配置面填写 ps.pwsh_path（pwsh.exe 全路径；获取方式 powershell -Command where.exe pwsh）";
                return "";
            }
            if (!System.IO.File.Exists(path))
            {
                error = "ERR|PS_PWSH_NOT_FOUND|配置的 PowerShell 7 路径不存在：" + path + "——请更新 ps.pwsh_path";
                return "";
            }
            return path;
        }

        /// <summary>
        /// 清洗错误文本——CLIXML 序列化解包（PowerShell stderr 重定向时原生格式）+ ANSI 色码剥离（LLM 消费面需纯文本）
        /// </summary>
        /// <param name="raw">原始 stderr 文本</param>
        /// <returns>清洗后的错误文本（无 CLIXML 壳时仅剥色）</returns>
        private static string CleanErrorText(string raw)
        {
            if (raw.Length == 0)
            {
                return raw;
            }
            string text = raw;
            // [段1] CLIXML 解包——拼接全部 <S S="Error">…</S> 片段（无 Objs 壳时原样保留）
            int objStart = text.IndexOf("<Objs", StringComparison.Ordinal);
            if (objStart >= 0)
            {
                StringBuilder sb = new StringBuilder();
                int cursor = objStart;
                while (true)
                {
                    int openTag = text.IndexOf("<S S=\"", cursor, StringComparison.Ordinal);
                    if (openTag < 0)
                    {
                        break;
                    }
                    int contentStart = text.IndexOf('>', openTag);
                    if (contentStart < 0)
                    {
                        break;
                    }
                    int contentEnd = text.IndexOf("</S>", contentStart + 1, StringComparison.Ordinal);
                    if (contentEnd < 0)
                    {
                        break;
                    }
                    sb.Append(UnescapeCliXml(text.Substring(contentStart + 1, contentEnd - contentStart - 1)));
                    cursor = contentEnd + 4;
                }
                if (sb.Length > 0)
                {
                    text = sb.ToString();
                }
            }
            // [段2] ANSI 剥离
            return StripAnsi(text);
        }

        /// <summary>
        /// CLIXML 转义还原——_xNNNN_ 控制字符 + XML 实体（&amp;lt; &amp;gt; &amp;amp; &amp;quot; &amp;apos;）
        /// </summary>
        /// <param name="text">转义文本</param>
        /// <returns>还原文本</returns>
        private static string UnescapeCliXml(string text)
        {
            StringBuilder sb = new StringBuilder();
            int i = 0;
            while (i < text.Length)
            {
                if (text[i] == '_' && i + 6 < text.Length && text[i + 1] == 'x' && text[i + 6] == '_')
                {
                    int code = Hex4(text, i + 2);
                    if (code >= 0)
                    {
                        sb.Append((char)code);
                        i = i + 7;
                        continue;
                    }
                }
                if (text[i] == '&')
                {
                    if (i + 4 <= text.Length && string.CompareOrdinal(text, i, "&lt;", 0, 4) == 0)
                    {
                        sb.Append('<');
                        i = i + 4;
                        continue;
                    }
                    if (i + 4 <= text.Length && string.CompareOrdinal(text, i, "&gt;", 0, 4) == 0)
                    {
                        sb.Append('>');
                        i = i + 4;
                        continue;
                    }
                    if (i + 5 <= text.Length && string.CompareOrdinal(text, i, "&amp;", 0, 5) == 0)
                    {
                        sb.Append('&');
                        i = i + 5;
                        continue;
                    }
                    if (i + 6 <= text.Length && string.CompareOrdinal(text, i, "&quot;", 0, 6) == 0)
                    {
                        sb.Append('"');
                        i = i + 6;
                        continue;
                    }
                    if (i + 6 <= text.Length && string.CompareOrdinal(text, i, "&apos;", 0, 6) == 0)
                    {
                        sb.Append('\'');
                        i = i + 6;
                        continue;
                    }
                }
                sb.Append(text[i]);
                i = i + 1;
            }
            return sb.ToString();
        }

        /// <summary>
        /// 四位十六进制解析——"000D" → 13（非法返回 -1）
        /// </summary>
        /// <param name="text">源文本</param>
        /// <param name="start">起始下标</param>
        /// <returns>数值（-1=非法）</returns>
        private static int Hex4(string text, int start)
        {
            if (start + 4 > text.Length)
            {
                return -1;
            }
            int value = 0;
            for (int i = 0; i < 4; i = i + 1)
            {
                char c = text[start + i];
                int digit;
                if (c >= '0' && c <= '9')
                {
                    digit = c - '0';
                }
                else if (c >= 'a' && c <= 'f')
                {
                    digit = c - 'a' + 10;
                }
                else if (c >= 'A' && c <= 'F')
                {
                    digit = c - 'A' + 10;
                }
                else
                {
                    return -1;
                }
                value = value * 16 + digit;
            }
            return value;
        }

        /// <summary>
        /// ANSI 转义剥离——ESC[ 参数 终止字母 序列整体删除
        /// </summary>
        /// <param name="text">含转义文本</param>
        /// <returns>纯文本</returns>
        private static string StripAnsi(string text)
        {
            StringBuilder sb = new StringBuilder();
            int i = 0;
            while (i < text.Length)
            {
                if (text[i] == '\u001B' && i + 1 < text.Length && text[i + 1] == '[')
                {
                    int j = i + 2;
                    while (j < text.Length)
                    {
                        char c = text[j];
                        if ((c >= '0' && c <= '9') || c == ';' || c == '?' || c == ' ')
                        {
                            j = j + 1;
                            continue;
                        }
                        break;
                    }
                    if (j < text.Length && ((text[j] >= 'A' && text[j] <= 'Z') || (text[j] >= 'a' && text[j] <= 'z')))
                    {
                        i = j + 1;
                        continue;
                    }
                }
                sb.Append(text[i]);
                i = i + 1;
            }
            return sb.ToString();
        }

        /// <summary>
        /// 追加文本到 StringBuilder——上限截断（超限后丢弃后续——截断标记由调用方比对长度）
        /// </summary>
        /// <param name="sb">目标缓冲区</param>
        /// <param name="line">追加行</param>
        /// <param name="max">上限字符数</param>
        private static void AppendCapped(StringBuilder sb, string line, int max)
        {
            if (sb.Length >= max)
            {
                return;
            }
            if (sb.Length > 0)
            {
                sb.Append('\n');
            }
            int remain = max - sb.Length;
            if (remain <= 0)
            {
                return;
            }
            if (line.Length <= remain)
            {
                sb.Append(line);
            }
            else
            {
                sb.Append(line, 0, remain);
            }
        }

        /// <summary>词法拦截检查——最小面（单命令语句：禁多段/管道/块/过程语句/变量赋值）/ 写文件语义 / 目录列举 / Start-Process / ReadKey-ReadLine。git 仅豁免写文件与列目录词表。</summary>
        /// <param name="command">命令全文</param>
        /// <returns>拦截错误文本（空=放行）</returns>
        private static string DetectForbidden(string command)
        {
            // [段1] git 前缀豁免——仅豁免写文件 / 列目录词表（命令内路径文本会误伤——判例 2026-09-11）；最小面与重定向检查照常
            bool isGit = IsGitCommand(command);
            // [段2] 最小面检查——语句形态：多段 / 管道 / 块 / 过程语句 / 变量赋值（一次调用只发一个命令，判断归 LLM）
            string minimal = DetectNonMinimal(command);
            if (minimal.Length > 0)
            {
                return minimal;
            }
            // [段3] 写文件 cmdlet 族——词法检测（大小写不敏感；单词边界防误伤）；git 豁免
            if (!isGit)
            {
                string[] writeCmdlets = new string[]
                {
                            "Set-Content", "Add-Content", "Out-File", "New-Item", "Copy-Item",
                            "Move-Item", "Remove-Item", "Rename-Item", "Clear-Content",
                            "Set-ItemProperty", "Set-Content-Encoding", "Export-Csv", "Export-Clixml",
                            "File.WriteAllText", "File.WriteAllLines", "File.WriteAllBytes",
                            "File.AppendAllText", "File.Copy", "File.Move", "File.Delete", "File.Create",
                            "IO.File.WriteAllText", "IO.File.WriteAllLines", "IO.File.WriteAllBytes"
                };
                for (int i = 0; i < writeCmdlets.Length; i = i + 1)
                {
                    if (ContainsWord(command, writeCmdlets[i]))
                    {
                        return "ERR|PS_WRITE_FORBIDDEN|命令含文件写语义（" + writeCmdlets[i] + "）——文件操作请走 text-* 读写工具（text-write/append/replace/move/delete）";
                    }
                }
                // [段3b] 读文件语义——文件内容读取一律走 text-*（含 .NET API 形态）；字节级读取（ReadAllBytes）例外保留
                string[] readApis = new string[]
                {
                    "File.ReadAllText", "File.ReadAllLines", "File.OpenText", "File.OpenRead", "File.Open",
                    "IO.File.ReadAllText", "IO.File.ReadAllLines", "IO.File.OpenText", "IO.File.OpenRead",
                    "Get-Content"
                };
                for (int i = 0; i < readApis.Length; i = i + 1)
                {
                    if (ContainsWord(command, readApis[i]))
                    {
                        return "ERR|PS_READ_FILE_FORBIDDEN|命令含文件读取语义（" + readApis[i] + "）——文件内容读取请走 text-* 工具（text-read / text-read_between / text-read_lines）";
                    }
                }
                // [段3c] 目录列举语义——列目录一律走 file-tree（按文件名查找走 file-find）；git 豁免同族（路径文本防误伤）
                string[] listCmdlets = new string[]
                {
                            "Get-ChildItem", "gci", "ls", "dir"
                };
                for (int i = 0; i < listCmdlets.Length; i = i + 1)
                {
                    if (ContainsWord(command, listCmdlets[i]))
                    {
                        return ListForbidden(listCmdlets[i]);
                    }
                }
                // tree 原生 exe——仅段首形态判（路径 / 参数中的同形词，如 Tree.md，不误伤）
                if (StartsWithWord(command.TrimStart(), "tree"))
                {
                    return ListForbidden("tree");
                }
            }
            // [段4] 重定向操作符——语义判定（引号内文本 / 箭头 / 比较符 一律放行）
            if (HasRedirectOperator(command))
            {
                return "ERR|PS_WRITE_FORBIDDEN|命令含重定向操作符 > ——文件操作请走 text-* 读写工具";
            }
            // [段5] Start-Process——无交互控制台主循环不 pump（判例 2026-08-18）；改起进程走宿主既有机制
            if (ContainsWord(command, "Start-Process") || ContainsWord(command, "Start-Job"))
            {
                return "ERR|PS_START_FORBIDDEN|命令含 Start-Process/Start-Job——禁止在工具内启动新进程（宿主进程管理面）";
            }
            // [段6] ReadKey/ReadLine——自动化环境 stdout 重定向 → 控制台等待死锁（判例 2026-09-01）
            if (ContainsWord(command, "ReadKey") || ContainsWord(command, "ReadLine") || ContainsWord(command, "Read-Host"))
            {
                return "ERR|PS_READ_FORBIDDEN|命令含 ReadKey/ReadLine/Read-Host——自动化环境控制台等待会死锁";
            }
            return "";
        }
        /// <summary>
        /// git 前缀判定——仓库级操作（仅豁免写文件词表，不豁免最小面与重定向检查）。
        /// </summary>
        /// <param name="command">命令全文</param>
        /// <returns>true=git 命令</returns>
        private static bool IsGitCommand(string command)
        {
            string trimmed = command.TrimStart();
            if (trimmed.StartsWith("git ", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (trimmed.StartsWith("git.exe ", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            return false;
        }

        /// <summary>最小面检查——一次调用只允许一个命令语句（引号感知扫描）。禁：段首过程语句关键字 / 段首代码注入命令 / 段首变量赋值 / 多段分隔 / 管道 / 块标记 / 代码形态（类型引用、方法调用、表达式、变量）。动机：ps 只提供最基本功能，不接受任何形式的可运行代码注入。</summary>
        /// <param name="command">命令全文</param>
        /// <returns>拦截错误文本（空=通过）</returns>
        private static string DetectNonMinimal(string command)
        {
            string trimmed = command.TrimStart();
            // [段1] 段首过程语句关键字——位置约束（命令开头）消除词表误伤（-Filter 等参数不受影响）
            string[] keywords = new string[]
            {
                        "foreach", "for", "while", "do", "until", "if", "elseif", "else", "switch",
                        "try", "catch", "finally", "function", "filter", "param", "trap",
                        "begin", "process", "end", "break", "continue", "return", "throw"
            };
            for (int i = 0; i < keywords.Length; i = i + 1)
            {
                if (StartsWithWord(trimmed, keywords[i]))
                {
                    return "ERR|PS_BLOCK_FORBIDDEN|命令为过程语句（" + keywords[i] + "）——最小面规则：一次调用只发一个命令，不要流程控制；需要判断请取数后自行推理";
                }
            }
            // [段2] 段首代码注入命令——动态执行 / 类型构造 / 对象构造一律拒绝
            string[] codeCmds = new string[]
            {
                        "Invoke-Expression", "iex", "Invoke-Command", "Add-Type", "New-Object", "Set-Item", "New-ItemProperty"
            };
            for (int i = 0; i < codeCmds.Length; i = i + 1)
            {
                if (StartsWithWord(trimmed, codeCmds[i]))
                {
                    return "ERR|PS_CODE_FORBIDDEN|命令为代码注入形态（" + codeCmds[i] + "）——powershell 仅支持单行指令（命令 + 字面量参数）";
                }
            }
            // [段3] 段首变量赋值——过程状态不允许
            if (StartsWithAssignment(trimmed))
            {
                return "ERR|PS_ASSIGN_FORBIDDEN|命令为变量赋值——最小面规则：不允许过程状态；请直接调用命令并用其输出";
            }
            // [段4] 引号外字符扫描——只允许「命令 + 字面量参数」：禁多段/管道/块/类型引用/表达式/变量
            bool inSingle = false;
            bool inDouble = false;
            for (int i = 0; i < command.Length; i = i + 1)
            {
                char c = command[i];
                if (inSingle)
                {
                    if (c == '\'')
                    {
                        if (i + 1 < command.Length && command[i + 1] == '\'')
                        {
                            i = i + 1;
                            continue;
                        }
                        inSingle = false;
                    }
                    continue;
                }
                if (inDouble)
                {
                    if (c == '`')
                    {
                        i = i + 1;
                        continue;
                    }
                    if (c == '"')
                    {
                        inDouble = false;
                    }
                    continue;
                }
                if (c == '\'')
                {
                    inSingle = true;
                    continue;
                }
                if (c == '"')
                {
                    inDouble = true;
                    continue;
                }
                if (c == '`')
                {
                    i = i + 1;
                    continue;
                }
                if (c == ';')
                {
                    return MultiErr(";");
                }
                if (c == '|')
                {
                    if (i + 1 < command.Length && command[i + 1] == '|')
                    {
                        return MultiErr("||");
                    }
                    return "ERR|PS_PIPE_FORBIDDEN|命令含管道（|）——最小面规则：不要输出后处理；请直接调用取数命令，用完整输出自行判断";
                }
                if (c == '&' && i + 1 < command.Length && command[i + 1] == '&')
                {
                    return MultiErr("&&");
                }
                if (c == '{' || c == '}')
                {
                    return "ERR|PS_BLOCK_FORBIDDEN|命令含块标记（" + c + "）——最小面规则：不要脚本块；请拆成多次工具调用";
                }
                if (c == '[' || c == ']' || c == '(' || c == ')' || c == '$')
                {
                    return "ERR|PS_CODE_FORBIDDEN|命令含代码形态（" + c + "）——powershell 仅支持单行指令（命令 + 字面量参数）：禁类型引用 / 方法调用 / 表达式 / 变量";
                }
                if (c == '\n' || c == '\r')
                {
                    bool tailOnly = true;
                    for (int j = i + 1; j < command.Length; j = j + 1)
                    {
                        if (!char.IsWhiteSpace(command[j]))
                        {
                            tailOnly = false;
                            break;
                        }
                    }
                    if (tailOnly)
                    {
                        break;
                    }
                    return MultiErr("换行");
                }
            }
            return "";
        }

        /// <summary>
        /// 多段拦截文案——统一指引（拆成多次工具调用，同一轮可并发提交）。
        /// </summary>
        /// <param name="token">命中的分隔符</param>
        /// <returns>错误文本</returns>
        private static string MultiErr(string token)
        {
            return "ERR|PS_MULTI_FORBIDDEN|命令含多段（" + token + "）——最小面规则：一次调用只发一个命令；请拆成多次工具调用（同一轮可并发提交）";
        }
        /// <summary>
        /// 目录列举拦截文案——统一指引（列目录走 file-tree；按文件名查找走 file-find）。
        /// </summary>
        /// <param name="hit">命中的词</param>
        /// <returns>错误文本</returns>
        private static string ListForbidden(string hit)
        {
            return "ERR|PS_LIST_FORBIDDEN|命令含目录列举语义（" + hit + "）——列目录请使用 file-tree 工具（按文件名查找用 file-find）";
        }

        /// <summary>
        /// 段首词匹配——关键字作为独立单词出现在文本开头。
        /// </summary>
        /// <param name="text">文本</param>
        /// <param name="word">关键字</param>
        /// <returns>true=以该词开头</returns>
        private static bool StartsWithWord(string text, string word)
        {
            if (!text.StartsWith(word, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            if (text.Length == word.Length)
            {
                return true;
            }
            return !IsWordChar(text[word.Length]);
        }

        /// <summary>
        /// 段首变量赋值判定——变量名后跟单个等号（双等号比较不算）。
        /// </summary>
        /// <param name="text">文本</param>
        /// <returns>true=变量赋值</returns>
        private static bool StartsWithAssignment(string text)
        {
            if (text.Length == 0 || text[0] != '$')
            {
                return false;
            }
            int i = 1;
            while (i < text.Length && IsVariableChar(text[i]))
            {
                i = i + 1;
            }
            if (i == 1)
            {
                return false;
            }
            while (i < text.Length && char.IsWhiteSpace(text[i]))
            {
                i = i + 1;
            }
            if (i >= text.Length || text[i] != '=')
            {
                return false;
            }
            if (i + 1 < text.Length && text[i + 1] == '=')
            {
                return false;
            }
            return true;
        }

        /// <summary>
        /// 变量名字符判定——字母/数字/下划线/冒号（环境变量形式）。
        /// </summary>
        /// <param name="c">字符</param>
        /// <returns>true=变量名字符</returns>
        private static bool IsVariableChar(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == ':';
        }

        /// <summary>
        /// 重定向操作符检测——只认"作为重定向操作符出现的 &gt;"（引号感知 + 语义豁免）。
        /// 豁免面：引号内文本（'...' / "..."——PowerShell 双引号 ` 转义、单引号 '' 字面）/ 箭头 `-&gt;` / 比较 `&gt;=`。
        /// </summary>
        /// <param name="command">命令全文</param>
        /// <returns>true=含真正的重定向操作符</returns>
        private static bool HasRedirectOperator(string command)
        {
            bool inSingle = false;
            bool inDouble = false;
            for (int i = 0; i < command.Length; i = i + 1)
            {
                char c = command[i];
                // [段1] 引号态跟踪——引号内字符一律按文本内容处理
                if (inSingle)
                {
                    if (c == '\'')
                    {
                        if (i + 1 < command.Length && command[i + 1] == '\'')
                        {
                            i = i + 1;
                            continue;
                        }
                        inSingle = false;
                    }
                    continue;
                }
                if (inDouble)
                {
                    if (c == '`')
                    {
                        i = i + 1;
                        continue;
                    }
                    if (c == '"')
                    {
                        inDouble = false;
                    }
                    continue;
                }
                if (c == '\'')
                {
                    inSingle = true;
                    continue;
                }
                if (c == '"')
                {
                    inDouble = true;
                    continue;
                }
                // [段2] 引号外——按语义判定（箭头/比较符/流合并 非文件写）
                if (c != '>')
                {
                    continue;
                }
                char prev = PrevNonSpace(command, i);
                char next = '\0';
                if (i + 1 < command.Length)
                {
                    next = command[i + 1];
                }
                if (prev == '-' || prev == '=' || prev == '<')
                {
                    continue;
                }
                if (next == '=')
                {
                    continue;
                }
                // `n>&m` 流合并（2>&1 等）——控制台流操作，非文件写语义
                if (next == '&' && i + 2 < command.Length)
                {
                    char stream = command[i + 2];
                    if (stream == '*' || (stream >= '0' && stream <= '9'))
                    {
                        continue;
                    }
                }
                return true;
            }
            return false;
        }
        /// <summary>
        /// 前一非空白字符——重定向语义判定用（行首/全空白返回 '\0'）。
        /// </summary>
        /// <param name="text">全文</param>
        /// <param name="index">当前下标（不含）</param>
        /// <returns>前一非空白字符；无则 '\0'</returns>
        private static char PrevNonSpace(string text, int index)
        {
            for (int i = index - 1; i >= 0; i = i - 1)
            {
                if (!char.IsWhiteSpace(text[i]))
                {
                    return text[i];
                }
            }
            return '\0';
        }

        /// <summary>
        /// 单词边界包含检测——cmdlet 名在命令中作为独立单词出现
        /// </summary>
        /// <param name="text">全文</param>
        /// <param name="word">目标词</param>
        /// <returns>true=作为独立单词出现</returns>
        private static bool ContainsWord(string text, string word)
        {
            int idx = 0;
            while (true)
            {
                idx = text.IndexOf(word, idx, StringComparison.OrdinalIgnoreCase);
                if (idx < 0)
                {
                    return false;
                }
                bool prevOk = idx == 0 || !IsWordChar(text[idx - 1]);
                bool nextOk = idx + word.Length >= text.Length || !IsWordChar(text[idx + word.Length]);
                if (prevOk && nextOk)
                {
                    return true;
                }
                idx = idx + word.Length;
            }
        }

        /// <summary>
        /// 词字符判定——字母/数字/下划线（单词边界检测用）
        /// </summary>
        /// <param name="c">字符</param>
        /// <returns>true=词字符</returns>
        private static bool IsWordChar(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_';
        }

        /// <summary>
        /// 超时解析——非法/空回退默认；超上限钳制
        /// </summary>
        /// <param name="raw">原始字符串</param>
        /// <returns>超时毫秒</returns>
        private static long ParseTimeout(string raw)
        {
            long ms;
            if (long.TryParse(raw, out ms) && ms > 0)
            {
                if (ms > MaxTimeoutMs)
                {
                    return MaxTimeoutMs;
                }
                return ms;
            }
            return DefaultTimeoutMs;
        }

        /// <summary>
        /// 杀进程树——taskkill /T /F（子进程一并终止；杀不掉静默——进程已退出）
        /// </summary>
        /// <param name="proc">目标进程</param>
        private static void KillTree(Process proc)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "taskkill.exe";
                psi.Arguments = "/T /F /PID " + proc.Id.ToString();
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                Process killer = Process.Start(psi);
                if (killer != null)
                {
                    killer.WaitForExit(5000);
                }
            }
            catch (Exception ex)
            {
                // 杀树失败静默——进程可能已退出
                LogStore.Add("CatHome4", 2, "进程树强杀失败: " + ex.Message, "SYS");
            }
            try
            {
                proc.Kill();
            }
            catch (Exception ex)
            {
                // 已退出
                LogStore.Add("CatHome4", 1, "进程已退出，Kill 跳过: " + ex.Message, "SYS");
            }
        }

        /// <summary>
        /// 展平参数提取——argsJson 中取字符串值（不存在返回空串）
        /// </summary>
        /// <param name="argumentsJson">参数 JSON</param>
        /// <param name="key">参数名</param>
        /// <returns>参数值</returns>
        private static string ExtractArg(string argumentsJson, string key)
        {
            try
            {
                JsonDocument doc = JsonDocument.Parse(argumentsJson);
                try
                {
                    if (doc.RootElement.TryGetProperty(key, out JsonElement el))
                    {
                        if (el.ValueKind == JsonValueKind.String)
                        {
                            string s = el.GetString();
                            if (s == null)
                            {
                                return "";
                            }
                            return s;
                        }
                        return el.GetRawText();
                    }
                }
                finally
                {
                    doc.Dispose();
                }
            }
            catch (Exception ex)
            {
                LogStore.Add("CatHome4", 2, "powershell 参数提取失败: " + ex.Message, "PS");
            }
            return "";
        }
    }
}

// ═══════════════════════════════════════════════════
// 积木: ps.exec
// ID:   BRIK-PS-001
// 类别: PS
// 作用: PowerShell 执行——整段命令 EncodedCommand 免转义直达 PS；UTF-8 输出内建；词法拦截（最小面/写文件/读文件/目录列举/代码形态/重定向/起进程/控制台等待）；超时进程树杀
// 依赖: 无
// 引用: Mau.Runtime（IPsService / JsonArgs / DataBox）
// 原理: JsonArgs.Get 取 command → DetectForbidden 词法拦截（A228 档 2：判定自 CH4.PsService 下沉至积木层——词表与文案随积木热改，不再要求宿主重启）
//       → DataBox.TryResolve<IPsService> → Exec(argsJson, shell)（执行 / 编码 / 超时留实现侧）
//       shell 解释器线标识（powershell=默认解释器 / powershell7=PowerShell 7）
// 常用: ps_cat.mau 认领线——'ps.exec'[@args, "powershell"] > @result
// ═══════════════════════════════════════════════════
using System;
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// PowerShell 执行积木——powershell 工具（PsCat 工具组 Flow 认领线消费）
    /// </summary>
    public static class PsExecBrick
    {
        /// <summary>
        /// 执行 PowerShell 命令——先做词法拦截（业务规则归积木层），通过后交宿主实现执行
        /// </summary>
        /// <param name="argsJson">工具参数 JSON（command/cwd/timeout_ms）</param>
        /// <param name="shell">解释器线标识（powershell=默认解释器 / powershell7=PowerShell 7）</param>
        /// <param name="result">结果 JSON 或 ERR| 错误文本</param>
        /// <returns>true=执行成功</returns>
        public static bool Exec(string argsJson, string shell, out string result)
        {
            result = "";
            if (argsJson == null || argsJson.Length == 0)
            {
                result = "ERR|PS_BAD_ARGS|缺少参数 command";
                return false;
            }
            try
            {
                // [段1] 词法拦截（A228 档 2——自 CH4.PsService 下沉；词表与文案归积木层，改规则即重翻组 + host-reload）
                string command = JsonArgs.Get(argsJson, "command");
                if (command.Length > 0 && command != "§PARSE_FAIL§")
                {
                    string block = DetectForbidden(command);
                    if (block.Length > 0)
                    {
                        result = block;
                        return false;
                    }
                }
                IPsService service;
                DataBox.TryResolve<IPsService>(out service);
                if (service == null)
                {
                    result = "ERR|PS_NO_SERVICE|宿主未注入 IPsService";
                    return false;
                }
                result = service.Exec(argsJson, shell);
                return true;
            }
            catch (Exception ex)
            {
                result = "ERR|" + ex.GetType().Name + "|" + ex.Message;
                return false;
            }
        }

        /// <summary>词法拦截检查——最小面（单命令语句：禁多段/管道/块/过程语句/变量赋值）/ 写文件语义 / 读文件语义 / 目录列举 / Start-Process / ReadKey-ReadLine。git 仅豁免写文件与列目录词表。</summary>
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
    }
}
// #MAU_CHECKSUM:SHA256:E1CC269BDBDE87417F6C1D9A2A531155BF5032F152B3FE860FDA4124E9F6413C

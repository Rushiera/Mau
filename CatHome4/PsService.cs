using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace CH4
{
    /// <summary>
    /// PowerShell 执行服务——powershell 工具实现（PsCat 工具组）。
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

        /// <summary>超时上限毫秒——超长任务须显式放宽</summary>
        private const long MaxTimeoutMs = 300000;

        /// <summary>
        /// 执行 PowerShell 命令——拦截检查 → EncodedCommand 启动 → 超时/输出处理 → JSON 回执
        /// </summary>
        /// <param name="argsJson">工具参数 JSON（command/cwd/timeout_ms）</param>
        /// <returns>结果 JSON 或 ERR| 错误文本</returns>
        public string Exec(string argsJson)
        {
            string command = ExtractArg(argsJson, "command");
            if (command.Length == 0)
            {
                return "ERR|PS_BAD_ARGS|缺少参数 command";
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
            string prefixed = "[Console]::OutputEncoding=[Text.Encoding]::UTF8; $ProgressPreference='SilentlyContinue'; chcp 65001 > $null; " + command;
            // [段3] EncodedCommand——UTF-16LE → Base64（免转义：引号/反斜杠/JSON 原样直达 PS 解析器）
            string base64 = Convert.ToBase64String(Encoding.Unicode.GetBytes(prefixed));
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = "powershell.exe";
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
                catch (Exception)
                {
                    // 工作目录无效——回退默认（不阻断执行）
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
            catch (Exception)
            {
                // 等待异常（进程已退出等）——按已退出处理
            }
            string outText = stdout.ToString();
            string errText = stderr.ToString();
            bool outTrunc = outText.Length >= MaxStdoutChars;
            bool errTrunc = errText.Length >= MaxStderrChars;
            // [段6] JSON 回执——exit/stdout/stderr/truncated/timeout（失败侧非零退出也回执，Dog 可见）
            Dictionary<string, object> resp = new Dictionary<string, object>();
            resp["exit"] = timeout ? -1 : proc.ExitCode;
            resp["stdout"] = outText;
            resp["stderr"] = errText;
            resp["truncated"] = outTrunc || errTrunc;
            resp["timeout"] = timeout;
            return Mau.Runtime.JsonUtil.Serialize(resp);
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

        /// <summary>
        /// 词法拦截检查——写文件语义 / Start-Process / ReadKey-ReadLine
        /// </summary>
        /// <param name="command">命令全文</param>
        /// <returns>拦截错误文本（空=放行）</returns>
        private static string DetectForbidden(string command)
        {
            // [段1] git 前缀豁免——仓库级操作（git commit/checkout 等）非文件内容写；git 命令本身不含写文件 cmdlet
            string trimmed = command.TrimStart();
            if (trimmed.StartsWith("git ", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("git.exe ", StringComparison.OrdinalIgnoreCase))
            {
                return "";
            }
            // [段2] 写文件 cmdlet 族——词法检测（大小写不敏感；单词边界防误伤）
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
            // [段3] 重定向操作符——语义判定（引号内文本 / 箭头 / 比较符 一律放行）
            if (HasRedirectOperator(command))
            {
                return "ERR|PS_WRITE_FORBIDDEN|命令含重定向操作符 > ——文件操作请走 text-* 读写工具";
            }
            // [段4] Start-Process——无交互控制台主循环不 pump（判例 2026-08-18）；改起进程走宿主既有机制
            if (ContainsWord(command, "Start-Process") || ContainsWord(command, "Start-Job"))
            {
                return "ERR|PS_START_FORBIDDEN|命令含 Start-Process/Start-Job——禁止在工具内启动新进程（宿主进程管理面）";
            }
            // [段5] ReadKey/ReadLine——自动化环境 stdout 重定向 → 控制台等待死锁（判例 2026-09-01）
            if (ContainsWord(command, "ReadKey") || ContainsWord(command, "ReadLine") || ContainsWord(command, "Read-Host"))
            {
                return "ERR|PS_READ_FORBIDDEN|命令含 ReadKey/ReadLine/Read-Host——自动化环境控制台等待会死锁";
            }
            return "";
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
            catch (Exception)
            {
                // 杀树失败静默——进程可能已退出
            }
            try
            {
                proc.Kill();
            }
            catch (Exception)
            {
                // 已退出
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
            catch (Exception)
            {
            }
            return "";
        }
    }
}

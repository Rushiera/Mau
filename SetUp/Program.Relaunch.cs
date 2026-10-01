using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace SetUp
{
    /// <summary>SetUp 部署链入口——prepare（就地自举：build/test/publish/组翻译/publish/宿主自检）与 deploy（发布到目标目录）双模式 + relaunch（宿主自更新接力）。</summary>
    /// <summary>
    /// Program 分部——relaunch 模式：宿主自更新接力（等旧 pid 退出 → 部署 → 自启 → 回执）。
    /// </summary>
    public static partial class Program
    {
        /// <summary>
        /// 等待旧宿主退出的上限毫秒——超时中止（不触碰运行区）
        /// </summary>
        private const int RelaunchWaitPidMs = 120000;

        /// <summary>
        /// 自启探活窗口毫秒——窗口内进程存活即视为启动成功
        /// </summary>
        private const int RelaunchProbeMs = 5000;

        /// <summary>
        /// 运行区产物快照——relaunch 报告附加项（宿主 exe + version.txt + Flows dll 时间戳）
        /// </summary>
        private static readonly List<ArtifactReport> _extraArtifacts = new List<ArtifactReport>();

        /// <summary>relaunch 模式入口（三类重启）——参数解析（零容忍）→ 按 --mode 组合三段原语：incr 前置段（哨兵/拒绝面/准备段/探活/落 ready 哨兵）→ 等旧宿主退出 → full 走 prepare、incr 仅原子切换、host 原地 → 自启 → 已替换但新版本起不来才回退旧版本。失败面：验证不过 → 运行区保持现状（服务照旧、版本未变，无需回退）；已替换但新版本起不来 → 以 _old 回退。</summary>
        /// <param name="repoRoot">仓库根（Main 已探测）</param>
        /// <param name="args">命令行参数（--mode / --wait-pid / --target / --majordomopush / --report）</param>
        /// <returns>退出码（0=替换（host 为原地）且自启成功；2=已回退旧版本；3=未替换、以现有版本运行；1=需人工介入）</returns>
        private static int Relaunch(string repoRoot, string[] args)
        {
            Console.WriteLine("[SetUp] relaunch —— 宿主自更新接力");
            // A116 重启用时起算点——接力进程进入 relaunch 段（终点 = 新宿主拉起前一刻；探活窗口为固定常数，不计入）
            long relaunchStartMs = Environment.TickCount64;
            string badArgs = ValidateRelaunchArgs(args);
            if (badArgs.Length > 0)
            {
                Console.WriteLine("[SetUp] " + badArgs);
                PrintHelp();
                return 1;
            }
            string mode;
            string modeErr = ParseMode(ExtractOption(args, "--mode"), out mode);
            if (modeErr.Length > 0)
            {
                Console.WriteLine("[SetUp] " + modeErr);
                PrintHelp();
                return 1;
            }
            string pidRaw = ExtractOption(args, "--wait-pid");
            string target = ExtractOption(args, "--target");
            string pushIn = ExtractOption(args, "--majordomopush");
            int waitPid = int.Parse(pidRaw);
            string targetFull = Path.GetFullPath(target);
            Console.WriteLine("  模式: " + mode + "（full=全链 / incr=纯搬运 / host=原地重启）");
            Console.WriteLine("  等待宿主退出: pid " + waitPid.ToString());
            Console.WriteLine("  运行区: " + targetFull);

            // [段0] incr 前置段——哨兵与拒绝面 → 准备段 → 探活 → 落 ready 哨兵（全程在旧宿主存活期完成）
            //       失败即落 ok=false 哨兵：宿主不自杀、撤销重启态；接力者等 pid 超时中止，不触碰运行区
            if (mode == "incr")
            {
                bool preflightOk = RunIncrPreflight(repoRoot, targetFull, Path.Combine(repoRoot, "public", "app"));
                if (!preflightOk)
                {
                    return Fail("incr 前置段未通过——宿主将撤销重启态（运行区未触碰）。");
                }
            }

            // [段1] 等旧宿主退出——宿主必须先退（宿主程序集 dll 被 ALC 持锁，运行中不可覆盖）
            long waitMs = Environment.TickCount64;
            bool exited = WaitProcessExit(waitPid, RelaunchWaitPidMs);
            _steps.Add(new StepReport() { Step = 11, Name = "等待旧宿主退出 (pid " + waitPid.ToString() + ")", Ok = exited, Ms = Environment.TickCount64 - waitMs });
            if (!exited)
            {
                return Fail("旧宿主未在 " + RelaunchWaitPidMs.ToString() + " ms 内退出——中止，不触碰运行区。");
            }

            // [段2] prepare 全链——仅 full（失败即不替换运行区）；incr / host 跳过本段
            long prepMs = Environment.TickCount64;
            bool prepOk = true;
            if (mode == "full")
            {
                prepOk = Prepare(repoRoot) == 0;
                if (!prepOk)
                {
                    Console.WriteLine("[SetUp] 警告：prepare 未通过——按定位继续部署（回执标注失败）。");
                }
                _steps.Add(new StepReport() { Step = 12, Name = "prepare 全链（步明细见 1-6）", Ok = prepOk, Ms = Environment.TickCount64 - prepMs });
            }
            else
            {
                _steps.Add(new StepReport() { Step = 12, Name = "prepare 跳过（mode=" + mode + "）", Ok = true, Ms = Environment.TickCount64 - prepMs });
            }

            // [段3] 替换运行区——full：prepare 通过才 deploy；incr：准备段已备好 _new，仅原子切换；host：跳过（原地重启）
            long depMs = Environment.TickCount64;
            bool replaced = false;
            if (mode == "full")
            {
                if (prepOk)
                {
                    replaced = Deploy(repoRoot, targetFull) == 0;
                    if (!replaced)
                    {
                        Console.WriteLine("[SetUp] 警告：deploy 未通过——尝试以现有运行区启动（步明细见 deploy 段）。");
                    }
                    _steps.Add(new StepReport() { Step = 13, Name = "deploy 原子切换（步明细见 1-3）", Ok = replaced, Ms = Environment.TickCount64 - depMs });
                }
                else
                {
                    Console.WriteLine("[SetUp] 警告：prepare 未通过——跳过 deploy，运行区保持现状（未替换）。");
                    _steps.Add(new StepReport() { Step = 13, Name = "deploy 跳过（prepare 未通过——运行区未替换）", Ok = true, Ms = Environment.TickCount64 - depMs });
                }
            }
            else if (mode == "incr")
            {
                string switchErr = SwitchDirectory(targetFull, targetFull + "_new", targetFull + "_old");
                replaced = switchErr.Length == 0;
                _steps.Add(new StepReport() { Step = 13, Name = "incr 原子切换运行区", Ok = replaced, Ms = Environment.TickCount64 - depMs, Detail = switchErr });
                if (!replaced)
                {
                    Console.WriteLine("[SetUp] 警告：原子切换失败——" + switchErr + "；尝试以现有运行区启动。");
                }
            }
            else
            {
                _steps.Add(new StepReport() { Step = 13, Name = "替换跳过（mode=host——原地重启）", Ok = true, Ms = Environment.TickCount64 - depMs });
            }

            // [段4] 拼回执注入串——由 SetUp 生成（非 LLM 自述），带模式 / 部署结论 / 产物时间戳 / 重启用时（A116）
            string version = ReadVersion(repoRoot);
            string reportPath = ExtractOption(args, "--report");

            // [段4b] 前置报告落盘（A136）——自启前先落一版（Complete=false）：读者（含回执引导）任何时刻都能读到文件，
            //        不必因「文件还没出现」而重试；终版在 Main 收尾时原子替换（Complete=true）
            if (reportPath.Length > 0)
            {
                WriteReport(reportPath, "relaunch", -1, repoRoot, false);
            }

            // [段5] 自启新宿主 + 探活（重启用时终点 = 拉起动作发起前一刻——探活窗口固定，不计入）
            long bootMs = Environment.TickCount64;
            string push = BuildPushText(mode, prepOk, replaced, version, targetFull, pushIn, reportPath, bootMs - relaunchStartMs);
            bool bootOk = StartHost(targetFull, push);
            _steps.Add(new StepReport() { Step = 14, Name = "自启新宿主（含探活 " + RelaunchProbeMs.ToString() + " ms）", Ok = bootOk, Ms = Environment.TickCount64 - bootMs });

            // [段6] 回退——仅在本次已替换且新版本起不来时，_old 才是有效回退源（"最坏 = 原地重启"的兑现路径）
            bool fallbackOk = false;
            if (!bootOk && replaced)
            {
                Console.WriteLine("[SetUp] 警告：新版本启动失败——回退旧版本目录。");
                long fallbackElapsedMs = Environment.TickCount64 - relaunchStartMs;
                string fallbackPush = "[宿主自更新] 新版本启动失败——已回退旧版本。"
                    + Environment.NewLine + "模式: " + mode
                    + Environment.NewLine + "重启用时: " + (fallbackElapsedMs / 1000.0).ToString("0.0") + " s（接力起算）"
                    + Environment.NewLine + "说明: " + pushIn;
                long fbMs = Environment.TickCount64;
                fallbackOk = StartHost(targetFull + "_old", fallbackPush);
                _steps.Add(new StepReport() { Step = 15, Name = "回退启动旧版本 (<target>_old)", Ok = fallbackOk, Ms = Environment.TickCount64 - fbMs });
            }

            // [段7] 运行区产物快照——报告附加项（版本自证）
            CollectTargetArtifacts(targetFull);
            bool effectiveOk = prepOk && (replaced || mode == "host");
            // [段8] 收尾哨兵（A135）——切换与自启的最终结论回写哨兵（phase=done）：事后读哨兵即知「到底成没成」
            bool finalOk = bootOk && effectiveOk;
            string finalReason = "";
            if (!finalOk)
            {
                if (!prepOk)
                {
                    finalReason = "prepare 未通过（运行区未替换）";
                }
                else if (!replaced && mode != "host")
                {
                    finalReason = "运行区未替换：mode=" + mode + " 原子切换失败";
                }
                else
                {
                    finalReason = "新宿主启动失败";
                }
            }
            WriteRelay(repoRoot, finalOk, finalReason, "done");
            if (bootOk && effectiveOk)
            {
                Console.WriteLine("[SetUp] relaunch 完成（mode=" + mode + "）——宿主已接管运行区。");
                return 0;
            }
            if (bootOk)
            {
                // 未替换（prepare / 切换未过）但服务已恢复——版本未变，失败面由返回码与报告承载
                Console.WriteLine("[SetUp] relaunch 完成——运行区未替换，以现有版本继续运行。");
                return 3;
            }
            if (fallbackOk)
            {
                Console.WriteLine("[SetUp] relaunch 完成——已回退旧版本运行。");
                return 2;
            }
            return Fail("新版本与回退版本均未能启动——需人工介入。");
        }

        /// <summary>
        /// relaunch 参数校验——声明面口径零容忍：未知参数 / 缺值 / 非法值一律报错（不静默忽略、不回落默认）。
        /// </summary>
        /// <param name="args">命令行参数</param>
        /// <returns>错误文本（空=通过）</returns>
        private static string ValidateRelaunchArgs(string[] args)
        {
            string[] allowed = new string[] { "--mode", "--wait-pid", "--target", "--majordomopush", "--report" };
            for (int i = 0; i < args.Length; i = i + 1)
            {
                string arg = args[i];
                if (arg.Length == 0 || arg[0] != '-')
                {
                    continue;
                }
                bool known = false;
                for (int k = 0; k < allowed.Length; k = k + 1)
                {
                    if (arg == allowed[k])
                    {
                        known = true;
                    }
                }
                if (!known)
                {
                    return "参数错误：未知参数 " + arg + "（支持 --mode / --wait-pid / --target / --majordomopush / --report）。";
                }
                if (i + 1 >= args.Length)
                {
                    return "参数错误：" + arg + " 缺值。";
                }
                i = i + 1;
            }
            string pidRaw = ExtractOption(args, "--wait-pid");
            int pid;
            if (pidRaw.Length == 0 || !int.TryParse(pidRaw, out pid) || pid <= 0)
            {
                return "参数错误：--wait-pid 需为正整数（旧宿主进程号）。";
            }
            if (ExtractOption(args, "--target").Length == 0)
            {
                return "参数错误：--target 需运行区绝对路径。";
            }
            return "";
        }

        /// <summary>
        /// 取选项值——未出现返回空串（值缺失由校验段负责报错）。
        /// </summary>
        /// <param name="args">命令行参数</param>
        /// <param name="name">选项名</param>
        /// <returns>选项值或空串</returns>
        private static string ExtractOption(string[] args, string name)
        {
            for (int i = 0; i < args.Length; i = i + 1)
            {
                if (args[i] == name && i + 1 < args.Length)
                {
                    return args[i + 1];
                }
            }
            return "";
        }

        /// <summary>
        /// 等待目标进程退出——进程不存在视为已退出；超时返回 false（调用方不触碰运行区）。
        /// </summary>
        /// <param name="pid">目标进程号</param>
        /// <param name="timeoutMs">超时毫秒</param>
        /// <returns>true=已退出</returns>
        private static bool WaitProcessExit(int pid, int timeoutMs)
        {
            try
            {
                Process target = Process.GetProcessById(pid);
                bool done = target.WaitForExit(timeoutMs);
                Console.WriteLine("[SetUp] 等待 pid " + pid.ToString() + (done ? " 已退出。" : " 超时未退出。"));
                return done;
            }
            catch (ArgumentException)
            {
                Console.WriteLine("[SetUp] 目标进程 " + pid.ToString() + " 不存在——视为已退出。");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[SetUp] 警告：等待进程异常——" + ex.Message);
                return false;
            }
        }
        /// <summary>
        /// 拼回执注入串（三类重启）——模式 + 结论 + 版本 + 产物时间戳 + 报告路径 + 附加说明（由 SetUp 生成，非 LLM 自述）。
        /// </summary>
        /// <param name="mode">重启模式（full / incr / host）</param>
        /// <param name="prepOk">prepare 是否通过（incr / host 跳过本段，恒 true）</param>
        /// <param name="replaced">本次是否替换了运行区（host 恒 false——原地重启）</param>
        /// <param name="version">仓库单点版本号（full 为自增后新号；incr / host 不自增）</param>
        /// <param name="targetFull">运行区绝对路径</param>
        /// <param name="pushIn">调用方附加说明（可为空）</param>
        /// <param name="reportPath">报告路径（可为空）</param>
        /// <param name="elapsedMs">重启用时毫秒（relaunch 入口起算 → 新宿主拉起前一刻；A116）</param>
        /// <returns>注入串全文</returns>
        private static string BuildPushText(string mode, bool prepOk, bool replaced, string version, string targetFull, string pushIn, string reportPath, long elapsedMs)
        {
            string exeTs = "";
            string exePath = Path.Combine(targetFull, "CatHome4.exe");
            if (File.Exists(exePath))
            {
                exeTs = File.GetLastWriteTime(exePath).ToString("yyyy-MM-dd HH:mm:ss");
            }
            bool ok = prepOk && (replaced || mode == "host");
            string prepText = mode == "full" ? (prepOk ? "OK" : "FAIL") : "跳过";
            string depText = mode == "host" ? "跳过" : (replaced ? "OK" : "FAIL");
            string bootText = replaced ? "新版本" : (mode == "host" ? "原地重启（运行区未替换）" : "现有版本（运行区未替换）");
            StringBuilder sb = new StringBuilder();
            sb.Append("[宿主自更新] 重启完成");
            sb.Append(Environment.NewLine);
            sb.Append("模式: " + mode);
            sb.Append(Environment.NewLine);
            sb.Append("结果: " + (ok ? "OK" : "FAIL") + (prepOk ? "" : " | prepare 未通过") + (mode == "full" && !replaced ? " | deploy 未通过" : ""));
            sb.Append(Environment.NewLine);
            sb.Append("版本: " + version + (mode == "full" ? "" : "（incr / host 不自增）"));
            sb.Append(Environment.NewLine);
            sb.Append("部署: prepare " + prepText + " · deploy " + depText);
            sb.Append(Environment.NewLine);
            sb.Append("自启: " + bootText);
            sb.Append(Environment.NewLine);
            sb.Append("重启用时: " + (elapsedMs / 1000.0).ToString("0.0") + " s（接力起算）");
            sb.Append(Environment.NewLine);
            sb.Append("产物: CatHome4.exe " + exeTs);
            sb.Append(Environment.NewLine);
            if (reportPath.Length > 0)
            {
                sb.Append("报告: " + reportPath + "（前置版先落盘；终版 Complete=true 在自启后原子替换）");
                sb.Append(Environment.NewLine);
            }
            if (pushIn.Length > 0)
            {
                sb.Append("说明: " + pushIn);
            }
            return sb.ToString();
        }

        /// <summary>
        /// 启动宿主——工作目录 = 运行区（与手动启动一致）；带 <c>--majordomopush</c> 回执注入；探活窗口内退出即判失败。
        /// </summary>
        /// <param name="targetDir">运行区目录（或回退用的 _old 目录）</param>
        /// <param name="pushText">回执注入串（空=不带参启动）</param>
        /// <returns>true=启动成功（进程在探活窗口后仍存活）</returns>
        private static bool StartHost(string targetDir, string pushText)
        {
            string exe = Path.Combine(targetDir, "CatHome4.exe");
            if (!File.Exists(exe))
            {
                Console.WriteLine("[SetUp] 错误：宿主不存在——" + exe);
                return false;
            }
            ProcessStartInfo psi = new ProcessStartInfo(exe);
            psi.WorkingDirectory = targetDir;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = false;   // 宿主运行必须前台可见（跑测纪律）
            if (pushText.Length > 0)
            {
                psi.ArgumentList.Add("--majordomopush");
                psi.ArgumentList.Add(pushText);
            }
            Process proc;
            try
            {
                proc = Process.Start(psi);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[SetUp] 错误：宿主启动失败——" + ex.Message);
                return false;
            }
            if (proc == null)
            {
                Console.WriteLine("[SetUp] 错误：宿主启动失败（Process.Start 返回空）。");
                return false;
            }
            Console.WriteLine("[SetUp] 宿主已启动：pid " + proc.Id.ToString() + "（工作目录 " + targetDir + "）");
            bool earlyExit = proc.WaitForExit(RelaunchProbeMs);
            if (earlyExit)
            {
                Console.WriteLine("[SetUp] 错误：宿主启动后 " + RelaunchProbeMs.ToString() + " ms 内退出（exit=" + proc.ExitCode.ToString() + "）。");
                return false;
            }
            return true;
        }

        /// <summary>
        /// 收集运行区关键产物——宿主 exe / version.txt / Flows dll（报告附加项，时间戳自证）。
        /// </summary>
        /// <param name="targetFull">运行区绝对路径</param>
        private static void CollectTargetArtifacts(string targetFull)
        {
            AddArtifact(_extraArtifacts, Path.Combine(targetFull, "CatHome4.exe"));
            AddArtifact(_extraArtifacts, Path.Combine(targetFull, "version.txt"));
            string flowsDir = Path.Combine(targetFull, "Flows");
            if (Directory.Exists(flowsDir))
            {
                string[] flows = Directory.GetFiles(flowsDir, "*.dll");
                Array.Sort(flows, StringComparer.Ordinal);
                for (int i = 0; i < flows.Length; i = i + 1)
                {
                    AddArtifact(_extraArtifacts, flows[i]);
                }
            }
        }
        /// <summary>
        /// 解析 relaunch 模式——值域零容忍：full / incr / host（大小写不敏感）；缺省 = full（旧调用形态兼容）。
        /// </summary>
        /// <param name="raw">--mode 原始值（空 = 缺省）</param>
        /// <param name="mode">规范化模式（full / incr / host）</param>
        /// <returns>错误文本（空=通过）</returns>
        private static string ParseMode(string raw, out string mode)
        {
            mode = "full";
            if (raw.Length == 0)
            {
                return "";
            }
            string low = raw.ToLowerInvariant();
            if (low == "full" || low == "incr" || low == "host")
            {
                mode = low;
                return "";
            }
            return "参数错误：--mode 取值须为 full / incr / host（当前 " + raw + "）。";
        }
        /// <summary>
        /// ready 哨兵路径——宿主与接力者约定的握手文件（仓库根 CatTemp/restart_relay.json）。
        /// </summary>
        /// <param name="repoRoot">仓库根</param>
        /// <returns>哨兵文件绝对路径</returns>
        private static string RelayPath(string repoRoot)
        {
            return Path.Combine(repoRoot, "CatTemp", "restart_relay.json");
        }
        /// <summary>
        /// 写 ready 哨兵——incr 前置段结论落盘（宿主轮询到 ok=true 才退出；反序则探活无从前置）。
        /// 写失败必须出声：哨兵缺失 = 宿主等不到放行（超时撤销），静默会让失败路径不可见。
        /// </summary>
        /// <param name="repoRoot">仓库根</param>
        /// <param name="ok">前置段是否通过</param>
        /// <param name="reason">失败原因（ok=true 时为空串）</param>
        /// <param name="phase">阶段标记（preflight=前置段结论 / done=收尾结论——切换与自启最终结果）</param>
        private static void WriteRelay(string repoRoot, bool ok, string reason, string phase = "preflight")
        {
            try
            {
                string path = RelayPath(repoRoot);
                string dir = Path.GetDirectoryName(path);
                if (dir != null && dir.Length > 0)
                {
                    Directory.CreateDirectory(dir);
                }
                StringBuilder sb = new StringBuilder();
                sb.Append("{");
                sb.Append("\"ok\":" + (ok ? "true" : "false"));
                sb.Append(",\"reason\":" + System.Text.Json.JsonSerializer.Serialize(reason));
                // A135——阶段标记：preflight（前置段结论）/ done（收尾结论：切换与自启最终结果）
                sb.Append(",\"phase\":" + System.Text.Json.JsonSerializer.Serialize(phase));
                sb.Append(",\"ts\":\"" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\"");
                sb.Append("}");
                File.WriteAllText(path, sb.ToString());
            }
            catch (Exception ex)
            {
                Console.WriteLine("[SetUp] 警告：ready 哨兵写入失败——" + ex.Message);
            }
        }
        /// <summary>
        /// 是否测试项目路径（A134）——`*.Tests` 目录不进运行区产物，不构成「生成物面改动」判据。
        /// </summary>
        /// <param name="path">文件绝对路径</param>
        /// <returns>true=测试项目内文件（扫描时跳过）</returns>
        private static bool IsTestProjectPath(string path)
        {
            string[] parts = path.Split(Path.DirectorySeparatorChar);
            for (int i = 0; i < parts.Length; i = i + 1)
            {
                if (parts[i].EndsWith(".Tests", StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }
        /// <summary>
        /// 取目录下匹配文件的最新修改时刻——跳过 bin / obj（生成物噪声），并给出最新文件路径（拒绝提示用）。
        /// </summary>
        /// <param name="dir">根目录（不存在返回 0）</param>
        /// <param name="pattern">文件名模式（如 *.cs）</param>
        /// <param name="newestPath">最新文件的绝对路径（无匹配为空串）</param>
        /// <returns>最新修改时刻（Unix 毫秒；无匹配返回 0）</returns>
        private static long MaxTimeMs(string dir, string pattern, out string newestPath)
        {
            newestPath = "";
            long max = 0;
            if (!Directory.Exists(dir))
            {
                return 0;
            }
            string sep = Path.DirectorySeparatorChar.ToString();
            string[] files = Directory.GetFiles(dir, pattern, SearchOption.AllDirectories);
            for (int i = 0; i < files.Length; i = i + 1)
            {
                string p = files[i];
                if (p.IndexOf(sep + "bin" + sep, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continue;
                }
                if (p.IndexOf(sep + "obj" + sep, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continue;
                }
                // A134——测试项目不进运行区产物：不计入「生成物面改动」判据（误报源）
                if (IsTestProjectPath(p))
                {
                    continue;
                }
                long t = File.GetLastWriteTimeUtc(p).Ticks / TimeSpan.TicksPerMillisecond;
                if (t > max)
                {
                    max = t;
                    newestPath = p;
                }
            }
            return max;
        }
        /// <summary>
        /// incr 哨兵与拒绝面检查——源码比产物新即拒绝（搬运旧产物 = 静默「改了没生效」）；
        /// 命中生成物面（Bricks / Mau 基座 / 语料）时定向提示走 full（生成物不重翻 = 静默失效）。
        /// 判据 = 变更分类（Mau Job skill 方法一 A-E）：只搬「已在一键部署中验证过」的产物。
        /// </summary>
        /// <param name="repoRoot">仓库根</param>
        /// <param name="sourceDir">产物区（public/app）</param>
        /// <param name="reason">拒绝原因（空=通过）</param>
        /// <returns>true=通过</returns>
        private static bool CheckIncrSentinels(string repoRoot, string sourceDir, out string reason)
        {
            reason = "";
            string exe = Path.Combine(sourceDir, "CatHome4.exe");
            if (!File.Exists(exe))
            {
                reason = "产物区未就绪（" + exe + " 不存在）——请先跑一键部署（prepare）再走 incr。";
                return false;
            }
            string p1 = "";
            long exeMs = File.GetLastWriteTimeUtc(exe).Ticks / TimeSpan.TicksPerMillisecond;
            long artMs = exeMs;
            long flowMs = MaxTimeMs(Path.Combine(sourceDir, "Flows"), "*.dll", out p1);
            if (flowMs > artMs)
            {
                artMs = flowMs;
            }
            long genMs = MaxTimeMs(Path.Combine(repoRoot, "Bricks"), "*.cs", out p1);
            string genPath = p1;
            long t2 = MaxTimeMs(Path.Combine(repoRoot, "Mau"), "*.cs", out p1);
            if (t2 > genMs)
            {
                genMs = t2;
                genPath = p1;
            }
            long t3 = MaxTimeMs(Path.Combine(repoRoot, "Mau"), "*.csproj", out p1);
            if (t3 > genMs)
            {
                genMs = t3;
                genPath = p1;
            }
            if (genMs > artMs)
            {
                reason = "命中生成物面改动（Bricks / Mau 基座）——最新 " + genPath + "；生成物不重翻 = 静默失效，请走 restart-full。";
                return false;
            }
            long corpMs = MaxTimeMs(Path.Combine(repoRoot, "corpus", "ch4"), "*.mau", out p1);
            string corpPath = p1;
            long corpMs2 = MaxTimeMs(Path.Combine(repoRoot, "corpus", "ch4"), "*.mauproj", out p1);
            if (corpMs2 > corpMs)
            {
                corpMs = corpMs2;
                corpPath = p1;
            }
            if (corpMs > artMs)
            {
                reason = "语料比产物新——最新 " + corpPath + "；组翻译未重做 = 静默失效，请走 restart-full。";
                return false;
            }
            long srcMs = MaxTimeMs(Path.Combine(repoRoot, "CatHome4"), "*.cs", out p1);
            if (srcMs > exeMs)
            {
                reason = "宿主源码比产物新——最新 " + p1 + "；请先编译并一键部署（prepare），或走 restart-full。";
                return false;
            }
            return true;
        }
        /// <summary>探活超时毫秒（A135）——离线自检正常 ~5 s；30 s 上限（6 倍余量，与 full 全链 ~70 s 同量级），超时按失败处置并终止进程树</summary>
        private const int ProbeTargetTimeoutMs = 30000;
        /// <summary>
        /// 探活——用候选产物跑一次离线自检（非交互 / 零端口占用，A74）：--run "session count" 退出码 0 即通过。
        /// 探活即 prepare 步6 自检位的替代物（incr 跳过 prepare，该位空出，由前置探活补上）。
        /// </summary>
        /// <param name="targetDir">候选运行区（&lt;target&gt;_new）</param>
        /// <returns>true=探活通过</returns>
        private static bool ProbeTarget(string targetDir)
        {
            string exe = Path.Combine(targetDir, "CatHome4.exe");
            if (!File.Exists(exe))
            {
                Console.WriteLine("[SetUp] 错误：候选运行区缺少宿主——" + exe);
                return false;
            }
            Console.WriteLine("[SetUp] 探活：新产物离线自检（--run session count）…");
            // A135——探活带超时：自检挂起时按失败处置并终止进程树（不留孤儿占住暂存目录句柄）
            return RunProcess(exe, "--run \"session count\"", targetDir, ProbeTargetTimeoutMs);
        }
        /// <summary>
        /// incr 前置段——哨兵与拒绝面 → 准备段（关 html 对齐）→ 探活 → 落 ready 哨兵。
        /// 全程在旧宿主存活期完成：过了才放行宿主退场，停摆窗口只剩「等退出 + 切换 + 自启」。
        /// </summary>
        /// <param name="repoRoot">仓库根</param>
        /// <param name="targetFull">运行区绝对路径</param>
        /// <param name="sourceDir">产物区（public/app）</param>
        /// <returns>true=前置段通过（已落 ok 哨兵）</returns>
        private static bool RunIncrPreflight(string repoRoot, string targetFull, string sourceDir)
        {
            // [段16] 哨兵与拒绝面——源码比产物新即拒绝
            long chkMs = Environment.TickCount64;
            string reason;
            bool chkOk = CheckIncrSentinels(repoRoot, sourceDir, out reason);
            _steps.Add(new StepReport() { Step = 16, Name = "incr 哨兵与拒绝面检查", Ok = chkOk, Ms = Environment.TickCount64 - chkMs, Detail = chkOk ? "" : reason });
            if (!chkOk)
            {
                Console.WriteLine("[SetUp] ❌ " + reason);
                WriteRelay(repoRoot, false, reason);
                return false;
            }
            // [段17] 准备段——复制 _new + config 模板 + version.txt（关 html 对齐：incr 是搬运产物区，前端改动走 sync-html）
            long prepMs = Environment.TickCount64;
            string prepErr = PrepareTargetDir(repoRoot, sourceDir, targetFull, false);
            _steps.Add(new StepReport() { Step = 17, Name = "incr 准备段（复制 _new）", Ok = prepErr.Length == 0, Ms = Environment.TickCount64 - prepMs, Detail = prepErr });
            if (prepErr.Length > 0)
            {
                Console.WriteLine("[SetUp] ❌ " + prepErr);
                WriteRelay(repoRoot, false, prepErr);
                return false;
            }
            // [段18] 探活——新产物离线自检（prepare 步6 自检位的替代物）
            long probeMs = Environment.TickCount64;
            bool probeOk = ProbeTarget(targetFull + "_new");
            _steps.Add(new StepReport() { Step = 18, Name = "incr 探活（新产物离线自检）", Ok = probeOk, Ms = Environment.TickCount64 - probeMs });
            if (!probeOk)
            {
                string probeReason = "新产物离线自检未通过（--run session count 非零退出）——请走 restart-full，或先修好构建。";
                Console.WriteLine("[SetUp] ❌ " + probeReason);
                WriteRelay(repoRoot, false, probeReason);
                return false;
            }
            WriteRelay(repoRoot, true, "");
            Console.WriteLine("[SetUp] incr 前置段通过——已放行宿主退场。");
            return true;
        }
    }
}

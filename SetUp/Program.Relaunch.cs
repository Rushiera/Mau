using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace SetUp
{
    /// <summary>
    /// SetUp relaunch 模式——宿主自更新接力链：等旧宿主退出 → prepare → 原子切换部署 → 自启新宿主（带回执注入）。
    /// 定位：由旧宿主在退出前拉起（detached）；旧宿主保证"先起接力者再自杀"，本模式保证"接力者必拉起一个宿主"。
    /// 规范：Project/CH4/design-ch4-host-restart.md（§三 T5 / §四 4.4 / §六 失败矩阵）
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

        /// <summary>
        /// relaunch 模式入口——参数解析（零容忍）→ 等旧宿主退出 → prepare（失败继续）→ deploy 原子切换 → 自启新宿主 → 失败回退旧版本。
        /// </summary>
        /// <param name="repoRoot">仓库根（Main 已探测）</param>
        /// <param name="args">命令行参数（--wait-pid / --target / --majordomopush / --report）</param>
        /// <returns>退出码（0=部署与自启均成功；非0=有失败段——以报告为准）</returns>
        private static int Relaunch(string repoRoot, string[] args)
        {
            Console.WriteLine("[SetUp] relaunch —— 宿主自更新接力");
            string badArgs = ValidateRelaunchArgs(args);
            if (badArgs.Length > 0)
            {
                Console.WriteLine("[SetUp] " + badArgs);
                PrintHelp();
                return 1;
            }
            string pidRaw = ExtractOption(args, "--wait-pid");
            string target = ExtractOption(args, "--target");
            string pushIn = ExtractOption(args, "--majordomopush");
            int waitPid = int.Parse(pidRaw);
            Console.WriteLine("  等待宿主退出: pid " + waitPid.ToString());
            Console.WriteLine("  运行区: " + target);

            // [段1] 等旧宿主退出——宿主必须先退（宿主程序集 dll 被 ALC 持锁，运行中不可覆盖）
            long waitMs = Environment.TickCount64;
            bool exited = WaitProcessExit(waitPid, RelaunchWaitPidMs);
            _steps.Add(new StepReport() { Step = 11, Name = "等待旧宿主退出 (pid " + waitPid.ToString() + ")", Ok = exited, Ms = Environment.TickCount64 - waitMs });
            if (!exited)
            {
                return Fail("旧宿主未在 " + RelaunchWaitPidMs.ToString() + " ms 内退出——中止，不触碰运行区。");
            }

            // [段2] prepare 全链——失败继续（定位：小改动自测；失败步随注入串回执给 Majordomo）
            long prepMs = Environment.TickCount64;
            bool prepOk = Prepare(repoRoot) == 0;
            if (!prepOk)
            {
                Console.WriteLine("[SetUp] 警告：prepare 未通过——按定位继续部署（回执标注失败）。");
            }
            _steps.Add(new StepReport() { Step = 12, Name = "prepare 全链（步明细见 1-6）", Ok = prepOk, Ms = Environment.TickCount64 - prepMs });

            // [段3] deploy 原子切换——失败不中止，交由启动段决定是否回退
            long depMs = Environment.TickCount64;
            bool depOk = Deploy(repoRoot, target) == 0;
            if (!depOk)
            {
                Console.WriteLine("[SetUp] 警告：deploy 未通过——尝试以现有运行区启动（步明细见 deploy 段）。");
            }
            _steps.Add(new StepReport() { Step = 13, Name = "deploy 原子切换（步明细见 1-3）", Ok = depOk, Ms = Environment.TickCount64 - depMs });

            // [段4] 拼回执注入串——由 SetUp 生成（非 LLM 自述），带部署结论与产物时间戳
            string targetFull = Path.GetFullPath(target);
            string version = ReadVersion(repoRoot);
            string reportPath = ExtractOption(args, "--report");
            string push = BuildPushText(prepOk, depOk, version, targetFull, pushIn, reportPath);

            // [段5] 自启新宿主 + 探活
            long bootMs = Environment.TickCount64;
            bool bootOk = StartHost(targetFull, push);
            _steps.Add(new StepReport() { Step = 14, Name = "自启新宿主（含探活 " + RelaunchProbeMs.ToString() + " ms）", Ok = bootOk, Ms = Environment.TickCount64 - bootMs });

            // [段6] 回退——新版本起不来则以旧版本目录启动（"最坏 = 原地重启"的兑现路径）
            bool fallbackOk = false;
            if (!bootOk)
            {
                Console.WriteLine("[SetUp] 警告：新版本启动失败——回退旧版本目录。");
                string fallbackPush = "[宿主自更新] 新版本启动失败——已回退旧版本。"
                    + Environment.NewLine + "说明: " + pushIn;
                long fbMs = Environment.TickCount64;
                fallbackOk = StartHost(targetFull + "_old", fallbackPush);
                _steps.Add(new StepReport() { Step = 15, Name = "回退启动旧版本 (<target>_old)", Ok = fallbackOk, Ms = Environment.TickCount64 - fbMs });
            }

            // [段7] 运行区产物快照——报告附加项（版本自证）
            CollectTargetArtifacts(targetFull);
            if (bootOk)
            {
                Console.WriteLine("[SetUp] relaunch 完成——新宿主已接管运行区。");
                return 0;
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
            string[] allowed = new string[] { "--wait-pid", "--target", "--majordomopush", "--report" };
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
                    return "参数错误：未知参数 " + arg + "（支持 --wait-pid / --target / --majordomopush / --report）。";
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
        /// 拼回执注入串——部署结论 + 版本 + 产物时间戳 + 报告路径 + 附加说明（由 SetUp 生成，非 LLM 自述）。
        /// </summary>
        /// <param name="prepOk">prepare 是否通过</param>
        /// <param name="depOk">deploy 是否通过</param>
        /// <param name="version">版本号（CatHome4.csproj Version）</param>
        /// <param name="targetFull">运行区绝对路径</param>
        /// <param name="pushIn">调用方附加说明（可为空）</param>
        /// <param name="reportPath">报告路径（可为空）</param>
        /// <returns>注入串全文</returns>
        private static string BuildPushText(bool prepOk, bool depOk, string version, string targetFull, string pushIn, string reportPath)
        {
            string exeTs = "";
            string exePath = Path.Combine(targetFull, "CatHome4.exe");
            if (File.Exists(exePath))
            {
                exeTs = File.GetLastWriteTime(exePath).ToString("yyyy-MM-dd HH:mm:ss");
            }
            StringBuilder sb = new StringBuilder();
            sb.Append("[宿主自更新] 重启完成");
            sb.Append(Environment.NewLine);
            sb.Append("结果: " + (prepOk && depOk ? "OK" : "FAIL")
                + (prepOk ? "" : " | prepare 未通过")
                + (depOk ? "" : " | deploy 未通过"));
            sb.Append(Environment.NewLine);
            sb.Append("版本: " + version);
            sb.Append(Environment.NewLine);
            sb.Append("部署: prepare " + (prepOk ? "OK" : "FAIL") + " · deploy " + (depOk ? "OK" : "FAIL"));
            sb.Append(Environment.NewLine);
            sb.Append("自启: 新版本");
            sb.Append(Environment.NewLine);
            sb.Append("产物: CatHome4.exe " + exeTs);
            sb.Append(Environment.NewLine);
            if (reportPath.Length > 0)
            {
                sb.Append("报告: " + reportPath);
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
    }
}

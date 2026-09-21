using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Mau.Runtime;

namespace CH4
{
    /// <summary>
    /// Program 宿主重启分部——重启态判定 / 停机拒收 / 全局 Idle 闸门 / 接力拉起（SetUp relaunch）/ 启动回执注入。
    /// 规范：Project/CH4/design-ch4-host-restart.md（§三 T2-T6 / §四 4.3 / §五 参数契约）。
    /// 定位：小改动自测的宿主自更新链——调用方固定为 Majordomo 会话经 majordomo-restart 特权工具。
    /// </summary>
    public static partial class Program
    {
        /// <summary>重启停机态——DataBox 全局盒键（"requested" = 停机；新需求一律拒收）</summary>
        private const string RestartStateKey = "host_restart_state";

        /// <summary>重启请求载荷——DataBox 全局盒键（JSON：target/push）</summary>
        private const string RestartRequestKey = "host_restart_request";

        /// <summary>运行模式标记——DataBox 全局盒键（"interactive" = 常驻主循环；其余 = 无闸门可等，工具侧拒绝）</summary>
        private const string RunModeKey = "host_run_mode";

        /// <summary>启动回执注入串——<c>--majordomopush</c> 值（主循环首帧投递一次后清空）</summary>
        private static string _majordomoPush = "";

        /// <summary>
        /// 宿主参数面校验——零容忍：未知参数 / 缺值 / 与非常驻模式互斥一律报错（design-ch4-host-restart §五）。
        /// </summary>
        /// <param name="args">命令行参数</param>
        /// <param name="nonInteractive">非交互模式（--run/--script/--selfcheck/--tool-check——不启动外观层与常驻附属）</param>
        /// <returns>错误文本（空=通过）</returns>
        private static string ValidateHostArgs(string[] args, out bool nonInteractive)
        {
            nonInteractive = false;
            bool hasPush = false;
            bool hasNonInteractive = false;
            for (int i = 0; i < args.Length; i = i + 1)
            {
                string arg = args[i];
                if (arg == "-dll" || arg == "--run" || arg == "--script" || arg == "--probe-llm")
                {
                    if (i + 1 >= args.Length)
                    {
                        return "参数错误：" + arg + " 缺值。";
                    }
                    if (arg == "--run" || arg == "--script")
                    {
                        hasNonInteractive = true;
                    }
                    i = i + 1;
                }
                else if (arg == "--tool-check")
                {
                    if (i + 2 >= args.Length)
                    {
                        return "参数错误：--tool-check 需 <工具名> <参数JSON>。";
                    }
                    hasNonInteractive = true;
                    i = i + 2;
                }
                else if (arg == "--selfcheck")
                {
                    hasNonInteractive = true;
                }
                else if (arg == "--majordomopush")
                {
                    if (i + 1 >= args.Length || args[i + 1].Length == 0)
                    {
                        return "参数错误：--majordomopush 缺值（需注入串）。";
                    }
                    hasPush = true;
                    i = i + 1;
                }
                else if (arg.Length > 0 && arg[0] == '-')
                {
                    return "参数错误：未知参数 " + arg + "。";
                }
            }
            if (hasPush && hasNonInteractive)
            {
                return "参数错误：--majordomopush 仅用于常驻交互启动（不可与 --run/--script/--selfcheck/--tool-check 同用）。";
            }
            nonInteractive = hasNonInteractive;
            return "";
        }

        /// <summary>
        /// 取参数值——未出现返回空串（缺值由校验段负责报错）。
        /// </summary>
        /// <param name="args">命令行参数</param>
        /// <param name="name">参数名</param>
        /// <returns>参数值或空串</returns>
        private static string ExtractArgValue(string[] args, string name)
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
        /// 宿主是否处于重启停机态。
        /// </summary>
        /// <returns>true=停机态（拒收新需求 + 等闸门）</returns>
        private static bool IsRestarting()
        {
            string state;
            if (DataBox.TryGet<string>("global", RestartStateKey, out state))
            {
                return state == "requested";
            }
            return false;
        }

        /// <summary>
        /// 启动回执注入泵——主循环每帧调用；有注入串则投给 Majordomo 会话（来源 system），投完即清（一次性）。
        /// </summary>
        private static void PumpMajordomoPush()
        {
            if (_majordomoPush.Length == 0)
            {
                return;
            }
            string push = _majordomoPush;
            _majordomoPush = "";
            if (_chatBridge == null)
            {
                return;
            }
            // A72——回执经延迟队列投递（dueAt=now 立即注入；统一出口，可观测、落盘可查——design-ch4-delay §7.2）
            string added = DelayQueue.Add(_chatBridge.DefaultSession.Id, push, "restart", DelayQueue.Now());
            if (added.StartsWith("ERR|", StringComparison.Ordinal))
            {
                // 失败可见——回落直投（回执不丢），并出声
                LogStore.Add("CatHome4", 2, "宿主启动回执登记延迟队列失败（回落直投）: " + added, "RESTART");
                _chatBridge.DefaultSession.PostUserMessage(push, "system");
                return;
            }
            LogStore.Add("CatHome4", 1, "宿主启动回执已登记延迟队列（到点注入 Majordomo 会话）", "RESTART");
        }

        /// <summary>
        /// 重启闸门泵——主循环每帧调用：停机态 + 全局 Idle → 执行接力（起 SetUp 后退出）。
        /// 在途轮次由停机态自然收尾；闸门等的是在途，不是等人（design-ch4-host-restart §三 T3）。
        /// </summary>
        private static void PumpHostRestart()
        {
            if (!IsRestarting())
            {
                return;
            }
            if (!AllIdle())
            {
                return;
            }
            ExecuteRestart();
        }

        /// <summary>
        /// 执行接力——起 SetUp relaunch（detached）→ 确认进程已起 → 观测收尾 → 退出宿主。
        /// 顺序铁律：先确认接力者已起，再自杀——反序即无人拉起（design-ch4-host-restart §三 T4）。
        /// </summary>
        private static void ExecuteRestart()
        {
            int pid = Environment.ProcessId;
            string reqJson = "";
            string raw;
            if (DataBox.TryGet<string>("global", RestartRequestKey, out raw) && raw != null)
            {
                reqJson = raw;
            }
            string target = ReadJsonField(reqJson, "target");
            string push = ReadJsonField(reqJson, "push");
            if (target.Length == 0)
            {
                target = ResolveMauOutRoot();
            }
            if (target.Length == 0)
            {
                AbortRestart("重启请求缺少目标运行区（target 未提供且受控根 mauout 未配置）——已撤销重启态。");
                return;
            }
            string repoRoot = ResolveRepoRootForRestart();
            if (repoRoot.Length == 0)
            {
                AbortRestart("未找到仓库根（运行区向上探测失败且受控根 mau 未配置）——无法定位 SetUp.exe，已撤销重启态。");
                return;
            }
            string setupExe = Path.Combine(repoRoot, "SetUp.exe");
            if (!File.Exists(setupExe))
            {
                AbortRestart("SetUp.exe 不存在：" + setupExe + "——已撤销重启态。");
                return;
            }
            string report = Path.Combine(repoRoot, "CatTemp", "restart_report.json");
            try
            {
                string reportDir = Path.GetDirectoryName(report);
                if (reportDir != null && reportDir.Length > 0)
                {
                    Directory.CreateDirectory(reportDir);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[CMD] 警告：重启报告目录创建失败——" + ex.Message);
            }
            ProcessStartInfo psi = new ProcessStartInfo(setupExe);
            psi.WorkingDirectory = repoRoot;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.ArgumentList.Add("relaunch");
            psi.ArgumentList.Add("--wait-pid");
            psi.ArgumentList.Add(pid.ToString());
            psi.ArgumentList.Add("--target");
            psi.ArgumentList.Add(target);
            if (push.Length > 0)
            {
                psi.ArgumentList.Add("--majordomopush");
                psi.ArgumentList.Add(push);
            }
            psi.ArgumentList.Add("--report");
            psi.ArgumentList.Add(report);
            Process relay;
            try
            {
                relay = Process.Start(psi);
            }
            catch (Exception ex)
            {
                AbortRestart("SetUp relaunch 启动失败——" + ex.Message + "（已撤销重启态）");
                return;
            }
            if (relay == null)
            {
                AbortRestart("SetUp relaunch 启动失败（Process.Start 返回空）——已撤销重启态。");
                return;
            }
            // 确认接力者确已起来——1 秒内未退出即视为存活（先起接力者，再自杀）
            bool relayDead = relay.WaitForExit(1000);
            if (relayDead)
            {
                AbortRestart("SetUp relaunch 启动后立即退出（exit=" + relay.ExitCode.ToString() + "）——已撤销重启态。");
                return;
            }
            LogStore.Add("CatHome4", 1, "重启接力已拉起：SetUp relaunch pid " + relay.Id.ToString() + " → 目标 " + target, "RESTART");
            Console.WriteLine("[CMD] 重启接力已拉起（SetUp pid " + relay.Id.ToString() + "）——宿主退出。");
            // A33——转发态全量快照兜底（T4）：稳态虽已「变更即落盘」，此处再覆写一次（覆盖收尾窗口内尚未落盘的状态变更）
            CatHome4.QQ.QQBotService.SaveForwardState();
            // 观测收尾——四文件 flush（与 Main finally 同一路径）
            LogStore.CloseWriters();
            FrameStore.Close();
            Environment.Exit(0);
        }

        /// <summary>
        /// 撤销重启态——接力未能拉起（或前置缺失）：清停机态与请求载荷，宿主继续运行。
        /// </summary>
        /// <param name="reason">撤销原因（日志 + 控制台）</param>
        private static void AbortRestart(string reason)
        {
            DataBox.Set<string>("global", RestartStateKey, "");
            DataBox.Remove("global", RestartRequestKey);
            LogStore.Add("CatHome4", 3, "重启已撤销：" + reason, "RESTART");
            Console.WriteLine("[CMD] " + reason);
        }
        /// <summary>
        /// 重启链仓库根解析——三级锚定（与 ResolveDataRoot 同构）：
        /// 1. CH4_REPO_ROOT 环境变量（显式逃逸口——多实例/自定义落位）
        /// 2. 运行区向上探测（Mau.sln——开发/测试区形态：运行区位于仓库内）
        /// 3. 受控根 mau（workspace 配置——外部部署区形态：运行区不在仓库内，双校验 Mau.sln + SetUp.exe）
        /// </summary>
        /// <returns>仓库根绝对路径（空=三级皆未命中）</returns>
        private static string ResolveRepoRootForRestart()
        {
            string envRoot = Environment.GetEnvironmentVariable("CH4_REPO_ROOT");
            if (envRoot != null && envRoot.Length > 0 && File.Exists(Path.Combine(envRoot, "SetUp.exe")))
            {
                return envRoot;
            }
            string probed = FindRepoRoot(AppContext.BaseDirectory);
            if (probed.Length > 0 && File.Exists(Path.Combine(probed, "SetUp.exe")))
            {
                return probed;
            }
            WorkspaceConfig cfg;
            if (DataBox.TryResolve<WorkspaceConfig>(out cfg) && cfg != null && cfg.Roots != null)
            {
                for (int i = 0; i < cfg.Roots.Length; i = i + 1)
                {
                    if (!string.Equals(cfg.Roots[i].Id, "mau", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    string path = cfg.Roots[i].Path;
                    if (File.Exists(Path.Combine(path, "Mau.sln")) && File.Exists(Path.Combine(path, "SetUp.exe")))
                    {
                        return path;
                    }
                }
            }
            return "";
        }

        /// <summary>
        /// 解析受控根 mauout 路径——workspace 配置（Bootstrap 已 Bind）中 id=mauout 的根。
        /// </summary>
        /// <returns>运行区绝对路径（空=未配置）</returns>
        private static string ResolveMauOutRoot()
        {
            WorkspaceConfig cfg;
            if (DataBox.TryResolve<WorkspaceConfig>(out cfg) && cfg != null && cfg.Roots != null)
            {
                for (int i = 0; i < cfg.Roots.Length; i = i + 1)
                {
                    if (string.Equals(cfg.Roots[i].Id, "mauout", StringComparison.OrdinalIgnoreCase))
                    {
                        return cfg.Roots[i].Path;
                    }
                }
            }
            return "";
        }

        /// <summary>
        /// 取 JSON 对象字符串字段——防御式（非对象/缺字段/解析失败一律返回空串）。
        /// </summary>
        /// <param name="json">JSON 文本</param>
        /// <param name="field">字段名</param>
        /// <returns>字段值或空串</returns>
        private static string ReadJsonField(string json, string field)
        {
            if (json == null || json.Length == 0)
            {
                return "";
            }
            try
            {
                JsonDocument doc = JsonDocument.Parse(json);
                try
                {
                    JsonElement el;
                    if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty(field, out el) && el.ValueKind == JsonValueKind.String)
                    {
                        return el.GetString() ?? "";
                    }
                }
                finally
                {
                    doc.Dispose();
                }
            }
            catch (Exception ex)
            {
                LogStore.Add("CatHome4", 2, "重启参数提取失败: " + ex.Message, "RESTART");
            }
            return "";
        }
    }
}

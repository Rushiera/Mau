using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Mau.Cli
{
    /// <summary>
    /// mau up 命令——Build 全链聚合器（design-mau-rebuild.md §一）。
    /// 解析构建清单 → 逐环节指纹判定 → 拓扑序执行 → 报告 → 更新 CatTemp 中间态。
    /// Mau 系环节进程内执行（不产生第二实例，D22）；外部工程命令（dotnet/ch4）子进程执行。
    /// </summary>
    public static class CommandUp
    {
        /// <summary>
        /// 状态文件目录名——清单仓库根 CatTemp/build-state/
        /// </summary>
        private const string StateDirName = "build-state";

        /// <summary>
        /// 执行 mau up
        /// </summary>
        /// <param name="args">参数——[-f 清单路径] [--force]</param>
        /// <returns>退出码——0 全过/跳过，1 有失败，2 内部错误</returns>
        public static int Execute(string[] args)
        {
            string manifestPath = "";
            bool force = false;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-f" && i + 1 < args.Length)
                {
                    manifestPath = args[i + 1];
                    i = i + 1;
                }
                else if (args[i] == "--force")
                {
                    force = true;
                }
            }
            if (manifestPath.Length == 0)
            {
                // 默认查找——当前目录向上：*.build 清单（工程语料库次级约定）
                manifestPath = FindDefaultManifest();
                if (manifestPath.Length == 0)
                {
                    Console.WriteLine("FAIL: 未指定清单（-f <路径>），当前目录向上未找到 *.build");
                    return 2;
                }
            }

            BuildManifest manifest = BuildManifest.Load(manifestPath);
            if (manifest.Error.Length > 0)
            {
                Console.WriteLine("FAIL: " + manifest.Error);
                return 2;
            }
            if (manifest.Steps.Count == 0)
            {
                Console.WriteLine("FAIL: 清单 " + manifest.Name + " 无环节");
                return 2;
            }

            // 状态文件——清单仓库根 CatTemp/build-state/{name}.json
            string repoRoot = FindRepoRoot(manifest.DirectoryPath);
            string stateDir = Path.Combine(repoRoot, "CatTemp", StateDirName);
            string stateFile = Path.Combine(stateDir, manifest.Name + ".json");
            Dictionary<string, string> previous = LoadState(stateFile);

            Console.WriteLine("构建: " + manifest.Name + "（清单 " + Path.GetFileName(manifestPath) + "）");
            Console.WriteLine("环节 " + manifest.Steps.Count + " 个 | " + (force ? "--force 全量" : "指纹判定"));

            // 拓扑序执行
            List<string> order = manifest.TopologicalOrder();
            Dictionary<string, string> fingerprints = new Dictionary<string, string>();
            Dictionary<string, bool> rerun = new Dictionary<string, bool>();
            for (int i = 0; i < order.Count; i++)
            {
                BuildManifest.BuildStep? step = manifest.FindStep(order[i]);
                if (step == null)
                {
                    continue;
                }
                List<string> files = manifest.ExpandInputs(step);
                string fingerprint = BuildManifest.ComputeListFingerprint(files);
                fingerprints[step.Name] = fingerprint;
                int brickMismatch = BuildManifest.CountBrickChecksumMismatch(files);
                string? previousFp;
                if (!previous.TryGetValue(step.Name + ".fp", out previousFp))
                {
                    previousFp = "";
                }
                bool changed = !force && (previousFp.Length == 0 || previousFp != fingerprint || brickMismatch > 0);
                // 前置重跑传播
                for (int p = 0; p < step.Prereqs.Count; p++)
                {
                    bool preRerun;
                    if (rerun.TryGetValue(step.Prereqs[p], out preRerun) && preRerun)
                    {
                        changed = true;
                    }
                }
                rerun[step.Name] = changed;
                string result = changed ? "RUN" : "SKIP";
                if (changed)
                {
                    Console.WriteLine("▶ " + step.Name + "（指纹" + (previousFp.Length == 0 ? "首次" : "变化") + (brickMismatch > 0 ? "·块尾失配" + brickMismatch : "") + "）");
                    Stopwatch sw = Stopwatch.StartNew();
                    int code = ExecuteStep(manifest, step);
                    sw.Stop();
                    if (code != 0)
                    {
                        Console.WriteLine("❌ " + step.Name + "（退出码 " + code + "·" + sw.ElapsedMilliseconds + "ms）");
                        SaveState(stateFile, manifest, fingerprints, rerun);
                        return 1;
                    }
                    Console.WriteLine("✅ " + step.Name + "（" + sw.ElapsedMilliseconds + "ms）");
                }
                else
                {
                    Console.WriteLine("⏭ " + step.Name + "（指纹未变·跳过）");
                }
            }

            SaveState(stateFile, manifest, fingerprints, rerun);
            Console.WriteLine("MAU_UP_OK");
            return 0;
        }

        /// <summary>
        /// 执行单环节——mau 系进程内 / 外部子进程
        /// </summary>
        /// <param name="manifest">清单</param>
        /// <param name="step">环节</param>
        /// <returns>退出码</returns>
        private static int ExecuteStep(BuildManifest manifest, BuildManifest.BuildStep step)
{
            string command = step.Command.Trim();
            if (command.Length == 0)
            {
                return 2;
            }
            // 首词切分
            int space = command.IndexOf(' ');
            string head = space > 0 ? command.Substring(0, space) : command;
            string rest = space > 0 ? command.Substring(space + 1).Trim() : "";
            string workDir = FindRepoRoot(manifest.DirectoryPath);
            if (head == "mau")
            {
                // 进程内执行——不产生第二实例（D22）；工作目录切到仓库根（相对路径命令基于仓库根解析）
                string oldCwd = Environment.CurrentDirectory;
                Environment.CurrentDirectory = workDir;
                try
                {
                    string[] innerArgs = rest.Length > 0 ? rest.Split(' ') : new string[0];
                    return Program.Dispatch(innerArgs);
                }
                finally
                {
                    Environment.CurrentDirectory = oldCwd;
                }
            }
            // 外部命令子进程——工作目录 = 仓库根（清单所在仓库；相对路径命令基于仓库根解析）
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = head;
            psi.Arguments = rest;
            psi.WorkingDirectory = workDir;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            try
            {
                using (Process? process = Process.Start(psi))
                {
                    if (process == null)
                    {
                        Console.Error.WriteLine("  子进程启动失败: " + head);
                        return 2;
                    }
                    string stdout = process.StandardOutput.ReadToEnd();
                    string stderr = process.StandardError.ReadToEnd();
                    process.WaitForExit();
                    int exitCode = process.ExitCode;
                    if (exitCode != 0)
                    {
                        if (stdout.Trim().Length > 0)
                        {
                            Console.Error.WriteLine(stdout.Trim());
                        }
                        if (stderr.Trim().Length > 0)
                        {
                            Console.Error.WriteLine(stderr.Trim());
                        }
                    }
                    return exitCode;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("  子进程执行失败: " + head + "——" + ex.Message);
                return 2;
            }
        }        /// <summary>
        /// 加载上次状态——{环节}.fp → 指纹
        /// </summary>
        /// <param name="stateFile">状态文件路径</param>
        /// <returns>状态字典</returns>
        private static Dictionary<string, string> LoadState(string stateFile)
        {
            Dictionary<string, string> state = new Dictionary<string, string>();
            if (!File.Exists(stateFile))
            {
                return state;
            }
            try
            {
                string[] lines = File.ReadAllText(stateFile).Replace("\r\n", "\n").Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.Length == 0)
                    {
                        continue;
                    }
                    int eq = line.IndexOf('=');
                    if (eq > 0)
                    {
                        state[line.Substring(0, eq)] = line.Substring(eq + 1);
                    }
                }
            }
            catch
            {
                // 状态损坏——视为首次（全量重跑）
            }
            return state;
        }

        /// <summary>
        /// 保存状态——每环节指纹 + 结果
        /// </summary>
        /// <param name="stateFile">状态文件路径</param>
        /// <param name="manifest">清单</param>
        /// <param name="fingerprints">指纹表</param>
        /// <param name="rerun">重跑表</param>
        private static void SaveState(string stateFile, BuildManifest manifest,
            Dictionary<string, string> fingerprints, Dictionary<string, bool> rerun)
        {
            try
            {
                string dir = Path.GetDirectoryName(stateFile) ?? ".";
                Directory.CreateDirectory(dir);
                StringBuilder sb = new StringBuilder();
                sb.Append("name=").Append(manifest.Name).Append('\n');
                sb.Append("version=").Append(manifest.Version).Append('\n');
                sb.Append("time=").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append('\n');
                for (int i = 0; i < manifest.Steps.Count; i++)
                {
                    string name = manifest.Steps[i].Name;
                    string? fp;
                    if (!fingerprints.TryGetValue(name, out fp))
                    {
                        fp = "";
                    }
                    bool run;
                    rerun.TryGetValue(name, out run);
                    sb.Append(name).Append(".fp=").Append(fp).Append('\n');
                    sb.Append(name).Append(".run=").Append(run ? "1" : "0").Append('\n');
                }
                File.WriteAllText(stateFile, sb.ToString(), new UTF8Encoding(false));
            }
            catch
            {
                // 状态保存失败不影响结果（下次全量重跑兜底）
            }
        }

        /// <summary>
        /// 查找默认清单——当前目录向上第一个 *.build
        /// </summary>
        /// <returns>清单路径或空</returns>
        private static string FindDefaultManifest()
        {
            string? dir = Directory.GetCurrentDirectory();
            while (dir != null)
            {
                string[] candidates = Directory.GetFiles(dir, "*.build", SearchOption.TopDirectoryOnly);
                if (candidates.Length > 0)
                {
                    return Path.GetFullPath(candidates[0]);
                }
                dir = Directory.GetParent(dir)?.FullName;
            }
            return "";
        }

        /// <summary>
        /// 查找仓库根——向上找含 CatTemp 的目录（或含 .sln/.git）
        /// </summary>
        /// <param name="startDir">起始目录</param>
        /// <returns>仓库根（找不到返回起始目录）</returns>
        private static string FindRepoRoot(string startDir)
        {
            string? dir = new DirectoryInfo(startDir).FullName;
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir, "CatTemp"))
                    || File.Exists(Path.Combine(dir, ".git"))
                    || Directory.Exists(Path.Combine(dir, ".git")))
                {
                    return dir;
                }
                dir = Directory.GetParent(dir)?.FullName;
            }
            return new DirectoryInfo(startDir).FullName;
        }
    }
}

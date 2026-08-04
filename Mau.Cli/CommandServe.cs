using System;
using System.IO;
using System.Text;
using System.Threading;
using Mau.Runtime;
using Mau.Translator;
using Mau.Observer;

namespace Mau.Cli
{
    /// <summary>
    /// mau serve 命令——持久 HTTP 观察面板
    /// 需要环境 .NET 8 SDK
    /// </summary>
    public static class CommandServe
    {
        /// <summary>
        /// 执行 mau serve
        /// </summary>
        /// <param name="args">命令行参数——不含 "serve" 本身</param>
        /// <returns>退出码</returns>
        public static int Execute(string[] args)
        {
            if (args.Length == 0)
            {
                Console.Error.WriteLine("用法: mau serve <file.mau> [--port N] [--ticks-per-sec N]");
                Console.Error.WriteLine("需要环境 .NET 8 SDK");
                return 1;
            }

            string mauFile = args[0];
            int port = 9230;
            int ticksPerSec = 10;

            for (int i = 1; i < args.Length; i = i + 1)
            {
                if (args[i] == "--port" && i + 1 < args.Length)
                {
                    if (int.TryParse(args[i + 1], out int p))
                    {
                        port = p;
                    }
                    i = i + 1;
                }
                else if (args[i] == "--ticks-per-sec" && i + 1 < args.Length)
                {
                    if (int.TryParse(args[i + 1], out int t))
                    {
                        ticksPerSec = t;
                    }
                    i = i + 1;
                }
            }

            if (!File.Exists(mauFile))
            {
                Console.Error.WriteLine("错误: .mau 文件不存在: " + mauFile);
                return 1;
            }

            // [1] 解析 + 验证 + 生成
            string sourceText = File.ReadAllText(mauFile, Encoding.UTF8);
            ParseResult parseResult = MauParser.Parse(sourceText);
            if (parseResult.Diagnostics.Count > 0)
            {
                Console.Error.WriteLine("Mau 语法错误:");
                for (int i = 0; i < parseResult.Diagnostics.Count; i = i + 1)
                {
                    Console.Error.WriteLine("  " + parseResult.Diagnostics[i].ToString());
                }
                return 1;
            }

            MauDocument doc = parseResult.Document;
            System.Collections.Generic.List<MauDiagnostic> diags = MauValidator.Validate(doc);
            if (diags.Count > 0)
            {
                Console.Error.WriteLine("Mau 验证失败:");
                for (int i = 0; i < diags.Count; i = i + 1)
                {
                    Console.Error.WriteLine("  " + diags[i].ToString());
                }
                return 1;
            }

            string flowName = Path.GetFileNameWithoutExtension(mauFile);
            string csSource = CodeGenerator.Generate(doc, flowName);
            string className = "FL_" + flowName;

            // [2] 临时编译
            string tempDir = Path.Combine(Path.GetTempPath(), "mau_serve_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            TempProjectBuilder.BuildResult buildResult = TempProjectBuilder.Build(csSource, className, tempDir);
            if (!buildResult.Success)
            {
                Console.Error.WriteLine("C# 编译失败:");
                if (buildResult.BuildOutput != null)
                {
                    Console.Error.WriteLine(buildResult.BuildOutput);
                }
                return 2;
            }

            // [3] 加载
            FlowHost host = new FlowHost();
            try
            {
                host.Load(buildResult.DllPath!);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("DLL 加载失败: " + ex.Message);
                return 3;
            }

            // [4] 启动 HTTP 传输层
            HttpTransport transport = new HttpTransport(port);
            if (!transport.Start())
            {
                Console.Error.WriteLine("错误: 端口 " + port.ToString() + " 已被占用。请使用 --port 指定其他端口。");
                host.Dispose();
                return 4;
            }

            // [5] 启动观察器
            RuntimeObserver observer = new RuntimeObserver(host);
            observer.Bind(transport);

            // [6] 主循环
            int sleepMs = 1000 / ticksPerSec;
            if (sleepMs < 1)
            {
                sleepMs = 1;
            }

            Console.WriteLine("🐱 Mau Observer 已启动");
            Console.WriteLine("   HTTP:       http://localhost:" + port.ToString() + "/");
            Console.WriteLine("   Status API: http://localhost:" + port.ToString() + "/status");
            Console.WriteLine("   WebSocket:  ws://localhost:" + port.ToString() + "/ws");
            Console.WriteLine("   Tick 频率:  " + ticksPerSec.ToString() + "/秒");
            Console.WriteLine("   按 Ctrl+C 停止...");
            Console.WriteLine();

            CancellationTokenSource cts = new CancellationTokenSource();
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            int statusInterval = ticksPerSec * 5;
            if (statusInterval < 1)
            {
                statusInterval = 1;
            }
            int tickCount = 0;

            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    host.TickAll();
                    observer.Tick();
                    tickCount = tickCount + 1;

                    if (tickCount % statusInterval == 0)
                    {
                        AggregatedSnapshot? snap = observer.GetLatestSnapshot();
                        FlowSnapshotEntry[] flows = snap?.Flows ?? Array.Empty<FlowSnapshotEntry>();
                        SystemEvent[] sysEvents = snap?.SystemEvents ?? Array.Empty<SystemEvent>();

                        StringBuilder line = new StringBuilder();
                        line.Append("\r🐱 帧 " + observer.Frame.ToString());
                        for (int i = 0; i < flows.Length; i = i + 1)
                        {
                            line.Append("  " + flows[i].FlowName + " [");
                            bool hasActive = false;
                            for (int p = 0; p < flows[i].Status.Propositions.Length; p = p + 1)
                            {
                                PropSnapshot prop = flows[i].Status.Propositions[p];
                                if (prop.Value)
                                {
                                    line.Append(prop.Name + " ");
                                    hasActive = true;
                                }
                            }
                            for (int t = 0; t < flows[i].Status.Transitions.Length; t = t + 1)
                            {
                                TransSnapshot trans = flows[i].Status.Transitions[t];
                                if (trans.CubeState != "Idle")
                                {
                                    line.Append(trans.Name + ":" + trans.CubeState + " ");
                                    hasActive = true;
                                }
                            }
                            if (!hasActive)
                            {
                                line.Append("idle");
                            }
                            line.Append("]");
                            int logCount = flows[i].Logs.Length;
                            if (logCount > 0)
                            {
                                line.Append(" Log×" + logCount.ToString());
                            }
                        }
                        if (sysEvents.Length > 0)
                        {
                            line.Append("  ⚠ " + sysEvents.Length.ToString() + " 事件");
                        }
                        Console.Write(line.ToString().PadRight(90));
                    }

                    Thread.Sleep(sleepMs);
                }
            }
            catch (OperationCanceledException)
            {
                // 正常退出
            }
            Console.WriteLine();

            // [7] 清理
            Console.WriteLine("正在停止...");
            transport.Stop();
            host.Dispose();
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
            catch
            {
                // 忽略
            }

            Console.WriteLine("已停止。");
            return 0;
        }
    }
}

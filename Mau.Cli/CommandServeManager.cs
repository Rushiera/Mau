using System;
using Mau.Serve;

namespace Mau.Cli
{
    /// <summary>
    /// mau serve 服务管理命令——spawn / stop / status / call
    /// </summary>
    public static class CommandServeManager
    {
        /// <summary>
        /// 执行服务管理命令
        /// </summary>
        /// <param name="args">命令行参数——不含 "serve" 本身</param>
        /// <returns>退出码</returns>
        public static int Execute(string[] args)
        {
            if (args.Length < 1)
            {
                PrintUsage();
                return 1;
            }
            string sub = args[0];
            if (sub == "spawn")
            {
                return Spawn(args);
            }
            if (sub == "stop")
            {
                return Stop(args);
            }
            if (sub == "status")
            {
                return Status(args);
            }
            if (sub == "call")
            {
                return Call(args);
            }
            PrintUsage();
            return 1;
        }

        /// <summary>
        /// spawn 子命令——派生工作进程
        /// </summary>
        /// <param name="args">参数</param>
        /// <returns>退出码</returns>
        private static int Spawn(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("用法: mau serve spawn <项目根>");
                return 1;
            }
            try
            {
                string pipe = MauServeLauncher.Spawn(args[1], null, 10000);
                Console.WriteLine("就绪: " + pipe);
                Console.WriteLine("项目: " + System.IO.Path.GetFullPath(args[1]));
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("spawn 失败: " + ex.Message);
                return 1;
            }
        }

        /// <summary>
        /// stop 子命令——停止工作进程
        /// </summary>
        /// <param name="args">参数</param>
        /// <returns>退出码</returns>
        private static int Stop(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("用法: mau serve stop <项目根>");
                return 1;
            }
            bool sent = MauServeLauncher.Stop(args[1]);
            if (sent)
            {
                Console.WriteLine("停止指令已发送。");
                return 0;
            }
            Console.WriteLine("服务不在线——无需停止。");
            return 0;
        }

        /// <summary>
        /// status 子命令——查询服务状态
        /// </summary>
        /// <param name="args">参数</param>
        /// <returns>退出码</returns>
        private static int Status(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("用法: mau serve status <项目根>");
                return 1;
            }
            bool running = MauServeLauncher.IsRunning(args[1]);
            string pipe = MauServeLauncher.PipeNameFor(args[1]);
            if (running)
            {
                Console.WriteLine("在线: " + pipe);
                return 0;
            }
            Console.WriteLine("离线: " + pipe);
            return 1;
        }

        /// <summary>
        /// call 子命令——调试用直接发请求
        /// </summary>
        /// <param name="args">参数</param>
        /// <returns>退出码</returns>
        private static int Call(string[] args)
        {
            if (args.Length < 3)
            {
                Console.WriteLine("用法: mau serve call <管道名> <op> [arg1] [arg2]");
                Console.WriteLine("  op: ping / list / read / find_ref / diag / comment_check / patch / compile");
                return 1;
            }
            string pipe = args[1];
            ServeRequest request = new ServeRequest();
            request.Op = args[2];
            if (args.Length > 3)
            {
                request.Arg1 = args[3];
            }
            if (args.Length > 4)
            {
                request.Arg2 = args[4];
            }
            try
            {
                ServeResponse response = MauServeClient.Request(pipe, request, 10000);
                if (!response.Ok)
                {
                    Console.Error.WriteLine("失败: " + response.Error);
                    return 1;
                }
                if (response.Text.Length > 0)
                {
                    Console.WriteLine(response.Text);
                }
                for (int i = 0; i < response.Lines.Length; i = i + 1)
                {
                    Console.WriteLine(response.Lines[i]);
                }
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("调用失败: " + ex.Message);
                return 1;
            }
        }

        /// <summary>
        /// 输出用法
        /// </summary>
        private static void PrintUsage()
        {
            Console.WriteLine("用法:");
            Console.WriteLine("  mau serve spawn <项目根>      —— 派生 Roslyn 工作进程");
            Console.WriteLine("  mau serve stop <项目根>       —— 停止工作进程");
            Console.WriteLine("  mau serve status <项目根>     —— 查询服务状态");
            Console.WriteLine("  mau serve call <管道> <op>    —— 调试用直接发请求");
            Console.WriteLine("  mau serve <file.mau>          —— 持久 HTTP 观察面板");
        }
    }
}

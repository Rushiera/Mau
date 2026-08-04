using System;
using Mau.Serve;

namespace Mau.Cli
{
    /// <summary>
    /// serve-work 命令——Roslyn 工作进程入口（由 launcher 派生，常驻监听 NamedPipe）
    /// </summary>
    public static class CommandServeWork
    {
        /// <summary>
        /// 执行工作进程
        /// </summary>
        /// <param name="args">命令行参数——不含 "serve-work" 本身：项目根 管道名 口袋输出根</param>
        /// <returns>退出码</returns>
        public static int Execute(string[] args)
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine("用法: mau serve-work <项目根> <管道名> <口袋输出根>");
                return 1;
            }
            string projectRoot = args[0];
            string pipeName = args[1];
            string pocketRoot = args[2];
            try
            {
                MauServeWorker worker = new MauServeWorker(pipeName, projectRoot, pocketRoot);
                Console.WriteLine("🐱 Roslyn 工作进程就绪: " + pipeName);
                worker.Run();
                Console.WriteLine("工作进程已停止。");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("工作进程启动失败: " + ex.Message);
                return 1;
            }
        }
    }
}

using System;
using System.Diagnostics;
using Mau.Runtime;

namespace Mau.Cli
{
    /// <summary>
    /// mau ps / mau kill 命令——Mau 程序进程管理（Supervisor 命令面）
    /// 索引来源：AppRegistry（%LOCALAPPDATA%/Mau/apps/*.json）
    /// </summary>
    public static class CommandSupervisor
    {
        /// <summary>
        /// 执行 ps——列出全部注册程序 + 存活检测（僵尸标记）
        /// </summary>
        /// <returns>退出码</returns>
        public static int Ps()
        {
            AppIdentity[] apps = AppRegistry.List();
            if (apps.Length == 0)
            {
                Console.WriteLine("无注册的 Mau 程序（%LOCALAPPDATA%/Mau/apps/ 为空）");
                return 0;
            }
            Console.WriteLine("名称".PadRight(16) + "版本".PadRight(10) + "PID".PadRight(8) + "状态".PadRight(12) + "模块".PadRight(6) + "启动时间");
            Console.WriteLine(new string('-', 70));
            for (int i = 0; i < apps.Length; i++)
            {
                AppIdentity app = apps[i];
                bool alive = AppRegistry.IsAlive(app);
                string state = alive ? (app.State == "stopping" ? "stopping" : "running") : "zombie";
                Console.WriteLine(
                    app.Name.PadRight(16)
                    + app.Version.PadRight(10)
                    + app.Pid.ToString().PadRight(8)
                    + state.PadRight(12)
                    + app.Modules.ToString().PadRight(6)
                    + app.StartedAt);
            }
            return 0;
        }

        /// <summary>
        /// 执行 status——注册信息 + 存活状态 + 运行快照（若管道可用）
        /// </summary>
        /// <param name="name">程序名</param>
        /// <returns>退出码</returns>
        public static int Status(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                Console.WriteLine("用法: mau status <程序名>");
                return 1;
            }
            AppIdentity? app = AppRegistry.Get(name);
            if (app == null)
            {
                Console.WriteLine("未注册的程序: " + name + "（mau ps 查看列表）");
                return 1;
            }
            bool alive = AppRegistry.IsAlive(app);
            Console.WriteLine("程序:   " + app.Name);
            Console.WriteLine("版本:   " + app.Version);
            Console.WriteLine("PID:    " + app.Pid + (alive ? "（存活）" : "（已死——zombie）"));
            Console.WriteLine("入口:   " + app.Exe);
            Console.WriteLine("启动:   " + app.StartedAt);
            Console.WriteLine("模块:   " + app.Modules);
            Console.WriteLine("管道:   " + (app.PipeName.Length > 0 ? app.PipeName : "未开启"));
            if (!alive)
            {
                Console.WriteLine("快照:   进程不存在——无法拉取（mau kill --clean 清理）");
                return 0;
            }
            string? snapshot = PipeRequest(app, "{\"cmd\":\"snapshot\"}");
            if (snapshot == null)
            {
                Console.WriteLine("快照:   管道不可用（程序未开启快照服务）");
            }
            else
            {
                Console.WriteLine("快照:   " + snapshot);
            }
            return 0;
        }

        /// <summary>
        /// 执行 snapshot——远程拉取运行态快照（NamedPipe 协议）
        /// </summary>
        /// <param name="name">程序名</param>
        /// <returns>退出码</returns>
        public static int Snapshot(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                Console.WriteLine("用法: mau snapshot <程序名>");
                return 1;
            }
            AppIdentity? app = AppRegistry.Get(name);
            if (app == null)
            {
                Console.WriteLine("未注册的程序: " + name + "（mau ps 查看列表）");
                return 1;
            }
            if (!AppRegistry.IsAlive(app))
            {
                Console.WriteLine("进程不存在（zombie）: " + name + " — mau kill --clean 清理");
                return 1;
            }
            if (app.PipeName.Length == 0)
            {
                Console.WriteLine("程序未开启快照管道: " + name);
                return 1;
            }
            string? snapshot = PipeRequest(app, "{\"cmd\":\"snapshot\"}");
            if (snapshot == null)
            {
                Console.WriteLine("快照拉取失败——管道不可用: " + app.PipeName);
                return 1;
            }
            Console.WriteLine(snapshot);
            return 0;
        }

        /// <summary>
        /// 执行 kill——按名终止程序：管道优雅请求 → 等 3 秒 → 超时强杀 → 清理注册文件
        /// </summary>
        /// <param name="name">程序名</param>
        /// <param name="clean">true=清理全部僵尸注册（不杀进程）</param>
        /// <returns>退出码</returns>
        public static int Kill(string? name, bool clean)
        {
            if (clean)
            {
                return CleanZombies();
            }
            if (string.IsNullOrEmpty(name))
            {
                Console.WriteLine("用法: mau kill <程序名> | mau kill --clean");
                return 1;
            }
            AppIdentity? app = AppRegistry.Get(name);
            if (app == null)
            {
                Console.WriteLine("未注册的程序: " + name + "（mau ps 查看列表）");
                return 1;
            }
            bool alive = AppRegistry.IsAlive(app);
            if (!alive)
            {
                Console.WriteLine("进程已不存在（僵尸注册）——清理注册文件: " + name);
                AppRegistry.Unregister(name);
                return 0;
            }
            // [1] 优雅终止——管道 kill 请求（程序自退）
            if (app.PipeName.Length > 0)
            {
                string? response = PipeRequest(app, "{\"cmd\":\"kill\"}");
                if (response != null)
                {
                    Console.WriteLine("已请求优雅终止: " + name + "（等 3 秒确认）");
                    System.Threading.Thread.Sleep(3000);
                    if (!AppRegistry.IsAlive(app))
                    {
                        Console.WriteLine("已优雅退出: " + name + "（PID " + app.Pid + "）");
                        AppRegistry.Unregister(name);
                        return 0;
                    }
                    Console.WriteLine("优雅终止超时——强杀: " + name);
                }
            }
            // [2] 强杀兜底
            try
            {
                using (Process? process = Process.GetProcessById(app.Pid))
                {
                    process.Kill();
                    process.WaitForExit(5000);
                }
                Console.WriteLine("已终止: " + name + "（PID " + app.Pid + "）");
            }
            catch (Exception ex)
            {
                Console.WriteLine("终止失败: " + name + " — " + ex.Message);
                return 1;
            }
            AppRegistry.Unregister(name);
            return 0;
        }

        /// <summary>
        /// NamedPipe 客户端请求——发送一行 JSON，等待一行响应
        /// </summary>
        /// <param name="app">目标程序身份</param>
        /// <param name="cmdJson">请求 JSON</param>
        /// <returns>响应 JSON 行，失败返回 null</returns>
        private static string? PipeRequest(AppIdentity app, string cmdJson)
{
            if (app == null || app.PipeName.Length == 0)
            {
                return null;
            }
            try
            {
                // 统一客户端——PipeClient（审查修复轮 2026-08-11 决策3；supervisor 快照协议——请求即 cmdJson 行）
                return Mau.Runtime.PipeClient.Request(app.PipeName, cmdJson, 2000);
            }
            catch (Exception)
            {
                return null;
            }
        }
        /// <summary>
        /// 清理僵尸注册——PID 不存在的注册文件全部删除
        /// </summary>
        /// <returns>退出码</returns>
        private static int CleanZombies()
        {
            AppIdentity[] apps = AppRegistry.List();
            int cleaned = 0;
            for (int i = 0; i < apps.Length; i++)
            {
                if (!AppRegistry.IsAlive(apps[i]))
                {
                    Console.WriteLine("清理僵尸: " + apps[i].Name + "（PID " + apps[i].Pid + " 已不存在）");
                    AppRegistry.Unregister(apps[i].Name);
                    cleaned = cleaned + 1;
                }
            }
            Console.WriteLine("完成: 清理 " + cleaned + " 个僵尸注册");
            return 0;
        }
    }
}

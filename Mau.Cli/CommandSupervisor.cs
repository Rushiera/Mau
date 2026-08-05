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
        /// 执行 kill——按名终止程序：进程强杀（管道优雅终止后续版本）+ 清理注册文件
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
            // 强杀（优雅终止请求 = 管道协议，SnapshotServer 接入后启用）
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

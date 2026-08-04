using System;
using System.IO;
using Mau.Serve;
using Xunit;

namespace Mau.Serve.Tests
{
    /// <summary>
    /// 进程级服务测试——launcher 派生真工作进程 → 请求 → 停止
    /// </summary>
    public class ServeProcessTests : IDisposable
    {
        /// <summary>
        /// 临时项目根
        /// </summary>
        private readonly string _root;

        /// <summary>
        /// Mau.exe 路径
        /// </summary>
        private readonly string _mauExe;

        /// <summary>
        /// 创建临时项目根并定位 Mau.exe
        /// </summary>
        public ServeProcessTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "mau_process_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            File.WriteAllText(Path.Combine(_root, "Sample.cs"),
                "namespace ProcessSample { public class Sample { } }");
            _mauExe = FindMauExe();
        }

        /// <summary>
        /// 停止服务并清理
        /// </summary>
        public void Dispose()
        {
            MauServeLauncher.Stop(_root);
            try
            {
                if (Directory.Exists(_root))
                {
                    Directory.Delete(_root, true);
                }
            }
            catch
            {
                // 忽略
            }
        }

        /// <summary>
        /// spawn→list→stop 进程级闭环
        /// </summary>
        [Fact]
        public void Spawn_Call_Stop_FullCycle()
        {
            if (!File.Exists(_mauExe))
            {
                Assert.Fail("Mau.exe 不存在——请先构建 Mau.Cli: " + _mauExe);
                return;
            }

            // [1] spawn——派生工作进程
            string pipe = MauServeLauncher.Spawn(_root, _mauExe, 15000);
            Assert.True(MauServeClient.Ping(pipe, 2000));

            // [2] 请求——list 命中样例
            ServeRequest request = new ServeRequest();
            request.Op = "list";
            ServeResponse response = MauServeClient.Request(pipe, request, 10000);
            Assert.True(response.Ok);
            Assert.Contains(response.Lines, l => l.Contains("Sample.cs | type | Sample"));

            // [3] 复用——再次 spawn 返回同一管道
            string pipeAgain = MauServeLauncher.Spawn(_root, _mauExe, 5000);
            Assert.Equal(pipe, pipeAgain);

            // [4] stop——进程优雅退出
            Assert.True(MauServeClient.Stop(pipe, 2000));
            bool gone = false;
            for (int i = 0; i < 30; i = i + 1)
            {
                if (!MauServeClient.Ping(pipe, 300))
                {
                    gone = true;
                    break;
                }
                System.Threading.Thread.Sleep(200);
            }
            Assert.True(gone, "工作进程未退出。");
        }

        /// <summary>
        /// 定位 Mau.exe——从测试程序集目录向上找 Mau.sln
        /// </summary>
        /// <returns>Mau.exe 绝对路径</returns>
        private static string FindMauExe()
        {
            DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string sln = Path.Combine(dir.FullName, "Mau.sln");
                if (File.Exists(sln))
                {
                    string exe = Path.Combine(dir.FullName, "Mau.Cli", "bin", "Debug", "net8.0", "Mau.exe");
                    if (File.Exists(exe))
                    {
                        return exe;
                    }
                    exe = Path.Combine(dir.FullName, "Mau.Cli", "bin", "Release", "net8.0", "Mau.exe");
                    if (File.Exists(exe))
                    {
                        return exe;
                    }
                    return exe;
                }
                DirectoryInfo? parent = Directory.GetParent(dir.FullName);
                if (parent == null)
                {
                    break;
                }
                dir = parent;
            }
            return "";
        }
    }
}

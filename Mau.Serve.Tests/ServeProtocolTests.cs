using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Mau.Serve;
using Xunit;

namespace Mau.Serve.Tests
{
    /// <summary>
    /// 服务协议测试——同进程 Worker + Client 闭环（不派生子进程）
    /// </summary>
    public class ServeProtocolTests : IDisposable
    {
        /// <summary>
        /// 临时项目根
        /// </summary>
        private readonly string _root;

        /// <summary>
        /// 管道名
        /// </summary>
        private readonly string _pipe;

        /// <summary>
        /// 工作进程任务
        /// </summary>
        private readonly Task _workerTask;

        /// <summary>
        /// 创建临时项目根 + 样例源码 + 启动 Worker
        /// </summary>
        public ServeProtocolTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "mau_serve_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            File.WriteAllText(Path.Combine(_root, "Sample.cs"), SampleSource);
            _pipe = "mau-test-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string pocketRoot = Path.Combine(Path.GetTempPath(), "mau_serve_pocket_" + Guid.NewGuid().ToString("N"));
            MauServeWorker worker = new MauServeWorker(_pipe, _root, pocketRoot);
            _workerTask = Task.Run(() => worker.Run());
            // 等待管道就绪
            bool ready = false;
            for (int i = 0; i < 50; i = i + 1)
            {
                if (MauServeClient.Ping(_pipe, 500))
                {
                    ready = true;
                    break;
                }
                Thread.Sleep(100);
            }
            Assert.True(ready, "工作进程未就绪。");
        }

        /// <summary>
        /// 停止 Worker 并清理
        /// </summary>
        public void Dispose()
        {
            MauServeClient.Stop(_pipe, 1000);
            try
            {
                _workerTask.Wait(3000);
            }
            catch
            {
                // 忽略
            }
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
        /// 样例源码
        /// </summary>
        private const string SampleSource = @"namespace ServeSample
{
    /// <summary>
    /// 样例类型
    /// </summary>
    public class Sample
    {
        /// <summary>
        /// 获取值
        /// </summary>
        /// <returns>固定值</returns>
        public int GetValue()
        {
            return 42;
        }
    }
}
";

        /// <summary>
        /// ping——在线应答
        /// </summary>
        [Fact]
        public void Ping_ReturnsPong()
        {
            ServeRequest request = new ServeRequest();
            request.Op = "ping";
            ServeResponse response = MauServeClient.Request(_pipe, request, 5000);

            Assert.True(response.Ok);
            Assert.Equal("pong", response.Text);
        }

        /// <summary>
        /// list——类型和成员索引
        /// </summary>
        [Fact]
        public void List_ReturnsIndex()
        {
            ServeRequest request = new ServeRequest();
            request.Op = "list";
            ServeResponse response = MauServeClient.Request(_pipe, request, 5000);

            Assert.True(response.Ok);
            Assert.Contains(response.Lines, l => l.Contains("Sample.cs | type | Sample"));
            Assert.Contains(response.Lines, l => l.Contains("Sample.cs | member | Sample.GetValue"));
        }

        /// <summary>
        /// read——成员源码
        /// </summary>
        [Fact]
        public void Read_ReturnsMemberSource()
        {
            ServeRequest request = new ServeRequest();
            request.Op = "read";
            request.Arg1 = "Sample";
            request.Arg2 = "GetValue";
            ServeResponse response = MauServeClient.Request(_pipe, request, 5000);

            Assert.True(response.Ok);
            Assert.Contains("public int GetValue()", response.Text);
        }

        /// <summary>
        /// find_ref——标识符引用
        /// </summary>
        [Fact]
        public void FindRef_ReturnsReferences()
        {
            ServeRequest request = new ServeRequest();
            request.Op = "find_ref";
            request.Arg1 = "GetValue";
            ServeResponse response = MauServeClient.Request(_pipe, request, 5000);

            Assert.True(response.Ok);
            Assert.NotEmpty(response.Lines);
        }

        /// <summary>
        /// patch——方法体替换落盘
        /// </summary>
        [Fact]
        public void Patch_ReplacesBodyOnDisk()
        {
            ServeRequest request = new ServeRequest();
            request.Op = "patch";
            request.Arg1 = "Sample";
            request.Arg2 = "GetValue";
            request.Body = "{ return 7; }";
            ServeResponse response = MauServeClient.Request(_pipe, request, 5000);

            Assert.True(response.Ok);
            string content = File.ReadAllText(Path.Combine(_root, "Sample.cs"));
            Assert.Contains("return 7;", content);
        }

        /// <summary>
        /// compile——口袋编译生成 DLL
        /// </summary>
        [Fact]
        public void Compile_ProducesDll()
        {
            string source = @"using Mau.Contracts;
namespace ServePocket
{
    /// <summary>
    /// 口袋样例
    /// </summary>
    public static class Sample
    {
        /// <summary>
        /// 导出
        /// </summary>
        /// <returns>文本</returns>
        [MauExport]
        public static string Hello()
        {
            return ""serve-ok"";
        }
    }
}
";
            ServeRequest request = new ServeRequest();
            request.Op = "compile";
            request.Arg1 = "ServeSample";
            request.Body = source;
            ServeResponse response = MauServeClient.Request(_pipe, request, 15000);

            Assert.True(response.Ok, "编译失败: " + response.Error + " " + string.Join(";", response.Lines));
            Assert.True(File.Exists(response.Text));
        }

        /// <summary>
        /// 未知操作——错误响应
        /// </summary>
        [Fact]
        public void UnknownOp_ReturnsError()
        {
            ServeRequest request = new ServeRequest();
            request.Op = "nope";
            ServeResponse response = MauServeClient.Request(_pipe, request, 5000);

            Assert.False(response.Ok);
            Assert.Contains("未知操作", response.Error);
        }
    }
}

using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;

namespace Mau.Runtime
{
    /// <summary>
    /// 快照管道服务——程序侧 Supervisor 接入点（NamedPipe 协议）。
    /// 管道名：mau-supervisor-&lt;程序名&gt;；请求/响应均为一行 JSON。
    /// 请求：{"cmd":"snapshot"} → 响应 {"ok":true,"data":"<快照JSON文本>"}
    /// 请求：{"cmd":"kill"}     → 响应 {"ok":true} + 触发 onKillRequest（优雅退出）
    /// </summary>
    public sealed class SnapshotServer : IDisposable
    {
        /// <summary>
        /// 管道名
        /// </summary>
        private readonly string _pipeName;

        /// <summary>
        /// 快照 JSON 提供者——调用方序列化运行态（如 FlowRunner.GetStatus()）
        /// </summary>
        private readonly Func<string> _snapshotProvider;

        /// <summary>
        /// 优雅终止回调——收到 kill 请求时调用（置退出标志）
        /// </summary>
        private readonly Action _onKillRequest;

        /// <summary>
        /// 监听线程
        /// </summary>
        private Thread? _thread;

        /// <summary>
        /// 运行标志
        /// </summary>
        private volatile bool _running;

        /// <summary>
        /// 构造快照服务
        /// </summary>
        /// <param name="pipeName">管道名（不含前缀——自动加 mau-supervisor-）</param>
        /// <param name="snapshotProvider">快照 JSON 提供者</param>
        /// <param name="onKillRequest">kill 请求回调</param>
        public SnapshotServer(string pipeName, Func<string> snapshotProvider, Action onKillRequest)
        {
            if (string.IsNullOrEmpty(pipeName))
            {
                throw new ArgumentException("管道名不能为空", "pipeName");
            }
            if (snapshotProvider == null)
            {
                throw new ArgumentNullException("snapshotProvider");
            }
            _pipeName = "mau-supervisor-" + pipeName;
            _snapshotProvider = snapshotProvider;
            _onKillRequest = onKillRequest != null ? onKillRequest : delegate { };
        }

        /// <summary>
        /// 完整管道名——供客户端连接
        /// </summary>
        public string FullPipeName
        {
            get { return _pipeName; }
        }

        /// <summary>
        /// 启动监听——后台线程循环 Accept
        /// </summary>
        public void Start()
        {
            if (_running)
            {
                return;
            }
            _running = true;
            _thread = new Thread(new ThreadStart(ListenLoop));
            _thread.IsBackground = true;
            _thread.Start();
        }

        /// <summary>
        /// 停止监听
        /// </summary>
        public void Stop()
        {
            _running = false;
        }

        /// <summary>
        /// 释放
        /// </summary>
        public void Dispose()
        {
            Stop();
        }

        /// <summary>
        /// 监听循环——持续接受客户端连接并处理请求
        /// </summary>
        private void ListenLoop()
        {
            while (_running)
            {
                NamedPipeServerStream? pipe = null;
                try
                {
                    pipe = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte);
                    pipe.WaitForConnection();
                    HandleClient(pipe);
                }
                catch (Exception ex)
                {
                    // 管道竞争/停止中——忽略；服务端异常保留日志（诊断快照管道）
                    Console.Error.WriteLine("[SnapshotServer] 监听异常: " + ex.Message);
                }
                finally
                {
                    if (pipe != null)
                    {
                        try
                        {
                            pipe.Dispose();
                        }
                        catch (Exception)
                        {
                            // 释放失败不影响
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 处理单个客户端连接——读一行请求 → 响应
        /// </summary>
        /// <param name="pipe">已连接管道</param>
        private void HandleClient(NamedPipeServerStream pipe)
        {
            try
            {
                using (StreamReader reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, true))
                using (StreamWriter writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, true))
                {
                    string? line = reader.ReadLine();
                    if (string.IsNullOrEmpty(line))
                    {
                        return;
                    }
                    if (line.Contains("\"snapshot\"", StringComparison.Ordinal))
                    {
                        string snapshot = _snapshotProvider();
                        writer.WriteLine("{\"ok\":true,\"data\":" + snapshot + "}");
                        writer.Flush();
                    }
                    else if (line.Contains("\"kill\"", StringComparison.Ordinal))
                    {
                        writer.WriteLine("{\"ok\":true}");
                        writer.Flush();
                        _onKillRequest();
                    }
                    else
                    {
                        writer.WriteLine("{\"ok\":false,\"error\":\"未知命令\"}");
                        writer.Flush();
                    }
                }
            }
            catch (Exception ex)
            {
                // 客户端异常断开/处理异常——保留日志（诊断快照管道）
                Console.Error.WriteLine("[SnapshotServer] 处理异常: " + ex.Message);
            }
        }
    }
}

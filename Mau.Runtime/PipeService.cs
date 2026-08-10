using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;

namespace Mau.Runtime
{
    /// <summary>
    /// NamedPipe 服务基座——监听循环 + 连接管理 + 一行 JSON 请求/响应协议（基建评审 GAP.3）。
    /// 收敛样板：SnapshotServer（Supervisor 快照/终止）/ Ch4ServeServer（CH4 serve 管道）/ MauServeWorker（Roslyn 查看器）三套合一。
    /// 模式：singleShot=true 每连接一请求（短连接）；false 连接内循环多请求（直到 EOF 或 handler 返回 null）。
    /// handler：请求行 → 响应行；返回 null = 关闭本连接。协议解析/序列化由 handler 负责（不绑定 DTO 形态）。
    /// 线程：监听线程只做 IO + handler 调用；Stop/Dispose 任意线程。
    /// </summary>
    public sealed class PipeService : IDisposable
    {
        /// <summary>
        /// 管道名（完整名——各服务自带前缀）
        /// </summary>
        private readonly string _pipeName;

        /// <summary>
        /// 请求行处理器——请求行 → 响应行；null=关闭连接
        /// </summary>
        private readonly Func<string, string?> _handleLine;

        /// <summary>
        /// 单请求模式——每连接仅处理一请求（短连接；默认 false=连接内循环）
        /// </summary>
        private readonly bool _singleShot;

        /// <summary>
        /// 监听线程
        /// </summary>
        private Thread? _thread;

        /// <summary>
        /// 运行标志
        /// </summary>
        private volatile bool _running;

        /// <summary>
        /// 构造管道服务
        /// </summary>
        /// <param name="pipeName">完整管道名</param>
        /// <param name="handleLine">请求行处理器（null=关闭连接）</param>
        /// <param name="singleShot">true=每连接一请求（短连接）</param>
        public PipeService(string pipeName, Func<string, string?> handleLine, bool singleShot)
        {
            if (string.IsNullOrEmpty(pipeName))
            {
                throw new ArgumentException("管道名不能为空", "pipeName");
            }
            if (handleLine == null)
            {
                throw new ArgumentNullException("handleLine");
            }
            _pipeName = pipeName;
            _handleLine = handleLine;
            _singleShot = singleShot;
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
            // 打断阻塞中的 WaitForConnection——自连一次使监听线程完成当前连接并退出循环（Stop 后新连接立即失败）
            try
            {
                using (NamedPipeClientStream poke = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut))
                {
                    poke.Connect(500);
                }
            }
            catch
            {
                // 监听已退出/连接失败——忽略
            }
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
                    ServeConnection(pipe);
                }
                catch (Exception ex)
                {
                    // 管道竞争/停止中——忽略
                    RuntimeLog.ErrorOut("[PipeService] 监听异常: " + ex.Message);
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
        /// 处理单个客户端连接——读请求行 → handler → 写响应行
        /// singleShot=每连接一请求；多请求模式直到 EOF/handler 返回 null/客户端断开
        /// </summary>
        /// <param name="pipe">已连接管道</param>
        private void ServeConnection(NamedPipeServerStream pipe)
        {
            try
            {
                using (StreamReader reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, true))
                using (StreamWriter writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, true))
                {
                    while (true)
                    {
                        string? line = reader.ReadLine();
                        if (line == null || line.Length == 0)
                        {
                            return;
                        }
                        string? response;
                        try
                        {
                            response = _handleLine(line);
                        }
                        catch (Exception ex)
                        {
                            // handler 异常——关闭连接（客户端读到 EOF；协议层错误由 handler 内自管）
                            RuntimeLog.ErrorOut("[PipeService] handler 异常: " + ex.Message);
                            return;
                        }
                        if (response == null)
                        {
                            return;
                        }
                        try
                        {
                            writer.WriteLine(response);
                            writer.Flush();
                        }
                        catch
                        {
                            // 客户端已断开——结束本连接
                            return;
                        }
                        if (_singleShot)
                        {
                            return;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // 客户端异常断开/处理异常——忽略
                RuntimeLog.ErrorOut("[PipeService] 连接处理异常: " + ex.Message);
            }
        }
    }
}

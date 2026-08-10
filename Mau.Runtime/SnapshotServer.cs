using System;

namespace Mau.Runtime
{
    /// <summary>
    /// 快照管道服务——程序侧 Supervisor 接入点（NamedPipe 协议）。
    /// 管道名：mau-supervisor-&lt;程序名&gt;；请求/响应均为一行 JSON。
    /// 请求：{"cmd":"snapshot"} → 响应 {"ok":true,"data":"<快照JSON文本>"}
    /// 请求：{"cmd":"kill"}     → 响应 {"ok":true} + 触发 onKillRequest（优雅退出）
    /// 基建评审 GAP.3（v0.84）：监听样板下沉 PipeService——本类只保留协议 handler。
    /// </summary>
    public sealed class SnapshotServer : IDisposable
    {
        /// <summary>
        /// 管道服务基座——监听循环 + 连接管理（singleShot 短连接）
        /// </summary>
        private readonly PipeService _service;

        /// <summary>
        /// 快照 JSON 提供者——调用方序列化运行态（如 FlowRunner.GetStatus()）
        /// </summary>
        private readonly Func<string> _snapshotProvider;

        /// <summary>
        /// 优雅终止回调——收到 kill 请求时调用（置退出标志）
        /// </summary>
        private readonly Action _onKillRequest;

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
            _snapshotProvider = snapshotProvider;
            _onKillRequest = onKillRequest != null ? onKillRequest : delegate { };
            _service = new PipeService("mau-supervisor-" + pipeName, HandleLine, true);
        }

        /// <summary>
        /// 完整管道名——供客户端连接
        /// </summary>
        public string FullPipeName
        {
            get { return _service.FullPipeName; }
        }

        /// <summary>
        /// 启动监听——后台线程循环 Accept
        /// </summary>
        public void Start()
        {
            _service.Start();
        }

        /// <summary>
        /// 停止监听
        /// </summary>
        public void Stop()
        {
            _service.Stop();
        }

        /// <summary>
        /// 释放
        /// </summary>
        public void Dispose()
        {
            _service.Dispose();
        }

        /// <summary>
        /// 请求行处理器——snapshot/kill/未知（协议保持原状，仅样板下沉）
        /// </summary>
        /// <param name="line">请求行 JSON</param>
        /// <returns>响应行 JSON</returns>
        private string HandleLine(string line)
        {
            if (line.Contains("\"snapshot\"", StringComparison.Ordinal))
            {
                return "{\"ok\":true,\"data\":" + _snapshotProvider() + "}";
            }
            if (line.Contains("\"kill\"", StringComparison.Ordinal))
            {
                _onKillRequest();
                return "{\"ok\":true}";
            }
            return "{\"ok\":false,\"error\":\"未知命令\"}";
        }
    }
}

using System;
using System.IO;
using System.IO.Pipes;
using System.Text;

namespace Mau.Serve
{
    /// <summary>
    /// 服务客户端——按请求连接管道，发一行 JSON，收一行 JSON。
    /// 无状态协议：连接即请求，请求即断开；Roslyn 状态由管道名对应的常驻进程持有。
    /// </summary>
    public static class MauServeClient
    {
        /// <summary>
        /// 发送请求并接收响应
        /// </summary>
        /// <param name="pipeName">管道名</param>
        /// <param name="request">请求</param>
        /// <param name="timeoutMs">连接超时（毫秒）</param>
        /// <returns>响应</returns>
        public static ServeResponse Request(string pipeName, ServeRequest request, int timeoutMs)
{
            // 统一客户端——PipeClient（审查修复轮 2026-08-11 决策3；协议载荷由 ServeRequest 序列化）
            string line = Mau.Runtime.PipeClient.Request(pipeName, request.ToJsonLine(), timeoutMs);
            return ServeResponse.FromJsonLine(line);
        }
        /// <summary>
        /// 探测管道是否可用
        /// </summary>
        /// <param name="pipeName">管道名</param>
        /// <param name="timeoutMs">连接超时（毫秒）</param>
        /// <returns>是否在线</returns>
        public static bool Ping(string pipeName, int timeoutMs)
        {
            try
            {
                ServeRequest request = new ServeRequest();
                request.Op = "ping";
                ServeResponse response = Request(pipeName, request, timeoutMs);
                return response.Ok;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 请求停止工作进程
        /// </summary>
        /// <param name="pipeName">管道名</param>
        /// <param name="timeoutMs">连接超时（毫秒）</param>
        /// <returns>是否已发出停止指令</returns>
        public static bool Stop(string pipeName, int timeoutMs)
        {
            try
            {
                ServeRequest request = new ServeRequest();
                request.Op = "stop";
                ServeResponse response = Request(pipeName, request, timeoutMs);
                return response.Ok;
            }
            catch
            {
                return false;
            }
        }
    }
}

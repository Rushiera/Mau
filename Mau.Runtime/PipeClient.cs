using System.IO;
using System.IO.Pipes;
using System.Text;

namespace Mau.Runtime
{
    /// <summary>
    /// NamedPipe 客户端统一实现——连接 → 写一行 → 读一行（supervisor 快照/serve 请求共用；审查修复轮 2026-08-11 决策3）。
    /// 收敛历史：CommandSupervisor.PipeRequest（Mau.Cli）与 MauServeClient.Request（Mau.Serve）原两份独立样板——多客户端规划下统一为基座能力。
    /// 协议差异由调用方载荷处理（supervisor 用快照 JSON，serve 用 ServeRequest 序列化行）。
    /// </summary>
    public static class PipeClient
    {
        /// <summary>
        /// 发送一行请求并读取一行响应——leaveOpen 由最外层管道关闭（NamedPipe 流包装铁律 H14）
        /// </summary>
        /// <param name="pipeName">完整管道名</param>
        /// <param name="requestLine">请求行</param>
        /// <param name="timeoutMs">连接超时（毫秒）</param>
        /// <returns>响应行（服务端无响应抛异常）</returns>
        public static string Request(string pipeName, string requestLine, int timeoutMs)
        {
            using (NamedPipeClientStream client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
            {
                client.Connect(timeoutMs);
                // leaveOpen: true——reader/writer 释放时不关闭底层管道，由 client 统一关闭
                using (StreamWriter writer = new StreamWriter(client, new UTF8Encoding(false), 1024, true))
                using (StreamReader reader = new StreamReader(client, Encoding.UTF8, false, 1024, true))
                {
                    writer.AutoFlush = true;
                    writer.WriteLine(requestLine);
                    string? line = reader.ReadLine();
                    if (line == null)
                    {
                        throw new System.InvalidOperationException("服务端未返回响应。");
                    }
                    return line;
                }
            }
        }

        /// <summary>
        /// 探测管道是否可用——连接成功即关闭
        /// </summary>
        /// <param name="pipeName">完整管道名</param>
        /// <param name="timeoutMs">连接超时（毫秒）</param>
        /// <returns>可用为真</returns>
        public static bool Ping(string pipeName, int timeoutMs)
        {
            try
            {
                using (NamedPipeClientStream client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
                {
                    client.Connect(timeoutMs);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}

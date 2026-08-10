using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using Mau.Runtime;
using Xunit;

namespace Mau.Runtime.Tests
{
    /// <summary>
    /// PipeService 基座测试（基建评审 GAP.3——NamedPipe 服务样板收敛）。
    /// 覆盖：singleShot 短连接 / 多请求长连接 / handler null 关闭 / 未知请求 / 断连容错 / Stop 停止。
    /// </summary>
    public sealed class PipeServiceTests
    {
        /// <summary>
        /// 发送一次请求——一行 JSON 请求 → 响应行（short 模式：连接即关）
        /// </summary>
        /// <param name="pipeName">完整管道名</param>
        /// <param name="line">请求行</param>
        /// <returns>响应行；失败返回错误文本</returns>
        private static string Request(string pipeName, string line)
        {
            string result = "（空响应）";
            using (NamedPipeClientStream client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut))
            {
                client.Connect(3000);
                StreamWriter writer = new StreamWriter(client, new UTF8Encoding(false), 1024, true);
                StreamReader reader = new StreamReader(client, Encoding.UTF8, false, 1024, true);
                try
                {
                    writer.WriteLine(line);
                    writer.Flush();
                    string? resp = reader.ReadLine();
                    if (resp != null)
                    {
                        result = resp;
                    }
                }
                catch (IOException)
                {
                    // 服务端关闭连接——读取中断（空响应）
                }
                finally
                {
                    try
                    {
                        writer.Dispose();
                    }
                    catch
                    {
                        // 对端已关闭——释放异常忽略
                    }
                    try
                    {
                        reader.Dispose();
                    }
                    catch
                    {
                        // 忽略
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// singleShot——每连接一请求：ping 往返 + 未知请求错误
        /// </summary>
        [Fact]
        public void SingleShot_PingAndUnknown()
        {
            string pipe = "pipe-test-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            using (PipeService service = new PipeService(pipe, delegate (string line)
            {
                if (line.Contains("ping", StringComparison.Ordinal))
                {
                    return "{\"ok\":true,\"text\":\"pong\"}";
                }
                return "{\"ok\":false,\"error\":\"未知\"}";
            }, true))
            {
                service.Start();
                Assert.Equal("{\"ok\":true,\"text\":\"pong\"}", Request(pipe, "{\"op\":\"ping\"}"));
                Assert.Contains("未知", Request(pipe, "{\"op\":\"nope\"}"));
            }
        }

        /// <summary>
        /// 多请求模式——同一连接内连续多请求（EOF 结束）
        /// </summary>
        [Fact]
        public void MultiShot_SameConnectionMultipleRequests()
        {
            string pipe = "pipe-test-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            int count = 0;
            using (PipeService service = new PipeService(pipe, delegate (string line)
            {
                count = count + 1;
                return "{\"ok\":true,\"text\":\"" + count + "\"}";
            }, false))
            {
                service.Start();
                using (NamedPipeClientStream client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut))
                {
                    client.Connect(3000);
                    using (StreamWriter writer = new StreamWriter(client, new UTF8Encoding(false), 1024, true))
                    using (StreamReader reader = new StreamReader(client, Encoding.UTF8, false, 1024, true))
                    {
                        writer.WriteLine("{\"op\":\"a\"}");
                        writer.Flush();
                        Assert.Contains("\"1\"", reader.ReadLine() ?? "");
                        writer.WriteLine("{\"op\":\"b\"}");
                        writer.Flush();
                        Assert.Contains("\"2\"", reader.ReadLine() ?? "");
                        writer.WriteLine("{\"op\":\"c\"}");
                        writer.Flush();
                        Assert.Contains("\"3\"", reader.ReadLine() ?? "");
                    }
                }
            }
            Assert.Equal(3, count);
        }

        /// <summary>
        /// handler 返回 null——关闭本连接（后续请求断开）
        /// </summary>
        [Fact]
        public void HandlerNull_ClosesConnection()
        {
            string pipe = "pipe-test-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            using (PipeService service = new PipeService(pipe, delegate (string line)
            {
                if (line.Contains("bye", StringComparison.Ordinal))
                {
                    return null;
                }
                return "{\"ok\":true,\"text\":\"ok\"}";
            }, false))
            {
                service.Start();
                // 第一次——正常响应（完整 JSON 行）
                Assert.Equal("{\"ok\":true,\"text\":\"ok\"}", Request(pipe, "{\"op\":\"x\"}"));
                using (NamedPipeClientStream client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut))
                {
                    client.Connect(3000);
                    StreamWriter writer = new StreamWriter(client, new UTF8Encoding(false), 1024, true);
                    StreamReader reader = new StreamReader(client, Encoding.UTF8, false, 1024, true);
                    try
                    {
                        writer.WriteLine("{\"op\":\"bye\"}");
                        writer.Flush();
                        string? resp = reader.ReadLine();
                        Assert.Null(resp);
                    }
                    catch (IOException)
                    {
                        // 服务端关闭连接——EOF 等价
                    }
                    finally
                    {
                        try
                        {
                            writer.Dispose();
                        }
                        catch
                        {
                            // 忽略
                        }
                        try
                        {
                            reader.Dispose();
                        }
                        catch
                        {
                            // 忽略
                        }
                    }
                }
            }
        }

        /// <summary>
        /// handler 异常——连接关闭不中断监听（后续请求仍服务）
        /// </summary>
        [Fact]
        public void HandlerException_DoesNotKillService()
        {
            string pipe = "pipe-test-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            using (PipeService service = new PipeService(pipe, delegate (string line)
            {
                if (line.Contains("boom", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("handler 失败");
                }
                return "{\"ok\":true,\"text\":\"pong\"}";
            }, true))
            {
                service.Start();
                // 异常请求——连接关闭（空响应/EOF）
                Assert.Equal("（空响应）", Request(pipe, "{\"op\":\"boom\"}"));
                // 后续请求正常
                Assert.Equal("{\"ok\":true,\"text\":\"pong\"}", Request(pipe, "{\"op\":\"ping\"}"));
            }
        }

        /// <summary>
        /// Stop 后不再接受新连接（客户端超时/连接失败）
        /// </summary>
        [Fact]
        public void Stop_StopsAccepting()
        {
            string pipe = "pipe-test-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            PipeService service = new PipeService(pipe, delegate (string line)
            {
                return "{\"ok\":true,\"text\":\"pong\"}";
            }, true);
            service.Start();
            Assert.Equal("{\"ok\":true,\"text\":\"pong\"}", Request(pipe, "{\"op\":\"ping\"}"));
            service.Dispose();
            // 停止后连接失败（超时 500ms 抛）
            Assert.ThrowsAny<Exception>(delegate
            {
                using (NamedPipeClientStream client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut))
                {
                    client.Connect(500);
                }
            });
        }
    }
}

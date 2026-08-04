using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using Mau.Development;

namespace Mau.Serve
{
    /// <summary>
    /// Roslyn 查看器工作进程——NamedPipe 常驻，服务 csharpcode 读/查/改 + 口袋编译。
    /// Roslyn 状态单写者：请求串行处理，天然无并发冲突。
    /// </summary>
    public sealed class MauServeWorker
    {
        /// <summary>
        /// 管道名
        /// </summary>
        private readonly string MauServeWorker_PipeName;

        /// <summary>
        /// 源码工作区——读/查/改
        /// </summary>
        private readonly MauRoslynSourceWorkspace MauServeWorker_Workspace;

        /// <summary>
        /// 口袋编译器——源码 → DLL
        /// </summary>
        private readonly MauPocketCompiler MauServeWorker_Compiler;

        /// <summary>
        /// 停止标记
        /// </summary>
        private volatile bool MauServeWorker_Stopped;

        /// <summary>
        /// 绑定工作区并准备监听
        /// </summary>
        /// <param name="pipeName">管道名</param>
        /// <param name="projectRoot">项目源码根</param>
        /// <param name="pocketRoot">口袋编译输出根</param>
        public MauServeWorker(string pipeName, string projectRoot, string pocketRoot)
        {
            if (string.IsNullOrWhiteSpace(pipeName))
            {
                throw new ArgumentException("Pipe name is empty.", "pipeName");
            }
            MauServeWorker_PipeName = pipeName;
            MauServeWorker_Workspace = new MauRoslynSourceWorkspace(projectRoot);
            MauServeWorker_Compiler = new MauPocketCompiler(pocketRoot);
        }

        /// <summary>
        /// 进入监听循环——阻塞直到 stop 请求或管道关闭
        /// </summary>
        public void Run()
        {
            using (NamedPipeServerStream server = new NamedPipeServerStream(
                MauServeWorker_PipeName, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
            {
                while (!MauServeWorker_Stopped)
                {
                    try
                    {
                        server.WaitForConnection();
                    }
                    catch (Exception ex)
                    {
                        if (!MauServeWorker_Stopped)
                        {
                            Console.Error.WriteLine("等待连接失败: " + ex.Message);
                        }
                        break;
                    }
                    try
                    {
                        ServeConnection(server);
                    }
                    catch (Exception ex)
                    {
                        if (!MauServeWorker_Stopped)
                        {
                            Console.Error.WriteLine("连接处理失败: " + ex.GetType().Name + ": " + ex.Message);
                            Console.Error.WriteLine(ex.StackTrace);
                        }
                    }
                    try
                    {
                        server.Disconnect();
                    }
                    catch
                    {
                        // 客户端已断开——忽略
                    }
                }
            }
        }

        /// <summary>
        /// 处理一次连接的请求循环
        /// </summary>
        /// <param name="server">管道服务端</param>
        private void ServeConnection(NamedPipeServerStream server)
        {
            // leaveOpen: true——reader/writer 释放时不关闭底层管道，支持重连
            StreamReader reader = new StreamReader(server, Encoding.UTF8,
                false, 1024, true);
            StreamWriter writer = new StreamWriter(server, new UTF8Encoding(false),
                1024, true);
            try
            {
                writer.AutoFlush = true;
                while (!MauServeWorker_Stopped)
                {
                    string? line = reader.ReadLine();
                    if (line == null)
                    {
                        break;
                    }
                    ServeRequest request;
                    try
                    {
                        request = ServeRequest.FromJsonLine(line);
                    }
                    catch (Exception ex)
                    {
                        WriteError(writer, "请求解析失败: " + ex.Message);
                        continue;
                    }
                    ServeResponse response = Dispatch(request);
                    try
                    {
                        writer.WriteLine(response.ToJsonLine());
                    }
                    catch
                    {
                        // 客户端已断开——结束本连接
                        break;
                    }
                    if (request.Op == "stop")
                    {
                        MauServeWorker_Stopped = true;
                        break;
                    }
                }
            }
            finally
            {
                // 对端断开后 Flush 必抛 broken——吞掉，不干扰重连
                try
                {
                    reader.Dispose();
                }
                catch
                {
                    // 忽略
                }
                try
                {
                    writer.Dispose();
                }
                catch
                {
                    // 忽略
                }
            }
        }

        /// <summary>
        /// 分发请求到工作区或编译器
        /// </summary>
        /// <param name="request">请求</param>
        /// <returns>响应</returns>
        private ServeResponse Dispatch(ServeRequest request)
        {
            ServeResponse response = new ServeResponse();
            try
            {
                if (request.Op == "ping")
                {
                    response.Ok = true;
                    response.Text = "pong";
                    return response;
                }
                if (request.Op == "stop")
                {
                    response.Ok = true;
                    response.Text = "stopping";
                    return response;
                }
                if (request.Op == "list")
                {
                    response.Ok = true;
                    response.Lines = MauServeWorker_Workspace.ListMembers();
                    return response;
                }
                if (request.Op == "read")
                {
                    response.Ok = true;
                    response.Text = MauServeWorker_Workspace.ReadMember(request.Arg1, request.Arg2);
                    return response;
                }
                if (request.Op == "find_ref")
                {
                    response.Ok = true;
                    response.Lines = MauServeWorker_Workspace.FindIdentifierReferences(request.Arg1);
                    return response;
                }
                if (request.Op == "diag")
                {
                    response.Ok = true;
                    response.Lines = MauServeWorker_Workspace.GetSyntaxDiagnostics();
                    return response;
                }
                if (request.Op == "comment_check")
                {
                    response.Ok = true;
                    response.Lines = MauServeWorker_Workspace.CheckXmlComments();
                    return response;
                }
                if (request.Op == "patch")
                {
                    MauServeWorker_Workspace.ReplaceMethodBody(request.Arg1, request.Arg2, request.Body);
                    response.Ok = true;
                    response.Text = "patched";
                    return response;
                }
                if (request.Op == "compile")
                {
                    MauPocketCompileResult result = MauServeWorker_Compiler.Compile(request.Body, request.Arg1);
                    response.Ok = result.Success;
                    response.Text = result.AssemblyPath;
                    response.Lines = result.Diagnostics;
                    return response;
                }
                response.Ok = false;
                response.Error = "未知操作: " + request.Op;
                return response;
            }
            catch (Exception ex)
            {
                response.Ok = false;
                response.Error = ex.Message;
                return response;
            }
        }

        /// <summary>
        /// 写出错误响应
        /// </summary>
        /// <param name="writer">写入器</param>
        /// <param name="message">错误消息</param>
        private void WriteError(StreamWriter writer, string message)
        {
            ServeResponse response = new ServeResponse();
            response.Ok = false;
            response.Error = message;
            writer.WriteLine(response.ToJsonLine());
        }
    }
}

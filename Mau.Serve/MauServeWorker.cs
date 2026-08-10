using System;
using Mau.Development;
using Mau.Runtime;

namespace Mau.Serve
{
    /// <summary>
    /// Roslyn 查看器工作进程——NamedPipe 常驻，服务 csharpcode 读/查/改 + 口袋编译。
    /// Roslyn 状态单写者：请求串行处理，天然无并发冲突。
    /// 基建评审 GAP.3（v0.84）：监听样板下沉 PipeService（多请求连接模式）——本类只保留协议分发。
    /// </summary>
    public sealed class MauServeWorker
    {
        /// <summary>
        /// 管道服务基座——监听循环 + 连接管理（多请求：连接内循环直到 EOF/stop）
        /// </summary>
        private readonly PipeService _service;

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
        /// 停止等待事件——Run 阻塞直到 stop
        /// </summary>
        private readonly System.Threading.ManualResetEventSlim _stopEvent = new System.Threading.ManualResetEventSlim(false);

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
            MauServeWorker_Workspace = new MauRoslynSourceWorkspace(projectRoot);
            MauServeWorker_Compiler = new MauPocketCompiler(pocketRoot);
            _service = new PipeService(pipeName, HandleLine, false);
        }

        /// <summary>
        /// 进入监听——阻塞直到 stop 请求（原 Run 语义保持：调用方 Task 驱动）
        /// </summary>
        public void Run()
        {
            _service.Start();
            while (!MauServeWorker_Stopped)
            {
                System.Threading.Thread.Sleep(50);
            }
            _service.Dispose();
            _stopEvent.Set();
        }

        /// <summary>
        /// 请求行处理器——解析 ServeRequest → 分发 → 响应行；stop 关闭连接
        /// </summary>
        /// <param name="line">请求行 JSON</param>
        /// <returns>响应行 JSON；null=关闭连接</returns>
        private string? HandleLine(string line)
{
            ServeRequest request;
            try
            {
                request = ServeRequest.FromJsonLine(line);
            }
            catch (Exception ex)
            {
                return ErrorResponse("请求解析失败: " + ex.Message);
            }
            ServeResponse response = Dispatch(request);
            if (request.Op == "stop")
            {
                // 写 "stopping" 响应后停止——MauServeClient.Stop 期望读到响应行（协议兼容）
                MauServeWorker_Stopped = true;
                return response.ToJsonLine();
            }
            return response.ToJsonLine();
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
        /// 生成错误响应行
        /// </summary>
        /// <param name="message">错误消息</param>
        /// <returns>响应行 JSON</returns>
        private static string ErrorResponse(string message)
        {
            ServeResponse response = new ServeResponse();
            response.Ok = false;
            response.Error = message;
            return response.ToJsonLine();
        }
    }
}

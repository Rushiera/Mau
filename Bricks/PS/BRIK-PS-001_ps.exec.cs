// ═══════════════════════════════════════════════════
// 积木: ps.exec
// ID:   BRIK-PS-001
// 类别: PS
// 作用: PowerShell 执行——整段命令 EncodedCommand 免转义直达 PS；UTF-8 输出内建；写文件语义拦截；超时进程树杀
// 依赖: 无
// 引用: Mau.Runtime（IPsService/DataBox）
// 原理: DataBox.TryResolve<IPsService> → Exec(argsJson)；argsJson 内解析（语料零 JSON 解析）；
//       拦截语义（写文件/Start-Process/ReadKey）与编码处理在实现侧（CH4.PsService）
// 常用: ps_cat.mau 认领线——'ps.exec'[@args] > @result
// ═══════════════════════════════════════════════════
using System;
using System.Text.Json;
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// PowerShell 执行积木——powershell 工具（PsCat 工具组 Flow 认领线消费）
    /// </summary>
    public static class PsExecBrick
    {
        /// <summary>
        /// 执行 PowerShell 命令——整段命令 EncodedCommand 传递；返回 JSON（exit/stdout/stderr/truncated/timeout）
        /// </summary>
        /// <param name="argsJson">工具参数 JSON（command/cwd/timeout_ms）</param>
        /// <param name="result">结果 JSON 或 ERR| 错误文本</param>
        /// <returns>true=执行成功</returns>
        public static bool Exec(string argsJson, out string result)
        {
            result = "";
            if (argsJson == null || argsJson.Length == 0)
            {
                result = "ERR|PS_BAD_ARGS|缺少参数 command";
                return false;
            }
            try
            {
                IPsService service;
                DataBox.TryResolve<IPsService>(out service);
                if (service == null)
                {
                    result = "ERR|PS_NO_SERVICE|宿主未注入 IPsService";
                    return false;
                }
                result = service.Exec(argsJson);
                return true;
            }
            catch (Exception ex)
            {
                result = "ERR|" + ex.GetType().Name + "|" + ex.Message;
                return false;
            }
        }
    }
}
// #MAU_CHECKSUM:SHA256:11BCEDE5F49070ECA77E1D60C6BE1E50E422C148039FA31FF12793DFFFF94E91

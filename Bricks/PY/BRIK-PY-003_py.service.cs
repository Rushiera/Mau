// ═══════════════════════════════════════════════════
// 积木: py.service
// ID:   BRIK-PY-003
// 类别: PY
// 作用: py 常驻服务控制（start 拉起并等就绪 / stop 优雅退出 + 强杀兜底 / status 查进程态）——LLM 工具 py-service 的语料执行面
// 依赖: 无
// 引用: Mau.Runtime（IPyService / DataBox）
// 原理: DataBox.TryResolve<IPyService> → Service(argsJson)（进程池 / 就绪握手 / 生命周期留服务侧）
// 常用: py_cat.mau 认领线——'py.service'[@pysArgs] > @pysRes
// ═══════════════════════════════════════════════════
using System;
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// py 服务控制积木——py-service 工具（PyCat 工具组 Flow 认领线消费）。
    /// </summary>
    public static class PyServiceBrick
    {
        /// <summary>
        /// 服务控制——交宿主 IPyService（进程池与生命周期归服务层，句柄不在 Flow 层）。
        /// </summary>
        /// <param name="argsJson">工具参数 JSON（name 必填；action 必填——start|stop|status）</param>
        /// <param name="result">结果 JSON 或 ERR| 错误文本</param>
        /// <returns>true=执行完成</returns>
        public static bool Control(string argsJson, out string result)
        {
            result = "";
            if (argsJson == null || argsJson.Length == 0)
            {
                result = "ERR|PY_BAD_ARGS|缺少参数 name";
                return false;
            }
            try
            {
                IPyService service;
                DataBox.TryResolve<IPyService>(out service);
                if (service == null)
                {
                    result = "ERR|PY_NO_SERVICE|宿主未注入 IPyService";
                    return false;
                }
                result = service.Service(argsJson);
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
// #MAU_CHECKSUM:SHA256:CBAB224CDD51AB14F2D25A69A34BC1646A9F68DCFAC9CF8BAAA9F2F98EC233BA

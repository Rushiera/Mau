// ═══════════════════════════════════════════════════
// 积木: py.run
// ID:   BRIK-PY-001
// 类别: PY
// 作用: 执行 py 脚本（短命进程，起-跑-退）——LLM 工具 py-run 的语料执行面
// 依赖: 无
// 引用: Mau.Runtime（IPyService / DataBox）
// 原理: DataBox.TryResolve<IPyService> → Run(argsJson)（受控根校验 / 解释器解析 / 进程与超时留服务侧）
// 常用: py_cat.mau 认领线——'py.run'[@pyrArgs] > @pyrRes
// ═══════════════════════════════════════════════════
using System;
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// py 执行积木——py-run 工具（PyCat 工具组 Flow 认领线消费）。
    /// </summary>
    public static class PyRunBrick
    {
        /// <summary>
        /// 执行 py 脚本——交宿主 IPyService（脚本路径校验 / 解释器解析 / 进程与超时均留服务侧）。
        /// </summary>
        /// <param name="argsJson">工具参数 JSON（script 必填；args / timeout_ms 可选）</param>
        /// <param name="result">结果 JSON 或 ERR| 错误文本</param>
        /// <returns>true=执行完成（结果含 ERR| 文本时亦为完成——失败原因随回执返回）</returns>
        public static bool Run(string argsJson, out string result)
        {
            result = "";
            if (argsJson == null || argsJson.Length == 0)
            {
                result = "ERR|PY_BAD_ARGS|缺少参数 script";
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
                result = service.Run(argsJson);
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
// #MAU_CHECKSUM:SHA256:25131D25A517A5446C8B6DCF953CBB88AA6B2984278F7765517B65425DD9568A

// ═══════════════════════════════════════════════════
// 积木: py.list
// ID:   BRIK-PY-002
// 类别: PY
// 作用: 列出可用 py 脚本——受控根内 *.py 扫描（可发现性）——LLM 工具 py-list 的语料执行面
// 依赖: 无
// 引用: Mau.Runtime（IPyService / DataBox）
// 原理: DataBox.TryResolve<IPyService> → List(argsJson)（扫描面 / 依赖目录跳过留服务侧）
// 常用: py_cat.mau 认领线——'py.list'[@pylArgs] > @pylRes
// ═══════════════════════════════════════════════════
using System;
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// py 清单积木——py-list 工具（PyCat 工具组 Flow 认领线消费）。
    /// </summary>
    public static class PyListBrick
    {
        /// <summary>
        /// 列出可用脚本——交宿主 IPyService。
        /// </summary>
        /// <param name="argsJson">工具参数 JSON（dir 可选——受控根内子目录；空 = 仓库 tools 面）</param>
        /// <param name="result">结果 JSON（scripts 数组）或 ERR| 错误文本</param>
        /// <returns>true=执行完成</returns>
        public static bool List(string argsJson, out string result)
        {
            result = "";
            try
            {
                IPyService service;
                DataBox.TryResolve<IPyService>(out service);
                if (service == null)
                {
                    result = "ERR|PY_NO_SERVICE|宿主未注入 IPyService";
                    return false;
                }
                result = service.List(argsJson == null ? "" : argsJson);
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
// #MAU_CHECKSUM:SHA256:5FA702F836D818FBB01856BF3CD5CE2B6737902974CECA8D69FED0179D7419FA

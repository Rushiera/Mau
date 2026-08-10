// ═══════════════════════════════════════════════════
// 积木: dog.collect_result
// ID:   BRIK-DOG-009
// 类别: DOG
// 作用: 一次性读 Dog 回执五键——result/error/call_id/session/tool_name（工具执行结果标准形态）
// 依赖: 无
// 引用: Mau.Runtime
// 原理: 经 FlowRunner 找到 IDog 实例 → GetResultStr + GetPayloadStr（不消费——finish 才回收）
// 常用: ToolPoster 语料——工单关闭后一次读齐回执（M2b：回填 LLM 上下文；tool_name 供 UI 摘要）
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 工单载体积木——dog.collect_result 一次性读回执四键（依赖 FlowRunner）
    /// </summary>
    public static class DogCollectResultBrick
    {
        /// <summary>
        /// 一次性读 Dog 回执六键——result/error/call_id/session/tool_name/args_json（仅 PickingUp 可读，不消费）
        /// </summary>
        /// <param name="dogId">Dog 注册 ID</param>
        /// <param name="result">执行结果文本</param>
        /// <param name="error">错误摘要（无则空）</param>
        /// <param name="callId">工具调用 ID</param>
        /// <param name="session">会话 Key</param>
        /// <param name="toolName">工具名（create_next 载荷——tool.display 分派依据）</param>
        /// <param name="argsJson">工具参数 JSON 原文（create_next 载荷——tool.display 摘要数据源）</param>
        /// <returns>true=Dog 可取回执</returns>
        public static bool CollectResult(long dogId, out string result, out string error,
            out string callId, out string session, out string toolName, out string argsJson)
        {
            result = "";
            error = "";
            callId = "";
            session = "";
            toolName = "";
            argsJson = "";
            FlowRunner? runner;
            DataBox.TryResolve<FlowRunner>(out runner);
            if (runner == null)
            {
                return false;
            }
            IDog? dog = runner.GetFlow(dogId) as IDog;
            if (dog == null)
            {
                return false;
            }
            string value;
            if (dog.GetResultStr("result", out value))
            {
                result = value;
            }
            if (dog.GetResultStr("error", out value))
            {
                error = value;
            }
            if (dog.GetPayloadStr("call_id", out value))
            {
                callId = value;
            }
            if (dog.GetPayloadStr("session", out value))
            {
                session = value;
            }
            if (dog.GetPayloadStr("tool_name", out value))
            {
                toolName = value;
            }
            if (dog.GetPayloadStr("args_json", out value))
            {
                argsJson = value;
            }
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:6A50C9519A9034503AAC93DF62F019109E83ABF9055D61F94B44BCB9C2391730

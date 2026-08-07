// ═══════════════════════════════════════════════════
// 积木: tool.dispatch
// ID:   BRIK-TOOL-010
// 类别: TOOL
// 作用: 批量分发工具调用——解析 tool_calls JSON → 逐个发 OA 单
// 依赖: 无
// 引用: System · System.Collections.Generic · Mau.Runtime
// 原理: 逐 call 解析 → oa.Post（TOOL 单）→ call_id + arguments 展平载荷
// 常用: 工具集批量分发
// ═══════════════════════════════════════════════════
using System.Collections.Generic;
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 工具积木——tool.dispatch 批量发单（依赖 OaBridge/ToolSupport）
    /// </summary>
    public static class ToolDispatchBrick
    {
        /// <summary>
        /// 批量分发工具调用——解析 tool_calls JSON → 逐个发 OA 单（officeType=TOOL，officeName=工具名）
        /// </summary>
        /// <param name="toolCallsJson">OpenAI 兼容 tool_calls JSON 数组</param>
        /// <param name="dogId">发单方 LongId（基座生成 Dog）</param>
        /// <param name="timeoutTicks">超时帧数</param>
        /// <param name="officeIds">发出的 OfficeId 数组（与入参顺序一致）</param>
        /// <returns>true=解析并发单成功</returns>
        public static bool Dispatch(string toolCallsJson, long dogId,
            long timeoutTicks, out long[] officeIds)
        {
            officeIds = new long[0];
            IOA? oa;
            DataBox.TryResolve<IOA>(out oa);
            if (oa == null || string.IsNullOrWhiteSpace(toolCallsJson))
            {
                return false;
            }
            List<long> ids = new List<long>();
            using (System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(toolCallsJson))
            {
                System.Text.Json.JsonElement root = doc.RootElement;
                if (root.ValueKind != System.Text.Json.JsonValueKind.Array)
                {
                    return false;
                }
                foreach (System.Text.Json.JsonElement call in root.EnumerateArray())
                {
                    string callId = ToolPayload.ReadString(call, "id");
                    string name = "";
                    System.Text.Json.JsonElement fn;
                    if (call.TryGetProperty("function", out fn)
                        && fn.ValueKind == System.Text.Json.JsonValueKind.Object)
                    {
                        name = ToolPayload.ReadString(fn, "name");
                    }
                    if (name.Length == 0)
                    {
                        continue;
                    }
                    long officeId = oa.Post(dogId, "TOOL", name, timeoutTicks);
                    oa.SetStr(officeId, dogId, "call_id", callId);
                    System.Text.Json.JsonElement args;
                    if (call.TryGetProperty("function", out fn)
                        && fn.ValueKind == System.Text.Json.JsonValueKind.Object
                        && fn.TryGetProperty("arguments", out args))
                    {
                        System.Text.Json.JsonElement argObj = args;
                        if (args.ValueKind == System.Text.Json.JsonValueKind.String)
                        {
                            string? argsText = args.GetString();
                            if (argsText != null && argsText.Length > 0)
                            {
                                try
                                {
                                    using (System.Text.Json.JsonDocument inner = System.Text.Json.JsonDocument.Parse(argsText))
                                    {
                                        ToolPayload.FlattenArgs(oa, officeId, dogId, inner.RootElement);
                                    }
                                    ids.Add(officeId);
                                    continue;
                                }
                                catch
                                {
                                    oa.SetStr(officeId, dogId, "args.raw", argsText);
                                    ids.Add(officeId);
                                    continue;
                                }
                            }
                        }
                        ToolPayload.FlattenArgs(oa, officeId, dogId, argObj);
                    }
                    ids.Add(officeId);
                }
            }
            officeIds = ids.ToArray();
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:A8AE64314E3BC29D25E173E1674E676CF01CAE17FFEE35270068CA6EE694BADB

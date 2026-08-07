// ═══════════════════════════════════════════════════
// 积木: tool.dispatch_one
// ID:   BRIK-TOOL-009
// 类别: TOOL
// 作用: 按索引分发单个工具调用——toolCallsJson[index] → 发一单
// 依赖: 无
// 引用: Mau.Runtime
// 原理: JSON 数组索引解析 → oa.Post → call_id + arguments 展平载荷
// 常用: 语料展开工具循环（按索引逐单发）
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 工具积木——tool.dispatch_one 按索引发单（依赖 OaBridge/ToolSupport）
    /// </summary>
    public static class ToolDispatchOneBrick
    {
        /// <summary>
        /// 按索引分发单个工具调用——toolCallsJson[index] → 发一单（officeType=TOOL + 工具名 + 载荷）
        /// </summary>
        /// <param name="toolCallsJson">OpenAI 兼容 tool_calls JSON 数组</param>
        /// <param name="index">工具索引（常量绑定）</param>
        /// <param name="dogId">发单方 LongId（基座生成 Dog）</param>
        /// <param name="timeoutTicks">超时帧数</param>
        /// <param name="officeId">发出的 OfficeId（无工具为 0）</param>
        /// <param name="toolName">工具名（无工具为空）</param>
        /// <returns>true=该索引有工具并发单成功</returns>
        public static bool DispatchOne(string toolCallsJson, long index, long dogId,
            long timeoutTicks, out long officeId, out string toolName)
        {
            officeId = 0;
            toolName = "";
            IOA? oa;
            DataBox.TryResolve<IOA>(out oa);
            if (oa == null || string.IsNullOrWhiteSpace(toolCallsJson) || index < 0)
            {
                return false;
            }
            using (System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(toolCallsJson))
            {
                System.Text.Json.JsonElement root = doc.RootElement;
                if (root.ValueKind != System.Text.Json.JsonValueKind.Array
                    || index >= root.GetArrayLength())
                {
                    return false;
                }
                System.Text.Json.JsonElement call = root[(int)index];
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
                    return false;
                }
                officeId = oa.Post(dogId, "TOOL", name, timeoutTicks);
                oa.SetStr(officeId, dogId, "call_id", callId);
                // arguments 展平协议：OpenAI 协议 arguments 为 JSON 字符串——先解析为对象再展平为 args.&lt;名&gt; Key
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
                                    argObj = inner.RootElement;
                                    ToolPayload.FlattenArgs(oa, officeId, dogId, argObj);
                                }
                                toolName = name;
                                return true;
                            }
                            catch
                            {
                                // 参数文本非 JSON——原样存（工具执行方自行处理）
                                oa.SetStr(officeId, dogId, "args.raw", argsText);
                                toolName = name;
                                return true;
                            }
                        }
                    }
                    ToolPayload.FlattenArgs(oa, officeId, dogId, argObj);
                }
                toolName = name;
                return true;
            }
        }
    }
}
// #MAU_CHECKSUM:SHA256:57AE9F8998C5E262D5D4314BDF64F777A2FAC67409C9E06C81F3E13BBAE02294

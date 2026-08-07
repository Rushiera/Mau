// ═══════════════════════════════════════════════════
// 积木: tool.dispatch_next
// ID:   BRIK-TOOL-002
// 类别: TOOL
// 作用: 游标式逐单分发——按 sessionKey 维护游标，每次发下一个未发工具单
// 依赖: 无
// 引用: Mau.Runtime
// 原理: sessionKey 游标 + oa.Post → call_id/args.*/session 载荷；越界游标重置
// 常用: TalkCat 工具循环逐单发（CH4 P2.2 核心）
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 工具积木——tool.dispatch_next 游标式逐单发（依赖 OaBridge/ToolSupport）
    /// </summary>
    public static class ToolDispatchNextBrick
    {
        /// <summary>
        /// 游标式逐单分发——按 sessionKey 维护游标，每次发下一个未发工具单
        /// </summary>
        /// <param name="toolCallsJson">OpenAI 兼容 tool_calls JSON 数组</param>
        /// <param name="sessionKey">会话 Key（写入工具单供 executor 回填定位）</param>
        /// <param name="dogId">发单方 LongId（基座生成 Dog）</param>
        /// <param name="timeoutTicks">超时帧数</param>
        /// <param name="officeId">发出的 OfficeId</param>
        /// <param name="toolName">工具名</param>
        /// <returns>true=该轮还有工具并已发单；false=越界（本轮完毕）</returns>
        public static bool DispatchNext(string toolCallsJson, string sessionKey,
            long dogId, long timeoutTicks, out long officeId, out string toolName)
        {
            officeId = 0;
            toolName = "";
            IOA? oa;
            DataBox.TryResolve<IOA>(out oa);
            if (oa == null || string.IsNullOrWhiteSpace(toolCallsJson))
            {
                return false;
            }
            string safeKey = sessionKey == null ? "" : sessionKey;
            long index = 0;
            lock (ToolSupport.Sync)
            {
                if (ToolSupport.Cursors.TryGetValue(safeKey, out index))
                {
                    // 已有游标
                }
                else
                {
                    ToolSupport.Cursors[safeKey] = 0;
                    index = 0;
                }
            }
            using (System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(toolCallsJson))
            {
                System.Text.Json.JsonElement root = doc.RootElement;
                if (root.ValueKind != System.Text.Json.JsonValueKind.Array
                    || index >= root.GetArrayLength())
                {
                    lock (ToolSupport.Sync)
                    {
                        ToolSupport.Cursors.Remove(safeKey);  // 越界——本轮完毕，游标重置
                    }
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
                    lock (ToolSupport.Sync)
                    {
                        ToolSupport.Cursors.Remove(safeKey);
                    }
                    return false;
                }
                officeId = oa.Post(dogId, "TOOL", name, timeoutTicks);
                oa.SetStr(officeId, dogId, "call_id", callId);
                oa.SetStr(officeId, dogId, "session", safeKey);
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
                                lock (ToolSupport.Sync)
                                {
                                    ToolSupport.Cursors[safeKey] = index + 1;
                                }
                                toolName = name;
                                return true;
                            }
                            catch
                            {
                                oa.SetStr(officeId, dogId, "args.raw", argsText);
                                lock (ToolSupport.Sync)
                                {
                                    ToolSupport.Cursors[safeKey] = index + 1;
                                }
                                toolName = name;
                                return true;
                            }
                        }
                    }
                    ToolPayload.FlattenArgs(oa, officeId, dogId, argObj);
                }
                lock (ToolSupport.Sync)
                {
                    ToolSupport.Cursors[safeKey] = index + 1;
                }
                toolName = name;
                return true;
            }
        }
    }
}
// #MAU_CHECKSUM:SHA256:A83173E8F405A24C9DF769097DDF10EFE330A1081E66BBDA5304D3C49228275E

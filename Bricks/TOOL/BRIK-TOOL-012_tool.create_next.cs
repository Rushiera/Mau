// ═══════════════════════════════════════════════════
// 积木: tool.create_next
// ID:   BRIK-TOOL-012
// 类别: TOOL
// 作用: Dog 载体逐单发单——游标式：按 sessionKey 维护游标，每个 tool_call 建 Dog + Post TOOL 单 + 展平载荷
// 依赖: 无
// 引用: Mau.Runtime
// 原理: sessionKey 游标（ToolSupport）+ DogBase（FlowRunner 注册）→ dog.Post("TOOL", toolName) → call_id/session/args.* 载荷
// 常用: ToolPoster 发单 Cat——Dog-OA 工具循环（M2b：Chat 工具调用 → Dog 载体工单）
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 工具积木——tool.create_next Dog 载体逐单发（依赖 FlowRunner/IOA/ToolSupport）
    /// </summary>
    public static class ToolCreateNextBrick
    {
        /// <summary>
        /// Dog 载体逐单发——游标式：每个 tool_call 建 Dog + Post TOOL 单 + 展平载荷（call_id/session/args.*）
        /// </summary>
        /// <param name="toolCallsJson">OpenAI 兼容 tool_calls JSON 数组</param>
        /// <param name="sessionKey">会话 Key（写入工具单供 executor 回填定位）</param>
        /// <param name="timeoutTicks">超时帧数</param>
        /// <param name="dogId">新建 Dog 的 Flow 注册 ID</param>
        /// <param name="toolName">工具名</param>
        /// <returns>true=该轮还有工具并已发单；false=越界（本轮完毕）</returns>
        public static bool CreateNext(string toolCallsJson, string sessionKey,
            long timeoutTicks, out long dogId, out string toolName)
        {
            dogId = 0;
            toolName = "";
            IOA? oa;
            FlowRunner? runner;
            DataBox.TryResolve<IOA>(out oa);
            DataBox.TryResolve<FlowRunner>(out runner);
            if (oa == null || runner == null || string.IsNullOrWhiteSpace(toolCallsJson))
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
                // 建 Dog 载体 + Post TOOL 单
                DogBase dog = new DogBase(oa, "Tool_" + name);
                dogId = runner.RegisterFlow(dog, "Tool_" + name);
                dog.BindId(dogId);
                if (!dog.Post("TOOL", name, timeoutTicks))
                {
                    return false;
                }
                dog.SetStr("call_id", callId);
                dog.SetStr("session", safeKey);
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
                                    FlattenArgsToDog(dog, inner.RootElement);
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
                                dog.SetStr("args.raw", argsText);
                                lock (ToolSupport.Sync)
                                {
                                    ToolSupport.Cursors[safeKey] = index + 1;
                                }
                                toolName = name;
                                return true;
                            }
                        }
                    }
                    FlattenArgsToDog(dog, argObj);
                }
                lock (ToolSupport.Sync)
                {
                    ToolSupport.Cursors[safeKey] = index + 1;
                }
                toolName = name;
                return true;
            }
        }

        /// <summary>
        /// 展平 arguments 对象为 args.&lt;名&gt; Key 序列——写入 Dog 载荷（同步更新 Dog 快照）
        /// </summary>
        /// <param name="dog">Dog 载体</param>
        /// <param name="argObj">arguments JSON 对象</param>
        private static void FlattenArgsToDog(IDog dog, System.Text.Json.JsonElement argObj)
        {
            if (argObj.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                return;
            }
            foreach (System.Text.Json.JsonProperty prop in argObj.EnumerateObject())
            {
                string key = "args." + prop.Name;
                if (prop.Value.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    dog.SetStr(key, prop.Value.GetString() ?? "");
                }
                else
                {
                    dog.SetStr(key, prop.Value.GetRawText());
                }
            }
        }
    }
}
// #MAU_CHECKSUM:SHA256:FC96DFED80BCFF568882EE70539C9287234C4CC8DC6D3EC715CAAC8705580AF0

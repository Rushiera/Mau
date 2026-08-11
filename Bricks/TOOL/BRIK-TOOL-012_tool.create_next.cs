// ═══════════════════════════════════════════════════
// 积木: tool.create_next
// ID:   BRIK-TOOL-012
// 类别: TOOL
// 作用: Dog 载体逐单发单——游标式：按 sessionKey 维护游标，每个 tool_call 建 Dog + Post TOOL 单 + 展平载荷
// 依赖: 无
// 引用: Mau.Runtime
// 原理: sessionKey 游标（ToolSupport）+ DogBase（FlowRunner 注册）→ dog.Post("TOOL", toolName) → call_id/session/args.* 载荷
//       🔴 超时按工具名分档（CH2 移植）：approval.*/Ask→1260（21s）shell.exec→3000（50s）其他→600（10s）——语料传参为 fallback
//       🔴 参数预处理（CH2 CreateToolDogs 移植）：字符串值白名单路径映射——workspace/→Data/Cats/{session}/workspace、CatCatBigParty//CatTemp/→Data 根
// 常用: ToolPoster 发单 Cat——Dog-OA 工具循环（M2b：Chat 工具调用 → Dog 载体工单）
// ═══════════════════════════════════════════════════
using System;
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
        /// <param name="timeoutTicks">默认超时帧数（fallback）——实际按工具名分档：approval 1260/shell 3000/其他 600</param>
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
                // 🔴 工具名归一化——LLM 声明名（下划线版 file_write）转回路由名（点号版 file.write）
                //   DeepSeek 工具名禁点号（实测 400）——cat.tools_json 声明点转下划线；
                //   发单即归一化——后续 claim 匹配/run_generic 路由全用点号名
                name = name.Replace('_', '.');
                // 建 Dog 载体 + Post TOOL 单（超时按工具名分档——CH2 MapToolTimeoutFrames 移植）
                DogBase dog = new DogBase(oa, "Tool_" + name);
                dogId = runner.RegisterFlow(dog, "Tool_" + name);
                dog.BindId(dogId);
                long effectiveTimeout = MapTimeoutTicks(name, timeoutTicks);
                if (!dog.Post("TOOL", name, effectiveTimeout))
                {
                    return false;
                }
                dog.SetStr("call_id", callId);
                dog.SetStr("session", safeKey);
                dog.SetStr("tool_name", name);
                System.Text.Json.JsonElement args;
                if (call.TryGetProperty("function", out fn)
                    && fn.ValueKind == System.Text.Json.JsonValueKind.Object
                    && fn.TryGetProperty("arguments", out args))
                {
                    dog.SetStr("args_json", args.GetRawText());
                    System.Text.Json.JsonElement argObj = args;
                    if (args.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        string? argsText = args.GetString();
                        if (argsText != null && argsText.Length > 0)
                        {
                            try
                            {
                                dog.SetStr("args_json", argsText);
                                using (System.Text.Json.JsonDocument inner = System.Text.Json.JsonDocument.Parse(argsText))
                                {
                                    FlattenArgsToDog(dog, inner.RootElement, safeKey);
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
                    FlattenArgsToDog(dog, argObj, safeKey);
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
        /// 工具超时分档——按工具名映射超时帧数（估 60fps；CH2 MapToolTimeoutFrames 移植，2026-08-10）
        /// </summary>
        /// <param name="toolName">工具名（点号化路由名）</param>
        /// <param name="fallback">语料传入的默认超时（无匹配时使用）</param>
        /// <returns>超时帧数</returns>
        private static long MapTimeoutTicks(string toolName, long fallback)
        {
            // 审批类（approval.*/Ask）——用户需要时间阅读弹窗（20s 弹窗 + 1s 余量）
            if (toolName == "approval.request" || toolName == "approval.resolve"
                || toolName == "approval.reject" || toolName == "approval.pending"
                || toolName == "Ask" || toolName == "human.ask")
            {
                return 1260;
            }
            // shell.exec——需覆盖白名单外确认时间（默认 20s）+ 命令执行时间
            if (toolName == "shell.exec" || toolName == "shell_exec")
            {
                return 3000;
            }
            // 其余工具 10s × 60fps
            return 600;
        }

        /// <summary>
        /// 展平 arguments 对象为 args.&lt;名&gt; Key 序列——写入 Dog 载荷（同步更新 Dog 快照）；
        /// 字符串值经白名单路径映射（workspace/CatCatBigParty/CatTemp → 绝对路径——CH2 CreateToolDogs 预处理移植）
        /// </summary>
        /// <param name="dog">Dog 载体</param>
        /// <param name="argObj">arguments JSON 对象</param>
        /// <param name="sessionKey">会话 Key（workspace/ 映射定位到 Data/Cats/{sessionKey}/workspace）</param>
        private static void FlattenArgsToDog(IDog dog, System.Text.Json.JsonElement argObj, string sessionKey)
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
                    dog.SetStr(key, MapWorkspacePath(prop.Value.GetString() ?? "", sessionKey));
                }
                else
                {
                    dog.SetStr(key, prop.Value.GetRawText());
                }
            }
        }

        /// <summary>
        /// 白名单路径映射——LLM 相对路径引用 → 绝对路径（对齐 FileSystemService roots）
        /// workspace/ → Data/Cats/{sessionKey}/workspace（每猫工作空间）；CatCatBigParty//CatTemp/ → Data 根下
        /// </summary>
        /// <param name="value">参数字符串值</param>
        /// <param name="sessionKey">会话 Key</param>
        /// <returns>映射后的路径（非路径引用原样返回）</returns>
        private static string MapWorkspacePath(string value, string sessionKey)
        {
            if (value == null || value.Length == 0)
            {
                return value;
            }
            string dataRoot = System.IO.Path.Combine(AppContext.BaseDirectory, "Data");
            string catWorkspace = System.IO.Path.Combine(dataRoot, "Cats", sessionKey ?? "default", "workspace");
            if (value.StartsWith("workspace\u002F", StringComparison.Ordinal))
            {
                return System.IO.Path.Combine(catWorkspace, value.Substring(11)).Replace('\\', '/');
            }
            if (value == "workspace")
            {
                return catWorkspace.Replace('\\', '/');
            }
            if (value.StartsWith("CatCatBigParty\u002F", StringComparison.Ordinal)
                || value.StartsWith("CatTemp\u002F", StringComparison.Ordinal))
            {
                return System.IO.Path.Combine(dataRoot, value).Replace('\\', '/');
            }
            if (value == "CatCatBigParty" || value == "CatTemp")
            {
                return System.IO.Path.Combine(dataRoot, value).Replace('\\', '/');
            }
            return value;
        }
    }
}
// #MAU_CHECKSUM:SHA256:ACAC95022231F8F9553EBBAAD25C7EAAAA9FB31361E0F97842EAF7E7DD738B5A

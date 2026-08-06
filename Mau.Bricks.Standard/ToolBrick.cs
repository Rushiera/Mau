// ═══════════════════════════════════════════════
// 积木: tool.exec
// ID:   BRIK-TOOL-001
// 作用: 工具执行机制积木——语料声明工具分发拓扑，宿主注入执行器
// 引用: Mau.Bricks.Standard → Mau.Contracts
// 原理: 静态宿主桥 Configure(Func) 注入执行器；工具名+参数 → 结果文本
// 常用: tool_dispatch.mau 工具分发拓扑——CH4 P2 前置；oa_flow.mau T_Execute 动作
// ═══════════════════════════════════════════════
using System;
using System.Collections.Generic;
using Mau.Contracts;
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 工具执行积木——语料变迁动作的工具调用通道。宿主启动时注入执行器。
    /// 线程约束：worker 友好——执行器内部决定线程模型（同步/异步由调用方积木形态决定）。
    /// dispatch/collect 为 OA 机制积木——ConfigureOA 注入 OA 实例（发单/收单统合）。
    /// </summary>
    public static class ToolBrick
    {
        /// <summary>
        /// 宿主注入的执行器——工具名 + 参数数组 → 结果文本数组
        /// </summary>
        private static Func<string, string[], string[]>? _executor;

        /// <summary>
        /// 宿主注入的 OA 实例——dispatch/collect 发单与统合（OaBrick 同款宿主桥）
        /// </summary>
        private static IOA? _oa;

        /// <summary>
        /// 分发游标表——sessionKey → 下一个待发工具索引（dispatch_next 用）
        /// </summary>
        private static readonly Dictionary<string, long> _cursors =
            new Dictionary<string, long>(StringComparer.Ordinal);

        /// <summary>
        /// 游标表锁——多 Cat 并发安全
        /// </summary>
        private static readonly object Sync = new object();

        /// <summary>
        /// 注入工具执行器——宿主启动时调用一次
        /// </summary>
        /// <param name="executor">工具执行委托</param>
        public static void Configure(Func<string, string[], string[]> executor)
        {
            _executor = executor;
        }

        /// <summary>
        /// 注入 OA 实例——dispatch/collect 使用（宿主启动时调用一次）
        /// </summary>
        /// <param name="oa">OA 工单平台</param>
        public static void ConfigureOA(IOA oa)
        {
            _oa = oa;
        }

        /// <summary>
        /// 执行工具——按工具名分发，返回结果文本
        /// </summary>
        /// <param name="toolName">工具名</param>
        /// <param name="args">参数数组</param>
        /// <param name="resultTexts">结果文本数组</param>
        /// <returns>true=执行成功</returns>
        public static bool Exec(string toolName, string[] args, out string[] resultTexts)
        {
            Func<string, string[], string[]>? executor = _executor;
            if (executor == null)
            {
                resultTexts = new string[0];
                return false;
            }
            resultTexts = executor(toolName, args);
            return true;
        }

        /// <summary>
        /// 批量分发工具调用——解析 tool_calls JSON → 逐个发 OA 单（officeType=TOOL，officeName=工具名）
        /// 载荷展平协议：call_id + arguments 对象展平为 args.&lt;属性名&gt; Key（规避嵌套 JSON 转义）
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
            IOA? oa = _oa;
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
                    string callId = ReadString(call, "id");
                    string name = "";
                    System.Text.Json.JsonElement fn;
                    if (call.TryGetProperty("function", out fn)
                        && fn.ValueKind == System.Text.Json.JsonValueKind.Object)
                    {
                        name = ReadString(fn, "name");
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
                                        FlattenArgs(oa, officeId, dogId, inner.RootElement);
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
                        FlattenArgs(oa, officeId, dogId, argObj);
                    }
                    ids.Add(officeId);
                }
            }
            officeIds = ids.ToArray();
            return true;
        }

        /// <summary>
        /// 统合工具结果——按 officeIds 顺序读回执，拼装 OpenAI 兼容结果 JSON
        /// 输出格式：[{"call_id":"...","content":"..."}, ...]
        /// </summary>
        /// <param name="officeIds">待统合的 OfficeId 数组</param>
        /// <param name="toolCallsJson">统合结果 JSON 数组</param>
        /// <returns>true=统合成功</returns>
        public static bool Collect(long[] officeIds, out string toolCallsJson)
        {
            toolCallsJson = "";
            IOA? oa = _oa;
            if (oa == null || officeIds == null)
            {
                return false;
            }
            using System.IO.MemoryStream stream = new System.IO.MemoryStream();
            using (System.Text.Json.Utf8JsonWriter writer = new System.Text.Json.Utf8JsonWriter(stream))
            {
                writer.WriteStartArray();
                for (int i = 0; i < officeIds.Length; i = i + 1)
                {
                    Office office = oa.GetOffice(officeIds[i]);
                    string callId = "";
                    string? rawId;
                    if (office.Data.Strs.TryGetValue("call_id", out rawId) && rawId != null)
                    {
                        callId = rawId;
                    }
                    string content = "";
                    string? rawContent;
                    if (office.Result.Strs.TryGetValue("content", out rawContent) && rawContent != null)
                    {
                        content = rawContent;
                    }
                    writer.WriteStartObject();
                    writer.WriteString("call_id", callId);
                    writer.WriteString("content", content);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
            }
            toolCallsJson = System.Text.Encoding.UTF8.GetString(stream.ToArray());
            return true;
        }

        /// <summary>
        /// 工具名匹配判断——查 OA 单 OfficeName 是否等于目标名（语料分支用）
        /// 返回 = 判断结果（true=匹配，false=不匹配或不可判）——与 is_end/is_tool 同语义
        /// </summary>
        /// <param name="officeId">Office ID</param>
        /// <param name="target">目标工具名（常量绑定）</param>
        /// <param name="matched">是否匹配</param>
        /// <returns>true=匹配</returns>
        public static bool IsName(long officeId, string target, out bool matched)
        {
            matched = false;
            IOA? oa = _oa;
            if (oa == null || string.IsNullOrWhiteSpace(target))
            {
                return false;
            }
            Office office = oa.GetOffice(officeId);
            matched = office.OfficeName == target;
            return matched;
        }

        /// <summary>
        /// 按索引分发单个工具调用——toolCallsJson[index] → 发一单（officeType=TOOL + 工具名 + 载荷）
        /// 载荷展平协议：call_id + arguments 对象展平为 args.&lt;属性名&gt; Key（规避嵌套 JSON 转义）
        /// 返回 true=该索引有工具（已发单）；false=越界或无工具（语料据此判断无更多工具）
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
            IOA? oa = _oa;
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
                string callId = ReadString(call, "id");
                string name = "";
                System.Text.Json.JsonElement fn;
                if (call.TryGetProperty("function", out fn)
                    && fn.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    name = ReadString(fn, "name");
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
                                    FlattenArgs(oa, officeId, dogId, argObj);
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
                    FlattenArgs(oa, officeId, dogId, argObj);
                }
                toolName = name;
                return true;
            }
        }

        /// <summary>
        /// 游标式逐单分发——按 sessionKey 维护游标，每次发下一个未发工具单
        /// 游标语义：调用返回 true=已发单（游标前进）；false=越界（本轮工具分发完毕，游标重置）
        /// 载荷：call_id + args.* 展平 + session（executor 回填上下文用）
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
            IOA? oa = _oa;
            if (oa == null || string.IsNullOrWhiteSpace(toolCallsJson))
            {
                return false;
            }
            string safeKey = sessionKey == null ? "" : sessionKey;
            long index = 0;
            lock (Sync)
            {
                if (_cursors.TryGetValue(safeKey, out index))
                {
                    // 已有游标
                }
                else
                {
                    _cursors[safeKey] = 0;
                    index = 0;
                }
            }
            using (System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(toolCallsJson))
            {
                System.Text.Json.JsonElement root = doc.RootElement;
                if (root.ValueKind != System.Text.Json.JsonValueKind.Array
                    || index >= root.GetArrayLength())
                {
                    lock (Sync)
                    {
                        _cursors.Remove(safeKey);  // 越界——本轮完毕，游标重置
                    }
                    return false;
                }
                System.Text.Json.JsonElement call = root[(int)index];
                string callId = ReadString(call, "id");
                string name = "";
                System.Text.Json.JsonElement fn;
                if (call.TryGetProperty("function", out fn)
                    && fn.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    name = ReadString(fn, "name");
                }
                if (name.Length == 0)
                {
                    lock (Sync)
                    {
                        _cursors.Remove(safeKey);
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
                                    FlattenArgs(oa, officeId, dogId, inner.RootElement);
                                }
                                lock (Sync)
                                {
                                    _cursors[safeKey] = index + 1;
                                }
                                toolName = name;
                                return true;
                            }
                            catch
                            {
                                oa.SetStr(officeId, dogId, "args.raw", argsText);
                                lock (Sync)
                                {
                                    _cursors[safeKey] = index + 1;
                                }
                                toolName = name;
                                return true;
                            }
                        }
                    }
                    FlattenArgs(oa, officeId, dogId, argObj);
                }
                lock (Sync)
                {
                    _cursors[safeKey] = index + 1;
                }
                toolName = name;
                return true;
            }
        }

        /// <summary>
        /// 游标式认领工具单——从 Open TOOL 单逐个尝试认领（file.read/file.write 候选）
        /// 返回 true=认领成功（officeId/toolName 有效）；false=无可用单（继续轮询）
        /// </summary>
        /// <param name="catId">执行方 LongId</param>
        /// <param name="officeId">认领的 OfficeId</param>
        /// <param name="toolName">工具名</param>
        /// <returns>true=认领成功</returns>
        public static bool ClaimNext(long catId, out long officeId, out string toolName)
        {
            officeId = 0;
            toolName = "";
            IOA? oa = _oa;
            if (oa == null)
            {
                return false;
            }
            Office[] open = oa.ListOpen("TOOL",
                new string[] { "file.read", "file.write" }).ToArray();
            for (int i = 0; i < open.Length; i = i + 1)
            {
                List<Office> claimed = oa.ClaimBatch(catId,
                    new long[] { open[i].OfficeId });
                if (claimed.Count > 0)
                {
                    officeId = claimed[0].OfficeId;
                    toolName = claimed[0].OfficeName;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 单值收集工具结果——读回执 call_id + content（语料逐单回填用，规避数组）
        /// </summary>
        /// <param name="officeId">Office ID</param>
        /// <param name="callId">工具调用 ID</param>
        /// <param name="content">结果内容</param>
        /// <returns>true=收集成功</returns>
        public static bool CollectOne(long officeId, out string callId, out string content)
        {
            callId = "";
            content = "";
            IOA? oa = _oa;
            if (oa == null)
            {
                return false;
            }
            Office office = oa.GetOffice(officeId);
            string? rawCall;
            if (office.Data.Strs.TryGetValue("call_id", out rawCall) && rawCall != null)
            {
                callId = rawCall;
            }
            string? rawContent;
            if (office.Result.Strs.TryGetValue("content", out rawContent) && rawContent != null)
            {
                content = rawContent;
            }
            return true;
        }

        /// <summary>
        /// 展平 arguments 对象为 args.&lt;名&gt; Key 序列
        /// </summary>
        /// <param name="oa">OA 实例</param>
        /// <param name="officeId">Office ID</param>
        /// <param name="dogId">所有者 LongId</param>
        /// <param name="argObj">arguments JSON 对象</param>
        private static void FlattenArgs(IOA oa, long officeId, long dogId,
            System.Text.Json.JsonElement argObj)
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
                    oa.SetStr(officeId, dogId, key, prop.Value.GetString() ?? "");
                }
                else
                {
                    oa.SetStr(officeId, dogId, key, prop.Value.GetRawText());
                }
            }
        }

        /// <summary>
        /// 工具适配器——file.write：读单展平参数（args.path/args.content）→ FileBrick.Write → 写回执 → Complete
        /// </summary>
        /// <param name="officeId">Office ID（已认领）</param>
        /// <param name="catId">执行方 LongId</param>
        /// <param name="result">执行结果文本</param>
        /// <returns>true=执行并完成</returns>
        public static bool RunFileWrite(long officeId, long catId, out string result,
            out string callId, out string session)
        {
            result = "";
            callId = "";
            session = "";
            IOA? oa = _oa;
            if (oa == null)
            {
                return false;
            }
            string path = "";
            string? rawPath;
            if (!oa.GetStr(officeId, "args.path", out rawPath) || rawPath == null)
            {
                return false;
            }
            path = rawPath;
            string content = "";
            string? rawContent;
            if (oa.GetStr(officeId, "args.content", out rawContent) && rawContent != null)
            {
                content = rawContent;
            }
            if (path.Length == 0)
            {
                return false;
            }
            if (!FileBrick.Write(path, content))
            {
                result = "ERR|FILE_WRITE_FAILED";
                return false;
            }
            // 附带输出——call_id/session（executor 结构化回填定位用）
            string? rawCall;
            if (oa.GetStr(officeId, "call_id", out rawCall) && rawCall != null)
            {
                callId = rawCall;
            }
            string? rawSession;
            if (oa.GetStr(officeId, "session", out rawSession) && rawSession != null)
            {
                session = rawSession;
            }
            OfficeData resultData = OfficeData.Empty();
            resultData.Strs["content"] = "写入成功";
            oa.Complete(officeId, catId, resultData);
            result = "写入成功";
            return true;
        }

        /// <summary>
        /// 工具适配器——file.read：读单展平参数（args.path）→ FileBrick.Read → 写回执 content → Complete
        /// </summary>
        /// <param name="officeId">Office ID（已认领）</param>
        /// <param name="catId">执行方 LongId</param>
        /// <param name="result">执行结果文本</param>
        /// <returns>true=执行并完成</returns>
        public static bool RunFileRead(long officeId, long catId, out string result,
            out string callId, out string session)
        {
            result = "";
            callId = "";
            session = "";
            IOA? oa = _oa;
            if (oa == null)
            {
                return false;
            }
            string path = "";
            string? rawPath;
            if (!oa.GetStr(officeId, "args.path", out rawPath) || rawPath == null)
            {
                return false;
            }
            path = rawPath;
            if (path.Length == 0)
            {
                return false;
            }
            string content;
            if (!FileBrick.Read(path, out content))
            {
                result = "ERR|FILE_READ_FAILED";
                return false;
            }
            // 附带输出——call_id/session（executor 结构化回填定位用）
            string? rawCall;
            if (oa.GetStr(officeId, "call_id", out rawCall) && rawCall != null)
            {
                callId = rawCall;
            }
            string? rawSession;
            if (oa.GetStr(officeId, "session", out rawSession) && rawSession != null)
            {
                session = rawSession;
            }
            OfficeData resultData = OfficeData.Empty();
            resultData.Strs["content"] = content;
            oa.Complete(officeId, catId, resultData);
            result = content;
            return true;
        }

        /// <summary>
        /// 读取对象内的可选字符串属性
        /// </summary>
        /// <param name="element">JSON 对象</param>
        /// <param name="name">属性名</param>
        /// <returns>字符串或空串</returns>
        private static string ReadString(System.Text.Json.JsonElement element, string name)
        {
            System.Text.Json.JsonElement value;
            if (element.TryGetProperty(name, out value)
                && value.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                string? text = value.GetString();
                if (text == null)
                {
                    return "";
                }
                return text;
            }
            return "";
        }
    }

    /// <summary>
    /// 工具积木注册——进程启动时调用一次
    /// </summary>
    public static class ToolBrickRegistration
    {
        /// <summary>
        /// 注册全部工具积木
        /// </summary>
        public static void RegisterAll()
        {
            RegisterExec();
            RegisterDispatch();
            RegisterDispatchOne();
            RegisterDispatchNext();
            RegisterClaimNext();
            RegisterCollect();
            RegisterCollectOne();
            RegisterIsName();
            RegisterRunFileRead();
            RegisterRunFileWrite();
        }

        /// <summary>
        /// 注册 tool.dispatch_next——游标式逐单发（sessionKey 游标）
        /// </summary>
        private static void RegisterDispatchNext()
        {
            BrickContract contract = new BrickContract("tool.dispatch_next", "Mau.Bricks.ToolBrick.DispatchNext");
            contract.Inputs.Add(new BrickPort("toolCallsJson", typeof(string), "tool_calls JSON 数组"));
            contract.Inputs.Add(new BrickPort("sessionKey", typeof(string), "会话 Key（写入工具单）"));
            contract.Inputs.Add(new BrickPort("dogId", typeof(long), "发单方 LongId"));
            contract.Inputs.Add(new BrickPort("timeoutTicks", typeof(long), "超时帧数"));
            contract.Outputs.Add(new BrickPort("officeId", typeof(long), "发出的 OfficeId"));
            contract.Outputs.Add(new BrickPort("toolName", typeof(string), "工具名"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }

        /// <summary>
        /// 注册 tool.claim_next——游标式认领工具单
        /// </summary>
        private static void RegisterClaimNext()
        {
            BrickContract contract = new BrickContract("tool.claim_next", "Mau.Bricks.ToolBrick.ClaimNext");
            contract.Inputs.Add(new BrickPort("catId", typeof(long), "执行方 LongId"));
            contract.Outputs.Add(new BrickPort("officeId", typeof(long), "认领的 OfficeId"));
            contract.Outputs.Add(new BrickPort("toolName", typeof(string), "工具名"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }

        /// <summary>
        /// 注册 tool.collect_one——单值收集工具结果
        /// </summary>
        private static void RegisterCollectOne()
        {
            BrickContract contract = new BrickContract("tool.collect_one", "Mau.Bricks.ToolBrick.CollectOne");
            contract.Inputs.Add(new BrickPort("officeId", typeof(long), "Office ID"));
            contract.Outputs.Add(new BrickPort("callId", typeof(string), "工具调用 ID"));
            contract.Outputs.Add(new BrickPort("content", typeof(string), "结果内容"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }

        /// <summary>
        /// 注册 tool.dispatch_one——按索引逐单发
        /// </summary>
        private static void RegisterDispatchOne()
        {
            BrickContract contract = new BrickContract("tool.dispatch_one", "Mau.Bricks.ToolBrick.DispatchOne");
            contract.Inputs.Add(new BrickPort("toolCallsJson", typeof(string), "tool_calls JSON 数组"));
            contract.Inputs.Add(new BrickPort("index", typeof(long), "工具索引（常量绑定）"));
            contract.Inputs.Add(new BrickPort("dogId", typeof(long), "发单方 LongId"));
            contract.Inputs.Add(new BrickPort("timeoutTicks", typeof(long), "超时帧数"));
            contract.Outputs.Add(new BrickPort("officeId", typeof(long), "发出的 OfficeId（无工具为 0）"));
            contract.Outputs.Add(new BrickPort("toolName", typeof(string), "工具名"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }

        /// <summary>
        /// 注册 tool.is_name——工具名匹配判断
        /// </summary>
        private static void RegisterIsName()
        {
            BrickContract contract = new BrickContract("tool.is_name", "Mau.Bricks.ToolBrick.IsName");
            contract.Inputs.Add(new BrickPort("officeId", typeof(long), "Office ID"));
            contract.Inputs.Add(new BrickPort("target", typeof(string), "目标工具名（常量绑定）"));
            contract.Outputs.Add(new BrickPort("matched", typeof(bool), "是否匹配"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }

        /// <summary>
        /// 注册 tool.run_file_write——file.write 工具适配器
        /// </summary>
        private static void RegisterRunFileWrite()
        {
            BrickContract contract = new BrickContract("tool.run_file_write", "Mau.Bricks.ToolBrick.RunFileWrite");
            contract.Inputs.Add(new BrickPort("officeId", typeof(long), "Office ID（已认领）"));
            contract.Inputs.Add(new BrickPort("catId", typeof(long), "执行方 LongId"));
            contract.Outputs.Add(new BrickPort("result", typeof(string), "执行结果文本"));
            contract.Outputs.Add(new BrickPort("callId", typeof(string), "工具调用 ID"));
            contract.Outputs.Add(new BrickPort("session", typeof(string), "会话 Key"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }

        /// <summary>
        /// 注册 tool.run_file_read——file.read 工具适配器
        /// </summary>
        private static void RegisterRunFileRead()
        {
            BrickContract contract = new BrickContract("tool.run_file_read", "Mau.Bricks.ToolBrick.RunFileRead");
            contract.Inputs.Add(new BrickPort("officeId", typeof(long), "Office ID（已认领）"));
            contract.Inputs.Add(new BrickPort("catId", typeof(long), "执行方 LongId"));
            contract.Outputs.Add(new BrickPort("result", typeof(string), "执行结果文本"));
            contract.Outputs.Add(new BrickPort("callId", typeof(string), "工具调用 ID"));
            contract.Outputs.Add(new BrickPort("session", typeof(string), "会话 Key"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }

        /// <summary>
        /// 注册 tool.dispatch——批量解析发单
        /// </summary>
        private static void RegisterDispatch()
        {
            BrickContract contract = new BrickContract("tool.dispatch", "Mau.Bricks.ToolBrick.Dispatch");
            contract.Inputs.Add(new BrickPort("toolCallsJson", typeof(string), "tool_calls JSON 数组"));
            contract.Inputs.Add(new BrickPort("dogId", typeof(long), "发单方 LongId"));
            contract.Inputs.Add(new BrickPort("timeoutTicks", typeof(long), "超时帧数"));
            contract.Outputs.Add(new BrickPort("officeIds", typeof(long[]), "发出的 OfficeId 数组"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }

        /// <summary>
        /// 注册 tool.collect——统合结果拼装
        /// </summary>
        private static void RegisterCollect()
        {
            BrickContract contract = new BrickContract("tool.collect", "Mau.Bricks.ToolBrick.Collect");
            contract.Inputs.Add(new BrickPort("officeIds", typeof(long[]), "待统合的 OfficeId 数组"));
            contract.Outputs.Add(new BrickPort("toolCallsJson", typeof(string), "统合结果 JSON 数组"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }

        /// <summary>
        /// 注册 tool.exec
        /// </summary>
        private static void RegisterExec()
        {
            BrickContract contract = new BrickContract("tool.exec", "Mau.Bricks.ToolBrick.Exec");
            contract.Inputs.Add(new BrickPort("toolName", typeof(string), "工具名"));
            contract.Inputs.Add(new BrickPort("args", typeof(string[]), "参数数组"));
            contract.Outputs.Add(new BrickPort("resultTexts", typeof(string[]), "结果文本数组"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "worker";
            BrickRegistry.Register(contract);
        }
    }
}

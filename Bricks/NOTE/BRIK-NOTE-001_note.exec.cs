// ═══════════════════════════════════════════════════
// 积木: note.exec
// ID:   BRIK-NOTE-001
// 类别: NOTE
// 作用: 内置 Note 工具执行——解析 toolCallsJson 中的 note_set/note_next 调用，执行 NoteStore 并回填 Tool 结果；非 note 调用原样输出
// 依赖: llm.ctx_push_tool
// 包: 无
// 引用: Mau.Runtime · System.Text
// 原理: 单次扫描工具调用数组——name 命中内置表（note_set/note_next）→ arguments JSON 防御式解析
//       （action=set+content → SetPlan（force 覆盖语义）/ 无 action → NextTask——CH2 ExecuteNote 移植）
//       → 结果经 llm.ctx_push_tool 同构回填（Tool 消息：call_id + 点号名 + args + result）
//       → 非内置调用收集为 externalJson（TalkCat 继续走 ToolPoster 外部工具链）
// 常用: TalkCat 语料 T_ExecBuiltin——G.5 Note 面板（内置工具会话内执行，不落 OA 工单）
// ═══════════════════════════════════════════════════
using System.Collections.Generic;
using System.IO;
using System.Text;

using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// Note 积木——note.exec 内置 Note 工具执行（依赖 NoteStore/ContextStore；G.5 Note 面板 2026-08-11 D.3）
    /// </summary>
    public static class NoteExecBrick
    {
        /// <summary>
        /// 内置工具名表——note_set/note_next（cat.tools_json 声明名下划线；兼容点号原名）
        /// </summary>
        private static bool IsBuiltinNote(string name)
        {
            return name == "note_set" || name == "note_next"
                || name == "note.set" || name == "note.next";
        }

        /// <summary>
        /// 执行 toolCallsJson 中的内置 Note 调用并回填结果——非 note 调用原样输出为 externalJson
        /// </summary>
        /// <param name="toolCallsJson">工具调用数组 JSON（DeepSeek 格式）</param>
        /// <param name="sessionKey">会话 Key（兼猫名——NoteStore scope）</param>
        /// <param name="externalJson">非 note 调用数组 JSON（原样保留——继续外部工具链）</param>
        /// <returns>true=成功</returns>
        public static bool Exec(string toolCallsJson, string sessionKey, out string externalJson)
        {
            externalJson = "";
            List<System.Text.Json.JsonElement> external = new List<System.Text.Json.JsonElement>();
            string safeJson = ContextStore.SafeText(toolCallsJson);
            if (safeJson.Length > 0)
            {
                try
                {
                    using (System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(safeJson))
                    {
                        if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array)
                        {
                            foreach (System.Text.Json.JsonElement call in doc.RootElement.EnumerateArray())
                            {
                                string id = "";
                                string name = "";
                                string args = "";
                                System.Text.Json.JsonElement idEl;
                                if (call.TryGetProperty("id", out idEl)
                                    && idEl.ValueKind == System.Text.Json.JsonValueKind.String)
                                {
                                    id = ContextStore.SafeText(idEl.GetString());
                                }
                                System.Text.Json.JsonElement fn;
                                if (call.TryGetProperty("function", out fn)
                                    && fn.ValueKind == System.Text.Json.JsonValueKind.Object)
                                {
                                    System.Text.Json.JsonElement nameEl;
                                    if (fn.TryGetProperty("name", out nameEl)
                                        && nameEl.ValueKind == System.Text.Json.JsonValueKind.String)
                                    {
                                        name = ContextStore.SafeText(nameEl.GetString());
                                    }
                                    System.Text.Json.JsonElement argsEl;
                                    if (fn.TryGetProperty("arguments", out argsEl)
                                        && argsEl.ValueKind == System.Text.Json.JsonValueKind.String)
                                    {
                                        args = ContextStore.SafeText(argsEl.GetString());
                                    }
                                }
                                if (IsBuiltinNote(name))
                                {
                                    ExecuteOne(sessionKey, id, name, args);
                                }
                                else
                                {
                                    external.Add(call.Clone());
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // 解析失败——全部视为外部工具（外部链容错）
                    externalJson = safeJson;
                    return true;
                }
            }
            // [段3] 外部工具原样重组 JSON 数组
            if (external.Count == 0)
            {
                externalJson = "";
                return true;
            }
            using (MemoryStream stream = new MemoryStream())
            {
                using (System.Text.Json.Utf8JsonWriter writer = new System.Text.Json.Utf8JsonWriter(stream))
                {
                    writer.WriteStartArray();
                    for (int i = 0; i < external.Count; i = i + 1)
                    {
                        external[i].WriteTo(writer);
                    }
                    writer.WriteEndArray();
                }
                externalJson = Encoding.UTF8.GetString(stream.ToArray());
            }
            return true;
        }

        /// <summary>
        /// 执行单个 Note 调用并回填 Tool 结果
        /// </summary>
        /// <param name="sessionKey">会话 Key（兼猫名）</param>
        /// <param name="callId">工具调用 ID</param>
        /// <param name="declName">声明名（下划线）</param>
        /// <param name="argsJson">参数 JSON</param>
        private static void ExecuteOne(string sessionKey, string callId, string declName, string argsJson)
        {
            NoteState state = NoteStore.GetOrCreate(sessionKey);
            string result = "";
            string action = "";
            string content = "";
            bool force = false;
            // [段1] 防御式解析参数——action/content/force（CH2 ExecuteNote 同构）
            if (argsJson.Length > 0 && argsJson.StartsWith("{"))
            {
                try
                {
                    using (System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(argsJson))
                    {
                        System.Text.Json.JsonElement root = doc.RootElement;
                        System.Text.Json.JsonElement a;
                        if (root.TryGetProperty("action", out a) && a.ValueKind == System.Text.Json.JsonValueKind.String)
                        {
                            action = ContextStore.SafeText(a.GetString());
                        }
                        System.Text.Json.JsonElement c;
                        if (root.TryGetProperty("content", out c) && c.ValueKind == System.Text.Json.JsonValueKind.String)
                        {
                            content = ContextStore.SafeText(c.GetString());
                        }
                        System.Text.Json.JsonElement f;
                        if (root.TryGetProperty("force", out f) && f.ValueKind == System.Text.Json.JsonValueKind.True)
                        {
                            force = true;
                        }
                    }
                }
                catch
                {
                    // 参数解析失败——按无参推进处理
                }
            }
            // [段2] 执行语义——action=set 且 content 非空 → 写计划；否则推进（CH2：action=set 但空 content → 推进）
            string toolName = declName.Replace('_', '.');
            if (action == "set" && content.Length > 0)
            {
                NoteStore.SetPlan(state, content, force, out result);
            }
            else
            {
                NoteStore.NextTask(state, out result);
            }
            // [段3] 回填 Tool 消息——与 llm.ctx_push_tool 同构（call_id + 点号名 + args + result）
            CtxPushToolBrick.CtxPushTool(sessionKey, callId, toolName, argsJson, result);
        }
    }
}
// #MAU_CHECKSUM:SHA256:920793E3A3FBA37A3EF37FAB77D5728CE58009AC04F7E73AF2F1E5F2D085797A

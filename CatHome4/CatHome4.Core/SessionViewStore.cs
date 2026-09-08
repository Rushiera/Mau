using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mau.Runtime;

namespace CH4
{
    /// <summary>
    /// 会话视图存储——F4 视图持久化（内存整块列表 + 文件落盘 + 从真实前文重建）。
    /// 只存整块（流式中间态/占位卡不落盘）；重建 = 真实前文绝对可用 → 完全重置视图层。
    /// 数据源语义：内存真源（运行时增量构建）；文件 = 落盘面（CloseRound 同步写）。
    /// </summary>
    internal sealed class SessionViewStore
    {
        /// <summary>视图文件路径——sessions/&lt;id&gt;/&lt;id&gt;.view.json</summary>
        private readonly string _path;

        /// <summary>内存视图块——按生成序（真实前文 append 序）</summary>
        private readonly List<ViewBlock> _blocks = new List<ViewBlock>();

        /// <summary>待配对工具——assistant 工具调用登记，tool 结果到达时生成工具卡块</summary>
        private readonly List<PendingTool> _pendingTools = new List<PendingTool>();
/// <summary>
/// 注入报告——session.new 时生成（独立字段：非真实前文派生，Rebuild 不清；Save 落盘）
/// </summary>
private string _injectReport = ""; 
/// <summary>
/// 轮末统计块——roundsum（每轮 CloseRound 生成：Token 消耗 + 四态用时；非真实前文派生，Rebuild 不清；Save 落盘，GetBlocks 按帧合并）
/// </summary>
private readonly List<ViewBlock> _roundSums = new List<ViewBlock>();
/// <summary>
/// 注入报告 JSON——写（HandleSessionNew 生成后调用；空=无注入报告）
/// </summary>
/// <param name = "json">注入报告 JSON（file/status/…）</param>
 public  void  SetInjectReport ( string  json ) { _injectReport  =  json ?? "" ;  } 
/// <summary>
/// 注入报告 JSON——读（前端渲染/历史重建数据源；空串=无注入报告）
/// </summary>
/// <returns>注入报告 JSON</returns>
 public  string  GetInjectReport ( ) { return  _injectReport ;  }

        /// <summary>待配对工具条目</summary>
        private sealed class PendingTool
        {
            /// <summary>工具调用 ID——与 tool 消息配对锚点</summary>
            public string ToolCallId;

            /// <summary>工具名</summary>
            public string Name;

            /// <summary>参数摘要（≤200）</summary>
            public string Arguments;

            /// <summary>并发序号（1-based——tool_calls 数组顺序）</summary>
            public int Index;

            /// <summary>并发总数（同批 tool_calls 数组长度）</summary>
            public int Total;
        }

        /// <summary>视图文件数据——JSON 形态（blocks 按到达序）</summary>
        private sealed class ViewFileData
        {
            /// <summary>协议版本</summary>
            public int Version { get; set; }

            /// <summary>会话 ID</summary>
            public string SessionId { get; set; }

            /// <summary>视图块数组（按到达序——重建后重新生成）</summary>
            public ViewBlock[] Blocks { get; set; }

            /// <summary>注入报告 JSON——会话元数据（非真实前文派生；Rebuild 不清，Save 落盘）</summary>
            public string InjectReport { get; set; }

            /// <summary>轮末统计块数组——roundsum（非真实前文派生；Rebuild 不清，Save 落盘，Load 恢复）</summary>
            public ViewBlock[] RoundSums { get; set; }
        }

        /// <summary>
        /// 建立会话视图存储
        /// </summary>
        /// <param name="path">视图文件路径</param>
        public SessionViewStore(string path)
        {
            _path = path;
        }

        /// <summary>内存视图块——按生成序（history 数据源）</summary>
        public ViewBlock[] GetBlocks()
{
            // 合并面——真实前文块 + roundsum 轮末统计块（按时间戳升序——同一坐标系：消息 CreatedAt / CloseRound 时刻）
            ViewBlock[] merged = new ViewBlock[_blocks.Count + _roundSums.Count];
            int bi = 0;
            int ri = 0;
            int mi = 0;
            while (bi < _blocks.Count || ri < _roundSums.Count)
            {
                if (ri >= _roundSums.Count || (bi < _blocks.Count && _blocks[bi].Timestamp <= _roundSums[ri].Timestamp))
                {
                    merged[mi] = _blocks[bi];
                    bi = bi + 1;
                }
                else
                {
                    merged[mi] = _roundSums[ri];
                    ri = ri + 1;
                }
                mi = mi + 1;
            }
            // 注入报告合成首块——会话元数据（非真实前文派生；前端首块渲染前文加载明细）
            if (_injectReport.Length > 0)
            {
                ViewBlock[] withReport = new ViewBlock[merged.Length + 1];
                ViewBlock report = new ViewBlock();
                report.Timestamp = 0;
                report.Hash = "inject_report";
                report.MsgIndex = -1;
                report.RenderType = "inject_report";
                report.Payload = _injectReport;
                withReport[0] = report;
                for (int i = 0; i < merged.Length; i = i + 1)
                {
                    withReport[i + 1] = merged[i];
                }
                return withReport;
            }
            return merged;
        }
        /// <summary>
        /// 真实前文 append 钩子——用户消息 → user 块
        /// </summary>
        /// <param name="m">真实前文消息</param>
        /// <param name="timestamp">创建时间戳（Unix 毫秒）</param>
        /// <param name="msgIndex">真实前文消息索引（节点定位锚）</param>
        public void OnUserMessage(LlmMessage m, long timestamp, int msgIndex)
        {
            Dictionary<string, object> payload = new Dictionary<string, object>();
            payload["content"] = m.Content ?? "";
            Append(m, "user", payload, timestamp, msgIndex);
        }

        /// <summary>
        /// 真实前文 append 钩子——assistant 纯文本回复 → text 块
        /// </summary>
        /// <param name="m">真实前文消息</param>
        /// <param name="timestamp">创建时间戳（Unix 毫秒）</param>
        /// <param name="msgIndex">真实前文消息索引（节点定位锚）</param>
        public void OnAssistantText(LlmMessage m, long timestamp, int msgIndex)
        {
            Dictionary<string, object> payload = new Dictionary<string, object>();
            payload["content"] = m.Content ?? "";
            Append(m, "text", payload, timestamp, msgIndex);
        }

        /// <summary>
        /// 真实前文 append 钩子——assistant 工具调用声明 → reason 块（有思考时）+ 登记待配对工具
        /// </summary>
        /// <param name="m">真实前文消息</param>
        /// <param name="timestamp">创建时间戳（Unix 毫秒）</param>
        /// <param name="msgIndex">真实前文消息索引（节点定位锚）</param>
        public void OnAssistantToolCalls(LlmMessage m, long timestamp, int msgIndex)
        {
            string reasoning = m.ReasoningContent ?? "";
            if (reasoning.Length > 0)
            {
                Dictionary<string, object> payload = new Dictionary<string, object>();
                payload["content"] = reasoning;
                Append(m, "reason", payload, timestamp, msgIndex);
            }
            RegisterPendingTools(m.ToolCallsJson ?? "");
        }

        /// <summary>
        /// 真实前文 append 钩子——tool 结果 → 配对生成工具卡块（孤立 tool 丢弃——视图容错）
        /// </summary>
        /// <param name="m">真实前文消息</param>
        /// <param name="timestamp">创建时间戳（Unix 毫秒）</param>
        /// <param name="msgIndex">真实前文消息索引（节点定位锚）</param>
        public void OnToolResult(LlmMessage m, long timestamp, int msgIndex)
        {
            string toolCallId = m.ToolCallId ?? "";
            PendingTool target = null;
            for (int i = _pendingTools.Count - 1; i >= 0; i = i - 1)
            {
                if (_pendingTools[i].ToolCallId == toolCallId)
                {
                    target = _pendingTools[i];
                    _pendingTools.RemoveAt(i);
                    break;
                }
            }
            if (target == null)
            {
                return; // 孤立 tool 丢弃
            }
            Dictionary<string, object> payload = new Dictionary<string, object>();
            payload["name"] = target.Name.Length > 0 ? target.Name : (m.ToolName ?? "");
            payload["arguments"] = target.Arguments;
            payload["result"] = TruncateText(m.Content ?? "", 300);
            payload["summary"] = ToolSummaryFormatter.Build(payload["name"] as string ?? "", target.Arguments, payload["result"] as string ?? "");
            payload["toolIndex"] = target.Index;
            payload["toolTotal"] = target.Total;
            Append(m, "toolcard", payload, timestamp, msgIndex);
        }

        /// <summary>
        /// 从真实前文重建视图层——完全重置（真实前文绝对可用；启动恢复/视图文件缺失时调用）。
        /// 块时间戳取消息 CreatedAt——真实时序权威（跨重启稳定；旧消息 CreatedAt=0 按 List 顺序稳定排前——重建是兜底场景，不做兼容维护）。
        /// </summary>
        /// <param name="messages">真实前文消息数组</param>
        public void Rebuild(LlmMessage[] messages)
        {
            _blocks.Clear();
            _pendingTools.Clear();
            for (int i = 0; i < messages.Length; i++)
            {
                LlmMessage m = messages[i];
                if (m.Role == LlmRole.System)
                {
                    continue;
                }
                if (m.Role == LlmRole.User)
                {
                    OnUserMessage(m, m.CreatedAt, i);
                    continue;
                }
                if (m.Role == LlmRole.Assistant)
                {
                    string toolCalls = m.ToolCallsJson ?? "";
                    if (toolCalls.Length > 0)
                    {
                        OnAssistantToolCalls(m, m.CreatedAt, i);
                    }
                    else
                    {
                        OnAssistantText(m, m.CreatedAt, i);
                    }
                    continue;
                }
                if (m.Role == LlmRole.Tool)
                {
                    OnToolResult(m, m.CreatedAt, i);
                }
            }
        }

        /// <summary>
        /// 视图文件落盘——全量覆写（CloseRound 与真实前文同批；流式中间态不含）
        /// </summary>
        public void Save()
{
            try
            {
                string dir = Path.GetDirectoryName(_path);
                if (dir != null && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                ViewFileData data = new ViewFileData();
                data.Version = 1;
                data.SessionId = "";
                data.Blocks = _blocks.ToArray();
                data.InjectReport = _injectReport;
                data.RoundSums = _roundSums.ToArray();
                JsonSerializerOptions options = new JsonSerializerOptions();
                options.IncludeFields = true;
                string json = JsonSerializer.Serialize(data, options);
                File.WriteAllText(_path, json);
            }
            catch (Exception)
            {
                // 保存失败不阻断会话（下次收工再试）——视图是派生态，真实前文可重建
            }
        }
        /// <summary>
        /// 追加轮末统计块——roundsum（CloseRound 生成：Token 消耗 + 工具次数 + 总耗时 + 四态用时；非真实前文派生，Rebuild 不清）。
        /// </summary>
        /// <param name="payloadJson">roundsum 载荷 JSON（{"type":"roundsum","data":{...}}）</param>
        /// <param name="timestamp">创建时间戳（Unix 毫秒——与消息块同坐标系，归并排序键）</param>
        public void AppendRoundSummary(string payloadJson, long timestamp)
        {
            ViewBlock block = new ViewBlock();
            block.Timestamp = timestamp;
            block.Hash = "roundsum_" + _roundSums.Count.ToString();
            block.MsgIndex = -1;
            block.RenderType = "roundsum";
            block.Payload = payloadJson;
            _roundSums.Add(block);
        }

        /// <summary>
        /// 清空视图层——session.new 清前文时同步（真实前文 Clear 后视图随生命周期清理）
        /// </summary>
        public void Clear()
{
            _blocks.Clear();
            _pendingTools.Clear();
            // 注入报告随视图层清理——session.new 后 HandleSessionNew 重新 Set + Save
            _injectReport = "";
            // 轮末统计随视图层清理——新会话不保留旧轮统计
            _roundSums.Clear();
        }

        /// <summary>
        /// 清空轮末统计块——回滚裁剪后调用（roundsum 非真实前文派生，Rebuild 不清——裁剪后残留旧统计）
        /// </summary>
        public void ClearRoundSums()
        {
            _roundSums.Clear();
        }
        /// <summary>
        /// 生成视图块——内容哈希 = 真实前文单块完整字段 SHA256（裁决：前文块哈希作唯一标识）
        /// </summary>
        /// <param name="m">真实前文消息</param>
        /// <param name="renderType">渲染类型</param>
        /// <param name="payload">渲染载荷（字典）</param>
        /// <param name="timestamp">创建时间戳（Unix 毫秒）</param>
        /// <param name="msgIndex">真实前文消息索引（节点定位锚）</param>
        private void Append(LlmMessage m, string renderType, Dictionary<string, object> payload, long timestamp, int msgIndex)
        {
            ViewBlock block = new ViewBlock();
            block.Timestamp = timestamp;
            block.Hash = ComputeHash(m);
            block.MsgIndex = msgIndex;
            block.RenderType = renderType;
            block.Payload = JsonSerializer.Serialize(payload);
            _blocks.Add(block);
        }

        /// <summary>
        /// 登记待配对工具——解析 tool_calls JSON 数组（{id,function:{name,arguments}}；解析失败空登记——容错）
        /// </summary>
        /// <param name="toolCallsJson">tool_calls JSON</param>
        private void RegisterPendingTools(string toolCallsJson)
        {
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(toolCallsJson))
                {
                    JsonElement root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Array)
                    {
                        return;
                    }
                    for (int i = 0; i < root.GetArrayLength(); i++)
                    {
                        JsonElement call = root[i];
                        string id = GetStringProp(call, "id");
                        string name = "";
                        string arguments = "";
                        JsonElement funcEl;
                        if (call.TryGetProperty("function", out funcEl))
                        {
                            name = GetStringProp(funcEl, "name");
                            arguments = GetStringProp(funcEl, "arguments");
                        }
                        PendingTool pt = new PendingTool();
                        pt.ToolCallId = id;
                        pt.Name = name;
                        pt.Arguments = TruncateText(arguments, 200);
                        pt.Index = i + 1;
                        pt.Total = root.GetArrayLength();
                        _pendingTools.Add(pt);
                    }
                }
            }
            catch (Exception)
            {
                // 解析失败——空登记（容错）
            }
        }

        /// <summary>
        /// 读取 JSON 对象字符串属性——防御式（缺字段返回空串）
        /// </summary>
        /// <param name="obj">JSON 对象</param>
        /// <param name="prop">属性名</param>
        /// <returns>属性值</returns>
        private static string GetStringProp(JsonElement obj, string prop)
        {
            JsonElement value;
            if (obj.TryGetProperty(prop, out value) && value.ValueKind == JsonValueKind.String)
            {
                string got = value.GetString();
                if (got != null)
                {
                    return got;
                }
            }
            return "";
        }

        /// <summary>
        /// 文本截断——超长保留头部 + 截断提示（视图层：参数 ≤200 / 结果 ≤300）
        /// </summary>
        /// <param name="text">原文</param>
        /// <param name="max">上限字符数</param>
        /// <returns>截断文本</returns>
        private static string TruncateText(string text, int max)
        {
            if (text == null)
            {
                return "";
            }
            if (text.Length <= max)
            {
                return text;
            }
            return text.Substring(0, max) + "…[截断:原" + text.Length.ToString() + "字符]";
        }

        /// <summary>
        /// 真实前文块内容哈希——单块完整字段 SHA256 十六进制（时空双索引的"空间"维）
        /// </summary>
        /// <param name="m">真实前文消息</param>
        /// <returns>哈希十六进制串</returns>
        private static string ComputeHash(LlmMessage m)
        {
            string raw = m.Role.ToString()
                + "\u0001" + (m.Content ?? "")
                + "\u0001" + (m.ToolCallId ?? "")
                + "\u0001" + (m.ToolName ?? "")
                + "\u0001" + (m.ToolCallsJson ?? "")
                + "\u0001" + (m.ReasoningContent ?? "");
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < bytes.Length; i = i + 1)
                {
                    sb.Append(bytes[i].ToString("x2"));
                }
                return sb.ToString();
            }
        }
/// <summary>
/// 加载注入报告 + 轮末统计——启动恢复时调用（Rebuild 后读回；view.json 缺失/损坏静默空报告）
/// </summary>
public void LoadInjectReport()
{
    try
    {
        if (!System.IO.File.Exists(_path))
        {
            return;
        }

        string json = System.IO.File.ReadAllText(_path);
        JsonSerializerOptions options = new JsonSerializerOptions();
        options.IncludeFields = true;
        ViewFileData data = JsonSerializer.Deserialize<ViewFileData>(json, options);
        if (data != null && data.InjectReport != null)
        {
            _injectReport = data.InjectReport;
        }
        if (data != null && data.RoundSums != null)
        {
            _roundSums.Clear();
            _roundSums.AddRange(data.RoundSums);
        }
    }
    catch (Exception)
    {
    // 加载失败静默——注入报告缺失不阻断（视图可重建）
    }
}    }
}

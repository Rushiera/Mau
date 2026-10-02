using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Mau.Runtime;

namespace CH4
{
    /// <summary>
    /// 完整前文留档条目——一条消息的四可载字段（与 LlmMessage 同构；解析产物，供视图与重建消费）。
    /// </summary>
    internal sealed class FullContextEntry
    {
        /// <summary>角色（system / user / assistant / tool）</summary>
        public string Role = "";

        /// <summary>消息时刻——Unix 毫秒（0=无）</summary>
        public long Time;

        /// <summary>工具调用 ID（tool 角色）</summary>
        public string ToolCallId = "";

        /// <summary>工具名（tool 角色）</summary>
        public string ToolName = "";

        /// <summary>消息正文</summary>
        public string Content = "";

        /// <summary>assistant 工具调用 JSON 数组</summary>
        public string ToolCalls = "";

        /// <summary>assistant 思考内容</summary>
        public string Reasoning = "";

        /// <summary>user 图片附件 JSON 数组</summary>
        public string Images = "";
    }

    /// <summary>
    /// 完整前文存储——一会话一份文本留档，尾部增量采集 + 份数轮转（design-ch4-fullctx）。
    /// 与「前文关键信息」（SessionViewStore）独立并行：各持落点、各持端点、互不读写。
    /// 采集不走 agent——消息入前文即触发（节流 30 秒）；会话定稿与端点读取各自兜底强制采集。
    /// 线程：主线程（消息追加）与 HTTP 线程（端点读取）共用同一把锁串行，文件写互斥。
    /// </summary>
    internal sealed class FullContextStore
    {
        /// <summary>采集最小间隔毫秒——30 秒（常量，不配置）</summary>
        private const int CaptureIntervalMs = 30000;

        /// <summary>增量比对锚点长度——400 字符（自文本尾端向前比对，完全一致即停止）</summary>
        private const int AnchorChars = 400;

        /// <summary>留档份数缺省值——chat.full_ctx_keep 缺失/非法时回落</summary>
        private const int DefaultKeep = 3;

        /// <summary>留档份数上限——与 schema 值域一致</summary>
        private const int MaxKeep = 20;

        /// <summary>条目正文截断上限——与 /api/v1/context 口径一致（前端点击展开全文）</summary>
        private const int ItemContentLimit = 8000;

        /// <summary>条目摘要长度——单行化后取首 N 字符</summary>
        private const int PreviewLimit = 120;

        /// <summary>文件头版本行——格式契约标识（剥离时跳过 # 开头行）</summary>
        private const string HeaderVersion = "# CH4-FULLCTX v1";

        /// <summary>消息段起始标记前缀</summary>
        private const string CtxOpen = "[[CH4-CTX ";

        /// <summary>消息段结束标记</summary>
        private const string CtxClose = "[[/CH4-CTX]]";
        /// <summary>巡检判据长度——前文与留档首段比对字符数（400）</summary>
        private const int HeadChars = 400;

        /// <summary>落点目录——&lt;data&gt;/sessions_ctx</summary>
        private readonly string _dir;

        /// <summary>猫 key——文件名前缀与段归属</summary>
        private readonly string _catKey;

        /// <summary>消息读取器——会话上下文实时真源（采集时全量构建）</summary>
        private readonly Func<LlmMessage[]> _messages;

        /// <summary>留档份数读取器——chat.full_ctx_keep 实时读取（null=缺省）</summary>
        private readonly Func<int> _keep;

        /// <summary>采集与轮转互斥锁——主线程与 HTTP 线程共用</summary>
        private readonly object _gate = new object();

        /// <summary>当前会话文件路径（空=本会话尚未创建留档）</summary>
        private string _path = "";

        /// <summary>会话起时戳——文件名与段头同源（yyyyMMdd_HHmmss）</summary>
        private string _stamp = "";

        /// <summary>会话起时刻文本——文件头 start 字段（yyyy-MM-dd HH:mm:ss）</summary>
        private string _startText = "";

        /// <summary>上次采集时刻——Environment.TickCount64（节流基准）</summary>
        private long _lastCaptureMs;
        /// <summary>留档头部漂移告警标志——每会话只出声一次（头部恢复即复位）</summary>
        private bool _headDriftWarned;

        /// <summary>
        /// 建立完整前文存储
        /// </summary>
        /// <param name="dir">落点目录（&lt;data&gt;/sessions_ctx）</param>
        /// <param name="catKey">猫 key（文件名前缀）</param>
        /// <param name="messages">消息读取器（会话上下文真源）</param>
        /// <param name="keep">留档份数读取器（null=缺省 3）</param>
        public FullContextStore(string dir, string catKey, Func<LlmMessage[]> messages, Func<int> keep)
        {
            _dir = dir == null ? "" : dir;
            _catKey = catKey == null ? "" : catKey;
            _messages = messages;
            _keep = keep;
        }

        /// <summary>
        /// 留档份数读取器——chat.full_ctx_keep 运行时读取（立即生效）；配置面不可用=缺省 3。
        /// 值存在但越界/非数 = 异常（出声告警后回落缺省——不静默）。
        /// </summary>
        /// <returns>份数（1..MaxKeep）</returns>
        public static int ConfigKeep()
        {
            ConfigStore cfg = null;
            if (DataBox.TryResolve<ConfigStore>(out cfg) && cfg != null)
            {
                string raw = cfg.Get("chat.full_ctx_keep", "");
                int parsed;
                if (int.TryParse(raw, out parsed) && parsed >= 1 && parsed <= MaxKeep)
                {
                    return parsed;
                }
                if (raw.Length > 0)
                {
                    LogStore.Add("FullContextStore", 2, "chat.full_ctx_keep 非法值（" + raw + "）——按缺省 "
                        + DefaultKeep.ToString() + " 份", "CHAT");
                }
            }
            return DefaultKeep;
        }

        /// <summary>
        /// 消息入前文触发——节流窗口内跳过（不排队；尾部由下次触发 / 定稿 / 端点读取兑现）。
        /// </summary>
        public void OnMessageAppended()
        {
            Capture(false);
        }

        /// <summary>
        /// 采集一次——全量构建文本后按尾部锚点增量追加。
        /// </summary>
        /// <param name="force">true=忽略节流窗口（定稿 / 端点读取兜底）</param>
        public void Capture(bool force)
        {
            lock (_gate)
            {
                if (!force && Environment.TickCount64 - _lastCaptureMs < CaptureIntervalMs)
                {
                    return;
                }
                CaptureLocked();
            }
        }

        /// <summary>
        /// 会话定稿——强制采集 + 当前份转历史 + 轮转（保留 chat.full_ctx_keep 份，销毁更旧）。
        /// 调用点：session.new（前文清空之前）。
        /// </summary>
        /// <returns>定稿文件绝对路径（空串=本会话无落盘）</returns>
        public string FinalizeSession()
        {
            lock (_gate)
            {
                string done = "";
                try
                {
                    CaptureLocked();
                    done = _path;
                    _path = "";
                    _stamp = "";
                    _startText = "";
                    _lastCaptureMs = 0;
                    int keep = KeepCount();
                    int removed = Rotate(keep);
                    LogStore.Add("CatHome4", 1, "完整前文定稿: " + (done.Length > 0 ? done : "（本会话无落盘）")
                        + " | 保留历史 " + keep.ToString() + " 份，销毁 " + removed.ToString() + " 份", "CHAT");
                }
                catch (Exception ex)
                {
                    // 定稿失败不阻断新会话（失败可见——ERR 日志 + 空返回）
                    LogStore.Add("FullContextStore", 3, "完整前文定稿失败: " + ex.Message, "CHAT");
                }
                return done;
            }
        }

        /// <summary>
        /// 完整前文视图 JSON——对话页弹层「完整前文」数据源（GET /api/v1/fullctx）。
        /// 读取前强制采集一次（展示面不受 30 秒节流滞后影响）；条目形态与 /api/v1/context、/api/v1/keyinfo 同构。
        /// </summary>
        /// <param name="max">返回条目上限（1-500 夹取，缺省 200；超出取尾部 + 恒含首条）</param>
        /// <returns>视图 JSON</returns>
        public string BuildView(int max)
        {
            Dictionary<string, object> resp = new Dictionary<string, object>();
            try
            {
                Capture(true);
                string path;
                string text;
                lock (_gate)
                {
                    path = _path;
                    text = (path.Length > 0 && File.Exists(path)) ? File.ReadAllText(path) : "";
                }
                resp["ok"] = true;
                resp["session"] = _catKey;
                resp["path"] = path;
                if (text.Length == 0)
                {
                    resp["count"] = 0;
                    resp["start"] = 0;
                    resp["shown"] = 0;
                    resp["chars"] = 0L;
                    resp["items"] = new List<object>();
                    return JsonUtil.Serialize(resp);
                }
                List<FullContextEntry> entries;
                string error;
                if (!TryParse(text, out entries, out error))
                {
                    // 结构非法=可见失败（不静默留白）——文件原样保留供排查
                    LogStore.Add("FullContextStore", 3, "完整前文解析失败: " + error + "（" + path + "）", "HTTP");
                    resp["ok"] = false;
                    resp["error"] = "完整前文解析失败: " + error;
                    return JsonUtil.Serialize(resp);
                }
                List<Dictionary<string, object>> all = new List<Dictionary<string, object>>();
                long totalChars = 0;
                for (int i = 0; i < entries.Count; i = i + 1)
                {
                    FullContextEntry e = entries[i];
                    string body = DisplayBody(e);
                    totalChars = totalChars + (long)body.Length;
                    bool truncated = body.Length > ItemContentLimit;
                    Dictionary<string, object> item = new Dictionary<string, object>();
                    item["i"] = i + 1;
                    item["role"] = e.Role;
                    item["tool"] = e.ToolName;
                    item["chars"] = (long)body.Length;
                    item["time"] = e.Time;
                    item["truncated"] = truncated;
                    item["preview"] = Preview(body);
                    item["content"] = truncated ? body.Substring(0, ItemContentLimit) : body;
                    all.Add(item);
                }
                int total = all.Count;
                int start = 0;
                if (max > 0 && total > max)
                {
                    start = total - max;
                }
                List<object> items = new List<object>();
                if (start > 0)
                {
                    // 首条恒在窗口内——前文头部（system 注入块）不因尾部窗口而不可见（2026-10-02 判例）
                    items.Add(all[0]);
                }
                for (int i = start; i < total; i = i + 1)
                {
                    items.Add(all[i]);
                }
                resp["count"] = total;
                resp["start"] = start + 1;
                resp["shown"] = items.Count;
                resp["chars"] = totalChars;
                resp["items"] = items;
            }
            catch (Exception ex)
            {
                LogStore.Add("FullContextStore", 3, "完整前文视图失败: " + ex.Message, "HTTP");
                resp["ok"] = false;
                resp["error"] = "完整前文视图失败: " + ex.Message;
            }
            return JsonUtil.Serialize(resp);
        }

        /// <summary>
        /// 剥离器——格式文本 → 消息条目序列（重建前文入口；按字段声明的字符数切片，不靠分隔符猜测）。
        /// </summary>
        /// <param name="text">留档文本（BOM 已剥）</param>
        /// <param name="entries">输出：条目序列</param>
        /// <param name="error">输出：结构错误描述（空=解析成功）</param>
        /// <returns>true=结构合法</returns>
        public static bool TryParse(string text, out List<FullContextEntry> entries, out string error)
        {
            entries = new List<FullContextEntry>();
            error = "";
            if (text == null || text.Length == 0)
            {
                return true;
            }
            int pos = 0;
            FullContextEntry current = null;
            while (pos < text.Length)
            {
                int lineEnd = text.IndexOf('\n', pos);
                if (lineEnd < 0)
                {
                    lineEnd = text.Length;
                }
                string line = text.Substring(pos, lineEnd - pos);
                if (line.Length == 0)
                {
                    pos = lineEnd + 1;
                    continue;
                }
                if (line.StartsWith("# ", StringComparison.Ordinal))
                {
                    pos = lineEnd + 1;
                    continue;
                }
                if (line.StartsWith(CtxOpen, StringComparison.Ordinal) && line.EndsWith("]]", StringComparison.Ordinal))
                {
                    current = ParseSectionHead(line);
                    if (current == null)
                    {
                        error = "消息段头非法（字符位置 " + pos.ToString() + "）";
                        return false;
                    }
                    pos = lineEnd + 1;
                    continue;
                }
                if (line == CtxClose)
                {
                    if (current == null)
                    {
                        error = "消息段结束标记无对应段头";
                        return false;
                    }
                    entries.Add(current);
                    current = null;
                    pos = lineEnd + 1;
                    continue;
                }
                if (line.StartsWith("[[", StringComparison.Ordinal) && line.EndsWith("]]", StringComparison.Ordinal))
                {
                    if (current == null)
                    {
                        error = "字段块出现在消息段之外: " + line;
                        return false;
                    }
                    int space = line.LastIndexOf(' ');
                    int count;
                    if (space < 0 || !int.TryParse(line.Substring(space + 1, line.Length - space - 3), out count) || count < 0)
                    {
                        error = "字段块头非法（缺字符数）: " + line;
                        return false;
                    }
                    string name = line.Substring(2, space - 2);
                    pos = lineEnd + 1;
                    if (pos + count > text.Length)
                    {
                        error = "字段 " + name + " 正文越界（声明 " + count.ToString() + " 字符）";
                        return false;
                    }
                    string body = text.Substring(pos, count);
                    pos = pos + count;
                    if (pos >= text.Length || text[pos] != '\n')
                    {
                        error = "字段 " + name + " 正文后缺换行";
                        return false;
                    }
                    pos = pos + 1;
                    string tail = "[[/" + name + "]]";
                    if (!StartsWithAt(text, pos, tail))
                    {
                        error = "字段 " + name + " 正文后缺结束标记";
                        return false;
                    }
                    pos = pos + tail.Length;
                    if (pos < text.Length && text[pos] == '\n')
                    {
                        pos = pos + 1;
                    }
                    if (!AssignField(current, name, body))
                    {
                        error = "未知字段: " + name;
                        return false;
                    }
                    continue;
                }
                error = "未识别行: " + (line.Length > 40 ? line.Substring(0, 40) : line);
                return false;
            }
            if (current != null)
            {
                error = "消息段未闭合";
                return false;
            }
            return true;
        }

        /// <summary>采集实现——须在 _gate 内调用（重入安全）。</summary>
        private void CaptureLocked()
        {
            _lastCaptureMs = Environment.TickCount64;
            try
            {
                LlmMessage[] msgs = _messages == null ? null : _messages();
                if (msgs == null || msgs.Length == 0)
                {
                    return;
                }
                string path = EnsurePath();
                string newText = BuildText(msgs);
                string stored = File.Exists(path) ? File.ReadAllText(path) : "";
                if (stored.Length == 0)
                {
                    File.WriteAllText(path, newText, new UTF8Encoding(true));
                    return;
                }
                // 头部漂移守卫——留档头部 ≠ 前文头部 = 前文丢头：拒绝全量重写
                // （留档是重建唯一源，被覆写即失去修复依据）；出声一次，头部恢复即复位
                bool headDrift = HeadDiffers(stored, newText);
                if (!headDrift)
                {
                    _headDriftWarned = false;
                }
                else
                {
                    if (!_headDriftWarned)
                    {
                        _headDriftWarned = true;
                        LogStore.Add("FullContextStore", 3, "留档头部与前文不一致——拒绝全量重写（等待前文重建）: " + path, "CHAT");
                    }
                    return;
                }
                if (stored.Length >= AnchorChars)
                {
                    // 尾部锚点——已存文本末 400 字符；自新文本尾端向前搜（LastIndexOf 即「从尾端向前」）
                    string anchor = stored.Substring(stored.Length - AnchorChars);
                    int hit = newText.LastIndexOf(anchor, StringComparison.Ordinal);
                    if (hit >= 0 && hit == stored.Length - AnchorChars && hit + AnchorChars <= newText.Length)
                    {
                        if (hit + AnchorChars < newText.Length)
                        {
                            // 增量追加——BOM 只在首写时落（文件非空不重复写）
                            File.AppendAllText(path, newText.Substring(hit + AnchorChars), new UTF8Encoding(true));
                        }
                        return;
                    }
                }
                // 已存不足锚点长度 / 锚点未命中 / 位置不符（回卷或结构修复改写了既有文本）→ 全量重写
                File.WriteAllText(path, newText, new UTF8Encoding(true));
            }
            catch (Exception ex)
            {
                LogStore.Add("FullContextStore", 3, "完整前文采集失败: " + ex.Message, "CHAT");
            }
        }

        /// <summary>
        /// 当前会话文件路径——懒创建（首次采集时定名，时戳即会话起点）；同秒重复加序号，不覆盖既有档案。
        /// </summary>
        /// <returns>文件绝对路径</returns>
        private string EnsurePath()
        {
            if (_path.Length > 0)
            {
                return _path;
            }
            if (_dir.Length == 0)
            {
                throw new InvalidOperationException("完整前文落点目录未配置");
            }
            if (!Directory.Exists(_dir))
            {
                Directory.CreateDirectory(_dir);
            }
            DateTime now = DateTime.Now;
            string stamp = now.ToString("yyyyMMdd_HHmmss");
            string name = stamp;
            string file = Path.Combine(_dir, _catKey + "_" + name + ".txt");
            int seq = 1;
            while (File.Exists(file))
            {
                seq = seq + 1;
                name = stamp + "_" + seq.ToString();
                file = Path.Combine(_dir, _catKey + "_" + name + ".txt");
            }
            _stamp = name;
            _startText = now.ToString("yyyy-MM-dd HH:mm:ss");
            _path = file;
            LogStore.Add("CatHome4", 1, "完整前文留档起点: " + file, "CHAT");
            return _path;
        }

        /// <summary>全量文本——文件头 + 逐消息段（采集构建面）。</summary>
        /// <param name="msgs">消息序列</param>
        /// <returns>格式文本</returns>
        private string BuildText(LlmMessage[] msgs)
        {
            StringBuilder sb = new StringBuilder();
            AppendHeader(sb);
            for (int i = 0; i < msgs.Length; i = i + 1)
            {
                AppendMessageSection(sb, msgs[i]);
            }
            return sb.ToString();
        }

        /// <summary>单消息段——段头（四属性恒全写）+ 非空字段块 + 段尾。</summary>
        /// <param name="sb">目标缓冲</param>
        /// <param name="m">消息</param>
        private static void AppendMessageSection(StringBuilder sb, LlmMessage m)
        {
            sb.Append(CtxOpen)
                .Append("role=").Append(RoleText(m.Role))
                .Append(" time=").Append(m.CreatedAt.ToString())
                .Append(" toolcallid=").Append(m.ToolCallId == null ? "" : m.ToolCallId)
                .Append(" tool=").Append(m.ToolName == null ? "" : m.ToolName)
                .Append("]]\n");
            AppendField(sb, "content", m.Content);
            AppendField(sb, "tool_calls", m.ToolCallsJson);
            AppendField(sb, "reasoning", m.ReasoningContent);
            AppendField(sb, "images", m.ImagesJson);
            sb.Append(CtxClose).Append('\n');
        }

        /// <summary>字段块——空正文整块不出现；字符数为 .NET string.Length（剥离按此切片）。</summary>
        /// <param name="sb">目标缓冲</param>
        /// <param name="name">字段名</param>
        /// <param name="body">正文（空=跳过）</param>
        private static void AppendField(StringBuilder sb, string name, string body)
        {
            if (body == null || body.Length == 0)
            {
                return;
            }
            sb.Append("[[").Append(name).Append(' ').Append(body.Length.ToString()).Append("]]\n");
            sb.Append(body);
            sb.Append("\n[[/").Append(name).Append("]]\n");
        }

        /// <summary>角色文本——wire 四 role 小写。</summary>
        /// <param name="role">角色</param>
        /// <returns>角色文本</returns>
        private static string RoleText(LlmRole role)
        {
            if (role == LlmRole.System)
            {
                return "system";
            }
            if (role == LlmRole.User)
            {
                return "user";
            }
            if (role == LlmRole.Assistant)
            {
                return "assistant";
            }
            return "tool";
        }

        /// <summary>段头解析——四属性列固定；缺 role 视为非法段头。</summary>
        /// <param name="line">段头行</param>
        /// <returns>条目（null=非法）</returns>
        private static FullContextEntry ParseSectionHead(string line)
        {
            string inner = line.Substring(CtxOpen.Length, line.Length - CtxOpen.Length - 2);
            string[] parts = inner.Split(' ');
            FullContextEntry entry = new FullContextEntry();
            bool hasRole = false;
            for (int i = 0; i < parts.Length; i = i + 1)
            {
                string part = parts[i];
                int eq = part.IndexOf('=');
                if (eq <= 0)
                {
                    continue;
                }
                string key = part.Substring(0, eq);
                string value = part.Substring(eq + 1);
                if (key == "role")
                {
                    entry.Role = value;
                    hasRole = value.Length > 0;
                }
                else if (key == "time")
                {
                    long parsed;
                    if (long.TryParse(value, out parsed))
                    {
                        entry.Time = parsed;
                    }
                }
                else if (key == "toolcallid")
                {
                    entry.ToolCallId = value;
                }
                else if (key == "tool")
                {
                    entry.ToolName = value;
                }
            }
            return hasRole ? entry : null;
        }

        /// <summary>字段名 → 条目字段（未知字段=结构非法）。</summary>
        /// <param name="entry">目标条目</param>
        /// <param name="name">字段名</param>
        /// <param name="body">正文</param>
        /// <returns>true=已知字段</returns>
        private static bool AssignField(FullContextEntry entry, string name, string body)
        {
            if (name == "content")
            {
                entry.Content = body;
                return true;
            }
            if (name == "tool_calls")
            {
                entry.ToolCalls = body;
                return true;
            }
            if (name == "reasoning")
            {
                entry.Reasoning = body;
                return true;
            }
            if (name == "images")
            {
                entry.Images = body;
                return true;
            }
            return false;
        }

        /// <summary>位置比对——不分配子串（大文本解析省内存）。</summary>
        /// <param name="text">目标文本</param>
        /// <param name="pos">起始位置</param>
        /// <param name="value">待比对值</param>
        /// <returns>true=一致</returns>
        private static bool StartsWithAt(string text, int pos, string value)
        {
            if (pos + value.Length > text.Length)
            {
                return false;
            }
            return string.CompareOrdinal(text, pos, value, 0, value.Length) == 0;
        }
        /// <summary>
        /// 消息载荷正文——四字段按序合并（content / tool_calls / reasoning / images），空字段跳过。
        /// 单点实现：前文条目（/api/v1/context）与完整前文条目（/api/v1/fullctx）共用同一口径。
        /// </summary>
        /// <param name="content">正文</param>
        /// <param name="toolCalls">工具调用 JSON（assistant）</param>
        /// <param name="reasoning">思考内容（assistant——有工具调用轮次随请求回传）</param>
        /// <param name="images">附件引用 JSON（user——请求构造时展开为 image_url 块）</param>
        /// <returns>合并正文（全空=空串）</returns>
        public static string MergeBody(string content, string toolCalls, string reasoning, string images)
        {
            string body = content == null ? "" : content;
            body = MergePart(body, toolCalls);
            body = MergePart(body, reasoning);
            body = MergePart(body, images);
            return body;
        }
        /// <summary>载荷正文拼接——非空才拼（空=原样返回）</summary>
        /// <param name="body">已拼接正文</param>
        /// <param name="part">待拼接字段</param>
        /// <returns>拼接后正文</returns>
        private static string MergePart(string body, string part)
        {
            if (part == null || part.Length == 0) { return body; }
            return body.Length > 0 ? body + "\n" + part : part;
        }

        /// <summary>条目展示正文——与 /api/v1/context 同口径（四字段合并；单点实现见 MergeBody）。</summary>
        /// <param name="entry">条目</param>
        /// <returns>展示文本</returns>
        private static string DisplayBody(FullContextEntry entry)
        {
            return MergeBody(entry.Content, entry.ToolCalls, entry.Reasoning, entry.Images);
        }

        /// <summary>条目摘要——单行化后取首 PreviewLimit 字符（弹层折叠行显示）。</summary>
        /// <param name="body">条目正文</param>
        /// <returns>摘要文本</returns>
        private static string Preview(string body)
        {
            if (body == null || body.Length == 0)
            {
                return "";
            }
            string flat = body.Replace("\r", " ").Replace("\n", " ");
            if (flat.Length > PreviewLimit)
            {
                flat = flat.Substring(0, PreviewLimit) + "…";
            }
            return flat;
        }

        /// <summary>留档份数——读取器缺省 / 越界收敛（与 schema 值域同口径）。</summary>
        /// <returns>份数（1..MaxKeep）</returns>
        private int KeepCount()
        {
            if (_keep == null)
            {
                return DefaultKeep;
            }
            int value = _keep();
            if (value < 1)
            {
                return DefaultKeep;
            }
            if (value > MaxKeep)
            {
                return MaxKeep;
            }
            return value;
        }

        /// <summary>
        /// 轮转——按文件名时戳序（同一猫前缀下字典序即时间序）保留最新 keep 份，其余销毁。
        /// </summary>
        /// <param name="keep">保留份数</param>
        /// <returns>销毁份数</returns>
        private int Rotate(int keep)
        {
            if (_dir.Length == 0 || !Directory.Exists(_dir))
            {
                return 0;
            }
            string[] files = Directory.GetFiles(_dir, _catKey + "_*.txt");
            Array.Sort(files, StringComparer.Ordinal);
            int removed = 0;
            for (int i = 0; i < files.Length - keep; i = i + 1)
            {
                File.Delete(files[i]);
                removed = removed + 1;
            }
            return removed;
        }
        /// <summary>
        /// 巡检判据——前文首段与留档首段逐字符一致（比对长度 = min(400, 两侧长度)）（design-ch4-ctx-rebuild §二）。
        /// 无留档 / 留档为空 = 无判据（返回 true，不触发重建）；首段不一致 = 前文丢头或被裁剪。
        /// </summary>
        /// <param name="msgs">当前前文消息序列</param>
        /// <returns>true=头部一致（无需重建）</returns>
        public bool HeadMatches(LlmMessage[] msgs)
        {
            lock (_gate)
            {
                if (_path.Length == 0 || !File.Exists(_path))
                {
                    return true;
                }
                if (msgs == null || msgs.Length == 0)
                {
                    return true;
                }
                string stored = ReadHead(_path, HeadChars);
                if (stored.Length == 0)
                {
                    return true;
                }
                string live = BuildHead(msgs, HeadChars);
                return !HeadDiffers(stored, live);
            }
        }
        /// <summary>
        /// 重建源——留档剥离为消息序列 + 留档末条之后的前文消息续接（节流窗口内的新消息不丢）。
        /// 调用方：前文巡检（design-ch4-ctx-rebuild §三）。
        /// </summary>
        /// <param name="live">当前前文消息序列（尾部续接来源；可空）</param>
        /// <param name="messages">输出：重建后的消息序列（失败=null）</param>
        /// <param name="error">输出：失败描述（空=成功）</param>
        /// <returns>true=重建源可用</returns>
        public bool TryRebuild(LlmMessage[] live, out LlmMessage[] messages, out string error)
        {
            messages = null;
            error = "";
            lock (_gate)
            {
                // [段1] 留档读取与剥离——无留档 / 结构非法即失败出声
                if (_path.Length == 0 || !File.Exists(_path))
                {
                    error = "本会话无完整前文留档";
                    return false;
                }
                string text;
                try
                {
                    text = File.ReadAllText(_path);
                }
                catch (Exception ex)
                {
                    error = "留档读取失败: " + ex.Message;
                    return false;
                }
                List<FullContextEntry> entries;
                if (!TryParse(text, out entries, out error))
                {
                    return false;
                }
                if (entries.Count == 0)
                {
                    error = "留档无消息段";
                    return false;
                }
                // [段2] 条目 → 消息——角色非法即失败（不静默降级）
                List<LlmMessage> list = new List<LlmMessage>();
                for (int i = 0; i < entries.Count; i = i + 1)
                {
                    LlmMessage msg;
                    if (!ToMessage(entries[i], out msg))
                    {
                        error = "留档角色非法: " + entries[i].Role;
                        return false;
                    }
                    list.Add(msg);
                }
                // [段3] 尾部续接——留档末条在现前文中定位，其后消息按序接上
                int resume = MatchTail(entries, live);
                if (resume < 0)
                {
                    LogStore.Add("FullContextStore", 2, "前文重建：留档末条未在当前前文中命中——尾部不续接（留档 "
                        + entries.Count.ToString() + " 条）", "CHAT");
                }
                else
                {
                    for (int i = resume; i < live.Length; i = i + 1)
                    {
                        list.Add(live[i]);
                    }
                }
                messages = list.ToArray();
                return true;
            }
        }
        /// <summary>读留档首段——有界读取（不整文件载入）；BOM 由 StreamReader 自动剥离。</summary>
        /// <param name="path">留档路径</param>
        /// <param name="chars">读取字符数上限</param>
        /// <returns>首段文本（不足=全部）</returns>
        private static string ReadHead(string path, int chars)
        {
            char[] buffer = new char[chars];
            int total = 0;
            using (StreamReader reader = new StreamReader(path, Encoding.UTF8, true))
            {
                while (total < chars)
                {
                    int read = reader.Read(buffer, total, chars - total);
                    if (read <= 0)
                    {
                        break;
                    }
                    total = total + read;
                }
            }
            return new string(buffer, 0, total);
        }
        /// <summary>构建前文首段——文件头 + 逐消息段（累积到 chars 即截断；与留档同一构建面）。</summary>
        /// <param name="msgs">消息序列</param>
        /// <param name="chars">构建字符数上限</param>
        /// <returns>首段文本（不足=全部）</returns>
        private string BuildHead(LlmMessage[] msgs, int chars)
        {
            StringBuilder sb = new StringBuilder(chars + 256);
            AppendHeader(sb);
            for (int i = 0; i < msgs.Length && sb.Length < chars; i = i + 1)
            {
                AppendMessageSection(sb, msgs[i]);
            }
            if (sb.Length > chars)
            {
                return sb.ToString(0, chars);
            }
            return sb.ToString();
        }
        /// <summary>
        /// 首段比对——留档首段与现构首段是否不一致（比对长度 = min(400, 两侧长度)；任一侧为空 = 无判据）。
        /// 前文被裁剪（丢头 / 丢尾）时现构首段更短——比对区内的段头差异即暴露不一致。
        /// </summary>
        /// <param name="stored">留档侧文本（首段）</param>
        /// <param name="live">现构侧文本（首段）</param>
        /// <returns>true=不一致（触发重建 / 拒绝覆写）</returns>
        private static bool HeadDiffers(string stored, string live)
        {
            int chars = stored.Length;
            if (chars > HeadChars)
            {
                chars = HeadChars;
            }
            if (live.Length < chars)
            {
                chars = live.Length;
            }
            if (chars == 0)
            {
                return false;
            }
            return !string.Equals(stored.Substring(0, chars), live.Substring(0, chars), StringComparison.Ordinal);
        }
        /// <summary>文件头——版本行 + 猫 / 会话 / 起时（采集与首段构建共用同一出口）。</summary>
        /// <param name="sb">目标缓冲</param>
        private void AppendHeader(StringBuilder sb)
        {
            sb.Append(HeaderVersion).Append('\n');
            sb.Append("# cat=").Append(_catKey).Append(" session=").Append(_stamp)
                .Append(" start=").Append(_startText).Append('\n');
        }
        /// <summary>留档条目 → 消息——四字段 + 配对 ID + 时刻全字段还原（角色非法返回 false）。</summary>
        /// <param name="entry">留档条目</param>
        /// <param name="message">输出：消息</param>
        /// <returns>true=角色合法</returns>
        private static bool ToMessage(FullContextEntry entry, out LlmMessage message)
        {
            message = new LlmMessage();
            LlmRole role;
            if (!RoleFromText(entry.Role, out role))
            {
                return false;
            }
            message.Role = role;
            message.Content = entry.Content;
            message.ToolCallId = entry.ToolCallId;
            message.ToolName = entry.ToolName;
            message.ToolCallsJson = entry.ToolCalls;
            message.ReasoningContent = entry.Reasoning;
            message.ImagesJson = entry.Images;
            message.CreatedAt = entry.Time;
            return true;
        }
        /// <summary>角色文本 → 角色（wire 四 role 小写；未知返回 false）。</summary>
        /// <param name="text">角色文本</param>
        /// <param name="role">输出：角色</param>
        /// <returns>true=已知角色</returns>
        private static bool RoleFromText(string text, out LlmRole role)
        {
            role = LlmRole.Tool;
            if (text == "system")
            {
                role = LlmRole.System;
                return true;
            }
            if (text == "user")
            {
                role = LlmRole.User;
                return true;
            }
            if (text == "assistant")
            {
                role = LlmRole.Assistant;
                return true;
            }
            if (text == "tool")
            {
                role = LlmRole.Tool;
                return true;
            }
            return false;
        }
        /// <summary>
        /// 尾部续接定位——留档末条在现前文中的位置（逐字段全等，自尾端向前找），命中即返回其后一条下标。
        /// 未命中返回 -1（两端不同源——不续接，由调用方出声）。
        /// </summary>
        /// <param name="entries">留档条目序列</param>
        /// <param name="live">当前前文消息序列</param>
        /// <returns>续接起点下标（-1=未命中）</returns>
        private static int MatchTail(List<FullContextEntry> entries, LlmMessage[] live)
        {
            if (live == null || live.Length == 0)
            {
                return -1;
            }
            FullContextEntry last = entries[entries.Count - 1];
            for (int i = live.Length - 1; i >= 0; i = i - 1)
            {
                if (SameMessage(last, live[i]))
                {
                    return i + 1;
                }
            }
            return -1;
        }
        /// <summary>留档条目与消息逐字段全等——续接定位判据（角色 / 四字段 / 配对 ID / 时刻）。</summary>
        /// <param name="entry">留档条目</param>
        /// <param name="msg">消息</param>
        /// <returns>true=同一消息</returns>
        private static bool SameMessage(FullContextEntry entry, LlmMessage msg)
        {
            LlmRole role;
            if (!RoleFromText(entry.Role, out role))
            {
                return false;
            }
            return role == msg.Role
                && entry.Time == msg.CreatedAt
                && string.Equals(entry.Content, msg.Content, StringComparison.Ordinal)
                && string.Equals(entry.ToolCallId, msg.ToolCallId, StringComparison.Ordinal)
                && string.Equals(entry.ToolCalls, msg.ToolCallsJson, StringComparison.Ordinal)
                && string.Equals(entry.Reasoning, msg.ReasoningContent, StringComparison.Ordinal)
                && string.Equals(entry.Images, msg.ImagesJson, StringComparison.Ordinal);
        }
    }
}

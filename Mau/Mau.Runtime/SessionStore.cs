using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mau.Runtime
{
    /// <summary>
    /// 会话统计——前文真实 usage 持久化（轮末/中断收尾写入；旧文件缺字段兼容——可空）。
    /// 只存真实值（LLM usage 回传），不做任何估算。
    /// </summary>
    public struct SessionStats
    {
        /// <summary>消息条数——真实前文消息数（含 system）</summary>
        public long EntryCount;

        /// <summary>最近一轮真实 prompt token——命中 + 非命中总和（usage.prompt_tokens）</summary>
        public long LastPromptTokens;

        /// <summary>最近一轮缓存命中 token（usage.prompt_tokens_details.cached_tokens）</summary>
        public long LastCacheHitTokens;

        /// <summary>最近一轮输出 token（usage.completion_tokens）</summary>
        public long LastCompletionTokens;

        /// <summary>最近一次请求的单次前文长度（非累计——前文长度数据源；旧文件缺省 0）</summary>
        public long LastContextTokens;
    }

    /// <summary>
    /// 会话前文管理器——JSONL 增量落盘（A47）。
    /// 落盘：sessions/&lt;id&gt;/&lt;id&gt;.jsonl——每行一条独立 JSON 记录（消息行 t=m / 元数据行 t=meta）。
    /// 写面：每消息完成即 append（崩溃只影响最后一行）+ 会话起点/截断原子重写。
    /// 读面：逐行解析 + 末行残缺补全（结构补齐 + 显式标注——不丢弃已落盘内容）。
    /// 策略：只保存/恢复 + 基础结构修复——上下文策略（截断/预算）不做。
    /// </summary>
    public sealed class SessionStore
    {
        /// <summary>消息行标记——t 字段取值</summary>
        private const string MessageTag = "m";

        /// <summary>元数据行标记——t 字段取值</summary>
        private const string MetaTag = "meta";

        /// <summary>截断修复标注——补全成功的消息在 Content 尾部追加（LLM 可见）</summary>
        public const string TruncatedRepairNote = "\n\n[系统自动修复] 本消息在宿主中断时被截断——以上为崩溃前已落盘部分";

        /// <summary>补全候选字符集——闭合字符串 / 转义引号 / 闭合对象 / 闭合数组</summary>
        private static readonly char[] RepairChars = { '"', '\\', '}', ']' };

        /// <summary>补全候选深度上限——候选序列最大长度</summary>
        private const int RepairMaxDepth = 4;

        /// <summary>UTF-8 无 BOM 编码——JSONL 写入（UTF-8 无 BOM + LF 行结束）</summary>
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        /// <summary>序列化选项——字段序列化 + 中文直出（静态复用）</summary>
        private static readonly JsonSerializerOptions SerializerOptions = BuildOptions();

        /// <summary>会话文件路径——sessions/&lt;id&gt;/&lt;id&gt;.jsonl</summary>
        private readonly string _path;

        /// <summary>会话标识——落盘记录字段（恒 = 猫 key；宿主构造注入，读面不回填）</summary>
        private string _sessionId;

        /// <summary>
        /// 建立前文管理器
        /// </summary>
        /// <param name="path">会话文件路径（.jsonl）</param>
        public SessionStore(string path)
        {
            _path = path;
            _sessionId = "";
        }

        /// <summary>
        /// 会话标识——唯一标识 = 猫 key（宿主构造注入；session.new 不换标识、读面不回填）。
        /// 语义：随 meta 行落盘作记录；LLM 侧请求身份（user_id / x-opencode-session）与网关 KV 缓存命名空间。
        /// </summary>
        public string SessionId
        {
            get
            {
                return _sessionId;
            }
            set
            {
                if (value == null)
                {
                    _sessionId = "";
                }
                else
                {
                    _sessionId = value;
                }
            }
        }

        /// <summary>
        /// 新标识——ticks 字符串（唯一生成点：cat.new 建猫 id / session.fork 新猫 id；会话标识不再是生成物）
        /// </summary>
        /// <returns>新标识（作猫 key 用）</returns>
        public static string NewSessionId()
        {
            return DateTime.Now.Ticks.ToString();
        }

        /// <summary>
        /// 尝试加载前文——文件不存在返回 false；无可用消息返回 false（调用方走隐式新会话注入）
        /// </summary>
        /// <param name="messages">加载的消息数组</param>
        /// <returns>true=加载成功</returns>
        public bool TryLoad(out LlmMessage[] messages)
        {
            SessionStats? stats;
            return TryLoad(out messages, out stats);
        }

        /// <summary>
        /// 尝试加载前文含统计——逐行解析 + 末行残缺补全（结构补齐 + 显式标注）。
        /// 末行残缺：补全为合法 JSON 后按消息消费（Content 追加修复标注）；无法构成消息 → 丢弃 + 告警。
        /// 非末行损坏：跳过坏行 + 告警，其余照常加载（不整文件作废）。
        /// 全文件不可解析 → 备份 .bad + 返回 false。
        /// </summary>
        /// <param name="messages">加载的消息数组</param>
        /// <param name="stats">会话统计（可空=文件无统计）</param>
        /// <returns>true=加载成功（至少一条可用消息）</returns>
        public bool TryLoad(out LlmMessage[] messages, out SessionStats? stats)
        {
            messages = new LlmMessage[0];
            stats = null;
            if (!File.Exists(_path))
            {
                return false;
            }
            string[] lines;
            try
            {
                lines = File.ReadAllLines(_path, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                LogStore.Add("CatHome4", 3, "前文读取失败（按无前文处理）: " + ex.Message, "CHAT");
                return false;
            }
            // [段1] 末条非空行定位——仅末行允许残缺补全
            int lastIndex = FindLastContentLine(lines);
            if (lastIndex < 0)
            {
                return false;
            }
            // [段2] 逐行解析——消息行 / 元数据行 / 坏行处置
            List<LlmMessage> list = new List<LlmMessage>();
            bool repairedAny = false;
            int repairedLineIndex = -1;
            string repairedLineText = "";
            int badLines = 0;
            int contentLines = 0;
            for (int i = 0; i < lines.Length; i = i + 1)
            {
                string line = lines[i].Trim();
                if (line.Length == 0)
                {
                    continue;
                }
                contentLines = contentLines + 1;
                bool isLast = i == lastIndex;
                bool repaired = false;
                JsonObject? obj = ParseObject(line);
                if (obj == null && isLast)
                {
                    // 末行残缺——结构补全（唯一允许的修复路径）
                    string fixedLine;
                    if (TryRepairLine(line, out fixedLine))
                    {
                        obj = ParseObject(fixedLine);
                        repaired = true;
                        repairedAny = true;
                        repairedLineIndex = i;
                    }
                }
                if (obj == null)
                {
                    badLines = badLines + 1;
                    if (isLast)
                    {
                        LogStore.Add("CatHome4", 2, "前文末行残缺且无法补全——该行丢弃（无角色信息）", "CHAT");
                    }
                    else
                    {
                        LogStore.Add("CatHome4", 2, "前文第 " + (i + 1).ToString() + " 行损坏——跳过（其余照常加载）", "CHAT");
                    }
                    continue;
                }
                string tag = ReadString(obj, "t");
                if (tag == MetaTag)
                {
                    // 元数据行——只取统计；会话标识不进读面（身份 = 猫 key，宿主构造注入）
                    JsonNode? statsNode = obj["Stats"];
                    if (statsNode != null)
                    {
                        SessionStats got = DeserializeStats(statsNode);
                        stats = got;
                    }
                    continue;
                }
                if (!obj.ContainsKey("Role"))
                {
                    // 无角色信息——无法构成消息（唯一丢弃情形）
                    badLines = badLines + 1;
                    LogStore.Add("CatHome4", 2, "前文第 " + (i + 1).ToString() + " 行无角色信息——丢弃（残缺丢失已落盘内容）", "CHAT");
                    continue;
                }
                LlmMessage msg = DeserializeMessage(obj);
                if (repaired)
                {
                    // 补全消息——Content 尾部追加修复标注（LLM 可见；已落盘半截内容全保留）
                    msg.Content = msg.Content + TruncatedRepairNote;
                    // 截断点在 CreatedAt 之前——时间戳缺省 0：取前一条 +1（视图归并按时间戳归并，0 会让该块排到历史最前）
                    if (msg.CreatedAt <= 0)
                    {
                        long prevStamp = 0;
                        if (list.Count > 0)
                        {
                            prevStamp = list[list.Count - 1].CreatedAt;
                        }
                        if (prevStamp > 0)
                        {
                            msg.CreatedAt = prevStamp + 1;
                        }
                        else
                        {
                            msg.CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                        }
                    }
                    // 回写行 = 带标注的完整行（标注与结构一并落盘——否则下次启动标注丢失，"内容不完整"信号静默消失）
                    repairedLineText = BuildMessageLine(Normalize(msg));
                    LogStore.Add("CatHome4", 2, "前文末行残缺——已补全为合法 JSON（内容不完整，已标注）", "CHAT");
                }
                list.Add(msg);
            }
            // [段3] 结果判定——无可用消息：全文件不可解析则备份（保留数据可追溯）
            if (list.Count == 0)
            {
                if (badLines > 0 && badLines == contentLines)
                {
                    BackupCorruptFile();
                }
                return false;
            }
            messages = list.ToArray();
            if (repairedAny && repairedLineText.Length > 0)
            {
                WriteBackRepairedLine(lines, repairedLineIndex, repairedLineText);
            }
            return true;
        }

        /// <summary>
        /// 追加一条消息行——每消息完成即落盘（文件缺失/为空时先补元数据行）。
        /// </summary>
        /// <param name="message">待追加消息</param>
        public void Append(LlmMessage message)
        {
            AppendLine(BuildMessageLine(Normalize(message)), true);
        }

        /// <summary>
        /// 追加一条元数据行——统计更新（轮末 / 中断收尾）与标识更新各 append 一行，读面取最后出现值。
        /// </summary>
        /// <param name="stats">会话统计（可空=只写标识）</param>
        public void AppendMeta(SessionStats? stats)
        {
            AppendLine(BuildMetaLine(stats), false);
        }

        /// <summary>
        /// 重写会话文件（原子——临时文件 + 替换）——会话起点 / 截断场景唯一写通道（无统计）。
        /// </summary>
        /// <param name="messages">保留的消息数组</param>
        public void Rewrite(LlmMessage[] messages)
        {
            Rewrite(messages, null);
        }

        /// <summary>
        /// 重写会话文件（原子——临时文件 + 替换）——会话起点 / 截断场景唯一写通道。
        /// append-only 的合法例外：起点写入与尾部截断无法用追加表达（低频操作）。
        /// </summary>
        /// <param name="messages">保留的消息数组</param>
        /// <param name="stats">会话统计（可空=不写统计）</param>
        public void Rewrite(LlmMessage[] messages, SessionStats? stats)
        {
            try
            {
                EnsureDirectory();
                StringBuilder sb = new StringBuilder();
                sb.Append(BuildMetaLine(stats));
                sb.Append('\n');
                if (messages != null)
                {
                    for (int i = 0; i < messages.Length; i = i + 1)
                    {
                        sb.Append(BuildMessageLine(Normalize(messages[i])));
                        sb.Append('\n');
                    }
                }
                string tmp = _path + ".tmp";
                File.WriteAllText(tmp, sb.ToString(), Utf8NoBom);
                File.Move(tmp, _path, true);
            }
            catch (Exception ex)
            {
                LogStore.Add("CatHome4", 3, "前文重写落盘失败: " + ex.Message, "CHAT");
            }
        }

        /// <summary>
        /// 追加一行到会话文件——开-写-关（无长驻句柄；读侧共享无冲突）。
        /// ensureMeta=true 且文件为空时先补元数据行（会话标识随首条消息落盘）。
        /// 失败不阻断会话——记 L3 告警（失败必须可见）。
        /// </summary>
        /// <param name="line">行内容（单行 JSON）</param>
        /// <param name="ensureMeta">文件为空时是否先补元数据行</param>
        private void AppendLine(string line, bool ensureMeta)
        {
            try
            {
                EnsureDirectory();
                using (FileStream fs = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                {
                    using (StreamWriter sw = new StreamWriter(fs, Utf8NoBom))
                    {
                        if (ensureMeta && fs.Length == 0)
                        {
                            sw.Write(BuildMetaLine(null));
                            sw.Write('\n');
                        }
                        sw.Write(line);
                        sw.Write('\n');
                    }
                }
            }
            catch (Exception ex)
            {
                LogStore.Add("CatHome4", 3, "前文增量落盘失败（本条未落盘）: " + ex.Message, "CHAT");
            }
        }

        /// <summary>
        /// 确保会话文件目录存在。
        /// </summary>
        private void EnsureDirectory()
        {
            string? dir = Path.GetDirectoryName(_path);
            if (dir != null && dir.Length > 0 && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }

        /// <summary>
        /// 构建消息行——LlmMessage 全字段 + t 标记。
        /// </summary>
        /// <param name="message">消息（已归一化）</param>
        /// <returns>单行 JSON</returns>
        private static string BuildMessageLine(LlmMessage message)
        {
            JsonNode? node = JsonSerializer.SerializeToNode(message, SerializerOptions);
            if (node == null)
            {
                return "";
            }
            JsonObject obj = node.AsObject();
            obj["t"] = MessageTag;
            return obj.ToJsonString(SerializerOptions);
        }

        /// <summary>
        /// 构建元数据行——t 标记 + 会话标识 + 统计（可空）。
        /// </summary>
        /// <param name="stats">会话统计（可空=不写）</param>
        /// <returns>单行 JSON</returns>
        private string BuildMetaLine(SessionStats? stats)
        {
            JsonObject obj = new JsonObject();
            obj["t"] = MetaTag;
            obj["SessionId"] = _sessionId == null ? "" : _sessionId;
            if (stats != null)
            {
                JsonNode? statsNode = JsonSerializer.SerializeToNode(stats.Value, SerializerOptions);
                if (statsNode != null)
                {
                    obj["Stats"] = statsNode;
                }
            }
            return obj.ToJsonString(SerializerOptions);
        }

        /// <summary>
        /// 归一化消息——struct 字符串字段默认 null（序列化判空会 NRE），落盘前统一补齐空串。
        /// </summary>
        /// <param name="message">原始消息</param>
        /// <returns>归一化消息</returns>
        private static LlmMessage Normalize(LlmMessage message)
        {
            if (message.Content == null)
            {
                message.Content = "";
            }
            if (message.ToolCallId == null)
            {
                message.ToolCallId = "";
            }
            if (message.ToolName == null)
            {
                message.ToolName = "";
            }
            if (message.ToolCallsJson == null)
            {
                message.ToolCallsJson = "";
            }
            if (message.ReasoningContent == null)
            {
                message.ReasoningContent = "";
            }
            return message;
        }

        /// <summary>
        /// 反序列化消息行——未知字段（t）忽略；字段缺失补齐空串。
        /// </summary>
        /// <param name="obj">消息行 JSON 对象</param>
        /// <returns>消息</returns>
        private static LlmMessage DeserializeMessage(JsonObject obj)
        {
            LlmMessage msg = obj.Deserialize<LlmMessage>(SerializerOptions);
            return Normalize(msg);
        }

        /// <summary>
        /// 反序列化统计——损坏字段回落零值（不阻断加载）。
        /// </summary>
        /// <param name="node">统计节点</param>
        /// <returns>统计（解析失败=零值）</returns>
        private static SessionStats DeserializeStats(JsonNode node)
        {
            try
            {
                return node.Deserialize<SessionStats>(SerializerOptions);
            }
            catch (Exception)
            {
                return new SessionStats();
            }
        }

        /// <summary>
        /// 解析单行 JSON 为对象——非对象 / 解析失败返回空。
        /// </summary>
        /// <param name="line">行文本</param>
        /// <returns>JSON 对象（可空）</returns>
        private static JsonObject? ParseObject(string line)
        {
            try
            {
                JsonNode? node = JsonNode.Parse(line);
                if (node == null)
                {
                    return null;
                }
                return node as JsonObject;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// 读取对象字符串字段——缺失 / 非字符串返回空串。
        /// </summary>
        /// <param name="obj">JSON 对象</param>
        /// <param name="key">字段名</param>
        /// <returns>字段值（缺省空串）</returns>
        private static string ReadString(JsonObject obj, string key)
        {
            JsonNode? node;
            if (obj.TryGetPropertyValue(key, out node))
            {
                if (node != null && node.GetValueKind() == JsonValueKind.String)
                {
                    string? v = node.GetValue<string>();
                    if (v != null)
                    {
                        return v;
                    }
                }
            }
            return "";
        }

        /// <summary>
        /// 定位末条非空行——仅末行允许残缺补全（append-only 下崩溃只影响最后一行）。
        /// </summary>
        /// <param name="lines">全部行</param>
        /// <returns>行索引（-1=无内容）</returns>
        private static int FindLastContentLine(string[] lines)
        {
            for (int i = lines.Length - 1; i >= 0; i = i - 1)
            {
                if (lines[i].Trim().Length > 0)
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// 末行残缺补全——闭合未终止的 JSON 字符串 + 补齐缺失的括号。
        /// 候选序列按长度递增枚举（补全字符集：引号 / 反斜杠 / 右括号 / 右方括号），
        /// 首个「合法且含角色信息」的结果即采用；已落盘内容全部保留（不裁剪）。
        /// </summary>
        /// <param name="line">残缺行</param>
        /// <param name="repaired">补全后的行</param>
        /// <returns>true=补全成功</returns>
        private static bool TryRepairLine(string line, out string repaired)
        {
            repaired = "";
            List<string> current = new List<string>();
            current.Add(line);
            for (int depth = 1; depth <= RepairMaxDepth; depth = depth + 1)
            {
                List<string> next = new List<string>();
                for (int i = 0; i < current.Count; i = i + 1)
                {
                    for (int c = 0; c < RepairChars.Length; c = c + 1)
                    {
                        string candidate = current[i] + RepairChars[c];
                        JsonObject? obj = ParseObject(candidate);
                        if (obj != null && (obj.ContainsKey("Role") || ReadString(obj, "t") == MetaTag))
                        {
                            repaired = candidate;
                            return true;
                        }
                        next.Add(candidate);
                    }
                }
                current = next;
            }
            return false;
        }
        /// <summary>
        /// 回写末行残缺补全结果——磁盘与内存一致。补全只改内存时，坏行会在下次追加后退居中段，
        /// 再次启动被当作损坏行跳过（该条消息永久丢失）——故补全同时回写。
        /// 原子写（临时文件 + 替换）；除补全行外其余行原样保留（UTF-8 无 BOM + LF）。
        /// </summary>
        /// <param name="lines">原始行数组（不含行结束符）</param>
        /// <param name="index">被补全的行下标</param>
        /// <param name="fixedLine">补全后的完整行文本（含修复标注——与内存前文一致）</param>
        private void WriteBackRepairedLine(string[] lines, int index, string fixedLine)
        {
            try
            {
                EnsureDirectory();
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < lines.Length; i = i + 1)
                {
                    if (i == index)
                    {
                        sb.Append(fixedLine);
                    }
                    else
                    {
                        sb.Append(lines[i]);
                    }
                    sb.Append('\n');
                }
                string tmp = _path + ".tmp";
                File.WriteAllText(tmp, sb.ToString(), Utf8NoBom);
                File.Move(tmp, _path, true);
                LogStore.Add("CatHome4", 1, "前文末行残缺——已回写补全结果（磁盘与内存一致）", "CHAT");
            }
            catch (Exception ex)
            {
                LogStore.Add("CatHome4", 3, "前文补全回写失败（内存已修复，磁盘未同步）: " + ex.Message, "CHAT");
            }
        }

        /// <summary>
        /// 备份不可用前文文件——改名 .bad（保留原样可手工修复/追溯；下次启动不再重复解析坏文件）。
        /// </summary>
        private void BackupCorruptFile()
        {
            try
            {
                string badPath = _path + ".bad";
                if (File.Exists(badPath))
                {
                    File.Delete(badPath);
                }
                File.Move(_path, badPath);
                LogStore.Add("CatHome4", 3, "会话前文全部不可解析，已备份为 " + badPath + "（本次按新会话启动）", "CHAT");
            }
            catch (Exception)
            {
                // 备份失败不阻断加载失败语义——原文件保留
            }
        }

        /// <summary>
        /// 构建序列化选项——字段序列化（LlmMessage / SessionStats 为 struct）+ 中文直出。
        /// </summary>
        /// <returns>序列化选项</returns>
        private static JsonSerializerOptions BuildOptions()
        {
            JsonSerializerOptions options = new JsonSerializerOptions();
            options.IncludeFields = true;
            options.Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
            return options;
        }
    }
}

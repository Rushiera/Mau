using System;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mau.Runtime;

namespace CH4
{
    /// <summary>
    /// 会话 token 三项——输入总量 / 命中缓存输入 / 输出（A201）。
    /// 未命中与命中率为派生值（不落盘——与「默认值不进落盘」同判据）。
    /// </summary>
    internal struct SessionTokens
    {
        /// <summary>输入 token 总量（含命中缓存部分）</summary>
        public long Prompt;

        /// <summary>输入中命中缓存部分 token</summary>
        public long CacheHit;

        /// <summary>输出 token</summary>
        public long Completion;

        /// <summary>未命中输入——派生值（prompt − cacheHit，不下穿 0；不落盘）</summary>
        [JsonIgnore]
        public long Miss
        {
            get
            {
                long m = Prompt - CacheHit;
                if (m < 0)
                {
                    return 0;
                }
                return m;
            }
        }

        /// <summary>命中率——派生值（cacheHit ÷ prompt；分母 0 → 0；不落盘）</summary>
        [JsonIgnore]
        public double Rate
        {
            get
            {
                if (Prompt <= 0)
                {
                    return 0;
                }
                return Math.Round((double)CacheHit / (double)Prompt, 4);
            }
        }
    }

    /// <summary>
    /// 本轮六态累计毫秒（A202）——idle 不计时故不落盘（与 roundsum 载荷同口径）。
    /// </summary>
    internal struct SessionPhases
    {
        /// <summary>等待（本地退避——重试退避期间）</summary>
        public long Wait;

        /// <summary>链路（远端等待——请求发出到首个语义增量帧）</summary>
        public long Link;

        /// <summary>思考（远端流——reasoning_content 增量）</summary>
        public long Think;

        /// <summary>工具（远端流——tool_calls 决策流式过程）</summary>
        public long Tool;

        /// <summary>执行（本地——工具批发单到下一请求发出）</summary>
        public long Run;

        /// <summary>回复（远端流——content 增量）</summary>
        public long Reply;
    }

    /// <summary>
    /// 任务追踪（Note）持久化形态（A202）——空计划 = Tasks 空数组。
    /// </summary>
    internal struct SessionNote
    {
        /// <summary>任务列表（当前计划全文；null / 空 = 无计划）</summary>
        public string[] Tasks;

        /// <summary>当前任务索引（0-based）</summary>
        public long Current;

        /// <summary>已完成任务数</summary>
        public long Done;
    }

    /// <summary>
    /// 会话元数据——会话自身参数的持久化模型（A201 · design-ch4-protocol §十三）。
    /// 独立于 cat.cfg（每猫配置）· 前文 jsonl（真实前文）· view.json（视图前文）。
    /// </summary>
    internal sealed class SessionMeta
    {
        /// <summary>会话实例 ID——session.new 生成；仅作显示与元数据定位（不参与 LLM 请求身份与网关缓存命名空间）</summary>
        public string SessionId = "";

        /// <summary>猫 key（寻址主键——会话目录名）</summary>
        public string CatId = "";

        /// <summary>会话显示名——会话创建时取自 cat.cfg</summary>
        public string DisplayName = "";

        /// <summary>会话创建时刻（Unix 毫秒）</summary>
        public long CreatedAt;

        /// <summary>最后活跃时刻（= 最后前文变动，含工具结果写入）</summary>
        public long LastActiveAt;

        /// <summary>前文条数</summary>
        public long ContextCount;

        /// <summary>前文总字符数（全消息 Content 长度之和）</summary>
        public long ContextChars;

        /// <summary>前文长度（最近一次请求 prompt token）</summary>
        public long ContextTokens;

        /// <summary>会话级累计三项——New 到下一个 New</summary>
        public SessionTokens SessionTokens;

        /// <summary>轮级累计三项——最后一次请求边界态（A202：每请求结算后落盘；内存值轮首清零）</summary>
        public SessionTokens RoundTokens;

        /// <summary>本轮六态累计毫秒——最后一次请求边界态（A202；idle 不落盘）</summary>
        public SessionPhases RoundPhases;

        /// <summary>本轮 API 请求次数（= link 段数）——最后一次请求边界态</summary>
        public long RoundRequests;

        /// <summary>本轮工具调用次数——最后一次请求边界态</summary>
        public long RoundTools;

        /// <summary>本轮起算 → 最后一次请求结算的耗时毫秒</summary>
        public long RoundElapsedMs;

        /// <summary>任务追踪（Note）——未完成时随重启恢复并继续自动拉起（莎 2026-10-06 批准）</summary>
        public SessionNote Note;
    }

    /// <summary>
    /// 会话元数据落盘器——覆盖式原子写 + 读面兜底（A201 · design-ch4-protocol §十三）。
    /// 路径 sessions/&lt;id&gt;/&lt;id&gt;.session.json；文件缺失 = 未初始化（调用方走首建）；
    /// 解析失败 = 回落默认 + 日志告警（失败必须可见，不阻断会话）。
    /// </summary>
    internal sealed class SessionMetaStore
    {
        /// <summary>UTF-8 无 BOM 编码——元数据写入</summary>
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        /// <summary>序列化选项——字段序列化 + 中文直出 + 缩进（静态复用）</summary>
        private static readonly JsonSerializerOptions SerializerOptions = BuildOptions();

        /// <summary>元数据文件路径</summary>
        private readonly string _path;

        /// <summary>
        /// 建立元数据落盘器
        /// </summary>
        /// <param name="path">元数据文件路径（.session.json）</param>
        public SessionMetaStore(string path)
        {
            _path = path;
        }

        /// <summary>元数据文件路径——调用方诊断用</summary>
        public string Path
        {
            get
            {
                return _path;
            }
        }

        /// <summary>
        /// 读取元数据——文件缺失返回 null（未初始化）；解析失败返回 null + L3 告警（不阻断，调用方走首建）。
        /// </summary>
        /// <returns>元数据；缺失 / 损坏 = null</returns>
        public SessionMeta Load()
        {
            if (_path == null || _path.Length == 0)
            {
                return null;
            }
            if (!File.Exists(_path))
            {
                return null;
            }
            try
            {
                string text = File.ReadAllText(_path, Utf8NoBom);
                if (text.Length == 0)
                {
                    return null;
                }
                return JsonSerializer.Deserialize<SessionMeta>(text, SerializerOptions);
            }
            catch (Exception ex)
            {
                LogStore.Add("CatHome4", 3, "会话元数据解析失败（按首建处理）: " + ex.Message, "CHAT");
                return null;
            }
        }

        /// <summary>
        /// 写入元数据——覆盖式原子替换（临时文件写全量 → 移动）；失败出声不阻断会话。
        /// </summary>
        /// <param name="meta">元数据快照</param>
        public void Save(SessionMeta meta)
        {
            if (_path == null || _path.Length == 0 || meta == null)
            {
                return;
            }
            try
            {
                string dir = System.IO.Path.GetDirectoryName(_path);
                if (dir != null && dir.Length > 0 && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                string json = JsonSerializer.Serialize(meta, SerializerOptions);
                // 🔴 A202 临时名唯一——本面写者跨线程（主线程 session.new + 流消费线程每请求结算）
                string tmp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(tmp, json, Utf8NoBom);
                File.Move(tmp, _path, true);
            }
            catch (Exception ex)
            {
                LogStore.Add("CatHome4", 3, "会话元数据落盘失败: " + ex.Message, "CHAT");
            }
        }

        /// <summary>构建序列化选项——字段序列化 + 中文直出 + 缩进</summary>
        private static JsonSerializerOptions BuildOptions()
        {
            JsonSerializerOptions options = new JsonSerializerOptions();
            options.IncludeFields = true;
            options.WriteIndented = true;
            options.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
            return options;
        }
    }
}

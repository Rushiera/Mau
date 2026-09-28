using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mau.Runtime;

namespace CH4
{
    /// <summary>
    /// timeback 全程归档——append-only JSONL（每猫一份；design-ch4-timeback §五 / §12.4）。
    /// 落点：Data/sessions/&lt;猫key&gt;/timeback.jsonl——行型 open / close，以 id 关联。
    /// 写面：追加即落盘（开-写-关，无长驻句柄；失败记 L3 且返回 false——归档不可写不阻断回卷，但必须可见）。
    /// 读面：逐行容错（坏行 / 末行残缺跳过），编号懒加载（首次使用时扫全文件取 max(id) 并缓存）。
    /// </summary>
    public sealed class TimebackArchive
    {
        /// <summary>归档文件路径——sessions/&lt;猫key&gt;/timeback.jsonl</summary>
        private readonly string _path;

        /// <summary>已知最大编号——懒加载缓存（文件不存在时保持 0）</summary>
        private long _maxId;

        /// <summary>编号缓存加载标志——首次取号时扫全文件一次</summary>
        private bool _loaded;
        /// <summary>最近一次写面结果——false=上次写入成功（或尚未写入）；true=最近一次写入失败（info timeback.archive.ok 数据源）。</summary>
        private bool _lastWriteFailed;

        /// <summary>UTF-8 无 BOM 编码——JSONL 写入（与 SessionStore 同族）</summary>
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        /// <summary>序列化选项——中文直出（默认 \uXXXX 转义人读不便）</summary>
        private static readonly JsonSerializerOptions SerializerOptions = BuildOptions();

        /// <summary>
        /// 建立归档实例——文件按需创建（首次写入时建目录）。
        /// </summary>
        /// <param name="path">归档文件路径（timeback.jsonl）</param>
        public TimebackArchive(string path)
        {
            _path = path;
        }

        /// <summary>
        /// 取下一个编号——本猫内永久递增（跨会话 / 跨重启；懒加载扫末次最大值）。
        /// </summary>
        /// <returns>新编号（从 1 起）</returns>
        public long NextId()
        {
            EnsureLoaded();
            _maxId = _maxId + 1;
            return _maxId;
        }
        /// <summary>
        /// 写面可用性——最近一次追加是否失败（false=可用 / 尚未写入；info timeback.archive.ok 数据源）。
        /// </summary>
        public bool LastWriteFailed
        {
            get
            {
                return _lastWriteFailed;
            }
        }

        /// <summary>
        /// 追加开锚行——start 登记时写入（n 是「多大算该回」的阈值原料；存活时长归 close 行）。
        /// </summary>
        /// <param name="id">作用域编号</param>
        /// <param name="catKey">猫 key</param>
        /// <param name="round">开锚时的轮次</param>
        /// <param name="anchor">锚点消息索引（不晚于调用时刻的最近安全边界）</param>
        /// <param name="startAt">开锚时刻（Unix 毫秒）</param>
        /// <param name="purpose">用途标签</param>
        /// <param name="n">开锚时前文条数</param>
        /// <param name="tokens">开锚时的已知前文长度快照（真实 usage 值，零估算）</param>
        /// <returns>true=已落盘</returns>
        public bool AppendOpen(long id, string catKey, long round, int anchor, long startAt, string purpose, int n, long tokens)
        {
            JsonObject obj = new JsonObject();
            obj["t"] = "open";
            obj["id"] = id;
            obj["catKey"] = catKey ?? "";
            obj["round"] = round;
            obj["anchor"] = anchor;
            obj["startAt"] = startAt;
            obj["purpose"] = purpose ?? "";
            obj["n"] = n;
            obj["tokens"] = tokens;
            return AppendLine(obj.ToJsonString(SerializerOptions));
        }

        /// <summary>
        /// 追加回收行——back 回卷同期写入（findings 是摘要质量校准原料）。
        /// </summary>
        /// <param name="id">作用域编号</param>
        /// <param name="backAt">回收时刻（Unix 毫秒）</param>
        /// <param name="n">回收条数（锚点之后被销毁的前文消息数）</param>
        /// <param name="seconds">作用域存活时长（秒）</param>
        /// <param name="findings">带回载荷全文（事实 + 指针）</param>
        /// <param name="tokens">回收时的已知前文长度（真实 usage 值，零估算）</param>
        /// <returns>true=已落盘</returns>
        public bool AppendClose(long id, long backAt, int n, long seconds, string findings, long tokens)
        {
            JsonObject obj = new JsonObject();
            obj["t"] = "close";
            obj["id"] = id;
            obj["backAt"] = backAt;
            obj["n"] = n;
            obj["seconds"] = seconds;
            obj["findings"] = findings ?? "";
            obj["tokens"] = tokens;
            return AppendLine(obj.ToJsonString(SerializerOptions));
        }

        /// <summary>
        /// 编号缓存加载——扫全文件取 max(id)；坏行 / 末行残缺跳过（编号单调不倒退）。
        /// </summary>
        private void EnsureLoaded()
        {
            if (_loaded)
            {
                return;
            }
            _loaded = true;
            if (!File.Exists(_path))
            {
                return;
            }
            string[] lines;
            try
            {
                lines = File.ReadAllLines(_path, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                LogStore.Add("CatHome4", 3, "timeback 归档读取失败（编号从 0 起算）: " + ex.Message, "TIMEBACK");
                return;
            }
            for (int i = 0; i < lines.Length; i = i + 1)
            {
                long id = ReadLineId(lines[i]);
                if (id > _maxId)
                {
                    _maxId = id;
                }
            }
        }

        /// <summary>
        /// 读取单行 id——不可解析（坏行 / 末行残缺 / 无 id 字段）返回 0。
        /// </summary>
        /// <param name="line">单行文本</param>
        /// <returns>id（0=不可用）</returns>
        private static long ReadLineId(string line)
        {
            string text = line == null ? "" : line.Trim();
            if (text.Length == 0)
            {
                return 0;
            }
            try
            {
                JsonNode node = JsonNode.Parse(text);
                JsonObject obj = node as JsonObject;
                if (obj == null)
                {
                    return 0;
                }
                JsonNode idNode = obj["id"];
                if (idNode == null)
                {
                    return 0;
                }
                return idNode.GetValue<long>();
            }
            catch (Exception)
            {
                return 0;
            }
        }
        /// <summary>
        /// 读取归档尾部若干行——逐行容错（坏行 / 末行残缺跳过），返回原始 JSON 行字符串（字段提取归调用方）。
        /// 读面只服务观测（info timeback.recent）——不参与编号与回收逻辑。
        /// </summary>
        /// <param name="max">最大行数（≤0 = 空列表）</param>
        /// <returns>JSON 行列表（文件顺序，最旧在前）</returns>
        public List<string> ReadTailJson(int max)
        {
            List<string> result = new List<string>();
            if (max <= 0 || !File.Exists(_path))
            {
                return result;
            }
            string[] lines;
            try
            {
                lines = File.ReadAllLines(_path, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                LogStore.Add("CatHome4", 3, "timeback 归档读取失败（读面不可用）: " + ex.Message, "TIMEBACK");
                return result;
            }
            int start = lines.Length - max;
            if (start < 0)
            {
                start = 0;
            }
            for (int i = start; i < lines.Length; i = i + 1)
            {
                string text = lines[i] == null ? "" : lines[i].Trim();
                if (text.Length == 0)
                {
                    continue;
                }
                try
                {
                    JsonNode node = JsonNode.Parse(text);
                    if (node as JsonObject == null)
                    {
                        continue;
                    }
                }
                catch (Exception)
                {
                    continue;
                }
                result.Add(text);
            }
            return result;
        }

        /// <summary>
        /// 追加一行到归档——开-写-关（无长驻句柄）；失败记 L3 并返回 false（失败必须可见，不阻断回卷）。
        /// </summary>
        /// <param name="line">单行 JSON</param>
        /// <returns>true=已落盘</returns>
        private bool AppendLine(string line)
        {
            try
            {
                string dir = Path.GetDirectoryName(_path);
                if (dir != null && dir.Length > 0 && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                using (FileStream fs = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                {
                    using (StreamWriter sw = new StreamWriter(fs, Utf8NoBom))
                    {
                        sw.Write(line);
                        sw.Write('\n');
                    }
                }
                _lastWriteFailed = false;
                return true;
            }
            catch (Exception ex)
            {
                _lastWriteFailed = true;
                LogStore.Add("CatHome4", 3, "timeback 归档写入失败（本次未落档）: " + ex.Message, "TIMEBACK");
                return false;
            }
        }

        /// <summary>
        /// 序列化选项——中文直出。
        /// </summary>
        /// <returns>选项实例</returns>
        private static JsonSerializerOptions BuildOptions()
        {
            JsonSerializerOptions options = new JsonSerializerOptions();
            options.Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
            return options;
        }
    }
}

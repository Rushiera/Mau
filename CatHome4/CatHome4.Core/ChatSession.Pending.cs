using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Mau.Runtime;

namespace CH4
{
    /// <summary>
    /// 宿主会话实体——待处理消息队列落盘分部（A232 · 2026-10-12）。
    /// 语义：**入队即物化**（PostUserMessage 受理后落盘）· **消费即移除**（TakeAllPendingByTime 取出后重写）·
    ///   **构造时加载**（跨宿主重启保留——原 A72「停机态丢弃在途消息」对人工队列已退役；sys 队列仍丢弃）。
    /// 落盘面：Data/runtime/pending_&lt;catKey&gt;.json（与 delays.json 同区；每猫一文件——无跨猫读改写竞争）。
    /// 空目录（未 Configure）= 仅内存（测试 / 裸部署兜底）；异常不抛，记 ERR/WARN 出声（失败必须可见）。
    /// </summary>
    internal sealed partial class ChatSession
    {
        /// <summary>pending 落盘目录——宿主启动期注入（空=仅内存）。</summary>
        private static string _pendingStoreDir = "";

        /// <summary>
        /// 配置 pending 落盘目录——宿主启动期注入（须先于会话创建；Core 不依赖配置面，路径由入口壳给）。
        /// </summary>
        /// <param name="dir">Data/runtime 目录绝对路径（空 = 关闭落盘）</param>
        public static void ConfigurePendingStore(string dir)
        {
            if (dir == null)
            {
                _pendingStoreDir = "";
            }
            else
            {
                _pendingStoreDir = dir;
            }
        }

        /// <summary>
        /// 本会话 pending 落盘文件路径——非法文件名字符净化为 `_`（目录为空返回空串）。
        /// </summary>
        /// <returns>落盘文件绝对路径（空 = 不落盘）</returns>
        private string PendingFilePath()
        {
            if (_pendingStoreDir.Length == 0)
            {
                return "";
            }
            StringBuilder sb = new StringBuilder();
            char[] bad = Path.GetInvalidFileNameChars();
            for (int i = 0; i < _catKey.Length; i = i + 1)
            {
                bool hit = false;
                for (int j = 0; j < bad.Length; j = j + 1)
                {
                    if (_catKey[i] == bad[j])
                    {
                        hit = true;
                        break;
                    }
                }
                if (hit)
                {
                    sb.Append('_');
                }
                else
                {
                    sb.Append(_catKey[i]);
                }
            }
            return Path.Combine(_pendingStoreDir, "pending_" + sb.ToString() + ".json");
        }

        /// <summary>已加载标志——首次加载后置位（防重复入队）。</summary>
        private bool _pendingLoaded;

        /// <summary>
        /// 从磁盘加载本会话待处理消息（SetCatKey 首次设 key 时调用）——按落盘序入队，序号计数器续接（防回退重号）。
        /// 文件缺失静默跳过；损坏记 WARN 出声（按空队列处理，不抛）；重复调用零动作（_pendingLoaded 守卫）。
        /// </summary>
        private void LoadPendingFromDisk()
        {
            if (_pendingLoaded)
            {
                return;
            }
            _pendingLoaded = true;
            string path = PendingFilePath();
            if (path.Length == 0 || !File.Exists(path))
            {
                return;
            }
            try
            {
                string json = File.ReadAllText(path);
                if (json.Length == 0)
                {
                    return;
                }
                using (JsonDocument doc = JsonUtil.ParseStrict(json))
                {
                    JsonElement entriesEl;
                    if (!doc.RootElement.TryGetProperty("entries", out entriesEl) || entriesEl.ValueKind != JsonValueKind.Array)
                    {
                        return;
                    }
                    long maxSeq = -1;
                    int count = entriesEl.GetArrayLength();
                    for (int i = 0; i < count; i = i + 1)
                    {
                        JsonElement e = entriesEl[i];
                        PendingMessage msg = new PendingMessage();
                        msg.Content = PendingElemStr(e, "content");
                        msg.Source = PendingElemStr(e, "source");
                        msg.ImagesJson = PendingElemStr(e, "imagesJson");
                        msg.Origin = PendingElemStr(e, "origin");
                        msg.Seq = PendingElemLong(e, "seq");
                        if (msg.Source.Length == 0)
                        {
                            msg.Source = "user";
                        }
                        _pending.Enqueue(msg);
                        if (msg.Seq > maxSeq)
                        {
                            maxSeq = msg.Seq;
                        }
                    }
                    if (maxSeq >= _pendingSeq)
                    {
                        _pendingSeq = maxSeq + 1;
                    }
                }
                LogStore.Add("CatHome4", 1, "待处理消息恢复: cat=" + _catKey + " | 载入 " + _pending.Count.ToString() + " 条", "PENDING");
            }
            catch (Exception ex)
            {
                LogStore.Add("CatHome4", 2, "待处理消息恢复失败（按空队列处理）: cat=" + _catKey + " | " + ex.Message, "PENDING");
            }
        }

        /// <summary>
        /// 待处理消息落盘（入队 / 消费后调用）——原子写（临时文件 + 覆盖移动）。
        /// 失败不抛（记 ERR 出声）；目录为空零动作。
        /// </summary>
        private void SavePendingToDisk()
        {
            string path = PendingFilePath();
            if (path.Length == 0)
            {
                return;
            }
            try
            {
                List<PendingMessage> list = new List<PendingMessage>(_pending);
                list.Sort(ComparePendingBySeq);
                List<string> items = new List<string>();
                for (int i = 0; i < list.Count; i = i + 1)
                {
                    items.Add(JsonUtil.Object(
                        ("content", list[i].Content),
                        ("source", list[i].Source),
                        ("imagesJson", list[i].ImagesJson),
                        ("origin", list[i].Origin),
                        ("seq", list[i].Seq)));
                }
                string json = JsonUtil.Object(("entries", JsonUtil.RawArray(items.ToArray())));
                Directory.CreateDirectory(_pendingStoreDir);
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, json);
                File.Move(tmp, path, true);
            }
            catch (Exception ex)
            {
                LogStore.Add("CatHome4", 3, "待处理消息落盘失败: cat=" + _catKey + " | " + ex.Message, "PENDING");
            }
        }

        /// <summary>JSON 对象取字符串字段（缺失 / 非字符串返回空串）。</summary>
        /// <param name="e">对象元素</param>
        /// <param name="name">字段名</param>
        /// <returns>字段值（无则空串）</returns>
        private static string PendingElemStr(JsonElement e, string name)
        {
            if (e.ValueKind != JsonValueKind.Object)
            {
                return "";
            }
            JsonElement v;
            if (!e.TryGetProperty(name, out v) || v.ValueKind != JsonValueKind.String)
            {
                return "";
            }
            string s = v.GetString();
            if (s == null)
            {
                return "";
            }
            return s;
        }

        /// <summary>JSON 对象取长整数字段（缺失 / 非数字返回 0）。</summary>
        /// <param name="e">对象元素</param>
        /// <param name="name">字段名</param>
        /// <returns>字段值（无则 0）</returns>
        private static long PendingElemLong(JsonElement e, string name)
        {
            if (e.ValueKind != JsonValueKind.Object)
            {
                return 0;
            }
            JsonElement v;
            if (!e.TryGetProperty(name, out v) || v.ValueKind != JsonValueKind.Number)
            {
                return 0;
            }
            long n;
            if (!v.TryGetInt64(out n))
            {
                return 0;
            }
            return n;
        }
    }
}

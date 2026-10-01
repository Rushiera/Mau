using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using CatHome4.Contracts;
using Mau.Runtime;

namespace CatHome4.Admin
{
    /// <summary>
    /// 待识别命令表分部——前端 PowerShell 解读器（chat-cmd.js）覆盖率采集面。
    /// 语义：前端把 CMD_RULES 未命中的命令段按 token 归并上报，本表聚合计数（token / 次数 / 首末时间 / 样本），
    /// 供定期查看后补充解析规则；核销 = clear（全清）。
    /// 落盘：Data/cmd-unknown.json（功能自持独立文件——与 qq-forward.json 平级，不介入配置面）。
    /// 上限：CmdUnknownMaxItems 条——超出按次数降序保留头部 + L2 留痕（不静默丢）。
    /// </summary>
    internal static partial class AdminService
    {
        /// <summary>表条目上限——超出按次数降序裁剪</summary>
        internal const int CmdUnknownMaxItems = 500;

        /// <summary>单词条样本上限——每 token 至多保留 3 条命令样本</summary>
        internal const int CmdUnknownMaxSamples = 3;

        /// <summary>样本截断长度——单条命令原文保留前 N 字</summary>
        internal const int CmdUnknownSampleMax = 200;

        /// <summary>
        /// 待识别命令表路径——Data/cmd-unknown.json。
        /// </summary>
        /// <returns>绝对路径</returns>
        private static string CmdUnknownPath()
        {
            return Path.Combine(_dataRoot, "Data", "cmd-unknown.json");
        }

        /// <summary>
        /// 读表——防御式（缺失 / 损坏 = 空表；不抛异常、不阻断）。
        /// </summary>
        /// <returns>条目列表（文件序）</returns>
        internal static List<Dictionary<string, object>> LoadCmdUnknown()
        {
            List<Dictionary<string, object>> items = new List<Dictionary<string, object>>();
            string path = CmdUnknownPath();
            if (!File.Exists(path))
            {
                return items;
            }
            try
            {
                using (JsonDocument doc = JsonUtil.ParseStrict(File.ReadAllText(path)))
                {
                    JsonElement itemsEl;
                    if (!doc.RootElement.TryGetProperty("items", out itemsEl) || itemsEl.ValueKind != JsonValueKind.Array)
                    {
                        return items;
                    }
                    foreach (JsonElement item in itemsEl.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.Object)
                        {
                            continue;
                        }
                        Dictionary<string, object> row = new Dictionary<string, object>();
                        row["token"] = GetJsonString(item, "token");
                        row["raw"] = GetJsonString(item, "raw");
                        long countVal = 0;
                        JsonElement countEl;
                        if (item.TryGetProperty("count", out countEl) && countEl.ValueKind == JsonValueKind.Number)
                        {
                            long cv;
                            if (countEl.TryGetInt64(out cv))
                            {
                                countVal = cv;
                            }
                        }
                        row["count"] = countVal;
                        row["firstSeen"] = GetJsonString(item, "firstSeen");
                        row["lastSeen"] = GetJsonString(item, "lastSeen");
                        List<string> samples = new List<string>();
                        JsonElement samplesEl;
                        if (item.TryGetProperty("samples", out samplesEl) && samplesEl.ValueKind == JsonValueKind.Array)
                        {
                            foreach (JsonElement s in samplesEl.EnumerateArray())
                            {
                                if (s.ValueKind == JsonValueKind.String)
                                {
                                    string sv = s.GetString();
                                    if (sv != null && sv.Length > 0)
                                    {
                                        samples.Add(sv);
                                    }
                                }
                            }
                        }
                        row["samples"] = samples;
                        items.Add(row);
                    }
                }
            }
            catch (Exception ex)
            {
                LogStore.Add("CatHome4", 2, "待识别命令表读取失败（按空表处理）：" + ex.Message, "CONFIG");
            }
            return items;
        }

        /// <summary>
        /// 合并上报项——按 token（小写归一）聚合：命中则次数累加 + 刷新末见时间 + 补样本；未命中则新增。
        /// </summary>
        /// <param name="items">表（就地修改）</param>
        /// <param name="reported">上报项（每项含 token / sample）</param>
        /// <param name="now">时间串（yyyy-MM-dd HH:mm:ss）</param>
        /// <returns>新增条数</returns>
        internal static int MergeCmdUnknown(List<Dictionary<string, object>> items, List<Dictionary<string, string>> reported, string now)
        {
            int added = 0;
            for (int i = 0; i < reported.Count; i = i + 1)
            {
                string token = GetReportField(reported[i], "token");
                if (token.Length == 0)
                {
                    continue;
                }
                string key = token.ToLowerInvariant();
                string raw = GetReportField(reported[i], "raw");
                if (raw.Length == 0)
                {
                    raw = token;
                }
                string sample = GetReportField(reported[i], "sample");
                if (sample.Length > CmdUnknownSampleMax)
                {
                    sample = sample.Substring(0, CmdUnknownSampleMax);
                }
                Dictionary<string, object> row = null;
                for (int k = 0; k < items.Count; k = k + 1)
                {
                    if (string.Equals(GetRowText(items[k], "token"), key, StringComparison.Ordinal))
                    {
                        row = items[k];
                        break;
                    }
                }
                if (row == null)
                {
                    row = new Dictionary<string, object>();
                    row["token"] = key;
                    row["raw"] = raw;
                    row["count"] = 1L;
                    row["firstSeen"] = now;
                    row["lastSeen"] = now;
                    row["samples"] = new List<string>();
                    items.Add(row);
                    added = added + 1;
                }
                else
                {
                    row["count"] = GetRowCount(row) + 1L;
                    row["lastSeen"] = now;
                }
                AddCmdUnknownSample(row, sample);
            }
            return added;
        }

        /// <summary>
        /// 表裁剪——超上限时按次数降序（同次数按首见时间升序）保留头部，其余移除。
        /// </summary>
        /// <param name="items">表（就地修改）</param>
        /// <returns>被丢弃条数</returns>
        internal static int TrimCmdUnknown(List<Dictionary<string, object>> items)
        {
            if (items.Count <= CmdUnknownMaxItems)
            {
                return 0;
            }
            items.Sort(CompareCmdUnknown);
            int dropped = items.Count - CmdUnknownMaxItems;
            items.RemoveRange(CmdUnknownMaxItems, dropped);
            return dropped;
        }

        /// <summary>
        /// 排序比较——次数降序；同次数按首见时间升序（保证裁剪结果稳定可复现）。
        /// </summary>
        private static int CompareCmdUnknown(Dictionary<string, object> a, Dictionary<string, object> b)
        {
            long ca = GetRowCount(a);
            long cb = GetRowCount(b);
            if (ca != cb)
            {
                if (ca > cb)
                {
                    return -1;
                }
                return 1;
            }
            return string.CompareOrdinal(GetRowText(a, "firstSeen"), GetRowText(b, "firstSeen"));
        }

        /// <summary>
        /// 写表——原子写（临时文件 + 改名）；失败 L2 留痕（不静默吞）。
        /// </summary>
        /// <param name="items">条目列表</param>
        internal static void SaveCmdUnknown(List<Dictionary<string, object>> items)
        {
            Dictionary<string, object> root = new Dictionary<string, object>();
            root["version"] = 1;
            root["items"] = items;
            string json = JsonUtil.Serialize(root);
            string path = CmdUnknownPath();
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (dir != null && dir.Length > 0)
                {
                    Directory.CreateDirectory(dir);
                }
                ConfigStore.AtomicWrite(path, json);
            }
            catch (Exception ex)
            {
                LogStore.Add("CatHome4", 2, "待识别命令表写入失败：" + ex.Message, "CONFIG");
            }
        }

        /// <summary>
        /// 表读取——GET /api/v1/cmd-unknown（前端 / curl 查看；items 按次数降序返回）。
        /// </summary>
        /// <returns>表 JSON</returns>
        internal static IResult HandleCmdUnknownGet()
        {
            List<Dictionary<string, object>> items = LoadCmdUnknown();
            items.Sort(CompareCmdUnknown);
            return Results.Json(new { ok = true, version = 1, total = items.Count, items = items });
        }

        /// <summary>
        /// 批量登记——POST /api/v1/cmd-unknown（body: {items:[{token,sample}]}）。
        /// </summary>
        /// <param name="ctx">HTTP 上下文</param>
        /// <returns>回执 JSON（总数 / 新增 / 合并 / 裁剪）</returns>
        internal static async Task<IResult> HandleCmdUnknownPost(HttpContext ctx)
        {
            string body = await ReadBodyText(ctx);
            List<Dictionary<string, string>> reported = new List<Dictionary<string, string>>();
            try
            {
                using (JsonDocument doc = JsonUtil.ParseStrict(body))
                {
                    JsonElement itemsEl;
                    if (doc.RootElement.TryGetProperty("items", out itemsEl) && itemsEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement item in itemsEl.EnumerateArray())
                        {
                            if (item.ValueKind != JsonValueKind.Object)
                            {
                                continue;
                            }
                            Dictionary<string, string> one = new Dictionary<string, string>();
                            one["token"] = GetJsonString(item, "token").Trim();
                            one["raw"] = GetJsonString(item, "raw").Trim();
                            one["sample"] = GetJsonString(item, "sample").Trim();
                            if (one["token"].Length > 0)
                            {
                                reported.Add(one);
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                return Results.Json(new { ok = false, error = "body 非 JSON" });
            }
            if (reported.Count == 0)
            {
                return Results.Json(new { ok = false, error = "items 为空（每项需 token）" });
            }
            List<Dictionary<string, object>> items = LoadCmdUnknown();
            string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            int added = MergeCmdUnknown(items, reported, now);
            int dropped = TrimCmdUnknown(items);
            if (dropped > 0)
            {
                LogStore.Add("CatHome4", 2, "待识别命令表超上限（" + CmdUnknownMaxItems.ToString() + " 条），裁剪丢弃 " + dropped.ToString() + " 条（按次数降序保留头部）", "CONFIG");
            }
            SaveCmdUnknown(items);
            return Results.Json(new { ok = true, total = items.Count, added = added, merged = reported.Count - added, dropped = dropped });
        }

        /// <summary>
        /// 整表清空——POST /api/v1/cmd-unknown/clear（补完规则后核销）。
        /// </summary>
        /// <returns>回执 JSON</returns>
        internal static IResult HandleCmdUnknownClear()
        {
            SaveCmdUnknown(new List<Dictionary<string, object>>());
            LogStore.Add("CatHome4", 1, "待识别命令表已清空（核销）", "CONFIG");
            return Results.Json(new { ok = true, total = 0 });
        }
        /// <summary>
        /// 采集面路由注册——对话页（chat.html）专用：管理端点默认只注册在主端口，而上报源头在每猫 / majordomo 对话页。
        /// 语义：只含采集端点（不含管理 CRUD），故可安全下发到每猫 host（RouteRegistrar 槽）。
        /// </summary>
        /// <param name="sink">HTTP 路由注册面</param>
        internal static void RegisterCmdUnknownRoutes(IHttpRouteSink sink)
        {
            sink.MapPost("/api/v1/cmd-unknown", (Delegate)HandleCmdUnknownPost);
        }

        // ═══════════════════════════════════════════
        // 条目取值小件
        // ═══════════════════════════════════════════

        /// <summary>取上报字段——缺失 / 空 → 空串（Trim）。</summary>
        private static string GetReportField(Dictionary<string, string> item, string key)
        {
            string v;
            if (item == null || !item.TryGetValue(key, out v) || v == null)
            {
                return "";
            }
            return v.Trim();
        }

        /// <summary>取条目字符串字段——缺失 / 非字符串 → 空串。</summary>
        private static string GetRowText(Dictionary<string, object> row, string key)
        {
            object v;
            if (!row.TryGetValue(key, out v) || v == null)
            {
                return "";
            }
            string s = v as string;
            if (s == null)
            {
                return "";
            }
            return s;
        }

        /// <summary>取条目次数——缺失 / 非数值 → 0。</summary>
        private static long GetRowCount(Dictionary<string, object> row)
        {
            object v;
            if (!row.TryGetValue("count", out v) || v == null)
            {
                return 0;
            }
            if (v is long)
            {
                return (long)v;
            }
            if (v is int)
            {
                return (long)((int)v);
            }
            return 0;
        }

        /// <summary>补样本——去重 + 上限（样本用于后续补规则时看真实用法）。</summary>
        private static void AddCmdUnknownSample(Dictionary<string, object> row, string sample)
        {
            if (sample == null || sample.Length == 0)
            {
                return;
            }
            List<string> samples = row["samples"] as List<string>;
            if (samples == null)
            {
                samples = new List<string>();
                row["samples"] = samples;
            }
            for (int i = 0; i < samples.Count; i = i + 1)
            {
                if (string.Equals(samples[i], sample, StringComparison.Ordinal))
                {
                    return;
                }
            }
            if (samples.Count >= CmdUnknownMaxSamples)
            {
                return;
            }
            samples.Add(sample);
        }
    }
}

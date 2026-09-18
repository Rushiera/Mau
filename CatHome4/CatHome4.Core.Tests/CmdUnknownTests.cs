using System;
using System.Collections.Generic;
using System.IO;
using CatHome4.Admin;
using Xunit;

namespace CatHome4.Core.Tests
{
    /// <summary>
    /// 待识别命令表测试（Admin 域覆盖率采集聚合面）——合并计数 / 样本去重与截断 / 超限裁剪 / 落盘往返。
    /// </summary>
    public class CmdUnknownTests
    {
        /// <summary>
        /// 合并——新 token 建条目（count=1 + 首末时间）；同 token 再报（大小写不同）累加次数并刷新末见时间。
        /// </summary>
        [Fact]
        public void Merge_NewThenExisting_AccumulatesCount()
        {
            List<Dictionary<string, object>> items = new List<Dictionary<string, object>>();
            List<Dictionary<string, string>> first = new List<Dictionary<string, string>>();
            first.Add(Report("Get-CimInstance", "Get-CimInstance Win32_Process"));
            int added = AdminService.MergeCmdUnknown(items, first, "2026-09-18 21:00:00");
            Assert.Equal(1, added);
            Assert.Single(items);
            Assert.Equal("get-ciminstance", items[0]["token"]);
            Assert.Equal("Get-CimInstance", items[0]["raw"]);
            Assert.Equal(1L, items[0]["count"]);
            Assert.Equal("2026-09-18 21:00:00", items[0]["firstSeen"]);
            List<Dictionary<string, string>> second = new List<Dictionary<string, string>>();
            second.Add(Report("GET-CIMINSTANCE", "Get-CimInstance Win32_Service"));
            added = AdminService.MergeCmdUnknown(items, second, "2026-09-18 21:05:00");
            Assert.Equal(0, added);
            Assert.Single(items);
            Assert.Equal(2L, items[0]["count"]);
            Assert.Equal("2026-09-18 21:05:00", items[0]["lastSeen"]);
            Assert.Equal("2026-09-18 21:00:00", items[0]["firstSeen"]);
        }

        /// <summary>
        /// 样本——每 token 至多 3 条；单条超 200 字截断。
        /// </summary>
        [Fact]
        public void Merge_SampleCappedAndTruncated()
        {
            List<Dictionary<string, object>> items = new List<Dictionary<string, object>>();
            for (int i = 0; i < 5; i = i + 1)
            {
                List<Dictionary<string, string>> one = new List<Dictionary<string, string>>();
                one.Add(Report("foo-bar", "Get-Foo -Index " + i.ToString()));
                AdminService.MergeCmdUnknown(items, one, "2026-09-18 21:00:00");
            }
            List<string> samples = items[0]["samples"] as List<string>;
            Assert.NotNull(samples);
            Assert.Equal(AdminService.CmdUnknownMaxSamples, samples.Count);
            // 截断——单条超长样本按 CmdUnknownSampleMax 截断（截断后等值即去重，故单条上报验证）
            List<Dictionary<string, object>> longItems = new List<Dictionary<string, object>>();
            List<Dictionary<string, string>> longOne = new List<Dictionary<string, string>>();
            longOne.Add(Report("bar-baz", new string('x', 300)));
            AdminService.MergeCmdUnknown(longItems, longOne, "2026-09-18 21:00:00");
            List<string> longSamples = longItems[0]["samples"] as List<string>;
            Assert.NotNull(longSamples);
            Assert.Equal(AdminService.CmdUnknownSampleMax, longSamples[0].Length);
        }

        /// <summary>
        /// 裁剪——超上限按次数降序保留头部，低次数条目移除（被丢条数如实返回）。
        /// </summary>
        [Fact]
        public void Trim_OverLimit_KeepsTopByCount()
        {
            List<Dictionary<string, object>> items = new List<Dictionary<string, object>>();
            for (int i = 0; i < AdminService.CmdUnknownMaxItems + 3; i = i + 1)
            {
                Dictionary<string, object> row = new Dictionary<string, object>();
                row["token"] = "t" + i.ToString();
                row["raw"] = "t" + i.ToString();
                row["count"] = (long)(i + 1);
                row["firstSeen"] = "2026-09-18 21:00:00";
                row["lastSeen"] = "2026-09-18 21:00:00";
                row["samples"] = new List<string>();
                items.Add(row);
            }
            int dropped = AdminService.TrimCmdUnknown(items);
            Assert.Equal(3, dropped);
            Assert.Equal(AdminService.CmdUnknownMaxItems, items.Count);
            Assert.Equal("t" + (AdminService.CmdUnknownMaxItems + 2).ToString(), items[0]["token"]);
        }

        /// <summary>
        /// 落盘往返——保存后读回同形（Data 根指向临时目录，测完恢复并清理）。
        /// </summary>
        [Fact]
        public void SaveLoad_RoundTrip()
        {
            string prevRoot = AdminService._dataRoot;
            string tmp = Path.Combine(Path.GetTempPath(), "cat4cmd_" + Guid.NewGuid().ToString("N"));
            try
            {
                AdminService._dataRoot = tmp;
                List<Dictionary<string, object>> items = new List<Dictionary<string, object>>();
                List<Dictionary<string, string>> one = new List<Dictionary<string, string>>();
                one.Add(Report("get-foo", "Get-Foo -Bar"));
                AdminService.MergeCmdUnknown(items, one, "2026-09-18 21:00:00");
                AdminService.SaveCmdUnknown(items);
                List<Dictionary<string, object>> loaded = AdminService.LoadCmdUnknown();
                Assert.Single(loaded);
                Assert.Equal("get-foo", loaded[0]["token"]);
                Assert.Equal(1L, loaded[0]["count"]);
                List<string> samples = loaded[0]["samples"] as List<string>;
                Assert.NotNull(samples);
                Assert.Equal("Get-Foo -Bar", samples[0]);
            }
            finally
            {
                AdminService._dataRoot = prevRoot;
                if (Directory.Exists(tmp))
                {
                    Directory.Delete(tmp, true);
                }
            }
        }

        /// <summary>构造上报项——token + 命令样本。</summary>
        private static Dictionary<string, string> Report(string token, string sample)
        {
            Dictionary<string, string> one = new Dictionary<string, string>();
            one["token"] = token;
            one["sample"] = sample;
            return one;
        }
        /// <summary>
        /// raw 字段——上报原文优先；缺省回落上报 token（表里既能看归一 key，也能看真实写法）。
        /// </summary>
        [Fact]
        public void Merge_RawPrefersReportedRaw()
        {
            List<Dictionary<string, object>> items = new List<Dictionary<string, object>>();
            List<Dictionary<string, string>> one = new List<Dictionary<string, string>>();
            Dictionary<string, string> item = Report("get-x", "Get-X -Flag");
            item["raw"] = "Get-X";
            one.Add(item);
            AdminService.MergeCmdUnknown(items, one, "2026-09-18 21:00:00");
            Assert.Equal("Get-X", items[0]["raw"]);
            List<Dictionary<string, object>> items2 = new List<Dictionary<string, object>>();
            List<Dictionary<string, string>> two = new List<Dictionary<string, string>>();
            two.Add(Report("get-y", "Get-Y"));
            AdminService.MergeCmdUnknown(items2, two, "2026-09-18 21:00:00");
            Assert.Equal("get-y", items2[0]["raw"]);
        }
    }
}

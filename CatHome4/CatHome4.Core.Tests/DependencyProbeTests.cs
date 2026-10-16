using System;
using System.Collections.Generic;
using System.IO;
using CatHome4.Observe;
using Xunit;

namespace CatHome4.Core.Tests
{
    /// <summary>
    /// 依赖探测测试（A253）——路径探针三态 / 排序权重 / 版本行提取。
    /// 边界：只覆盖探针纯逻辑——不调 EnsureFresh（避免起子进程与静态结果表污染）。
    /// </summary>
    public class DependencyProbeTests
    {
        /// <summary>
        /// 路径全存在——可用态。
        /// </summary>
        [Fact]
        public void PathsProbe_AllPresent_Ok()
        {
            string dir = NewTempDir();
            try
            {
                string file = Path.Combine(dir, "a.txt");
                File.WriteAllText(file, "x");
                List<string> paths = new List<string>();
                paths.Add(dir);
                paths.Add(file);
                DependencyProbe.ProbeResult result = DependencyProbe.ProbePathsForTest(DependencyProbe.LevelRequired, "测试项", paths);
                Assert.Equal(DependencyProbe.StateOk, result.State);
                Assert.Equal(DependencyProbe.LevelRequired, result.Level);
                Assert.Equal("测试项", result.Name);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        /// <summary>
        /// 缺一项——缺失态且 Detail 列出缺失路径（缺失面可行动）。
        /// </summary>
        [Fact]
        public void PathsProbe_MissingOne_ReportsMissingPath()
        {
            string dir = NewTempDir();
            try
            {
                string missing = Path.Combine(dir, "nope.exe");
                List<string> paths = new List<string>();
                paths.Add(dir);
                paths.Add(missing);
                DependencyProbe.ProbeResult result = DependencyProbe.ProbePathsForTest(DependencyProbe.LevelRequired, "测试项", paths);
                Assert.Equal(DependencyProbe.StateMissing, result.State);
                Assert.Contains("nope.exe", result.Detail);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        /// <summary>
        /// 空路径条目——计为缺失（不静默跳过）。
        /// </summary>
        [Fact]
        public void PathsProbe_EmptyEntry_CountsMissing()
        {
            List<string> paths = new List<string>();
            paths.Add("");
            DependencyProbe.ProbeResult result = DependencyProbe.ProbePathsForTest(DependencyProbe.LevelRequired, "测试项", paths);
            Assert.Equal(DependencyProbe.StateMissing, result.State);
        }

        /// <summary>
        /// 排序——级别序（required → optional → dev）优先，同级按名称序。
        /// </summary>
        [Fact]
        public void CompareResults_LevelOrderThenName()
        {
            DependencyProbe.ProbeResult requiredZ = NewResult(DependencyProbe.LevelRequired, "Z");
            DependencyProbe.ProbeResult requiredA = NewResult(DependencyProbe.LevelRequired, "A");
            DependencyProbe.ProbeResult optional = NewResult(DependencyProbe.LevelOptional, "A");
            DependencyProbe.ProbeResult dev = NewResult(DependencyProbe.LevelDev, "A");
            Assert.True(DependencyProbe.CompareResults(requiredA, requiredZ) < 0);
            Assert.True(DependencyProbe.CompareResults(requiredZ, optional) < 0);
            Assert.True(DependencyProbe.CompareResults(optional, dev) < 0);
        }

        /// <summary>
        /// 级别权重——required=0 / optional=1 / 其它（含未知）=2。
        /// </summary>
        [Fact]
        public void LevelOrder_Unknown_Last()
        {
            Assert.Equal(0, DependencyProbe.LevelOrder(DependencyProbe.LevelRequired));
            Assert.Equal(1, DependencyProbe.LevelOrder(DependencyProbe.LevelOptional));
            Assert.Equal(2, DependencyProbe.LevelOrder(DependencyProbe.LevelDev));
            Assert.Equal(2, DependencyProbe.LevelOrder("whatever"));
        }

        /// <summary>
        /// 版本行提取——多行取首行并去空白。
        /// </summary>
        [Fact]
        public void FirstLine_MultiLine_TakesFirst()
        {
            Assert.Equal("v1.2.3", DependencyProbe.FirstLine("  v1.2.3  \r\nsecond line\r\n"));
        }

        /// <summary>
        /// 版本行提取——空文本返回空串（不产生假版本）。
        /// </summary>
        [Fact]
        public void FirstLine_Empty_ReturnsEmpty()
        {
            Assert.Equal("", DependencyProbe.FirstLine(""));
            Assert.Equal("", DependencyProbe.FirstLine(null));
        }

        /// <summary>
        /// 文本净化判定——替换字符与孤立代理项视为解码失败；合法代理对放行。
        /// </summary>
        [Fact]
        public void IsTextClean_DetectsDecodeGarbage()
        {
            Assert.True(DependencyProbe.IsTextClean("Microsoft Edge 130.0.1.2"));
            Assert.True(DependencyProbe.IsTextClean("emoji \uD83D\uDE00 ok"));
            Assert.False(DependencyProbe.IsTextClean("garbled\uFFFDtext"));
            Assert.False(DependencyProbe.IsTextClean("\uDABE"));
            Assert.False(DependencyProbe.IsTextClean("\uDEA1"));
        }

        /// <summary>
        /// 新建临时目录——用例自持，结束即清。
        /// </summary>
        /// <returns>目录全路径</returns>
        private static string NewTempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "ch4-deps-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        /// <summary>
        /// 构造结果条目——排序用例输入。
        /// </summary>
        /// <param name="level">级别</param>
        /// <param name="name">名称</param>
        /// <returns>结果条目</returns>
        private static DependencyProbe.ProbeResult NewResult(string level, string name)
        {
            DependencyProbe.ProbeResult result = new DependencyProbe.ProbeResult();
            result.Level = level;
            result.Name = name;
            return result;
        }
    }
}

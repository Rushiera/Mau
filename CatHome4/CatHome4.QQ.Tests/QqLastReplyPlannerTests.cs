using System.Collections.Generic;
using CatHome4.QQ;
using Xunit;

namespace CatHome4.QQ.Tests
{
    /// <summary>
    /// QqLastReplyPlanner 测试（A112）——/last 发送计划：末尾优先取段 + 文本段与文件发送共用被动调用预算。
    /// 覆盖：短正文 + 文件 / 纯标记（正文剥离后为空）/ 无文件 / 段数超限取末尾 /
    /// 段与文件共用预算（混合分配）/ 段用满预算（文件全丢）/ 空输入。
    /// </summary>
    public sealed class QqLastReplyPlannerTests
    {
        /// <summary>构造文件列表</summary>
        private static List<string> Files(params string[] paths)
        {
            List<string> files = new List<string>();
            for (int i = 0; i < paths.Length; i = i + 1)
            {
                files.Add(paths[i]);
            }
            return files;
        }

        /// <summary>五行正文（每行恰好等于 limit——切分后每行独立成段）</summary>
        private static string FiveLines()
        {
            return "AAAAA\nBBBBB\nCCCCC\nDDDDD\nEEEEE";
        }

        /// <summary>短正文 + 单文件——一段 + 一文件，各占一次预算，无丢弃。</summary>
        [Fact]
        public void ShortBodyAndFile_BothPlanned()
        {
            QqLastReplyPlan plan = QqLastReplyPlanner.Plan("正文", Files("C:\\a.png"), 1800, 4);
            Assert.Single(plan.Segments);
            Assert.Equal("正文", plan.Segments[0]);
            Assert.Single(plan.Files);
            Assert.Equal("C:\\a.png", plan.Files[0]);
            Assert.Equal(0, plan.DroppedHeadSegments);
            Assert.Equal(0, plan.DroppedFiles);
        }

        /// <summary>纯标记（正文剥离后为空）——无文本段（不发空消息），文件照发。</summary>
        [Fact]
        public void EmptyBody_FileOnly_NoEmptySegment()
        {
            QqLastReplyPlan plan = QqLastReplyPlanner.Plan("", Files("C:\\a.png"), 1800, 4);
            Assert.Empty(plan.Segments);
            Assert.Single(plan.Files);
            Assert.Equal(0, plan.DroppedFiles);
        }

        /// <summary>无文件——只出文本段（files 空列表 / null 同效）。</summary>
        [Fact]
        public void NoFiles_SegmentsOnly()
        {
            QqLastReplyPlan plan = QqLastReplyPlanner.Plan("正文", null, 1800, 4);
            Assert.Single(plan.Segments);
            Assert.Empty(plan.Files);
            QqLastReplyPlan plan2 = QqLastReplyPlanner.Plan("正文", Files(), 1800, 4);
            Assert.Single(plan2.Segments);
            Assert.Empty(plan2.Files);
        }

        /// <summary>段数超上限——取末尾段（补感知语义：尾部最新），开头段计入丢弃。</summary>
        [Fact]
        public void TooManySegments_TakesTail()
        {
            QqLastReplyPlan plan = QqLastReplyPlanner.Plan(FiveLines(), null, 5, 4);
            Assert.Equal(4, plan.Segments.Count);
            Assert.Equal("BBBBB", plan.Segments[0]);
            Assert.Equal("EEEEE", plan.Segments[3]);
            Assert.Equal(1, plan.DroppedHeadSegments);
        }

        /// <summary>段与文件共用预算——三段正文占 3 次，文件只余 1 次额度，超出丢弃并计数。</summary>
        [Fact]
        public void SegmentsAndFiles_ShareBudget()
        {
            string body = "AAAAA\nBBBBB\nCCCCC";
            QqLastReplyPlan plan = QqLastReplyPlanner.Plan(body, Files("C:\\1.png", "C:\\2.png", "C:\\3.png"), 5, 4);
            Assert.Equal(3, plan.Segments.Count);
            Assert.Single(plan.Files);
            Assert.Equal("C:\\1.png", plan.Files[0]);
            Assert.Equal(2, plan.DroppedFiles);
        }

        /// <summary>段用满预算——文件全丢并计数（失败可见：调用方据此 L2 留痕）。</summary>
        [Fact]
        public void SegmentsFillBudget_AllFilesDropped()
        {
            QqLastReplyPlan plan = QqLastReplyPlanner.Plan(FiveLines(), Files("C:\\1.png"), 5, 4);
            Assert.Equal(4, plan.Segments.Count);
            Assert.Empty(plan.Files);
            Assert.Equal(1, plan.DroppedFiles);
        }

        /// <summary>空输入——无段无文件（空 Trim 正文 / 空文件列表）。</summary>
        [Fact]
        public void EmptyInput_EmptyPlan()
        {
            QqLastReplyPlan plan = QqLastReplyPlanner.Plan(null, null, 1800, 4);
            Assert.Empty(plan.Segments);
            Assert.Empty(plan.Files);
            Assert.Equal(0, plan.DroppedHeadSegments);
            Assert.Equal(0, plan.DroppedFiles);
            QqLastReplyPlan plan2 = QqLastReplyPlanner.Plan("   ", null, 1800, 4);
            Assert.Empty(plan2.Segments);
        }
    }
}

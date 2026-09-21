using CatHome4.Contracts;
using Xunit;

namespace CatHome4.Core.Tests
{
    /// <summary>
    /// 端口段测试（A74）——机器级端口空间按区划分：开发区向下（8079 起）/ 部署区向上（8080 起）。
    /// 核心不变式：两区端口集合互不相交（主端口 + 端口族区间 + 前端测试端口三面）。
    /// </summary>
    public class PortBandTests
    {
        /// <summary>
        /// 开发区段位——主端口 8079、端口族 8078 向下至 8070、前端测试 8069、步进 -1。
        /// </summary>
        [Fact]
        public void Resolve_DevArea_DescendsBelowMainPort()
        {
            PortBand band = PortBand.Resolve(true);
            Assert.Equal("dev", band.Area);
            Assert.Equal(8079, band.MainPort);
            Assert.Equal(8078, band.FamilyFrom);
            Assert.Equal(8070, band.FamilyTo);
            Assert.Equal(8069, band.FrontendTestPort);
            Assert.Equal(-1, band.FamilyStep());
        }

        /// <summary>
        /// 部署区段位——主端口 8080、端口族 8081 向上至 8180、前端测试 8099、步进 +1。
        /// </summary>
        [Fact]
        public void Resolve_DeployArea_AscendsFromMainPort()
        {
            PortBand band = PortBand.Resolve(false);
            Assert.Equal("deploy", band.Area);
            Assert.Equal(8080, band.MainPort);
            Assert.Equal(8081, band.FamilyFrom);
            Assert.Equal(8180, band.FamilyTo);
            Assert.Equal(8099, band.FrontendTestPort);
            Assert.Equal(1, band.FamilyStep());
        }

        /// <summary>
        /// 分区不变式——两区端口集合不相交（两区实例可同时运行的前提）。
        /// </summary>
        [Fact]
        public void Resolve_TwoAreas_PortSetsAreDisjoint()
        {
            PortBand dev = PortBand.Resolve(true);
            PortBand deploy = PortBand.Resolve(false);
            Assert.False(Overlaps(dev, deploy));
        }

        /// <summary>
        /// 端口集合重叠判定——主端口 / 前端测试端口 / 端口族区间三面逐一对照（任一面命中即重叠）。
        /// </summary>
        /// <param name="a">区 A</param>
        /// <param name="b">区 B</param>
        /// <returns>true=有重叠</returns>
        private static bool Overlaps(PortBand a, PortBand b)
        {
            if (a.MainPort == b.MainPort || a.FrontendTestPort == b.FrontendTestPort)
            {
                return true;
            }
            if (InRange(a.MainPort, b.FamilyFrom, b.FamilyTo) || InRange(b.MainPort, a.FamilyFrom, a.FamilyTo))
            {
                return true;
            }
            if (InRange(a.FrontendTestPort, b.FamilyFrom, b.FamilyTo) || InRange(b.FrontendTestPort, a.FamilyFrom, a.FamilyTo))
            {
                return true;
            }
            return RangesOverlap(a.FamilyFrom, a.FamilyTo, b.FamilyFrom, b.FamilyTo);
        }

        /// <summary>
        /// 区间重叠判定——按各自上下界归一后比较（段方向无关）。
        /// </summary>
        /// <param name="aFrom">区间 A 起点</param>
        /// <param name="aTo">区间 A 终点</param>
        /// <param name="bFrom">区间 B 起点</param>
        /// <param name="bTo">区间 B 终点</param>
        /// <returns>true=重叠</returns>
        private static bool RangesOverlap(int aFrom, int aTo, int bFrom, int bTo)
        {
            int aLow = aFrom;
            int aHigh = aTo;
            if (aFrom > aTo)
            {
                aLow = aTo;
                aHigh = aFrom;
            }
            int bLow = bFrom;
            int bHigh = bTo;
            if (bFrom > bTo)
            {
                bLow = bTo;
                bHigh = bFrom;
            }
            return aLow <= bHigh && bLow <= aHigh;
        }

        /// <summary>
        /// 端口落在区间内判定（段方向无关）。
        /// </summary>
        /// <param name="port">端口</param>
        /// <param name="from">区间起点</param>
        /// <param name="to">区间终点</param>
        /// <returns>true=在区间内</returns>
        private static bool InRange(int port, int from, int to)
        {
            int low = from;
            int high = to;
            if (from > to)
            {
                low = to;
                high = from;
            }
            return port >= low && port <= high;
        }
    }
}

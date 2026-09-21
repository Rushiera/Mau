namespace CatHome4.Contracts
{
    /// <summary>
    /// 端口段——机器级端口空间按区划分（开发区向下 / 部署区向上）。
    /// 区域判定与数据根解析同源（exe 是否位于仓库树内——入口壳判定后经 Resolve 取段），单一出口供三处消费：
    /// 主端口（HTTP 外观层）· 端口族（majordomo 独立端口 + 每猫端口）· 前端测试服务端口（node server.js）。
    /// 两区端口集合互不相交——开发区实例与部署区实例可同时运行。
    /// </summary>
    public sealed class PortBand
    {
        /// <summary>区标识（dev=开发区 / deploy=部署区）——观测与日志</summary>
        public string Area { get; private set; }

        /// <summary>主端口——HTTP 外观层（管理面板；显式 app.cfg:http.port 优先于本值）</summary>
        public int MainPort { get; private set; }

        /// <summary>端口族起点——majordomo 独立端口与每猫端口由此起扫</summary>
        public int FamilyFrom { get; private set; }

        /// <summary>端口族终点（含）——扫描边界，全占即分配失败，不越界到对方区段</summary>
        public int FamilyTo { get; private set; }

        /// <summary>前端测试服务端口——html/tests/server.js（FE_TEST_PORT 注入）</summary>
        public int FrontendTestPort { get; private set; }

        /// <summary>
        /// 构造端口段——仅供 Resolve 使用（段位是既定设计值，不接受外部拼装）。
        /// </summary>
        /// <param name="area">区标识</param>
        /// <param name="mainPort">主端口</param>
        /// <param name="familyFrom">端口族起点</param>
        /// <param name="familyTo">端口族终点（含）</param>
        /// <param name="frontendTestPort">前端测试服务端口</param>
        private PortBand(string area, int mainPort, int familyFrom, int familyTo, int frontendTestPort)
        {
            Area = area;
            MainPort = mainPort;
            FamilyFrom = familyFrom;
            FamilyTo = familyTo;
            FrontendTestPort = frontendTestPort;
        }

        /// <summary>
        /// 解析端口段——区域判定输入由入口壳提供（devArea = exe 位于仓库树内，与数据根解析同一出口）。
        /// 开发区：主端口 8079、端口族 8078 向下至 8070、前端测试 8069；
        /// 部署区：主端口 8080、端口族 8081 向上至 8180、前端测试 8099。
        /// 两区端口集合互不相交（前端测试端口不落在对方端口族区间内）——由 PortBandTests 守住该不变式。
        /// </summary>
        /// <param name="devArea">是否开发区实例</param>
        /// <returns>端口段</returns>
        public static PortBand Resolve(bool devArea)
        {
            if (devArea)
            {
                return new PortBand("dev", 8079, 8078, 8070, 8069);
            }
            return new PortBand("deploy", 8080, 8081, 8180, 8099);
        }

        /// <summary>
        /// 端口族扫描步进——按段方向（起点小于终点=向上 +1；起点大于终点=向下 -1）。
        /// </summary>
        /// <returns>步进值</returns>
        public int FamilyStep()
        {
            if (FamilyFrom <= FamilyTo)
            {
                return 1;
            }
            return -1;
        }
    }
}

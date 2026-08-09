namespace Mau.Contracts
{
    /// <summary>
    /// 猫扫描服务——宿主实现（Data/Cats 目录扫描 + 档案解析 + 损坏判定）。
    /// 积木经 DataBox.Bind 获取——机制积木宿主桥模式（漏 Bind → TryResolve 静默 false）。
    /// 来源：CH4 多猫框架（2026-08-09）——Data/Cats 目录即猫清单，初始化扫描 = BRIK 机制。
    /// </summary>
    public interface ICatScanner
    {
        /// <summary>
        /// 扫描猫实例清单——Data/Cats 目录即猫清单；每目录解析 config.cfg → 档案。
        /// 损坏判定：config.cfg 缺失/解析失败/字段非法 → damaged=true（默认域 system+file 兜底）。
        /// </summary>
        /// <returns>猫档案 JSON（CatSpec 数组：name/tools/hasContext/damaged）</returns>
        string ScanInstances();
    }
}

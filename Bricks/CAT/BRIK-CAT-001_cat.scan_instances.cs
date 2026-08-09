// ═══════════════════════════════════════════════════
// 积木: cat.scan_instances
// ID:   BRIK-CAT-001
// 类别: CAT
// 作用: 猫实例清单扫描——Data/Cats 目录即猫清单（宿主 ICatScanner 服务）
// 依赖: 无
// 引用: Mau.Contracts · Mau.Runtime
// 原理: DataBox.TryResolve<ICatScanner> → ScanInstances() → 猫档案 JSON（含损坏标记）
// 常用: 宿主初始化——按清单逐猫 GenCat（与新建猫同一套生成流程）
// 包: 无
// ═══════════════════════════════════════════════════
using Mau.Contracts;
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 猫积木——cat.scan_instances 猫实例清单扫描（依赖宿主 ICatScanner 服务）
    /// </summary>
    public static class CatScanInstancesBrick
    {
        /// <summary>
        /// 扫描猫实例清单——猫档案 JSON（name/tools/hasContext/damaged）
        /// </summary>
        /// <param name="instancesJson">猫清单 JSON</param>
        /// <returns>true=成功</returns>
        public static bool ScanInstances(out string instancesJson)
        {
            instancesJson = "";
            ICatScanner? scanner;
            DataBox.TryResolve<ICatScanner>(out scanner);
            if (scanner == null)
            {
                return false;
            }
            string json = scanner.ScanInstances();
            instancesJson = json != null ? json : "";
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:B39388CCAD823E3F71AEEE539BEC58FC90E72573F1B11CD3D1329B071E8919B5

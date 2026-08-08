// ═══════════════════════════════════════════════════
// 积木: excel.bridge
// ID:   BRIK-PACK-001
// 类别: PACK
// 作用: Excel 桥接口积木——外部包能力声明（PACK 类，普通积木只经本桥访问 Excel）
// 依赖: 无
// 包: 无
// 引用: Mau.Runtime（IExcelBridge）· System
// 原理: DataBox.TryResolve<IExcelBridge> → Invoke(method, argsJson, out result)
//       实现 = Mau.Office.OfficeBridge（ClosedXML 封装，依赖者宿主选装 Bind）
// 方法: excel.read → path,sheet,format
//        excel.write → path,content,sheet
// 常用: excel.read/write 积木的调度底座（PACK 协议——外部包与积木解耦）
// ═══════════════════════════════════════════════════
using System;

namespace Mau.Bricks
{
    /// <summary>
    /// PACK 接口积木——excel.bridge（调度 IExcelBridge）
    /// </summary>
    public static class ExcelBridgeBrick
    {
        /// <summary>
        /// Excel 桥单方法调度——method 白名单 + argsJson 展平参数
        /// </summary>
        /// <param name="method">操作名（excel.read/excel.write）</param>
        /// <param name="argsJson">展平参数 JSON</param>
        /// <param name="result">结果文本</param>
        /// <returns>true=调用成功</returns>
        public static bool Invoke(string method, string argsJson, out string result)
        {
            result = "";
            Mau.Runtime.IExcelBridge? bridge;
            Mau.Runtime.DataBox.TryResolve<Mau.Runtime.IExcelBridge>(out bridge);
            if (bridge == null)
            {
                result = "ERR|EXCEL_NO_BRIDGE|宿主未注入 IExcelBridge（Mau.Office.OfficeBridge）";
                return false;
            }
            return bridge.Invoke(method, argsJson, out result);
        }
    }
}
// #MAU_CHECKSUM:SHA256:B976A29260472FEB37BFB9633F2AFA77A27E3CA7BD1939B745839A47D1452AB2

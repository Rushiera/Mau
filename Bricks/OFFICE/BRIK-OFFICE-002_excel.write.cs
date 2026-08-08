// ═══════════════════════════════════════════════════
// 积木: excel.write
// ID:   BRIK-OFFICE-002
// 类别: OFFICE
// 作用: 将 TSV/CSV 文本写入 .xlsx 文件（PACK 调度——经 excel.bridge）
// 依赖: 无
// 包: 无
// 引用: Mau.Runtime（IExcelBridge）· System
// 原理: DataBox.TryResolve<IExcelBridge> → Invoke("excel.write", argsJson)
//       实现 = Mau.WorkApp.WorkAppBridge（ClosedXML 封装——PACK 隔离）
// 常用: OfficeCat 工具 Cat——报表导出（PACK 协议）
// ═══════════════════════════════════════════════════
using System;

namespace Mau.Bricks
{
    /// <summary>
    /// Excel 积木——excel.write 写入 .xlsx（PACK 调度 IExcelBridge）
    /// </summary>
    public static class ExcelWriteBrick
    {
        /// <summary>
        /// 将 TSV/CSV 文本写入 .xlsx 文件（覆盖已有文件）
        /// </summary>
        /// <param name="path">文件路径</param>
        /// <param name="content">TSV/CSV 格式文本</param>
        /// <param name="sheet">工作表名，空=Sheet1</param>
        /// <param name="result">结果描述</param>
        /// <returns>true=成功</returns>
        public static bool Write(string path, string content, string sheet, out string result)
        {
            result = "";
            Mau.Runtime.IExcelBridge? bridge;
            Mau.Runtime.DataBox.TryResolve<Mau.Runtime.IExcelBridge>(out bridge);
            if (bridge == null)
            {
                result = "ERR|EXCEL_NO_BRIDGE|宿主未注入 IExcelBridge（Mau.WorkApp.WorkAppBridge）";
                return false;
            }
            string argsJson = "{\"path\":\"" + Safe(path) + "\",\"content\":\"" + Safe(content) + "\",\"sheet\":\"" + Safe(sheet) + "\"}";
            return bridge.Invoke("excel.write", argsJson, out result);
        }

        /// <summary>
        /// JSON 字符串安全转义
        /// </summary>
        /// <param name="value">原始值</param>
        /// <returns>转义后</returns>
        private static string Safe(string value)
        {
            if (value == null) { return ""; }
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
        }
    }
}
// #MAU_CHECKSUM:SHA256:F105896A2C8E3169376D4D602AC6637AE20FE9642929035E7FAE399BBC980FEF

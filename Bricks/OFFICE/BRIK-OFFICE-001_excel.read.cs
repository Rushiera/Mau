// ═══════════════════════════════════════════════════
// 积木: excel.read
// ID:   BRIK-OFFICE-001
// 类别: OFFICE
// 作用: 读取 .xlsx 文件，返回 TSV/CSV 格式文本（PACK 调度——经 excel.bridge）
// 依赖: 无
// 包: 无
// 引用: Mau.Runtime（IExcelBridge）· System
// 原理: DataBox.TryResolve<IExcelBridge> → Invoke("excel.read", argsJson)
//       实现 = Mau.WorkApp.WorkAppBridge（ClosedXML 封装——PACK 隔离）
// 常用: OfficeCat 工具 Cat——表格读取（PACK 协议）
// ═══════════════════════════════════════════════════
using System;

namespace Mau.Bricks
{
    /// <summary>
    /// Excel 积木——excel.read 读取 .xlsx（PACK 调度 IExcelBridge）
    /// </summary>
    public static class ExcelReadBrick
    {
        /// <summary>
        /// 读取 .xlsx 文件，返回 TSV/CSV 格式文本
        /// </summary>
        /// <param name="path">文件路径</param>
        /// <param name="sheet">工作表名或序号（1开始），空=第一张</param>
        /// <param name="format">输出格式：tsv/csv</param>
        /// <param name="content">表格文本</param>
        /// <returns>true=成功</returns>
        public static bool Read(string path, string sheet, string format, out string content)
        {
            content = "";
            Mau.Runtime.IExcelBridge? bridge;
            Mau.Runtime.DataBox.TryResolve<Mau.Runtime.IExcelBridge>(out bridge);
            if (bridge == null)
            {
                content = "ERR|EXCEL_NO_BRIDGE|宿主未注入 IExcelBridge（Mau.WorkApp.WorkAppBridge）";
                return false;
            }
            string argsJson = "{\"path\":\"" + Safe(path) + "\",\"sheet\":\"" + Safe(sheet) + "\",\"format\":\"" + Safe(format) + "\"}";
            return bridge.Invoke("excel.read", argsJson, out content);
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
// #MAU_CHECKSUM:SHA256:D7FE3B7C825DBB0CF6F201C892E623A648F30C389981F9E851830E761C64DA49

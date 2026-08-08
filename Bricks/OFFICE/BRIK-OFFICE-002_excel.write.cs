// ═══════════════════════════════════════════════════
// 积木: excel.write
// ID:   BRIK-OFFICE-002
// 类别: OFFICE
// 作用: 将 TSV/CSV 文本写入 .xlsx 文件（覆盖已有文件）
// 依赖: 无
// 包: ClosedXML@0.104.2
// 引用: System · ClosedXML.Excel
// 原理: XLWorkbook 创建——分隔符自动检测（\t 优先），CSV 引号还原
// 常用: CH4 IO 工具组 / 报表导出
// ═══════════════════════════════════════════════════
using System;
using ClosedXML.Excel;

namespace Mau.Bricks
{
    /// <summary>
    /// Excel 积木——excel.write 写入 .xlsx（纯函数无状态）
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
        public static bool Write(string path, string content, string sheet,
            out string result)
        {
            result = "";
            if (string.IsNullOrEmpty(sheet))
            {
                sheet = "Sheet1";
            }
            try
            {
                using XLWorkbook wb = new XLWorkbook();
                IXLWorksheet ws = wb.Worksheets.Add(sheet);

                char delim;
                if (content.Contains('\t'))
                {
                    delim = '\t';
                }
                else
                {
                    delim = ',';
                }
                string[] rows = content.Split('\n');
                int written = 0;

                for (int r = 0; r < rows.Length; r = r + 1)
                {
                    string line = rows[r];
                    if (line.Length == 0)
                    {
                        continue;
                    }
                    string[] cols = line.Split(delim);
                    for (int c = 0; c < cols.Length; c = c + 1)
                    {
                        string val = cols[c];
                        if (delim == ',' && val.StartsWith("\"") && val.EndsWith("\""))
                        {
                            val = val.Substring(1, val.Length - 2).Replace("\"\"", "\"");
                        }
                        ws.Cell(written + 1, c + 1).Value = val;
                    }
                    written = written + 1;
                }

                wb.SaveAs(path);
                result = "写入成功: " + path + " (" + written + "行)";
                return true;
            }
            catch (Exception e)
            {
                result = "[错误] Excel 写入失败: " + e.Message;
                return false;
            }
        }
    }
}
// #MAU_CHECKSUM:SHA256:33820E428E4253123CFC06821E17625043EE2559D9A657C707A9AF46B31BB669

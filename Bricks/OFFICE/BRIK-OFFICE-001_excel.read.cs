// ═══════════════════════════════════════════════════
// 积木: excel.read
// ID:   BRIK-OFFICE-001
// 类别: OFFICE
// 作用: 读取 .xlsx 文件，返回 TSV/CSV 格式文本
// 依赖: 无
// 包: ClosedXML@0.104.2
// 引用: System · System.Text · ClosedXML.Excel
// 原理: XLWorkbook 打开——≤2000 行×100 列，CSV 引号转义
// 常用: CH4 IO 工具组 / 表格数据处理
// ═══════════════════════════════════════════════════
using System;
using System.Text;
using ClosedXML.Excel;

namespace Mau.Bricks
{
    /// <summary>
    /// Excel 积木——excel.read 读取 .xlsx（纯函数无状态）
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
        public static bool Read(string path, string sheet, string format,
            out string content)
        {
            content = "";
            if (string.IsNullOrEmpty(format))
            {
                format = "tsv";
            }
            try
            {
                using XLWorkbook wb = new XLWorkbook(path);
                IXLWorksheet ws;
                if (!string.IsNullOrEmpty(sheet))
                {
                    if (int.TryParse(sheet, out int idx) && idx >= 1)
                    {
                        if (idx > wb.Worksheets.Count)
                        {
                            idx = 1;
                        }
                        ws = wb.Worksheet(idx);
                    }
                    else
                    {
                        ws = wb.Worksheet(sheet);
                    }
                }
                else
                {
                    ws = wb.Worksheet(1);
                }

                IXLRange? range = ws.RangeUsed();
                if (range == null)
                {
                    content = "(空工作表)";
                    return true;
                }

                int maxRow = Math.Min(range.RowCount(), 2000);
                int maxCol = Math.Min(range.ColumnCount(), 100);
                char delim;
                if (format == "csv")
                {
                    delim = ',';
                }
                else
                {
                    delim = '\t';
                }

                StringBuilder sb = new StringBuilder();
                int fr = range.FirstRow().RowNumber();
                int fc = range.FirstColumn().ColumnNumber();
                for (int r = fr; r < fr + maxRow; r = r + 1)
                {
                    for (int c = fc; c < fc + maxCol; c = c + 1)
                    {
                        if (c > fc)
                        {
                            sb.Append(delim);
                        }
                        string val = ws.Cell(r, c).GetString();
                        if (format == "csv" && (val.Contains(',') || val.Contains('"')))
                        {
                            val = "\"" + val.Replace("\"", "\"\"") + "\"";
                        }
                        sb.Append(val);
                    }
                    sb.Append('\n');
                }

                string trunc;
                if (range.RowCount() > 2000 || range.ColumnCount() > 100)
                {
                    trunc = "（原始 " + range.RowCount() + "行×" + range.ColumnCount()
                        + "列，已截断）";
                }
                else
                {
                    trunc = "";
                }
                content = sb.ToString().TrimEnd('\n') + trunc;
                return true;
            }
            catch (Exception e)
            {
                content = "[错误] Excel 读取失败: " + e.Message;
                return false;
            }
        }
    }
}
// #MAU_CHECKSUM:SHA256:9A7F346C05AEB27B4B578B953E9A8AFA8D3B79979433A45B4663B602B9A408ED

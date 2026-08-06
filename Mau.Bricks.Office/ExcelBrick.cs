// ═══════════════════════════════════════════════
// 积木: excel.read / excel.write
// ID:   BRIK-OFFICE-001 ~ 002
// 作用: Excel 读写——.xlsx ↔ TSV/CSV 纯文本
// 引用: Mau.Bricks.Office → Mau.Contracts（BrickRegistry）· ClosedXML
// 依赖: ClosedXML
// 原理: XLWorkbook 打开/创建——≤2000 行×100 列，CSV 引号转义
// 常用: CH4 IO 工具组 / 表格数据处理 / 报表导出
// ═══════════════════════════════════════════════
using System;
using System.Text;
using ClosedXML.Excel;
using Mau.Contracts;

namespace Mau.Bricks
{
    /// <summary>
    /// Excel 积木——标准积木库 Office 类。纯函数无状态。
    /// 由 CH2 CH_Kit_Excel 移植。
    /// </summary>
    public static class ExcelBrick
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

    /// <summary>
    /// Excel 积木注册——进程启动时调用一次
    /// </summary>
    public static class ExcelBrickRegistration
    {
        /// <summary>
        /// 注册全部 Excel 积木
        /// </summary>
        public static void RegisterAll()
        {
            RegisterRead();
            RegisterWrite();
        }

        /// <summary>
        /// 注册 excel.read
        /// </summary>
        private static void RegisterRead()
        {
            BrickContract contract = new BrickContract("excel.read", "Mau.Bricks.ExcelBrick.Read");
            contract.Inputs.Add(new BrickPort("path", typeof(string), "文件路径"));
            contract.Inputs.Add(new BrickPort("sheet", typeof(string), "工作表名或序号"));
            contract.Inputs.Add(new BrickPort("format", typeof(string), "tsv/csv"));
            contract.Outputs.Add(new BrickPort("content", typeof(string), "表格文本"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }

        /// <summary>
        /// 注册 excel.write
        /// </summary>
        private static void RegisterWrite()
        {
            BrickContract contract = new BrickContract("excel.write", "Mau.Bricks.ExcelBrick.Write");
            contract.Inputs.Add(new BrickPort("path", typeof(string), "文件路径"));
            contract.Inputs.Add(new BrickPort("content", typeof(string), "TSV/CSV 文本"));
            contract.Inputs.Add(new BrickPort("sheet", typeof(string), "工作表名"));
            contract.Outputs.Add(new BrickPort("result", typeof(string), "结果描述"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }
    }
}

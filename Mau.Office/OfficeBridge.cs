#nullable disable warnings // ClosedXML/OpenXml 返回值可空假阳性
using System;
using System.Text;
using Mau.Runtime;

namespace Mau.Office
{
    /// <summary>
    /// Office 桥——IExcelBridge + IWordBridge 实现（PACK 类）。
    /// 单方法调度：method 白名单 + argsJson 展平参数；封装 ClosedXML / DocumentFormat.OpenXml。
    /// 契约：Bricks/PACK/BRIK-PACK-001_excel.bridge.cs + BRIK-PACK-002_word.bridge.cs。
    /// </summary>
    public sealed class OfficeBridge : IExcelBridge, IWordBridge
    {
        /// <summary>
        /// 单方法调度——method 白名单 + argsJson 展平参数
        /// </summary>
        /// <param name="method">操作名</param>
        /// <param name="argsJson">展平参数 JSON</param>
        /// <param name="result">结果文本</param>
        /// <returns>true=调用成功</returns>
        public bool Invoke(string method, string argsJson, out string result)
        {
            result = "";
            if (string.IsNullOrEmpty(method))
            {
                result = "ERR|OFFICE_NO_METHOD|用法: read/write";
                return false;
            }
            try
            {
                if (method == "excel.read")
                {
                    return ExcelRead(argsJson, out result);
                }
                if (method == "excel.write")
                {
                    return ExcelWrite(argsJson, out result);
                }
                if (method == "word.read")
                {
                    return WordRead(argsJson, out result);
                }
                if (method == "word.write")
                {
                    return WordWrite(argsJson, out result);
                }
                result = "ERR|OFFICE_UNKNOWN_METHOD|" + method + "|允许: excel.read/excel.write/word.read/word.write";
                return false;
            }
            catch (Exception ex)
            {
                result = "ERR|" + ex.GetType().Name + "|" + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Shutdown()
        {
        }

        /// <summary>
        /// Excel 读取——path/sheet/format → content
        /// </summary>
        /// <param name="argsJson">参数 JSON</param>
        /// <param name="result">表格文本</param>
        /// <returns>true=成功</returns>
        private static bool ExcelRead(string argsJson, out string result)
        {
            result = "";
            string path = ReadArg(argsJson, "path");
            string sheet = ReadArg(argsJson, "sheet");
            string format = ReadArg(argsJson, "format");
            string content;
            if (!ExcelReadCore(path, sheet, format, out content))
            {
                result = "ERR|EXCEL_READ_FAILED";
                return false;
            }
            result = content;
            return true;
        }

        /// <summary>
        /// Excel 写入——path/content/sheet → summary
        /// </summary>
        /// <param name="argsJson">参数 JSON</param>
        /// <param name="result">结果摘要</param>
        /// <returns>true=成功</returns>
        private static bool ExcelWrite(string argsJson, out string result)
        {
            result = "";
            string path = ReadArg(argsJson, "path");
            string content = ReadArg(argsJson, "content");
            string sheet = ReadArg(argsJson, "sheet");
            string summary;
            if (!ExcelWriteCore(path, content, sheet, out summary))
            {
                result = "ERR|EXCEL_WRITE_FAILED|" + summary;
                return false;
            }
            result = summary;
            return true;
        }

        /// <summary>
        /// Word 读取——path → content
        /// </summary>
        /// <param name="argsJson">参数 JSON</param>
        /// <param name="result">纯文本</param>
        /// <returns>true=成功</returns>
        private static bool WordRead(string argsJson, out string result)
        {
            result = "";
            string path = ReadArg(argsJson, "path");
            string content;
            if (!WordReadCore(path, out content))
            {
                result = "ERR|DOCX_READ_FAILED";
                return false;
            }
            result = content;
            return true;
        }

        /// <summary>
        /// Word 写入——path/content → summary
        /// </summary>
        /// <param name="argsJson">参数 JSON</param>
        /// <param name="result">结果摘要</param>
        /// <returns>true=成功</returns>
        private static bool WordWrite(string argsJson, out string result)
        {
            result = "";
            string path = ReadArg(argsJson, "path");
            string content = ReadArg(argsJson, "content");
            string summary;
            if (!WordWriteCore(path, content, out summary))
            {
                result = "ERR|DOCX_WRITE_FAILED|" + summary;
                return false;
            }
            result = summary;
            return true;
        }

        /// <summary>
        /// 读取 argsJson 中的参数——防御式解析
        /// </summary>
        /// <param name="argsJson">参数 JSON</param>
        /// <param name="key">参数名</param>
        /// <returns>值或空串</returns>
        private static string ReadArg(string argsJson, string key)
        {
            if (string.IsNullOrEmpty(argsJson))
            {
                return "";
            }
            try
            {
                using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(argsJson);
                if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    System.Text.Json.JsonElement value;
                    if (doc.RootElement.TryGetProperty(key, out value)
                        && value.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        return value.GetString() ?? "";
                    }
                    if (doc.RootElement.TryGetProperty(key, out value))
                    {
                        return value.GetRawText();
                    }
                }
            }
            catch
            {
                // 解析失败返回空串
            }
            return "";
        }

        // ═══════════════════════════════════════════
        // 核心实现——自包含（不依赖 Bricks 程序集，PACK 隔离）
        // ═══════════════════════════════════════════

        /// <summary>
        /// Excel 读取核心——ClosedXML 实现
        /// </summary>
        /// <param name="path">文件路径</param>
        /// <param name="sheet">工作表名或序号（1开始），空=第一张</param>
        /// <param name="format">输出格式：tsv/csv</param>
        /// <param name="content">表格文本</param>
        /// <returns>true=成功</returns>
        private static bool ExcelReadCore(string path, string sheet, string format, out string content)
        {
            content = "";
            if (string.IsNullOrEmpty(format)) { format = "tsv"; }
            try
            {
                using ClosedXML.Excel.XLWorkbook wb = new ClosedXML.Excel.XLWorkbook(path);
                ClosedXML.Excel.IXLWorksheet ws;
                if (!string.IsNullOrEmpty(sheet))
                {
                    if (int.TryParse(sheet, out int idx) && idx >= 1)
                    {
                        ws = idx > wb.Worksheets.Count ? wb.Worksheet(1) : wb.Worksheet(idx);
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
                ClosedXML.Excel.IXLRange? range = ws.RangeUsed();
                if (range == null)
                {
                    content = "(空工作表)";
                    return true;
                }
                int maxRow = Math.Min(range.RowCount(), 2000);
                int maxCol = Math.Min(range.ColumnCount(), 100);
                char delim = format == "csv" ? ',' : '\t';
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                int fr = range.FirstRow().RowNumber();
                int fc = range.FirstColumn().ColumnNumber();
                for (int r = fr; r < fr + maxRow; r = r + 1)
                {
                    for (int c = fc; c < fc + maxCol; c = c + 1)
                    {
                        if (c > fc) { sb.Append(delim); }
                        string val = ws.Cell(r, c).GetString();
                        if (format == "csv" && (val.Contains(',') || val.Contains('"')))
                        {
                            val = "\"" + val.Replace("\"", "\"\"") + "\"";
                        }
                        sb.Append(val);
                    }
                    sb.Append('\n');
                }
                string trunc = (range.RowCount() > 2000 || range.ColumnCount() > 100)
                    ? "（原始 " + range.RowCount() + "行×" + range.ColumnCount() + "列，已截断）" : "";
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
        /// Excel 写入核心——ClosedXML 实现
        /// </summary>
        /// <param name="path">文件路径</param>
        /// <param name="content">TSV/CSV 文本</param>
        /// <param name="sheet">工作表名</param>
        /// <param name="result">结果摘要</param>
        /// <returns>true=成功</returns>
        private static bool ExcelWriteCore(string path, string content, string sheet, out string result)
        {
            result = "";
            if (string.IsNullOrEmpty(sheet)) { sheet = "Sheet1"; }
            try
            {
                using ClosedXML.Excel.XLWorkbook wb = new ClosedXML.Excel.XLWorkbook();
                ClosedXML.Excel.IXLWorksheet ws = wb.Worksheets.Add(sheet);
                char delim = content.Contains('\t') ? '\t' : ',';
                string[] rows = content.Split('\n');
                int written = 0;
                for (int r = 0; r < rows.Length; r = r + 1)
                {
                    string line = rows[r];
                    if (line.Length == 0) { continue; }
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

        /// <summary>
        /// Word 读取核心——DocumentFormat.OpenXml 实现
        /// </summary>
        /// <param name="path">文件路径</param>
        /// <param name="content">纯文本</param>
        /// <returns>true=成功</returns>
        private static bool WordReadCore(string path, out string content)
        {
            content = "";
            try
            {
                using DocumentFormat.OpenXml.Packaging.WordprocessingDocument doc =
                    DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(path, false);
                DocumentFormat.OpenXml.Wordprocessing.Body? body =
                    doc.MainDocumentPart != null ? doc.MainDocumentPart.Document.Body : null;
                if (body == null)
                {
                    content = "(空文档)";
                    return true;
                }
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                bool first = true;
                foreach (DocumentFormat.OpenXml.Wordprocessing.Paragraph para in body.Elements<DocumentFormat.OpenXml.Wordprocessing.Paragraph>())
                {
                    if (!first) { sb.Append("\n\n"); }
                    first = false;
                    sb.Append(para.InnerText);
                }
                content = sb.ToString();
                return true;
            }
            catch (Exception e)
            {
                content = "[错误] Word 读取失败: " + e.Message;
                return false;
            }
        }

        /// <summary>
        /// Word 写入核心——DocumentFormat.OpenXml 实现
        /// </summary>
        /// <param name="path">文件路径</param>
        /// <param name="content">纯文本</param>
        /// <param name="result">结果摘要</param>
        /// <returns>true=成功</returns>
        private static bool WordWriteCore(string path, string content, out string result)
        {
            result = "";
            try
            {
                using DocumentFormat.OpenXml.Packaging.WordprocessingDocument doc =
                    DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Create(
                        path, DocumentFormat.OpenXml.WordprocessingDocumentType.Document);
                DocumentFormat.OpenXml.Packaging.MainDocumentPart mainPart = doc.AddMainDocumentPart();
                mainPart.Document = new DocumentFormat.OpenXml.Wordprocessing.Document();
                DocumentFormat.OpenXml.Wordprocessing.Body body = new DocumentFormat.OpenXml.Wordprocessing.Body();
                string[] lines = content.Split('\n');
                int paraCount = 0;
                foreach (string rawLine in lines)
                {
                    string line = rawLine.TrimEnd('\r');
                    DocumentFormat.OpenXml.Wordprocessing.Paragraph para = new DocumentFormat.OpenXml.Wordprocessing.Paragraph();
                    DocumentFormat.OpenXml.Wordprocessing.Run run = new DocumentFormat.OpenXml.Wordprocessing.Run();
                    run.Append(new DocumentFormat.OpenXml.Wordprocessing.Text(line));
                    para.Append(run);
                    body.Append(para);
                    paraCount = paraCount + 1;
                }
                mainPart.Document.Append(body);
                mainPart.Document.Save();
                result = "写入成功: " + path + " (" + paraCount + "段)";
                return true;
            }
            catch (Exception e)
            {
                result = "[错误] Word 写入失败: " + e.Message;
                return false;
            }
        }
    }
}

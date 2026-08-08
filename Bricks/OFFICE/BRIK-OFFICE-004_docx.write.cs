// ═══════════════════════════════════════════════════
// 积木: docx.write
// ID:   BRIK-OFFICE-004
// 类别: OFFICE
// 作用: 将纯文本写入 .docx 文件（覆盖已有文件，换行符分割段落）
// 依赖: 无
// 包: DocumentFormat.OpenXml@3.2.0
// 引用: System · DocumentFormat.OpenXml
// 原理: WordprocessingDocument 创建——Body 段落构建（每行一段）
// 常用: CH4 IO 工具组 / 报告生成
// ═══════════════════════════════════════════════════
using System;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Mau.Bricks
{
    /// <summary>
    /// Word 积木——docx.write 写入 .docx（纯函数无状态）
    /// </summary>
    public static class DocxWriteBrick
    {
        /// <summary>
        /// 将纯文本写入 .docx 文件（覆盖已有文件）
        /// </summary>
        /// <param name="path">文件路径</param>
        /// <param name="content">纯文本，换行符分割段落</param>
        /// <param name="result">结果描述</param>
        /// <returns>true=成功</returns>
        public static bool Write(string path, string content, out string result)
        {
            result = "";
            try
            {
                using WordprocessingDocument doc = WordprocessingDocument.Create(
                    path, WordprocessingDocumentType.Document);
                MainDocumentPart mainPart = doc.AddMainDocumentPart();
                mainPart.Document = new Document();
                Body body = new Body();

                string[] lines = content.Split('\n');
                int paraCount = 0;
                foreach (string rawLine in lines)
                {
                    string line = rawLine.TrimEnd('\r');
                    Paragraph para = new Paragraph();
                    Run run = new Run();
                    run.Append(new Text(line));
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
// #MAU_CHECKSUM:SHA256:DE937BC8FD09C106E500A7EAEB546F3EE13301CF82D99387925D29F86DF71D8F

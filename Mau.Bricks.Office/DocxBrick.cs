// ═══════════════════════════════════════════════
// 积木: docx.read / docx.write
// ID:   BRIK-OFFICE-003 ~ 004
// 作用: Word 读写——.docx ↔ 纯文本（段落间空行分隔）
// 引用: Mau.Bricks.Office → Mau.Contracts（BrickRegistry）· DocumentFormat.OpenXml
// 依赖: DocumentFormat.OpenXml
// 原理: WordprocessingDocument 打开/创建——Body 段落遍历/构建
// 常用: CH4 IO 工具组 / 文档处理 / 报告生成
// ═══════════════════════════════════════════════
using System;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Mau.Contracts;

namespace Mau.Bricks
{
    /// <summary>
    /// Word 积木——标准积木库 Office 类。纯函数无状态。
    /// 由 CH2 CH_Kit_Word 移植。
    /// </summary>
    public static class DocxBrick
    {
        /// <summary>
        /// 读取 .docx 文件，提取纯文本内容
        /// </summary>
        /// <param name="path">文件路径</param>
        /// <param name="content">纯文本内容</param>
        /// <returns>true=成功</returns>
        public static bool Read(string path, out string content)
        {
            content = "";
            try
            {
                using WordprocessingDocument doc = WordprocessingDocument.Open(
                    path, false);
                Body? body;
                if (doc.MainDocumentPart != null)
                {
                    body = doc.MainDocumentPart.Document.Body;
                }
                else
                {
                    body = null;
                }
                if (body == null)
                {
                    content = "(空文档)";
                    return true;
                }

                StringBuilder sb = new StringBuilder();
                bool first = true;
                foreach (Paragraph para in body.Elements<Paragraph>())
                {
                    if (!first)
                    {
                        sb.Append("\n\n");
                    }
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

    /// <summary>
    /// Word 积木注册——进程启动时调用一次
    /// </summary>
    public static class DocxBrickRegistration
    {
        /// <summary>
        /// 注册全部 Word 积木
        /// </summary>
        public static void RegisterAll()
        {
            RegisterRead();
            RegisterWrite();
        }

        /// <summary>
        /// 注册 docx.read
        /// </summary>
        private static void RegisterRead()
        {
            BrickContract contract = new BrickContract("docx.read", "Mau.Bricks.DocxBrick.Read");
            contract.Inputs.Add(new BrickPort("path", typeof(string), "文件路径"));
            contract.Outputs.Add(new BrickPort("content", typeof(string), "纯文本内容"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }

        /// <summary>
        /// 注册 docx.write
        /// </summary>
        private static void RegisterWrite()
        {
            BrickContract contract = new BrickContract("docx.write", "Mau.Bricks.DocxBrick.Write");
            contract.Inputs.Add(new BrickPort("path", typeof(string), "文件路径"));
            contract.Inputs.Add(new BrickPort("content", typeof(string), "纯文本"));
            contract.Outputs.Add(new BrickPort("result", typeof(string), "结果描述"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }
    }
}

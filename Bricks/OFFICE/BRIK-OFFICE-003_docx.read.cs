// ═══════════════════════════════════════════════════
// 积木: docx.read
// ID:   BRIK-OFFICE-003
// 类别: OFFICE
// 作用: 读取 .docx 文件，提取纯文本内容（段落间空行分隔）
// 依赖: 无
// 包: DocumentFormat.OpenXml@3.2.0
// 引用: System · System.Text · DocumentFormat.OpenXml
// 原理: WordprocessingDocument 打开——Body 段落遍历 InnerText 拼接
// 常用: CH4 IO 工具组 / 文档处理
// ═══════════════════════════════════════════════════
using System;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Mau.Bricks
{
    /// <summary>
    /// Word 积木——docx.read 读取 .docx（纯函数无状态）
    /// </summary>
    public static class DocxReadBrick
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
    }
}
// #MAU_CHECKSUM:SHA256:0C9B3B3FCE20B387DB118AAFE412AC513F13483B177939E302BE1F9758325506

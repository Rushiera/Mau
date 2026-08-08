// ═══════════════════════════════════════════════════
// 积木: tool.run_generic
// ID:   BRIK-TOOL-013
// 类别: TOOL
// 作用: 通用工具适配器——按 domain + toolName 路由到域积木执行，result/error 回执 + Complete
// 依赖: file.read, file.write, file.append, file.replace, file.read_lines, file.tree, file.find, file.move, file.delete, file.convert, file.batch, shell.exec, excel.read, excel.write, docx.read, docx.write
// 引用: Mau.Runtime
// 原理: 读单（toolName=OfficeName + args.* 展平载荷）→ 域路由表 → 域积木执行 → result/error 回执 → Complete
// 常用: 工具 Cat 语料统一执行动作（M2b：FileCat 接单 → run_generic("file") → 自动回执；M2c：+office 域 excel/docx）
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 工具积木——tool.run_generic 通用工具适配器（依赖 OaBridge + 域积木）
    /// </summary>
    public static class ToolRunGenericBrick
    {
        /// <summary>
        /// 通用工具适配器——按 domain + toolName 路由到域积木执行，result/error 回执 + Complete
        /// </summary>
        /// <param name="officeId">Office ID（已认领）</param>
        /// <param name="catId">执行方 LongId</param>
        /// <param name="domain">域——file/text/shell/office</param>
        /// <param name="callId">工具调用 ID（附带输出）</param>
        /// <param name="session">会话 Key（附带输出）</param>
        /// <returns>true=执行并完成</returns>
        public static bool RunGeneric(long officeId, long catId, string domain,
            out string callId, out string session)
        {
            callId = "";
            session = "";
            IOA oa;
            DataBox.TryResolve<IOA>(out oa);
            if (oa == null)
            {
                return false;
            }
            Office office = oa.GetOffice(officeId);
            string toolName = office.OfficeName;
            string rawCall;
            if (oa.GetStr(officeId, "call_id", out rawCall) && rawCall != null)
            {
                callId = rawCall;
            }
            string rawSession;
            if (oa.GetStr(officeId, "session", out rawSession) && rawSession != null)
            {
                session = rawSession;
            }
            string result = "";
            string error = "";
            bool ok = true;
            if (domain == "file")
            {
                ok = RunFileTool(oa, officeId, toolName, out result, out error);
            }
            else if (domain == "shell")
            {
                ok = RunShellTool(oa, officeId, toolName, out result, out error);
            }
            else if (domain == "office")
            {
                ok = RunOfficeTool(oa, officeId, toolName, out result, out error);
            }
            else
            {
                error = "ERR|UNSUPPORTED_DOMAIN|" + domain;
                ok = false;
            }
            OfficeData data = OfficeData.Empty();
            if (result.Length > 0)
            {
                data.Strs["result"] = result;
            }
            if (error.Length > 0)
            {
                data.Strs["error"] = error;
            }
            if (ok && error.Length == 0)
            {
                data.Strs["result"] = result.Length > 0 ? result : "ok";
            }
            oa.Complete(officeId, catId, data);
            return true;
        }

        /// <summary>
        /// File 域路由——按 toolName 调 file.* 积木
        /// </summary>
        /// <param name="oa">OA</param>
        /// <param name="officeId">Office ID</param>
        /// <param name="toolName">工具名</param>
        /// <param name="result">结果文本</param>
        /// <param name="error">错误摘要</param>
        /// <returns>true=执行成功</returns>
        private static bool RunFileTool(IOA oa, long officeId, string toolName,
            out string result, out string error)
        {
            result = "";
            error = "";
            string Arg(string key)
            {
                string value;
                if (oa.GetStr(officeId, "args." + key, out value) && value != null)
                {
                    return value;
                }
                return "";
            }
            bool ParseInt(string key, out int value)
            {
                string raw;
                if (oa.GetStr(officeId, "args." + key, out raw) && raw != null
                    && int.TryParse(raw, out value))
                {
                    return true;
                }
                value = 0;
                return false;
            }
            bool ParseBool(string key, out bool value)
            {
                string raw;
                if (oa.GetStr(officeId, "args." + key, out raw) && raw != null
                    && bool.TryParse(raw, out value))
                {
                    return true;
                }
                value = false;
                return false;
            }
            try
            {
                if (toolName == "file.read")
                {
                    string content;
                    if (!FileReadBrick.Read(Arg("path"), out content))
                    {
                        error = "ERR|FILE_READ_FAILED";
                        return false;
                    }
                    result = content;
                    return true;
                }
                if (toolName == "file.write")
                {
                    if (!FileWriteBrick.Write(Arg("path"), Arg("content")))
                    {
                        error = "ERR|FILE_WRITE_FAILED";
                        return false;
                    }
                    result = "written";
                    return true;
                }
                if (toolName == "file.append")
                {
                    if (!FileAppendBrick.Append(Arg("path"), Arg("content")))
                    {
                        error = "ERR|FILE_APPEND_FAILED";
                        return false;
                    }
                    result = "appended";
                    return true;
                }
                if (toolName == "file.replace")
                {
                    if (!FileReplaceBrick.Replace(Arg("path"), Arg("oldText"), Arg("newText")))
                    {
                        error = "ERR|FILE_REPLACE_FAILED";
                        return false;
                    }
                    result = "replaced";
                    return true;
                }
                if (toolName == "file.read_lines")
                {
                    string lines;
                    int startLine;
                    int endLine;
                    if (!ParseInt("startLine", out startLine))
                    {
                        startLine = 1;
                    }
                    if (!ParseInt("endLine", out endLine))
                    {
                        endLine = 0;
                    }
                    if (!FileReadLinesBrick.ReadLines(Arg("path"), startLine, endLine, out lines))
                    {
                        error = "ERR|FILE_READ_LINES_FAILED|" + lines;
                        return false;
                    }
                    result = lines;
                    return true;
                }
                if (toolName == "file.tree")
                {
                    string tree;
                    int depth;
                    int limit;
                    if (!ParseInt("depth", out depth))
                    {
                        depth = 2;
                    }
                    if (!ParseInt("limit", out limit))
                    {
                        limit = 50;
                    }
                    if (!FileTreeBrick.Tree(Arg("path"), depth, limit, out tree))
                    {
                        error = "ERR|FILE_TREE_FAILED|" + tree;
                        return false;
                    }
                    result = tree;
                    return true;
                }
                if (toolName == "file.find")
                {
                    string found;
                    bool recursive;
                    int limit;
                    if (!ParseBool("recursive", out recursive))
                    {
                        recursive = true;
                    }
                    if (!ParseInt("limit", out limit))
                    {
                        limit = 50;
                    }
                    if (!FileFindBrick.Find(Arg("directory"), Arg("pattern"), recursive, limit, out found))
                    {
                        error = "ERR|FILE_FIND_FAILED|" + found;
                        return false;
                    }
                    result = found;
                    return true;
                }
                if (toolName == "file.move")
                {
                    if (!FileMoveBrick.Move(Arg("source"), Arg("destination")))
                    {
                        error = "ERR|FILE_MOVE_FAILED";
                        return false;
                    }
                    result = "moved";
                    return true;
                }
                if (toolName == "file.delete")
                {
                    string recycled;
                    if (!FileDeleteBrick.Delete(Arg("path"), out recycled))
                    {
                        error = "ERR|FILE_DELETE_FAILED|" + recycled;
                        return false;
                    }
                    result = "recycled=" + recycled;
                    return true;
                }
                if (toolName == "file.convert")
                {
                    if (!FileConvertBrick.Convert(Arg("input"), Arg("output")))
                    {
                        error = "ERR|FILE_CONVERT_FAILED";
                        return false;
                    }
                    result = "converted";
                    return true;
                }
                if (toolName == "file.batch")
                {
                    string summary;
                    bool stopOnError;
                    if (!ParseBool("stopOnError", out stopOnError))
                    {
                        stopOnError = true;
                    }
                    if (!FileBatchBrick.Batch(Arg("operations"), stopOnError, out summary))
                    {
                        error = "ERR|FILE_BATCH_FAILED|" + summary;
                        return false;
                    }
                    result = summary;
                    return true;
                }
                error = "ERR|UNSUPPORTED_TOOL|" + toolName;
                return false;
            }
            catch (Exception ex)
            {
                error = "ERR|" + ex.GetType().Name + "|" + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Shell 域路由——按 toolName 调 shell.exec 积木
        /// </summary>
        /// <param name="oa">OA</param>
        /// <param name="officeId">Office ID</param>
        /// <param name="toolName">工具名</param>
        /// <param name="result">结果文本</param>
        /// <param name="error">错误摘要</param>
        /// <returns>true=执行成功</returns>
        private static bool RunShellTool(IOA oa, long officeId, string toolName,
            out string result, out string error)
        {
            result = "";
            error = "";
            string command;
            if (!oa.GetStr(officeId, "args.command", out command) || command == null || command.Length == 0)
            {
                error = "ERR|SHELL_NO_COMMAND";
                return false;
            }
            try
            {
                string output;
                int timeoutSeconds = 20;
                string rawTimeout;
                if (oa.GetStr(officeId, "args.timeout", out rawTimeout) && rawTimeout != null
                    && int.TryParse(rawTimeout, out timeoutSeconds))
                {
                    // 使用解析值
                }
                if (!ShellExecBrick.Exec(command, timeoutSeconds, out output))
                {
                    error = "ERR|SHELL_EXEC_FAILED|" + output;
                    return false;
                }
                result = output;
                return true;
            }
            catch (Exception ex)
            {
                error = "ERR|" + ex.GetType().Name + "|" + ex.Message;
                return false;
            }
        }
        /// <summary>
        /// Office 域路由——按 toolName 调 excel/docx 积木
        /// </summary>
        /// <param name="oa">OA</param>
        /// <param name="officeId">Office ID</param>
        /// <param name="toolName">工具名</param>
        /// <param name="result">结果文本</param>
        /// <param name="error">错误摘要</param>
        /// <returns>true=执行成功</returns>
        private static bool RunOfficeTool(IOA oa, long officeId, string toolName,
            out string result, out string error)
        {
            result = "";
            error = "";
            string Arg(string key)
            {
                string value;
                if (oa.GetStr(officeId, "args." + key, out value) && value != null)
                {
                    return value;
                }
                return "";
            }
            try
            {
                if (toolName == "excel.read")
                {
                    string content;
                    if (!ExcelReadBrick.Read(Arg("path"), Arg("sheet"), Arg("format"), out content))
                    {
                        error = "ERR|EXCEL_READ_FAILED";
                        return false;
                    }
                    result = content;
                    return true;
                }
                if (toolName == "excel.write")
                {
                    string summary;
                    if (!ExcelWriteBrick.Write(Arg("path"), Arg("content"), Arg("sheet"), out summary))
                    {
                        error = "ERR|EXCEL_WRITE_FAILED|" + summary;
                        return false;
                    }
                    result = summary;
                    return true;
                }
                if (toolName == "docx.read")
                {
                    string content;
                    if (!DocxReadBrick.Read(Arg("path"), out content))
                    {
                        error = "ERR|DOCX_READ_FAILED";
                        return false;
                    }
                    result = content;
                    return true;
                }
                if (toolName == "docx.write")
                {
                    string summary;
                    if (!DocxWriteBrick.Write(Arg("path"), Arg("content"), out summary))
                    {
                        error = "ERR|DOCX_WRITE_FAILED|" + summary;
                        return false;
                    }
                    result = summary;
                    return true;
                }
                error = "ERR|UNSUPPORTED_TOOL|" + toolName;
                return false;
            }
            catch (Exception ex)
            {
                error = "ERR|" + ex.GetType().Name + "|" + ex.Message;
                return false;
            }
        }
    }
}
// #MAU_CHECKSUM:SHA256:300746D54C29FC348BC87429D43159DAAA6D74A987D266DD7E823E51747AFED6

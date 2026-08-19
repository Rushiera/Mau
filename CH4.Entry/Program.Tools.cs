using System;
using Mau.Runtime;

namespace CH4
{
    /// <summary>
    /// Program 的工具执行面分部——P8 工具组（一期：宿主直执，无 OA）。
    /// 归属：agent 循环基建（宿主侧）——工具声明表 + 执行器路由 + 文本/Mau 执行器。
    /// 二期：执行器不变，入口换 OA 工单（dev_cat.mau 认领）；三期：Roslyn cs.* 域落本面。
    /// </summary>
    public static partial class Program
    {
        /// <summary>
        /// 工具结果截断上限——回传 LLM 上下文防爆（text.read 大文件场景）
        /// </summary>
        private const int MaxToolResultChars = 20000;

        /// <summary>
        /// 构建工具声明表——P8 一期 7 件（文本 4 + Mau 自查 3；ask/read_file 退役——二期待 OA 恢复）
        /// </summary>
        /// <returns>工具数组</returns>
        private static ToolSpec[] BuildToolSpecs()
        {
            ToolSpec[] specs = new ToolSpec[]
            {
                new ToolSpec("text.read", "读取 UTF-8 文本文件（受控根内路径），返回完整内容", "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"要读取的文件路径\"}},\"required\":[\"path\"]}"),
                new ToolSpec("text.write", "覆写文件（含新建）——整文件替换为 content", "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"目标文件路径\"},\"content\":{\"type\":\"string\",\"description\":\"完整新内容\"}},\"required\":[\"path\",\"content\"]}"),
                new ToolSpec("text.append", "追加文本到文件末尾（文件不存在则新建）", "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"目标文件路径\"},\"content\":{\"type\":\"string\",\"description\":\"要追加的文本\"}},\"required\":[\"path\",\"content\"]}"),
                new ToolSpec("text.replace", "替换文本——old 全部出现处替换为 new，返回替换数量；未找到报错", "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"目标文件路径\"},\"old\":{\"type\":\"string\",\"description\":\"要查找的旧文本\"},\"new\":{\"type\":\"string\",\"description\":\"替换后的新文本\"}},\"required\":[\"path\",\"old\",\"new\"]}"),
                new ToolSpec("mau.verify", "Mau 语料全链检查（词法→解析→验证→分析），返回诊断（文件:行:错误码:消息）；零产出", "{\"type\":\"object\",\"properties\":{\"file\":{\"type\":\"string\",\"description\":\".mau 文件路径\"}},\"required\":[\"file\"]}"),
                new ToolSpec("mau.gen", "组翻译——.mauproj 组声明 → 中间产物（验证全组 + BRIKGROUP.cs + FL_*.cs）；不编译", "{\"type\":\"object\",\"properties\":{\"proj\":{\"type\":\"string\",\"description\":\".mauproj 文件路径\"}},\"required\":[\"proj\"]}"),
                new ToolSpec("mau.proj", "组翻译 + 编译——.mauproj → Flows/FL_<组>.dll（长耗时；产物可在宿主热重载）", "{\"type\":\"object\",\"properties\":{\"proj\":{\"type\":\"string\",\"description\":\".mauproj 文件路径\"},\"build\":{\"type\":\"boolean\",\"description\":\"true=翻译后执行 dotnet build\"}},\"required\":[\"proj\"]}")
            };
            return specs;
        }

        /// <summary>
        /// 工具执行器路由——按工具名调度（一期直执；未知工具 ERR）
        /// </summary>
        /// <param name="name">工具名</param>
        /// <param name="argsJson">参数 JSON（展平）</param>
        /// <returns>执行结果（失败 ERR| 前缀——错误可见性）</returns>
        private static string ExecuteTool(string name, string argsJson)
        {
            if (name == "text.read")
            {
                return ExecTextRead(argsJson);
            }
            if (name == "text.write")
            {
                return ExecTextWrite(argsJson);
            }
            if (name == "text.append")
            {
                return ExecTextAppend(argsJson);
            }
            if (name == "text.replace")
            {
                return ExecTextReplace(argsJson);
            }
            if (name == "mau.verify")
            {
                return ExecMauVerify(argsJson);
            }
            if (name == "mau.gen")
            {
                return ExecMauGen(argsJson);
            }
            if (name == "mau.proj")
            {
                return ExecMauProj(argsJson);
            }
            return "ERR|UNKNOWN_TOOL|未知工具: " + name;
        }

        // [段1] 文本工具执行器——FileSystemService 直执（Bootstrap 已 Bind；受控根 = 数据根）

        /// <summary>
        /// text.read——读取 UTF-8 文本文件
        /// </summary>
        /// <param name="argsJson">参数 JSON</param>
        /// <returns>文件内容（超长截断）</returns>
        private static string ExecTextRead(string argsJson)
        {
            string path = ExtractArg(argsJson, "path");
            if (path.Length == 0)
            {
                return "ERR|BAD_ARGS|缺少参数 path";
            }
            try
            {
                FileSystemService fs = ResolveFileSystem();
                if (fs == null)
                {
                    return "ERR|FS_UNAVAILABLE|FileSystemService 未注入";
                }
                return TrimResult(fs.ReadText(path), MaxToolResultChars);
            }
            catch (Exception ex)
            {
                return "ERR|" + ex.GetType().Name + "|" + ex.Message;
            }
        }

        /// <summary>
        /// text.write——覆写文件（含新建；原子写，UTF-8 无 BOM）
        /// </summary>
        /// <param name="argsJson">参数 JSON</param>
        /// <returns>确认文本</returns>
        private static string ExecTextWrite(string argsJson)
        {
            string path = ExtractArg(argsJson, "path");
            string content = ExtractArg(argsJson, "content");
            if (path.Length == 0)
            {
                return "ERR|BAD_ARGS|缺少参数 path";
            }
            try
            {
                FileSystemService fs = ResolveFileSystem();
                if (fs == null)
                {
                    return "ERR|FS_UNAVAILABLE|FileSystemService 未注入";
                }
                fs.WriteText(path, content);
                return "OK 已覆写: " + path + "（" + content.Length.ToString() + " 字符）";
            }
            catch (Exception ex)
            {
                return "ERR|" + ex.GetType().Name + "|" + ex.Message;
            }
        }

        /// <summary>
        /// text.append——追加文本到文件末尾（自动创建父目录）
        /// </summary>
        /// <param name="argsJson">参数 JSON</param>
        /// <returns>确认文本</returns>
        private static string ExecTextAppend(string argsJson)
        {
            string path = ExtractArg(argsJson, "path");
            string content = ExtractArg(argsJson, "content");
            if (path.Length == 0)
            {
                return "ERR|BAD_ARGS|缺少参数 path";
            }
            try
            {
                FileSystemService fs = ResolveFileSystem();
                if (fs == null)
                {
                    return "ERR|FS_UNAVAILABLE|FileSystemService 未注入";
                }
                fs.AppendText(path, content);
                return "OK 已追加: " + path + "（+" + content.Length.ToString() + " 字符）";
            }
            catch (Exception ex)
            {
                return "ERR|" + ex.GetType().Name + "|" + ex.Message;
            }
        }

        /// <summary>
        /// text.replace——替换全部出现处并原子写回（old 未找到报错）
        /// </summary>
        /// <param name="argsJson">参数 JSON</param>
        /// <returns>替换数量确认</returns>
        private static string ExecTextReplace(string argsJson)
        {
            string path = ExtractArg(argsJson, "path");
            string oldText = ExtractArg(argsJson, "old");
            string newText = ExtractArg(argsJson, "new");
            if (path.Length == 0 || oldText.Length == 0)
            {
                return "ERR|BAD_ARGS|缺少参数 path 或 old";
            }
            try
            {
                FileSystemService fs = ResolveFileSystem();
                if (fs == null)
                {
                    return "ERR|FS_UNAVAILABLE|FileSystemService 未注入";
                }
                int count = fs.ReplaceText(path, oldText, newText);
                if (count == 0)
                {
                    return "ERR|NOT_FOUND|文件 " + path + " 中未找到目标文本";
                }
                return "OK 替换完成: " + count.ToString() + " 处（" + path + "）";
            }
            catch (Exception ex)
            {
                return "ERR|" + ex.GetType().Name + "|" + ex.Message;
            }
        }

        // [段2] Mau 自查执行器——B3 落地（mau.verify 直调 MauCompilerV3；mau.gen/proj 需 MauProjFile 下沉共享库）

        /// <summary>
        /// mau.verify——Mau 语料全链检查（B3 实现；一期占位）
        /// </summary>
        /// <param name="argsJson">参数 JSON</param>
        /// <returns>诊断文本</returns>
        private static string ExecMauVerify(string argsJson)
        {
            return "ERR|NOT_IMPLEMENTED|mau.verify 一期占位——B3 落地（MauCompilerV3 进程内直调）";
        }

        /// <summary>
        /// mau.gen——组翻译中间产物（B3 实现；一期占位）
        /// </summary>
        /// <param name="argsJson">参数 JSON</param>
        /// <returns>产物清单</returns>
        private static string ExecMauGen(string argsJson)
        {
            return "ERR|NOT_IMPLEMENTED|mau.gen 一期占位——B3 落地（MauProjFile 下沉共享库）";
        }

        /// <summary>
        /// mau.proj——组翻译 + 编译（B3 实现；一期占位）
        /// </summary>
        /// <param name="argsJson">参数 JSON</param>
        /// <returns>编译结果</returns>
        private static string ExecMauProj(string argsJson)
        {
            return "ERR|NOT_IMPLEMENTED|mau.proj 一期占位——B3 落地（MauProjFile 下沉共享库）";
        }

        /// <summary>
        /// 解析 FileSystemService——DataBox 服务解析（未注入返回 null）
        /// </summary>
        /// <returns>文件系统服务或 null</returns>
        private static FileSystemService ResolveFileSystem()
        {
            FileSystemService fs;
            if (DataBox.TryResolve<FileSystemService>(out fs))
            {
                return fs;
            }
            return null;
        }

        /// <summary>
        /// 工具结果截断——超长文本保留头部 + 截断提示（上下文防爆）
        /// </summary>
        /// <param name="text">原文</param>
        /// <param name="max">上限字符数</param>
        /// <returns>截断文本</returns>
        private static string TrimResult(string text, int max)
        {
            if (text == null || text.Length == 0)
            {
                return "";
            }
            if (text.Length <= max)
            {
                return text;
            }
            return text.Substring(0, max) + System.Environment.NewLine + "…[截断: 共 " + text.Length.ToString() + " 字符，仅保留前 " + max.ToString() + "]";
        }
    }
}
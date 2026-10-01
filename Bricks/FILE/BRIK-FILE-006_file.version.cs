// ═══════════════════════════════════════════════════
// 积木: file.version
// ID:   BRIK-FILE-006
// 类别: FILE
// 作用: 读取 PE 文件（exe/dll）版本信息——版本三件（Product/File/回落值）+ 修改时间 + 大小；非 PE / 无版本资源明确报错（失败可见）——LLM 工具 file-version 语料执行面
// 依赖: 无
// 引用: Mau.Runtime（FileSystemService/DataBox）
// 原理: FileSystemRegistry/DataBox 解析 FileSystemService → ReadVersionInfo(path)；argsJson 内解析 path
// 常用: FileCat 认领线——'file.version'[@args] > @result
// ═══════════════════════════════════════════════════
using System;
using System.Text.Json;
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 文件积木——file-version 版本信息读取（LLM 工具执行面：参数整包 argsJson）
    /// </summary>
    public static class FileVersionBrick
    {
        /// <summary>
        /// 读取文件版本信息（PE 文件）
        /// </summary>
        /// <param name="argsJson">工具参数 JSON（path）</param>
        /// <param name="result">版本信息文本或 ERR| 错误文本</param>
        /// <returns>true=执行成功</returns>
        public static bool Version(string argsJson, out string result)
        {
            result = "";
            // [参数面] 声明面口径零容忍——未知 / 缺值一律 ERR|BAD_ARGS（catId 保留键放行）
            string badArgs = JsonArgs.Validate(argsJson, "path", "path", "", "");
            if (badArgs.Length > 0)
            {
                result = badArgs;
                return false;
            }
            string path = JsonArgs.Get(argsJson, "path");
            if (path == "§PARSE_FAIL§")
            {
                result = "ERR|BAD_ARGS|工具参数 JSON 解析失败（LLM 生成参数可能被截断——超长内容请分段写入）";
                return false;
            }
            if (path.Length == 0)
            {
                result = "ERR|BAD_ARGS|缺少参数 path";
                return false;
            }
            try
            {
                FileSystemService? fs = FileSystemRegistry.ResolveScoped(JsonArgs.Get(argsJson, "catId"));
                if (fs == null)
                {
                    DataBox.TryResolve<FileSystemService>(out fs);
                }
                if (fs == null)
                {
                    result = "ERR|FS_NO_SERVICE|宿主未注入 FileSystemService";
                    return false;
                }
                result = fs.ReadVersionInfo(path);
                return true;
            }
            catch (Exception ex)
            {
                result = "ERR|" + ex.GetType().Name + "|" + ex.Message;
                return false;
            }
        }
    }
}
// #MAU_CHECKSUM:SHA256:D098A9DD3AAE53BF704D7389293203D00B6BC372B3441060211835F05A07452B

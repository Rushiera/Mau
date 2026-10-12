// ═══════════════════════════════════════════════════
// 积木: file.version
// ID:   BRIK-FILE-006
// 类别: FILE
// 作用: 读取 PE 文件（exe/dll）版本信息——版本三件（Product/File/回落值）+ 修改时间 + 大小；非 PE / 无版本资源明确报错（失败可见）——LLM 工具 file-version 语料执行面
// 依赖: 无
// 引用: Mau.Runtime（FileSystemService / FsVersionFacts / DataBox）
// 原理: FileSystemRegistry/DataBox 解析 FileSystemService → VersionFacts(path)；展示文案（回落注记 / (无) 形态）归积木层（A228 档 2 · B3）；argsJson 内解析 path
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
                FsVersionFacts facts = fs.VersionFacts(path);
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                // [展示层] 文案与回落注记归积木层（A228 档 2 · B3）
                sb.Append("路径: ").Append(facts.ResolvedPath);
                sb.Append("\n版本: ").Append(facts.Version);
                if (facts.FallbackUsed)
                {
                    sb.Append("（ProductVersion 为空——回落 FileVersion）");
                }
                sb.Append("\nFileVersion: ").Append(facts.FileVersion.Length == 0 ? "(无)" : facts.FileVersion);
                sb.Append("\nProductVersion: ").Append(facts.ProductVersion.Length == 0 ? "(无)" : facts.ProductVersion);
                sb.Append("\n修改时间: ").Append(facts.LastWrite.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.Append("\n大小: ").Append(facts.SizeBytes.ToString()).Append(" 字节");
                result = MetaHead("file-version", true, path, -1) + "\n" + path + " | " + sb.ToString();
                return true;
            }
            catch (Exception ex)
            {
                result = "ERR|" + ex.GetType().Name + "|" + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 结构化元数据头（统一口径·A214）——恒定 ok / tool + 主来源 target + 主计数 items（空串 / 负值 = 省略）。
        /// </summary>
        /// <param name="tool">工具名</param>
        /// <param name="ok">成败</param>
        /// <param name="target">主来源（空串 = 省略）</param>
        /// <param name="items">主计数（负值 = 省略）</param>
        /// <returns>单行 JSON</returns>
        private static string MetaHead(string tool, bool ok, string target, int items)
        {
            System.Collections.Generic.Dictionary<string, object> head = new System.Collections.Generic.Dictionary<string, object>();
            head["ok"] = ok;
            head["tool"] = tool;
            if (target.Length > 0)
            {
                head["target"] = target;
            }
            if (items >= 0)
            {
                head["items"] = items;
            }
            return JsonSerializer.Serialize(head);
        }
    }
}
// #MAU_CHECKSUM:SHA256:2C600CD0311F3340188D1260C9ABD34EF96C7D37762D921154E7162D69042352

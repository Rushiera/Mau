// ═══════════════════════════════════════════════════
// 积木: file.copy
// ID:   BRIK-FILE-005
// 类别: FILE
// 作用: 复制——文件与目录均支持（目录=整棵子树复制）；自动创建目标父目录；目标已存在拒绝——LLM 工具 file-copy 语料执行面
// 依赖: 无
// 引用: Mau.Runtime（FileSystemService/DataBox）
// 原理: DataBox.TryResolve<FileSystemService> → Copy(src, dest)；argsJson 内解析 src/dest
// 常用: FileCat 认领线——'file.copy'[@args] > @result
// ═══════════════════════════════════════════════════
using System;
using System.Text.Json;
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 文件积木——file-copy 复制（LLM 工具执行面：参数整包 argsJson）
    /// </summary>
    public static class FileCopyBrick
    {
        /// <summary>
        /// 复制文件/目录
        /// </summary>
        /// <param name="argsJson">工具参数 JSON（src/dest）</param>
        /// <param name="result">确认文本或 ERR| 错误文本</param>
        /// <returns>true=执行成功</returns>
        public static bool Copy(string argsJson, out string result)
        {
            result = "";
            // [参数面] 声明面口径零容忍——未知 / 缺值一律 ERR|BAD_ARGS（catId 保留键放行）
            string badArgs = JsonArgs.Validate(argsJson, "src dest", "src dest", "", "");
            if (badArgs.Length > 0)
            {
                result = badArgs;
                return false;
            }
            string src = JsonArgs.Get(argsJson, "src");
            string dest = JsonArgs.Get(argsJson, "dest");
            if (src == "§PARSE_FAIL§" || dest == "§PARSE_FAIL§")
            {
                result = "ERR|BAD_ARGS|工具参数 JSON 解析失败（LLM 生成参数可能被截断——超长内容请分段写入）";
                return false;
            }
            if (src.Length == 0 || dest.Length == 0)
            {
                result = "ERR|BAD_ARGS|缺少参数 src 或 dest";
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
                fs.Copy(src, dest);
                result = MetaHead("file-copy", true, src, -1) + "\n" + src + " → " + dest;
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
// #MAU_CHECKSUM:SHA256:91FDFB14AC852F7AB472CD7CBFACEC441D632E3472F1A4C940A3E92241D01B1B

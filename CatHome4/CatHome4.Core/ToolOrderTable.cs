using System;
using System.Collections.Generic;
using System.Text.Json;

namespace CH4
{
    /// <summary>
    /// 工具执行序表——工具分批调度的内核裁决面（A127 · design-ch4-tools §三·十四）。
    /// order 是宿主内部静态表，**不是 LLM 可写参数**：无论 LLM 以何序返回工具调用，一律按 order 分桶执行
    /// （同值一批 · 值升序 · 批间串行 / 批内并发）。LLM 知道机制存在（工具定义注释 + L1 元规则），
    /// 但调用序本身不承载语义。
    /// 档位：-100 timeback(start) 钉死 / -1 只读 / 0 默认 / 1 写入变更 / 2 构建执行部署 / 100 timeback(back) 钉死。
    /// 默认 0 落在只读之后、写入之前——未登记工具不会插到只读工具前面。
    /// 维护：全量登记——新增工具必须入表，对账门禁（ToolOrderTableTests）会红。
    /// </summary>
    public static class ToolOrderTable
    {
        /// <summary>钉死档——timeback(action=start)：会话结构约束优先，忽略 LLM 调用序</summary>
        public const int OrderTimebackStart = -100;

        /// <summary>只读 / 无副作用档</summary>
        public const int OrderReadOnly = -1;

        /// <summary>默认档——未登记回落值；也承载不产生外部副作用、又不属只读观测的工具（sleep / timer）</summary>
        public const int OrderDefault = 0;

        /// <summary>写入 / 变更档</summary>
        public const int OrderWrite = 1;

        /// <summary>构建 / 执行 / 部署档</summary>
        public const int OrderBuild = 2;

        /// <summary>钉死档——timeback(action=back)：会话结构约束优先，忽略 LLM 调用序</summary>
        public const int OrderTimebackBack = 100;

        /// <summary>只读档清单（-1）——观测 / 查询类，无外部副作用</summary>
        private static readonly string[] ReadOnlyNames =
        {
            "text-read", "text-read_lines", "text-read_between", "text-grep",
            "file-tree", "file-find", "file-version",
            "cs-check", "cs-list", "cs-read", "cs-find_ref", "cs-find", "cs-dead", "cs-comment_check",
            "mau-verify",
            "config-list", "config-get", "config-cat-get",
            "web-search", "image-analyze",
            "temp-info",
            "browser-open", "browser-read", "browser-shot",
            "majordomo-catinfo",
            "host-flows", "info", "time", "random", "pack", "Note"
        };

        /// <summary>写入档清单（1）——改文件 / 改配置 / 改仓库产物</summary>
        private static readonly string[] WriteNames =
        {
            "text-write", "text-append", "text-replace",
            "file-move", "file-delete", "file-copy",
            "cs-patch", "cs-member", "cs-comment", "cs-format",
            "config-set", "config-reset", "config-cat-set",
            "image-inject",
            "browser-eval", "browser-tabs"
        };

        /// <summary>构建 / 执行 / 部署档清单（2）——编译、外部通道执行、宿主动作</summary>
        private static readonly string[] BuildNames =
        {
            "powershell", "powershell7",
            "cs-build",
            "mau-gen", "mau-proj", "host-reload", "mau-setup",
            "majordomo-restart", "majordomo-cmd",
            "temp-exec"
        };

        /// <summary>默认档清单（0）显式登记——登记在案，对账门禁以「全量登记」为判据</summary>
        private static readonly string[] DefaultNames =
        {
            "sleep", "timer"
        };

        /// <summary>钉死档清单——值由调用参数（action）决定，非静态可解析；对账面须登记，裁决走 Resolve(name, args)</summary>
        private static readonly string[] PinnedNames =
        {
            "timeback"
        };

        /// <summary>
        /// 解析工具执行序——参数无关面（配置面 / 定义注记用）。timeback 取代表值前需带 action，
        /// 无 action 语义时落默认档（钉死值由带参重载裁决）。
        /// </summary>
        /// <param name="name">工具名</param>
        /// <returns>执行序值（未登记 = 默认档）</returns>
        public static int Resolve(string name)
        {
            if (name == null || name.Length == 0)
            {
                return OrderDefault;
            }
            if (Contains(ReadOnlyNames, name))
            {
                return OrderReadOnly;
            }
            if (Contains(WriteNames, name))
            {
                return OrderWrite;
            }
            if (Contains(BuildNames, name))
            {
                return OrderBuild;
            }
            return OrderDefault;
        }

        /// <summary>
        /// 解析工具执行序——带参数面（分桶裁决用）。timeback 按 action 取钉死值：
        /// start = -100 / back = 100；action 缺失或非法落默认档（参数面由工具校验层另行拒绝）。
        /// </summary>
        /// <param name="name">工具名</param>
        /// <param name="argsJson">参数 JSON</param>
        /// <returns>执行序值</returns>
        public static int Resolve(string name, string argsJson)
        {
            if (name == "timeback")
            {
                string action = ExtractAction(argsJson);
                if (action == "start")
                {
                    return OrderTimebackStart;
                }
                if (action == "back")
                {
                    return OrderTimebackBack;
                }
                return OrderDefault;
            }
            return Resolve(name);
        }

        /// <summary>
        /// 执行序显示文本——toolcard 载荷与配置面共用出口（前端零裁决）。
        /// timeback 双钉死值（按 action 分走两端）→ 显示 "-100/100"；其余为单值数字串。
        /// </summary>
        /// <param name="name">工具名</param>
        /// <returns>显示文本（未知工具 = 默认档文本）</returns>
        public static string OrderText(string name)
        {
            if (name == "timeback")
            {
                return OrderTimebackStart.ToString() + "/" + OrderTimebackBack.ToString();
            }
            return Resolve(name).ToString();
        }

        /// <summary>
        /// 工具定义标准注记——ToolPool.RebuildAll 单点注入（工具描述尾行）。
        /// 形态与 LLM 面约定一致：`order: &lt;n&gt;`（timeback 双值并列）。
        /// </summary>
        /// <param name="name">工具名</param>
        /// <returns>注记行（永不为空）</returns>
        public static string NoteLine(string name)
        {
            return "order: " + OrderText(name);
        }

        /// <summary>
        /// 已登记工具名全集——对账门禁基准（ToolPool 全量工具名须逐一登记）。
        /// </summary>
        /// <returns>已登记工具名数组（含默认档显式登记项）</returns>
        public static string[] RegisteredNames()
        {
            string[] all = new string[ReadOnlyNames.Length + WriteNames.Length + BuildNames.Length + DefaultNames.Length + PinnedNames.Length];
            int cursor = 0;
            for (int i = 0; i < ReadOnlyNames.Length; i = i + 1)
            {
                all[cursor] = ReadOnlyNames[i];
                cursor = cursor + 1;
            }
            for (int i = 0; i < WriteNames.Length; i = i + 1)
            {
                all[cursor] = WriteNames[i];
                cursor = cursor + 1;
            }
            for (int i = 0; i < BuildNames.Length; i = i + 1)
            {
                all[cursor] = BuildNames[i];
                cursor = cursor + 1;
            }
            for (int i = 0; i < DefaultNames.Length; i = i + 1)
            {
                all[cursor] = DefaultNames[i];
                cursor = cursor + 1;
            }
            for (int i = 0; i < PinnedNames.Length; i = i + 1)
            {
                all[cursor] = PinnedNames[i];
                cursor = cursor + 1;
            }
            return all;
        }

        /// <summary>
        /// 未登记工具名——对账门禁（A127）：池内工具必须逐一登记，未登记者静默落默认档。
        /// 调用面：宿主启动装配（LogToolOrderAccounting）；测试面：ToolOrderTests。
        /// </summary>
        /// <param name="names">工具名清单（池全量）</param>
        /// <returns>未登记名数组（顺序同入参；空 = 全量已登记）</returns>
        public static string[] FindUnregistered(string[] names)
        {
            if (names == null)
            {
                return new string[0];
            }
            string[] registered = RegisteredNames();
            List<string> missing = new List<string>();
            for (int i = 0; i < names.Length; i = i + 1)
            {
                if (names[i] == null || names[i].Length == 0)
                {
                    continue;
                }
                if (!Contains(registered, names[i]))
                {
                    missing.Add(names[i]);
                }
            }
            return missing.ToArray();
        }

        /// <summary>
        /// 名单线性查找——名单规模小（＜70），线性查找免建索引（勿增实体）。
        /// </summary>
        /// <param name="names">名单</param>
        /// <param name="name">工具名</param>
        /// <returns>true=命中</returns>
        private static bool Contains(string[] names, string name)
        {
            for (int i = 0; i < names.Length; i = i + 1)
            {
                if (string.Equals(names[i], name, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 取 timeback action 字段——缺失 / 非字符串 / JSON 不可析一律空串（调用方按默认档处理）。
        /// </summary>
        /// <param name="argsJson">参数 JSON</param>
        /// <returns>action 文本（空 = 未取到）</returns>
        private static string ExtractAction(string argsJson)
        {
            if (argsJson == null || argsJson.Length == 0)
            {
                return "";
            }
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(argsJson))
                {
                    JsonElement root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object)
                    {
                        return "";
                    }
                    JsonElement actionEl;
                    if (!root.TryGetProperty("action", out actionEl) || actionEl.ValueKind != JsonValueKind.String)
                    {
                        return "";
                    }
                    string action = actionEl.GetString();
                    if (action == null)
                    {
                        return "";
                    }
                    return action.Trim();
                }
            }
            catch (Exception)
            {
                return "";
            }
        }
    }
}

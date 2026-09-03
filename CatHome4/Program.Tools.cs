using System;
using System.Collections.Generic;
using Mau.Runtime;
using Mau.Development;

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
        /// 工具结果截断上限——回传 LLM 上下文防爆（text-read 大文件场景）
        /// </summary>
        private const int MaxToolResultChars = 20000;

        /// <summary>内置全量工具名清单——BuildToolSpecs 派生缓存（单一真相源；M2c 读/写比对基准）</summary>
        private static string[] _allToolNames;

        /// <summary>
        /// 内置全量工具名清单——懒加载从 BuildToolSpecs 派生（声明表是唯一真相源，清单不手工维护）。
        /// </summary>
        /// <returns>全量工具名数组</returns>
        private static string[] GetAllToolNames()
        {
            if (_allToolNames == null)
            {
                ToolSpec[] specs = ToolRegistry.BuildSpecs();
                _allToolNames = new string[specs.Length];
                for (int i = 0; i < specs.Length; i++)
                {
                    _allToolNames[i] = specs[i].Name;
                }
            }
            return _allToolNames;
        }

        /// <summary>
        /// 工具名比对——线性扫描内置清单（21 件量级，线性够用）。
        /// </summary>
        /// <param name="names">清单数组</param>
        /// <param name="name">目标名</param>
        /// <returns>true=在清单内</returns>
        private static bool ContainsToolName(string[] names, string name)
        {
            for (int i = 0; i < names.Length; i++)
            {
                if (string.Equals(names[i], name, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 工具名单解析——读时比对（M2c 外部损坏防御：本地持久化可能被外部改坏）。
        /// 语义：空/缺省 → 全量保底；含 * → 全量；逐个比对内置清单过滤非法名；过滤后全空 → 全量保底。
        /// </summary>
        /// <param name="raw">cat.cfg toolNames 原始串（逗号/空白分隔）</param>
        /// <returns>合法工具名数组（保底全量）</returns>
        private static string[] ResolveToolNames(string raw)
        {
            string[] all = GetAllToolNames();
            if (raw == null || raw.Trim().Length == 0)
            {
                return all;
            }
            string[] parts = raw.Split(new char[] { ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            List<string> names = new List<string>();
            for (int i = 0; i < parts.Length; i++)
            {
                string name = parts[i].Trim();
                if (name == "*")
                {
                    return all;
                }
                if (ContainsToolName(all, name))
                {
                    names.Add(name);
                }
            }
            if (names.Count == 0)
            {
                return all;
            }
            return names.ToArray();
        }

        /// <summary>
        /// 工具名单写时校验——序列化落盘前比对内置清单（M2c：非法名剔除，合法名逗号重拼）。
        /// 语义：空 → 空串（全量语义）；* 保留；非法名剔除；全非法 → 空串（全量保底）。
        /// </summary>
        /// <param name="raw">待写入工具名单原始串</param>
        /// <returns>过滤后逗号清单（空串=全量语义）</returns>
        private static string ValidateToolNames(string raw)
        {
            if (raw == null || raw.Trim().Length == 0)
            {
                return "";
            }
            string[] all = GetAllToolNames();
            string[] parts = raw.Split(new char[] { ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            List<string> names = new List<string>();
            for (int i = 0; i < parts.Length; i++)
            {
                string name = parts[i].Trim();
                if (name == "*" || ContainsToolName(all, name))
                {
                    names.Add(name);
                }
            }
            return string.Join(",", names.ToArray());
        }

        /// <summary>
        /// 工具声明面裁剪——按名单从全量表取子集（M2c 每会话独立声明面；保序）。
        /// </summary>
        /// <param name="names">合法工具名数组（ResolveToolNames 产物）</param>
        /// <returns>裁剪后工具数组；名单空 → 全量</returns>
        private static ToolSpec[] FilterToolSpecs(string[] names)
        {
            ToolSpec[] all = ToolRegistry.BuildSpecs();
            if (names == null || names.Length == 0)
            {
                return all;
            }
            List<ToolSpec> list = new List<ToolSpec>();
            for (int i = 0; i < all.Length; i++)
            {
                if (ContainsToolName(names, all[i].Name))
                {
                    list.Add(all[i]);
                }
            }
            if (list.Count == 0)
            {
                return all;
            }
            return list.ToArray();
        }

        /// <summary>
        /// 内置工具定义源——宿主内建小表（本质也是 BRIK，只是内置：Note/time/random/info/host-*）。
        /// 格式与 Flow.GetToolsJson 同构（{"group":"","tools":[...]}）——同一解析器，统一工具池。
        /// </summary>
        /// <returns>内置工具定义 JSON</returns>
        private static string BuildBuiltinToolsJson()
        {
            return "{\"group\":\"\",\"tools\":[" +
                "{\"name\":\"Note\",\"description\":\"轻量任务追踪器（内存存储，会话关闭即消失）。无参数=推进到下一条；action='set'+content='任务1\\\\n任务2'=写入新计划（已有未完成需force=true强制覆盖）。返回当前第X/Y条 已完成Z 待完成W 任务目标：... 最后一条时追加提示（已是最后一条需求，完成后可结束本轮）。全部完成后自动清空。剩余1条时引擎不自动拉起。\",\"parameters\":{\"type\":\"object\",\"properties\":{\"action\":{\"type\":\"string\",\"description\":\"set=写入新计划，不传=推进\"},\"content\":{\"type\":\"string\",\"description\":\"action=set时必填，\\\\n分割\"},\"force\":{\"type\":\"boolean\",\"description\":\"覆盖已有未完成计划时传true\"}},\"required\":[]}}," +
                "{\"name\":\"time\",\"description\":\"当前系统日期时间（yyyy-MM-dd HH:mm:ss）——会话内直执，无需 OA\",\"parameters\":{\"type\":\"object\",\"properties\":{}}}," +
                "{\"name\":\"random\",\"description\":\"生成 [min, max) 范围内的随机整数（min 含下限，max 不含上限，要求 min &lt; max）——会话内直执，无需 OA\",\"parameters\":{\"type\":\"object\",\"properties\":{\"min\":{\"type\":\"integer\",\"description\":\"随机范围下限（含）\"},\"max\":{\"type\":\"integer\",\"description\":\"随机范围上限（不含）\"}},\"required\":[\"min\",\"max\"]}}," +
                "{\"name\":\"info\",\"description\":\"查看运行时工具注册表——工具清单/参数/归属工具组 Flow/内置状态（agent 的眼睛；R1.2）\",\"parameters\":{\"type\":\"object\",\"properties\":{}}}," +
                "{\"name\":\"host-reload\",\"description\":\"热重载语料 dll（宿主级）——在 mau-proj 编译成功后单独调用（建议下一轮）；事务三段式：加载失败保留旧版本；cat=quick|dev\",\"parameters\":{\"type\":\"object\",\"properties\":{\"cat\":{\"type\":\"string\",\"description\":\"quick|dev\"}},\"required\":[\"cat\"]}}" +
                "]}";
        }

        /// <summary>
        /// 工具执行器路由——按工具名调度（一期直执；未知工具 ERR）
        /// </summary>
        /// <param name="name">工具名</param>
        /// <param name="argsJson">参数 JSON（展平）</param>
        /// <returns>执行结果（失败 ERR| 前缀——错误可见性）</returns>
        private static string ExecuteTool(string name, string argsJson)
{
            if (name == "text-read")
            {
                return ExecTextRead(argsJson);
            }
            if (name == "text-write")
            {
                return ExecTextWrite(argsJson);
            }
            if (name == "text-append")
            {
                return ExecTextAppend(argsJson);
            }
            if (name == "text-replace")
            {
                return ExecTextReplace(argsJson);
            }
            if (name == "text-read_lines")
            {
                return ExecTextReadLines(argsJson);
            }
            if (name == "text-read_between")
            {
                return ExecTextReadBetween(argsJson);
            }
            if (name == "text-tree")
            {
                return ExecTextTree(argsJson);
            }
            if (name == "text-find")
            {
                return ExecTextFind(argsJson);
            }
            if (name == "text-grep")
            {
                return ExecTextGrep(argsJson);
            }
            if (name == "text-move")
            {
                return ExecTextMove(argsJson);
            }
            if (name == "text-delete")
            {
                return ExecTextDelete(argsJson);
            }
            if (name == "mau-verify")
            {
                return ExecMauVerify(argsJson);
            }
            if (name == "mau-gen")
            {
                return ExecMauGen(argsJson);
            }
            if (name == "mau-proj")
            {
                return ExecMauProj(argsJson);
            }
            if (name == "host-reload")
            {
                return ExecHostReload(argsJson);
            }
            if (name == "web-search")
            {
                return ExecWebSearch(argsJson);
            }
            if (name == "image-analyze")
            {
                return ExecImageAnalyze(argsJson);
            }
            if (name == "powershell")
            {
                return ExecPowerShell(argsJson);
            }
            if (name.StartsWith("cs-", StringComparison.Ordinal))
            {
                return ExecCSharpTool(name, argsJson);
            }
            return "ERR|UNKNOWN_TOOL|未知工具: " + name;
        }
        // [段3] C# 工具桥执行器——P8 三期（cs-* 9 件经 ICSharpBridge/MauRoslynBridge 调度：磁盘权威 + 三态缓存 + 回滚保护）

        /// <summary>
        /// cs-* 统一执行——桥内分派（method = 工具名去 cs- 前缀）；结果截断防爆
        /// </summary>
        /// <param name="name">工具名（cs-check 等）</param>
        /// <param name="argsJson">参数整包 JSON</param>
        /// <returns>桥结果文本（OK/ROLLED_BACK/ERR| 语义）</returns>
        private static string ExecCSharpTool(string name, string argsJson)
        {
            Mau.Runtime.ICSharpBridge bridge;
            if (!DataBox.TryResolve<Mau.Runtime.ICSharpBridge>(out bridge))
            {
                return "ERR|CSHARP_NO_BRIDGE|宿主未注入 ICSharpBridge（Bootstrap 需 Bind MauRoslynBridge）";
            }
            string method = name.Substring(3);
            string result;
            bool ok = bridge.Invoke(method, argsJson, out result);
            if (!ok || result == null || result.Length == 0)
            {
                return "ERR|BRIDGE_FAIL|cs-" + method + " 调用失败（" + (result ?? "空结果") + "）";
            }
            return TrimResult(result, MaxToolResultChars);
        }

        /// <summary>
        /// web-search——联网搜索（R2.1；FALLBACK 直执保底面——主路径 SearchCat Flow 认领）
        /// </summary>
        /// <param name="argsJson">工具参数 JSON（query）</param>
        /// <returns>搜索回答（失败 ERR| 前缀）</returns>
        private static string ExecWebSearch(string argsJson)
        {
            string query = ExtractArg(argsJson, "query");
            if (query.Length == 0)
            {
                return "ERR|BAD_ARGS|缺少参数 query";
            }
            Mau.Runtime.IWebSearchService service;
            if (!DataBox.TryResolve<Mau.Runtime.IWebSearchService>(out service))
            {
                return "ERR|WEB_NO_SERVICE|宿主未注入 IWebSearchService";
            }
            return TrimResult(service.Search(query), MaxToolResultChars);
        }

        /// <summary>
        /// image-analyze——图像识别（R2.2；FALLBACK 直执保底面——主路径 VisionCat Flow 认领）
        /// </summary>
        /// <param name="argsJson">工具参数 JSON（path/question）</param>
        /// <returns>分析结果（失败 ERR| 前缀）</returns>
        private static string ExecImageAnalyze(string argsJson)
        {
            string path = ExtractArg(argsJson, "path");
            if (path.Length == 0)
            {
                return "ERR|BAD_ARGS|缺少参数 path";
            }
            string question = ExtractArg(argsJson, "question");
            Mau.Runtime.IVisionService service;
            if (!DataBox.TryResolve<Mau.Runtime.IVisionService>(out service))
            {
                return "ERR|VISION_NO_SERVICE|宿主未注入 IVisionService";
            }
            return TrimResult(service.Analyze(path, question), MaxToolResultChars);
        }

        /// <summary>
        /// powershell——执行 PowerShell 命令（PsCat；FALLBACK 直执保底面——主路径 PsCat Flow 认领）
        /// </summary>
        /// <param name="argsJson">工具参数 JSON（command/cwd/timeout_ms）</param>
        /// <returns>结果 JSON（失败 ERR| 前缀）</returns>
        private static string ExecPowerShell(string argsJson)
        {
            Mau.Runtime.IPsService service;
            if (!DataBox.TryResolve<Mau.Runtime.IPsService>(out service))
            {
                return "ERR|PS_NO_SERVICE|宿主未注入 IPsService";
            }
            return TrimResult(service.Exec(argsJson), MaxToolResultChars);
        }
    }
}
using System;
using System.Collections.Generic;
using Mau.Runtime;
using Mau.Development;

namespace CH4
{
    /// <summary>
    /// Program 的工具执行面分部——工具声明表 + 名单管理 + 内置工具定义源（P8 工具组）。
    /// 归属：agent 循环基建（宿主侧）。工具执行只走 OA 认领线（工具组 Flow）——宿主直执仅 host-reload 保留。
    /// </summary>
    public static partial class Program
    {
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
                "{\"name\":\"host-reload\",\"description\":\"热重载语料 dll（宿主级）——在 mau-proj 编译成功后单独调用（建议下一轮）；事务三段式：加载失败保留旧版本；cat=已加载 Flow 注册名（host-flows 查当前清单）\",\"parameters\":{\"type\":\"object\",\"properties\":{\"cat\":{\"type\":\"string\",\"description\":\"Flow 注册名（QuickCat/TextCat/MauCat/CsCat/ConfigCat/SearchCat/VisionCat/TempToolCat/PsCat——host-flows 查当前全部）\"}},\"required\":[\"cat\"]}}," +
                "{\"name\":\"host-flows\",\"description\":\"查看当前运行 Flow 现状（宿主级）——Registry 动态面：每个已加载 Flow 的 Id/Name/Kind/句柄状态/dll 路径；新增/重载后查询目标清单用\",\"parameters\":{\"type\":\"object\",\"properties\":{}}}" +
                "]}";
        }

        /// <summary>
        /// 工具执行器路由——按工具名调度（宿主直执面：仅 host-* 宿主级工具；其余工具一律 OA 认领）
        /// </summary>
        /// <param name="name">工具名</param>
        /// <param name="argsJson">参数 JSON（展平）</param>
        /// <returns>执行结果（失败 ERR| 前缀——错误可见性）</returns>
        private static string ExecuteTool(string name, string argsJson)
        {
            if (name == "host-reload")
            {
                return ExecHostReload(argsJson);
            }
            if (name == "host-flows")
            {
                return ExecHostFlows(argsJson);
            }
            return "ERR|UNKNOWN_TOOL|未知工具: " + name;
        }

    }
}
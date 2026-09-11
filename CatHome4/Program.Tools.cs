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
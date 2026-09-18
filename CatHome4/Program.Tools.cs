using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
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
            // 结构化构建——匿名对象树 → JsonSerializer（R6-P3-08：原手写字符串拼接易漏转义、难维护）
            object[] tools = new object[7];
            tools[0] = new
            {
                name = "Note",
                description = "轻量任务追踪器（内存存储，会话关闭即消失）。无参数=推进到下一条；action='set'+content='任务1\\n任务2'=写入新计划（已有未完成需force=true强制覆盖）。返回当前第X/Y条 已完成Z 待完成W 任务目标：... 最后一条时追加提示（已是最后一条需求，完成后可结束本轮）。全部完成后自动清空。剩余1条时引擎不自动拉起。",
                parameters = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                            {
                                { "action", new { type = "string", description = "set=写入新计划，不传=推进" } },
                                { "content", new { type = "string", description = "action=set时必填，\\n分割" } },
                                { "force", new { type = "boolean", description = "覆盖已有未完成计划时传true" } }
                            },
                    required = new string[0]
                }
            };
            tools[1] = new
            {
                name = "time",
                description = "当前系统日期时间（yyyy-MM-dd HH:mm:ss）——会话内直执，无需 OA",
                parameters = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>()
                }
            };
            tools[2] = new
            {
                name = "random",
                description = "生成 [min, max) 范围内的随机整数（min 含下限，max 不含上限，要求 min < max）——会话内直执，无需 OA",
                parameters = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                            {
                                { "min", new { type = "integer", description = "随机范围下限（含）" } },
                                { "max", new { type = "integer", description = "随机范围上限（不含）" } }
                            },
                    required = new string[] { "min", "max" }
                }
            };
            tools[3] = new
            {
                name = "info",
                description = "本会话环境自省——返回分类 JSON 块：version（版本 + 编译时刻）/ time（当前时间）/ llm（本猫生效端点）/ endpoint（本地端点：对话页 + 管理面板）/ roots（可见受控根）/ tokens（前文长度）/ packs（挂载包）/ qqbot（渠道说明）（agent 的眼睛；R1.2）",
                parameters = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>()
                }
            };
            tools[4] = new
            {
                name = "host-reload",
                description = "热重载语料 dll（宿主级）——在 mau-proj 编译成功后单独调用（建议下一轮）；事务三段式：加载失败保留旧版本；cat=已加载 Flow 注册名（host-flows 查当前清单）",
                parameters = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                            {
                                { "cat", new { type = "string", description = "Flow 注册名（QuickCat/TextCat/MauCat/CsCat/ConfigCat/SearchCat/VisionCat/TempToolCat/PsCat——host-flows 查当前全部）" } }
                            },
                    required = new string[] { "cat" }
                }
            };
            tools[5] = new
            {
                name = "host-flows",
                description = "查看当前运行 Flow 现状（宿主级）——Registry 动态面：每个已加载 Flow 的 Id/Name/Kind/句柄状态/dll 路径；新增/重载后查询目标清单用",
                parameters = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>()
                }
            };
            tools[6] = new
            {
                name = "pack",
                description = "加载包注入——按 key 注入预设文件包（池 packs.json 定义 + 本猫挂载授权），一次调用完成加载；返回逐件明细（件数/字符数/失败原因）。收工/蒸馏/审查等跨身份动作的前置加载入口。",
                parameters = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                            {
                                { "key", new { type = "string", description = "包 key（info 可见本猫已挂载包）" } }
                            },
                    required = new string[] { "key" }
                }
            };
            // 宽松转义——避免 < > 被编码为 \u003C（与旧手写字符串语义一致）
            JsonSerializerOptions options = new JsonSerializerOptions();
            options.Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
            return JsonSerializer.Serialize(new { group = "", tools = tools }, options);
        }

        /// <summary>
        /// 宿主内置工具参数面校验——白名单键 / 未知参数拒绝（ERR|BAD_ARGS；宿主注入保留键 catId 放行）。
        /// </summary>
        /// <param name="argsJson">参数 JSON（空=无参工具）</param>
        /// <param name="allowed">允许键（空格分隔；空=无参数）</param>
        /// <returns>错误文本（空=通过）</returns>
        private static string CheckHostArgs(string argsJson, string allowed)
        {
            if (argsJson == null || argsJson.Trim().Length == 0)
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
                        return "ERR|BAD_ARGS|参数必须是 JSON 对象";
                    }
                    foreach (JsonProperty property in root.EnumerateObject())
                    {
                        if (property.Name == "catId")
                        {
                            continue;
                        }
                        if (allowed.Length == 0)
                        {
                            return "ERR|BAD_ARGS|未知参数: " + property.Name + "（本工具无参数）";
                        }
                        if ((" " + allowed + " ").IndexOf(" " + property.Name + " ", StringComparison.Ordinal) < 0)
                        {
                            return "ERR|BAD_ARGS|未知参数: " + property.Name + "（支持 " + allowed + "）";
                        }
                    }
                    return "";
                }
            }
            catch (Exception ex)
            {
                return "ERR|BAD_ARGS|参数 JSON 解析失败: " + ex.Message;
            }
        }

        /// <summary>
        /// 工具自检工单 ownerId——独立于会话 ToolOwnerId（避免与会话工单归属混淆）
        /// </summary>
        private const long ToolCheckOwnerId = 9001;

        /// <summary>
        /// 工具全链自检——投递一条真实工具单（OA 走单 → 工具组 Flow 认领 → 桥）→ 驱动帧至闭环 → 输出回执。
        /// A23：工具交付自检须含一条全链实跑（单元测试直调桥会绕过宿主注入面）。
        /// </summary>
        /// <param name="name">工具名（如 text-read / cs-check）</param>
        /// <param name="argsJson">参数整包 JSON</param>
        /// <returns>退出码（0=闭环 / 3=挂单失败 / 4=未闭环）</returns>
        private static int RunToolCheck(string name, string argsJson)
        {
            ToolOrderDog dog = new ToolOrderDog("tool-check", name, argsJson);
            dog.Post(_oa, ToolCheckOwnerId);
            if (dog.OfficeId == 0)
            {
                Console.WriteLine("[TOOLCHECK] name=" + name + " ERR|OA_POST_FAIL|工单提交失败（OA 不可用）");
                return 3;
            }
            Console.WriteLine("[TOOLCHECK] name=" + name + " office=#" + dog.OfficeId + " timeoutFrames=" + dog.TimeoutFrames);
            long frames = 0;
            while (!dog.IsClosed && !dog.IsTimedOut && frames < dog.TimeoutFrames)
            {
                _runner.Tick();
                dog.Tick(_oa);
                frames = frames + 1;
                Thread.Sleep(FrameSleepMs);
            }
            Console.WriteLine("[TOOLCHECK] frames=" + frames + " closed=" + dog.IsClosed + " timedOut=" + dog.IsTimedOut);
            Console.WriteLine("[TOOLCHECK] result=" + (dog.Result == null ? "" : dog.Result));
            if (dog.IsClosed && dog.Result != null && dog.Result.Length > 0 && !dog.Result.StartsWith("ERR|", StringComparison.Ordinal))
            {
                return 0;
            }
            return 4;
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
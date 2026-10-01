using System;
using System.Collections.Generic;
using System.Text;
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
        /// 内置工具定义源——宿主内建小表（本质也是 BRIK，只是内置：Note/time/random/info/host-*/pack/sleep）。
        /// 格式与 Flow.GetToolsJson 同构（{"group":"","tools":[...]}）——同一解析器，统一工具池。
        /// </summary>
        /// <returns>内置工具定义 JSON</returns>
        private static string BuildBuiltinToolsJson()
        {
            // 结构化构建——匿名对象树 → JsonSerializer（R6-P3-08：原手写字符串拼接易漏转义、难维护）
            object[] tools = new object[10];
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
                description = "本会话环境自省——返回分类 JSON 块：version（版本 + 编译时刻）/ time（当前时间）/ llm（本猫生效端点）/ endpoint（本地端点：对话页 + 管理面板）/ roots（可见受控根 id/writable/note/path——绝对路径）/ tokens（前文长度 + 最近前文变动时刻）/ packs（挂载包）/ qqbot（渠道说明）/ tools_drift（漂移——added 附用法摘要 name/desc/args（*=必填）· removed 给名字；仅不一致时输出）/ tools_defect（工具组定义缺陷——group/stage/reason；仅非空时输出）/ timeback（上下文作用域——active 活跃作用域 · recent 归档尾部 5 条 · archive 写面可用性；无数据时不出该键）（agent 的眼睛；R1.2）",
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
                                { "cat", new { type = "string", description = "Flow 注册名（host-flows 查当前全部）" } }
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
            tools[7] = new
            {
                name = "sleep",
                description = "登记定时唤醒（时/分/秒 · 合计 1..3600 秒）——等待语义：本轮正常继续（不挂起），到点由宿主自动注入一条唤醒消息；主干被任何其他输入提前启动时本等待即作废（自动销毁并告知）。适合「等你回话 / 等外部条件」类需求。",
                parameters = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                            {
                                { "hours", new { type = "integer", description = "小时（可缺省=0）" } },
                                { "minutes", new { type = "integer", description = "分钟（可缺省=0）" } },
                                { "seconds", new { type = "integer", description = "秒（可缺省=0）" } }
                            },
                    required = new string[0]
                }
            };
            tools[8] = new
            {
                name = "timer",
                description = "登记延迟指令注入（content + 时/分/秒 · 合计 1..86400 秒）——排程语义：到点把 content 原样注入本会话（闹钟必响，不因主干启动而销毁）；loop=true 时触发后按「投递时刻 + 时长」重排（周期巡检）。本轮正常继续，且不影响本轮收尾语义。适合「长任务中途自查 / 周期巡检」。",
                parameters = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                            {
                                { "content", new { type = "string", description = "到点注入的指令文本（必填）" } },
                                { "hours", new { type = "integer", description = "小时（可缺省=0）" } },
                                { "minutes", new { type = "integer", description = "分钟（可缺省=0）" } },
                                { "seconds", new { type = "integer", description = "秒（可缺省=0）" } },
                                { "loop", new { type = "boolean", description = "true=周期重排（可缺省=false）" } }
                            },
                    required = new string[] { "content" }
                }
            };
            tools[9] = new
            {
                name = "timeback",
                description = "上下文作用域（取证型任务专用）——action='start' 开锚（purpose 记用途）→ 查证过程在作用域内膨胀 → action='back' 回卷：膨胀过程从上下文销毁，只把 findings 带回主干（下一轮首条可见）。只服务取证型任务（巡检 / 查文档 / 查日志 / 探路）；建设型与推理链型任务禁用。findings 只放事实 + 指针（路径 / 单号 / 时间戳），不放推理与结论；写入按骨架——首行「结论：<一句话>」，随后「事实：」逐条、「指针：」逐条（每段 ≤5 条，无内容写「（无）」）。v1 未闭合前禁止再次 start。",
                parameters = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                            {
                                { "action", new { type = "string", description = "start=开锚 / back=回卷回收" } },
                                { "purpose", new { type = "string", description = "start 必填——用途标签（短）" } },
                                { "findings", new { type = "string", description = "back 必填——带回载荷（骨架：结论 / 事实 / 指针 三段）" } }
                            },
                    required = new string[] { "action" }
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
        /// 工具组定义缺陷出声——池缺陷逐条落 L3 日志（A107：定义自曝失败不再静默丢组；design-ch4-tools §三·十三）。
        /// </summary>
        private static void LogToolPoolDefects()
        {
            ToolDefect[] defects = ToolPool.Defects();
            for (int i = 0; i < defects.Length; i = i + 1)
            {
                LogStore.Add("CatHome4", 3, "工具组定义缺陷 | " + defects[i].Group + " | " + defects[i].Stage + " | " + defects[i].Reason, "TOOL");
            }
        }

        /// <summary>
        /// 执行序对账出声——池内工具逐一比对 ToolOrderTable（A127）：未登记者静默落默认档，
        /// 此处出声 L2（新增工具忘记登记 = 隐性缺口，不静默）。
        /// </summary>
        private static void LogToolOrderAccounting()
        {
            string[] missing = ToolOrderTable.FindUnregistered(ToolPool.AllNames());
            if (missing.Length == 0)
            {
                return;
            }
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < missing.Length; i = i + 1)
            {
                if (i > 0)
                {
                    sb.Append(",");
                }
                sb.Append(missing[i]);
            }
            LogStore.Add("CatHome4", 2, "工具执行序未登记（落默认档 0）| " + sb.ToString(), "TOOL");
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
        /// <summary>
        /// 工具用法摘要——漂移新增工具（info tools_drift.added 条目；design-ch4-tools §三·十一）。
        /// 数据源 = 注册表声明（ToolRegistry.Find(name).Spec）：描述首句 + 参数键（必填标 *）。
        /// 注册表缺失 / 参数 JSON 不可析 → 退化为仅名字（未知工具仍可见，不静默丢条目）。
        /// </summary>
        /// <param name="name">工具名</param>
        /// <returns>摘要字典（name / desc / args）</returns>
        private static Dictionary<string, object> BuildToolBrief(string name)
        {
            Dictionary<string, object> brief = new Dictionary<string, object>();
            brief["name"] = name;
            brief["desc"] = "";
            brief["args"] = "";
            ToolRegistryEntry entry = ToolRegistry.Find(name);
            if (entry == null || entry.Spec == null)
            {
                return brief;
            }
            brief["desc"] = FirstSentence(entry.Spec.Description, 60);
            brief["args"] = BuildArgKeys(entry.Spec.ParametersJson);
            return brief;
        }
        /// <summary>
        /// 描述首句——截到首个句读（。；换行）；超出上限按字符截断加省略号。
        /// </summary>
        /// <param name="text">完整描述</param>
        /// <param name="max">字符上限</param>
        /// <returns>首句文本（无描述 = 空串）</returns>
        private static string FirstSentence(string text, int max)
        {
            if (text == null)
            {
                return "";
            }
            string line = text.Trim();
            // [段1] 取最早句读——多个标点命中时取位置最小者
            string[] stops = new string[] { "。", "；", "\n", ". " };
            int cut = -1;
            for (int i = 0; i < stops.Length; i = i + 1)
            {
                int idx = line.IndexOf(stops[i], StringComparison.Ordinal);
                if (idx >= 0 && (cut < 0 || idx < cut))
                {
                    cut = idx;
                }
            }
            if (cut >= 0)
            {
                line = line.Substring(0, cut);
            }
            line = line.Trim();
            // [段2] 超长截断——字符上限 + 省略号
            if (line.Length > max)
            {
                line = line.Substring(0, max) + "…";
            }
            return line;
        }
        /// <summary>
        /// 参数键摘要——从工具声明 parameters JSON 提取属性名（required 成员标 *）。
        /// </summary>
        /// <param name="parametersJson">参数 JSON Schema（空 = 无参数工具）</param>
        /// <returns>键摘要（如 "path*、start"；无参 / 不可析 = 空串）</returns>
        private static string BuildArgKeys(string parametersJson)
        {
            if (parametersJson == null || parametersJson.Length == 0)
            {
                return "";
            }
            try
            {
                // [段1] 必填清单——required 数组成员
                using (JsonDocument doc = JsonDocument.Parse(parametersJson))
                {
                    JsonElement root = doc.RootElement;
                    List<string> required = new List<string>();
                    JsonElement requiredEl;
                    if (root.TryGetProperty("required", out requiredEl) && requiredEl.ValueKind == JsonValueKind.Array)
                    {
                        for (int i = 0; i < requiredEl.GetArrayLength(); i = i + 1)
                        {
                            JsonElement item = requiredEl[i];
                            if (item.ValueKind == JsonValueKind.String)
                            {
                                required.Add(item.GetString() ?? "");
                            }
                        }
                    }
                    // [段2] 属性名清单——按声明序拼接，必填标 *
                    JsonElement propertiesEl;
                    if (!root.TryGetProperty("properties", out propertiesEl) || propertiesEl.ValueKind != JsonValueKind.Object)
                    {
                        return "";
                    }
                    StringBuilder keys = new StringBuilder();
                    foreach (JsonProperty property in propertiesEl.EnumerateObject())
                    {
                        if (keys.Length > 0)
                        {
                            keys.Append("、");
                        }
                        keys.Append(property.Name);
                        if (required.Contains(property.Name))
                        {
                            keys.Append("*");
                        }
                    }
                    return keys.ToString();
                }
            }
            catch (JsonException ex)
            {
                // 声明面 JSON 不可析——摘要退化（工具名仍可见；不阻断 info）
                LogStore.Add("CatHome4", 2, "工具参数摘要解析失败: " + ex.Message, "TOOLBRIEF");
                return "";
            }
        }

    }
}
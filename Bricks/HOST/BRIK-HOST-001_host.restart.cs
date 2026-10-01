// ═══════════════════════════════════════════════════
// 积木: host.restart
// ID:   BRIK-HOST-001
// 类别: HOST
// 作用: 宿主自更新请求登记——参数零容忍校验 + 模式值域校验 + 运行模式闸门 + 请求落 DataBox 全局盒（只登记不执行）
// 依赖: 无
// 引用: Mau.Runtime（DataBox）
// 原理: 工具执行面在会话轮内（前文尚未落盘），故本积木不碰进程、不碰文件系统、不碰宿主生命周期——
//       只把 mode/target/push 落到 host_restart_request，由宿主在"全局 Idle"后按 mode 执行接力
//       （full=全链 prepare / incr=搬运+探活 / host=原地）
// 常用: majordomo_cat.mau 认领线——'host.restart'[@args, "full" | "incr" | "host"] > @result
// ═══════════════════════════════════════════════════
using System;
using System.Text.Json;
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 宿主自更新请求登记积木——restart-full / restart-incr / restart-host 三工具（Majordomo 工具组 Flow 认领线消费）。
    /// 职责铁律：只登记不执行（design-ch4-host-restart §四 4.2）——进程动作全部由宿主与 SetUp 承担。
    /// </summary>
    public static class HostRestartBrick
    {
        /// <summary>
        /// 登记宿主重启请求——校验参数、模式与运行模式，落盒即回执（不阻塞、不起进程、不写文件）。
        /// </summary>
        /// <param name="argsJson">工具参数 JSON（target/push，均可选）</param>
        /// <param name="mode">重启模式（语料线常量入参：full / incr / host——mode 由工具名承载，不进参数面）</param>
        /// <param name="result">OK 回执或 ERR| 错误文本</param>
        /// <returns>true=请求已登记</returns>
        public static bool Restart(string argsJson, string mode, out string result)
        {
            result = "";
            string badArgs = ValidateArgs(argsJson, mode);
            if (badArgs.Length > 0)
            {
                result = badArgs;
                return false;
            }
            // 运行模式闸门——非常驻交互模式无常驻主循环可等全局 Idle 闸门（design-ch4-host-restart §五）
            string runMode;
            if (!DataBox.TryGet<string>("global", "host_run_mode", out runMode) || runMode != "interactive")
            {
                result = "ERR|RESTART_UNSUPPORTED|当前运行模式无常驻主循环可等闸门——仅常驻交互模式支持宿主自更新";
                return false;
            }
            string target = ExtractArg(argsJson, "target");
            string push = ExtractArg(argsJson, "push");
            // 归一载荷——只落声明面字段（catId 为宿主注入保留键，不入请求）；mode 由工具名承载（语料线常量入参）
            DataBox.Set<string>("global", "host_restart_request", "{\"mode\":" + JsonSerializer.Serialize(mode) + ",\"target\":" + JsonSerializer.Serialize(target) + ",\"push\":" + JsonSerializer.Serialize(push) + "}");
            string targetShow = (target.Length > 0) ? target : "受控根 mauout";
            string body;
            if (mode == "incr")
            {
                body = "宿主增量重启请求已登记（搬运产物区 + 前置探活 + 重启；目标运行区: " + targetShow
                    + "）。接力者先在旧宿主存活期内完成哨兵校验、搬运与离线探活，通过后宿主才退出——本轮回执后仍可继续对话，直到交接发生。";
            }
            else if (mode == "host")
            {
                body = "宿主原地重启请求已登记（不编译、不覆盖；目标运行区: " + targetShow
                    + "）。宿主在全局空闲后借接力者原地重启，结果回执将自动注入本会话。";
            }
            else
            {
                body = "宿主全链重启请求已登记（prepare + 原子切换 + 重启；目标运行区: " + targetShow
                    + "）。宿主在全局空闲后接力部署并重启，结果回执将自动注入本会话。";
            }
            // 结构化返回体（design-ch4-tools 附录）——首行 JSON 元数据头 + 正文定界
            result = MetaHead(mode, targetShow, push.Length > 0) + "\n" + body;
            return true;
        }

        /// <summary>
        /// 参数面校验——声明面口径零容忍：未知参数 / 参数 JSON 解析失败 / 非法 mode 一律 ERR|BAD_ARGS（宿主注入保留键 catId 放行）。
        /// </summary>
        /// <param name="argsJson">工具参数 JSON</param>
        /// <param name="mode">重启模式（值域 full / incr / host）</param>
        /// <returns>错误文本（空=通过）</returns>
        private static string ValidateArgs(string argsJson, string mode)
        {
            if (mode != "full" && mode != "incr" && mode != "host")
            {
                return "ERR|BAD_ARGS|重启模式非法: " + (mode ?? "") + "（支持 full / incr / host）";
            }
            if (argsJson == null || argsJson.Length == 0)
            {
                return "ERR|BAD_ARGS|工具参数 JSON 解析失败（LLM 生成参数可能被截断——超长内容请分段写入）";
            }
            try
            {
                JsonDocument doc = JsonDocument.Parse(argsJson);
                try
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
                        if (property.Name != "target" && property.Name != "push")
                        {
                            return "ERR|BAD_ARGS|未知参数: " + property.Name + "（支持 target push）";
                        }
                        if (property.Value.ValueKind != JsonValueKind.String)
                        {
                            return "ERR|BAD_ARGS|参数 " + property.Name + " 必须是字符串";
                        }
                    }
                    return "";
                }
                finally
                {
                    doc.Dispose();
                }
            }
            catch (Exception ex)
            {
                return "ERR|BAD_ARGS|参数 JSON 解析失败: " + ex.Message;
            }
        }

        /// <summary>
        /// 结构化元数据头——首行单行 JSON（ok/tool/mode/target/push；键序稳定 = 插入序）
        /// 约定（design-ch4-tools 附录）：返回体 = 首行 JSON 头 + 正文定界行（正文不塞进 JSON——避免转义膨胀）
        /// </summary>
        /// <param name="mode">重启模式（full / incr / host——映射回工具名）</param>
        /// <param name="target">目标运行区（已归一）</param>
        /// <param name="push">是否带推送回执串</param>
        /// <returns>单行 JSON</returns>
        private static string MetaHead(string mode, string target, bool push)
        {
            string tool = "restart-full";
            if (mode == "incr")
            {
                tool = "restart-incr";
            }
            else if (mode == "host")
            {
                tool = "restart-host";
            }
            System.Collections.Generic.Dictionary<string, object> head = new System.Collections.Generic.Dictionary<string, object>();
            head["ok"] = true;
            head["tool"] = tool;
            head["mode"] = mode;
            head["target"] = target;
            head["push"] = push;
            return JsonSerializer.Serialize(head);
        }

        /// <summary>
        /// 展平参数提取——argsJson 中取字符串值（不存在返回空串）。
        /// </summary>
        /// <param name="argsJson">参数 JSON</param>
        /// <param name="key">参数名</param>
        /// <returns>参数值或空串</returns>
        private static string ExtractArg(string argsJson, string key)
        {
            try
            {
                JsonDocument doc = JsonDocument.Parse(argsJson);
                try
                {
                    JsonElement el;
                    if (doc.RootElement.TryGetProperty(key, out el) && el.ValueKind == JsonValueKind.String)
                    {
                        return el.GetString() ?? "";
                    }
                }
                finally
                {
                    doc.Dispose();
                }
            }
            catch (Exception ex)
            {
                LogStore.Add("HOST", 2, "host.restart 参数提取失败: " + ex.Message, "TOOL");
            }
            return "";
        }
    }
}
// #MAU_CHECKSUM:SHA256:3F798752CE5AD19D0C9232CF756454AC8E2D92BB633B23BE41C90930063FC82E

// ═══════════════════════════════════════════════════
// 积木: host.command
// ID:   BRIK-HOST-002
// 类别: HOST
// 作用: 宿主指令执行——把一行管理指令交给宿主统一执行出口（cat.* / catcfg.*），回执原文返回
// 依赖: 无
// 引用: Mau.Runtime（DataBox / IHostCommandService）
// 原理: DataBox.TryResolve<IHostCommandService> → Execute(line) → 回执（单一内核：与 CLI / HTTP 面板同源）
//       特权面由会话工具面承担（工具定义积木组级 privileged 声明）——本积木不做权限判定，不判调用者身份
//       前缀白名单 / 线程域 / 停机态检查全部在宿主实现内（入口面 = 用法声明面）
// 常用: majordomo_cat.mau 认领线——'host.command'[@mjdArgs] > @mjdRes
// ═══════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Text.Json;
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 宿主指令执行积木——majordomo-cmd 工具（Majordomo 工具组 Flow 认领线消费）。
    /// 参数面：cmd（一行管理指令，必填）；未知参数 / 缺值一律 ERR|BAD_ARGS（入口面零容忍，不静默回落）。
    /// </summary>
    public static class HostCommandBrick
    {
        /// <summary>
        /// 执行一行宿主管理指令——经 IHostCommandService（宿主统一执行出口）。
        /// </summary>
        /// <param name="argsJson">工具参数 JSON（cmd 必填）</param>
        /// <param name="result">回执文本（成功带结构化头；失败 ERR| 前缀原文）</param>
        /// <returns>true=调用成功（result 为宿主回执）；false=参数错误 / 服务缺失 / 宿主回执为 ERR</returns>
        public static bool Exec(string argsJson, out string result)
        {
            result = "";
            string badArgs = ValidateArgs(argsJson);
            if (badArgs.Length > 0)
            {
                result = badArgs;
                return false;
            }
            string cmd = JsonArgs.Get(argsJson, "cmd");
            if (cmd == null || cmd.Trim().Length == 0)
            {
                result = "ERR|BAD_ARGS|缺少参数 cmd（一行管理指令，如 cat.list）";
                return false;
            }
            IHostCommandService service = null!;
            bool bound = DataBox.TryResolve<IHostCommandService>(out service);
            if (!bound || service == null)
            {
                result = "ERR|HOSTCMD_NO_SERVICE|宿主指令服务未注入（宿主未接线）";
                return false;
            }
            string reply = service.Execute(cmd);
            if (reply == null)
            {
                reply = "ERR|EMPTY_RESULT|宿主指令无回执";
            }
            // 错误面不动——失败仍走既有 ERR|CODE|消息（不包装头；前端按前缀判失败）
            if (reply.StartsWith("ERR|", StringComparison.Ordinal))
            {
                result = reply;
                return false;
            }
            // 结构化返回体（design-ch4-tools 附录 · A214）——首行 JSON 头（target/items 均省略）+ 正文摘要行
            System.Collections.Generic.Dictionary<string, object> head = new System.Collections.Generic.Dictionary<string, object>();
            head["ok"] = true;
            head["tool"] = "majordomo-cmd";
            result = JsonSerializer.Serialize(head) + "\n" + "宿主指令已执行" + "\n" + reply;
            return true;
        }

        /// <summary>
        /// 参数面校验——声明面口径零容忍：未知参数 / 参数 JSON 解析失败一律 ERR|BAD_ARGS（宿主注入保留键 catId 放行）。
        /// </summary>
        /// <param name="argsJson">工具参数 JSON</param>
        /// <returns>错误文本（空=通过）</returns>
        private static string ValidateArgs(string argsJson)
        {
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
                        if (property.Name != "cmd")
                        {
                            return "ERR|BAD_ARGS|未知参数: " + property.Name + "（支持 cmd）";
                        }
                        if (property.Value.ValueKind != JsonValueKind.String)
                        {
                            return "ERR|BAD_ARGS|参数 cmd 必须是字符串";
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
    }
}
// #MAU_CHECKSUM:SHA256:C5DC41B344AEEBE8C10AA9E46399673F30FBDC17435BA90E2ED2291E90ECBF1C

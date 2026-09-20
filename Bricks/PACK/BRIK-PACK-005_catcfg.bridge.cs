// ═══════════════════════════════════════════════════
// 积木: catcfg.bridge
// ID:   BRIK-PACK-005
// 类别: PACK
// 作用: 每猫配置桥积木——结构化参数翻译成内核指令行 → IHostCommandService（config-cat-get / config-cat-set 的执行底座）
// 依赖: 无
// 包: 无
// 引用: Mau.Runtime（DataBox / IHostCommandService）
// 原理: 参数翻译成 cat.cfg.get / cat.cfg.set 指令行 → 宿主统一执行出口（与 CLI / HTTP 面板同源单内核）
//       合并写语义由内核保证（读现值 → 改单字段 → 全量写回，未提交字段保留）——本积木只做参数面校验与翻译，不碰文件
//       特权面由会话工具面承担（组级 privileged 声明）——本积木不做权限判定
// 方法: get → cat
//        set → cat,field,value
// 常用: config_cat.mau 认领线——'catcfg.bridge'["get", @args] > @res
// ═══════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Text.Json;
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// PACK 接口积木——catcfg.bridge（每猫配置：结构化参数 → 内核指令行 → 宿主指令服务）。
    /// </summary>
    public static class CatCfgBridgeBrick
    {
        /// <summary>
        /// 每猫配置桥单方法调度——method 白名单 + argsJson 展平参数（get/set）。
        /// </summary>
        /// <param name="method">操作名（get/set）</param>
        /// <param name="argsJson">展平参数 JSON</param>
        /// <param name="result">结果文本（成功带结构化头；失败 ERR| 前缀原文）</param>
        /// <returns>true=调用成功</returns>
        public static bool Invoke(string method, string argsJson, out string result)
        {
            result = "";
            string allowedKeys;
            string requiredKeys;
            if (method == "get")
            {
                allowedKeys = "cat";
                requiredKeys = "cat";
            }
            else if (method == "set")
            {
                allowedKeys = "cat field value";
                requiredKeys = "cat field value";
            }
            else
            {
                result = "ERR|UNKNOWN_METHOD|catcfg." + method;
                return false;
            }
            string badArgs = ValidateArgs(argsJson, allowedKeys, requiredKeys);
            if (badArgs.Length > 0)
            {
                result = badArgs;
                return false;
            }
            string cat = ExtractArg(argsJson, "cat");
            IHostCommandService service = null!;
            bool bound = DataBox.TryResolve<IHostCommandService>(out service);
            if (!bound || service == null)
            {
                result = "ERR|HOSTCMD_NO_SERVICE|宿主指令服务未注入（宿主未接线）";
                return false;
            }
            // 参数 → 指令行（内核单一入口；值取行内剩余全部原文——persona 多行原样直达）
            string line;
            if (method == "get")
            {
                line = "cat.cfg.get " + cat;
            }
            else
            {
                line = "cat.cfg.set " + cat + " " + ExtractArg(argsJson, "field") + " " + ExtractArg(argsJson, "value");
            }
            string reply = service.Execute(line);
            if (reply == null)
            {
                reply = "ERR|EMPTY_RESULT|宿主指令无回执";
            }
            // 错误面不动——失败仍走既有 ERR|CODE|消息（不包装头）
            if (reply.StartsWith("ERR|", StringComparison.Ordinal))
            {
                result = reply;
                return false;
            }
            // 结构化返回体（design-ch4-tools 附录）——首行 JSON 元数据头 + 正文定界
            Dictionary<string, object> head = new Dictionary<string, object>();
            head["ok"] = true;
            head["tool"] = (method == "get") ? "config-cat-get" : "config-cat-set";
            head["cat"] = cat;
            result = JsonSerializer.Serialize(head) + "\n" + reply;
            return true;
        }

        /// <summary>
        /// 参数面校验——声明面口径零容忍：未知参数 / 必填缺值一律 ERR|BAD_ARGS（宿主注入保留键 catId 放行）。
        /// </summary>
        /// <param name="argsJson">工具参数 JSON</param>
        /// <param name="allowed">允许键（空格分隔）</param>
        /// <param name="required">必填键（空格分隔；空=无必填）</param>
        /// <returns>错误文本（空=通过）</returns>
        private static string ValidateArgs(string argsJson, string allowed, string required)
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
                        if ((" " + allowed + " ").IndexOf(" " + property.Name + " ", StringComparison.Ordinal) < 0)
                        {
                            return "ERR|BAD_ARGS|未知参数: " + property.Name + "（支持 " + allowed + "）";
                        }
                    }
                    if (required.Length > 0)
                    {
                        string[] must = required.Split(' ');
                        for (int i = 0; i < must.Length; i = i + 1)
                        {
                            JsonElement mustValue;
                            if (!root.TryGetProperty(must[i], out mustValue) ||
                                (mustValue.ValueKind == JsonValueKind.String && (mustValue.GetString() ?? "").Length == 0))
                            {
                                return "ERR|BAD_ARGS|缺参数 " + must[i] + "（必填：" + required + "）";
                            }
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
                LogStore.Add("PACK", 2, "cat.cfg 参数提取失败: " + ex.Message, "TOOL");
            }
            return "";
        }
    }
}
// #MAU_CHECKSUM:SHA256:7E6BD713AD73B2538483BF95A8EA5469766E88318FB06925EFC7A1C1A3B873FA

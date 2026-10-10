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
            string badArgs = JsonArgs.Validate(argsJson, allowedKeys, requiredKeys, "", "");
            if (badArgs.Length > 0)
            {
                result = badArgs;
                return false;
            }
            string cat = JsonArgs.Get(argsJson, "cat");
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
                line = "cat.cfg.set " + cat + " " + JsonArgs.Get(argsJson, "field") + " " + JsonArgs.Get(argsJson, "value");
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
            // 结构化返回体（design-ch4-tools 附录 · A214）——首行 JSON 头（target = cat）+ 正文摘要行
            Dictionary<string, object> head = new Dictionary<string, object>();
            head["ok"] = true;
            head["tool"] = (method == "get") ? "config-cat-get" : "config-cat-set";
            head["target"] = cat;
            result = JsonSerializer.Serialize(head) + "\n" + cat + " | cat.cfg " + ((method == "get") ? "读取" : "写入") + "\n" + reply;
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:0842C60C387056C42B275E37BCC144169B85EAB147ECEA5A9F97FDD387A42200

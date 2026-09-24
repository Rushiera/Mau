// ═══════════════════════════════════════════════════
// 积木: host.catinfo
// ID:   BRIK-HOST-003
// 类别: HOST
// 作用: 全猫状态统计——把内核指令 cat.info 的回执（整块分类 JSON）原样取回
// 依赖: 无
// 引用: Mau.Runtime（DataBox / IHostCommandService）
// 原理: DataBox.TryResolve<IHostCommandService> → Execute("cat.info") → 整块 JSON 原文返回
//       返回体不做「头 + 正文」包装——与 info 内置工具同规格（整块分类 JSON，LLM 消费第一目的）
//       特权面由会话工具面承担（工具定义积木组级 privileged 声明）——本积木不做权限判定，不判调用者身份
// 常用: majordomo_cat.mau 认领线——'host.catinfo'[@mciArgs] > @mciRes
// ═══════════════════════════════════════════════════
using System;
using System.Text.Json;
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 全猫状态统计积木——majordomo-catinfo 工具（Majordomo 工具组 Flow 认领线消费）。
    /// 参数面：无参数（宿主注入保留键 catId 放行）；未知参数 / 非法形态一律 ERR|BAD_ARGS（入口面零容忍，不静默忽略）。
    /// </summary>
    public static class HostCatInfoBrick
    {
        /// <summary>
        /// 取全猫状态统计——经 IHostCommandService 执行内核指令 cat.info。
        /// </summary>
        /// <param name="argsJson">工具参数 JSON（零参数；空串视为无参数）</param>
        /// <param name="result">整块分类 JSON（失败为 ERR| 前缀原文）</param>
        /// <returns>true=调用成功（result 为状态 JSON）；false=参数错误 / 服务缺失 / 内核回执为 ERR</returns>
        public static bool Exec(string argsJson, out string result)
        {
            result = "";
            string badArgs = ValidateArgs(argsJson);
            if (badArgs.Length > 0)
            {
                result = badArgs;
                return false;
            }
            IHostCommandService service = null!;
            bool bound = DataBox.TryResolve<IHostCommandService>(out service);
            if (!bound || service == null)
            {
                result = "ERR|HOSTCMD_NO_SERVICE|宿主指令服务未注入（宿主未接线）";
                return false;
            }
            string reply = service.Execute("cat.info");
            if (reply == null)
            {
                reply = "ERR|EMPTY_RESULT|宿主指令无回执";
            }
            result = reply;
            if (reply.StartsWith("ERR|", StringComparison.Ordinal))
            {
                return false;
            }
            return true;
        }

        /// <summary>
        /// 参数面校验——零参数工具：未知参数 / 非对象形态 / 解析失败一律 ERR|BAD_ARGS（catId 为宿主注入保留键）。
        /// </summary>
        /// <param name="argsJson">工具参数 JSON</param>
        /// <returns>错误文本（空=通过）</returns>
        private static string ValidateArgs(string argsJson)
        {
            if (argsJson == null || argsJson.Length == 0)
            {
                return "";
            }
            try
            {
                JsonDocument doc = JsonDocument.Parse(argsJson);
                try
                {
                    if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    {
                        return "ERR|BAD_ARGS|参数必须是 JSON 对象";
                    }
                    foreach (JsonProperty property in doc.RootElement.EnumerateObject())
                    {
                        if (property.Name == "catId")
                        {
                            continue;
                        }
                        return "ERR|BAD_ARGS|未知参数: " + property.Name + "（本工具无参数）";
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
// #MAU_CHECKSUM:SHA256:79856E3ACD362AC37CD79D95D0AF2E1B2400E4F73F185E08F8027AE57185B075

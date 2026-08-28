// ═══════════════════════════════════════════════════
// 积木: temp.exec
// ID:   BRIK-TEMP-001
// 类别: TEMP
// 作用: 临时工具万能执行接口——按 Key 分发到 C# 函数（str→str）；Key 不存在报错并返回可用 Key 组
// 依赖: 无
// 引用: System（Dictionary/Func/Exception）+ System.Text.Json
// 原理: argsJson 解析 key+content → TempRegistry 查 Key → 调用 handler（str→str）→ result 回执
//       临时工具 = 自举试验场：验证可用后迁移转正为独立工具组 Flow（见【迁移转正】）
// 常用: TempToolCat 认领线——'temp.exec'[@args] > @result（载荷 key=工具Key, content=输入）
// ═══════════════════════════════════════════════════
#nullable disable warnings
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Mau.Bricks
{
    /// <summary>
    /// 临时工具注册表——【LLM 可改区】唯一载体。Key → handler 静态字典 + 各 handler 实现。
    /// 换/增/删临时工具 = 只改本类；调度骨架（TempExecBrick）零改动。
    /// 【迁移转正】验证稳定后：handler 纯逻辑 → 独立积木（public static bool Xxx(string argsJson, out string result)，包一层 content 解析）；
    ///                       认领线 → 复制 TempToolCat 语料模板换工具名；声明 → BuildToolSpecs 加 ToolSpec；注册表 → ToolRegistry 动态登记。
    /// </summary>
    public static class TempRegistry
    {
        /// <summary>
        /// 临时工具注册表——Key → handler（str→str；Key 建议用未来独立工具名的连字符形式，迁移时同名转正）
        /// </summary>
        private static readonly Dictionary<string, Func<string, string>> _tools = new Dictionary<string, Func<string, string>>
        {
            { "echo", Echo },
            { "upper", Upper },
            { "reverse", Reverse },
        };

        /// <summary>
        /// 【临时工具】echo——原样返回 content（自举闭环最小验证样例）
        /// </summary>
        /// <param name="content">输入内容</param>
        /// <returns>原样内容</returns>
        private static string Echo(string content)
        {
            return content;
        }

        /// <summary>
        /// 【临时工具】upper——content 转大写（演示 str→str 转换；handler 只写纯逻辑，不碰 argsJson——迁移零阻力）
        /// </summary>
        /// <param name="content">输入内容</param>
        /// <returns>大写内容</returns>
        private static string Upper(string content)
        {
            return content.ToUpperInvariant();
        }

        /// <summary>
        /// 【临时工具】reverse——content 反转（演示 handler 增改；迁移转正时主体直接搬）
        /// </summary>
        /// <param name="content">输入内容</param>
        /// <returns>反转内容</returns>
        private static string Reverse(string content)
        {
            char[] chars = content.ToCharArray();
            Array.Reverse(chars);
            return new string(chars);
        }

        /// <summary>
        /// 按 Key 查 handler——不存在返回 null（internal：供同程序集 TempExecBrick 调度；非 public bool 不入积木契约）
        /// </summary>
        /// <param name="key">工具 Key</param>
        /// <returns>handler（不存在 = null）</returns>
        internal static Func<string, string>? Find(string key)
        {
            Func<string, string>? fn;
            if (_tools.TryGetValue(key, out fn))
            {
                return fn;
            }
            return null;
        }

        /// <summary>
        /// Key 组枚举——逗号分隔（temp.keys 消费面；agent 查看当前可用临时工具）
        /// </summary>
        /// <returns>Key 组文本（空 = 暂无临时工具）</returns>
        public static string ListKeys()
        {
            string text = "";
            foreach (KeyValuePair<string, Func<string, string>> kv in _tools)
            {
                if (text.Length > 0)
                {
                    text = text + ",";
                }
                text = text + kv.Key;
            }
            return text;
        }
    }

    /// <summary>
    /// 临时积木——temp.exec 万能执行接口（str→str 分发；TempToolCat 认领）
    /// </summary>
    public static class TempExecBrick
    {
        /// <summary>
        /// 万能执行——解析 argsJson 的 key+content → 注册表分发 → 结果回执
        /// </summary>
        /// <param name="argsJson">工具参数 JSON（key + content）</param>
        /// <param name="result">执行结果（失败侧 ERR| 文本也落——错误可见性）</param>
        /// <returns>true=执行成功</returns>
        public static bool Exec(string argsJson, out string result)
        {
            result = "";
            string key = ExtractArg(argsJson, "key");
            if (key.Length == 0)
            {
                result = "ERR|TEMP_BAD_ARGS|缺少参数 key";
                return false;
            }
            Func<string, string>? fn = TempRegistry.Find(key);
            if (fn == null)
            {
                result = "ERR|TEMP_KEY_NOT_FOUND|可用Key: " + TempRegistry.ListKeys();
                return false;
            }
            string content = ExtractArg(argsJson, "content");
            try
            {
                result = fn(content);
                return true;
            }
            catch (Exception ex)
            {
                result = "ERR|TEMP_EXEC|" + ex.GetType().Name + "|" + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 展平参数提取——argsJson 中取字符串值（不存在返回空串）
        /// </summary>
        /// <param name="argumentsJson">参数 JSON</param>
        /// <param name="key">参数名</param>
        /// <returns>参数值</returns>
        private static string ExtractArg(string argumentsJson, string key)
        {
            try
            {
                JsonDocument doc = JsonDocument.Parse(argumentsJson);
                try
                {
                    if (doc.RootElement.TryGetProperty(key, out JsonElement el))
                    {
                        if (el.ValueKind == JsonValueKind.String)
                        {
                            string? s = el.GetString();
                            if (s == null)
                            {
                                return "";
                            }
                            return s;
                        }
                        return el.GetRawText();
                    }
                }
                finally
                {
                    doc.Dispose();
                }
            }
            catch (Exception)
            {
            }
            return "";
        }
    }
}
// #MAU_CHECKSUM:SHA256:188FF527EEB621A2F53C2C294C2A3C5D2CB5839DB8518F424DE091BC7946066B

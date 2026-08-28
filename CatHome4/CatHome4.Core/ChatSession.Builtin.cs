using System;
using System.Text;
using System.Text.Json;
using Mau.Runtime;

namespace CH4
{
    /// <summary>
    /// 宿主会话实体——内置工具分部（R0.2/R1：会话内直执——无需 OA 的内置工具统一入口）。
    /// 分界铁律：内置（无需 OA——会话内/宿主直执）vs OA 工具（按工具组独立 Flow 认领）——双轨并存不走同一执行面。
    /// 内置工具：Note（M4a）+ time/random（R1.1）+ info（R1.2——读 ToolRegistry，agent 的眼睛）。
    /// </summary>
    internal sealed partial class ChatSession
    {
        /// <summary>
        /// 内置工具统一执行入口——EnterToolBatch 分派（Note/time/random/info 会话内直执，不进 OA）。
        /// </summary>
        /// <param name="name">内置工具名</param>
        /// <param name="argsJson">参数 JSON</param>
        /// <returns>执行结果文本</returns>
        private string ExecuteBuiltin(string name, string argsJson)
        {
            if (name == "Note")
            {
                return ExecuteNote(argsJson);
            }
            if (name == "time")
            {
                return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            }
            if (name == "random")
            {
                return ExecuteRandom(argsJson);
            }
            if (name == "info")
            {
                return ExecuteInfo();
            }
            return "ERR|UNKNOWN_BUILTIN|未知内置工具: " + name;
        }

        /// <summary>
        /// random 执行体——生成 [min, max) 范围内随机整数（min 含下限，max 不含上限；min &lt; max 校验）。
        /// </summary>
        /// <param name="argsJson">参数 JSON（min/max 整数）</param>
        /// <returns>随机整数文本</returns>
        private string ExecuteRandom(string argsJson)
        {
            int min = 0;
            int max = 0;
            bool hasMin = false;
            bool hasMax = false;
            if (argsJson != null && argsJson.Length > 0 && argsJson.StartsWith("{"))
            {
                try
                {
                    using (JsonDocument doc = JsonDocument.Parse(argsJson))
                    {
                        JsonElement root = doc.RootElement;
                        JsonElement mn;
                        if (root.TryGetProperty("min", out mn) && mn.ValueKind == JsonValueKind.Number)
                        {
                            min = mn.GetInt32();
                            hasMin = true;
                        }
                        JsonElement mx;
                        if (root.TryGetProperty("max", out mx) && mx.ValueKind == JsonValueKind.Number)
                        {
                            max = mx.GetInt32();
                            hasMax = true;
                        }
                    }
                }
                catch (Exception)
                {
                    return "ERR|BAD_ARGS|random 参数损坏";
                }
            }
            if (!hasMin || !hasMax || min >= max)
            {
                return "ERR|BAD_ARGS|random 需要 min < max 的整数参数";
            }
            Random rng = new Random();
            return rng.Next(min, max).ToString();
        }

        /// <summary>
        /// info 执行体——环境信息（运行版本 + LLM 端点类型 + 当前时间；R0.2 拍板：工具注册是前文初始化一次性——info 不重复工具清单）。
        /// </summary>
        /// <returns>环境信息文本</returns>
        private string ExecuteInfo()
        {
            if (_envInfoProvider != null)
            {
                return _envInfoProvider();
            }
            return "CH4 | 环境信息不可用（未注入 provider）";
        }
    }
}
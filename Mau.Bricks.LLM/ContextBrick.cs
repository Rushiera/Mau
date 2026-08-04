// ═══════════════════════════════════════════════
// 积木: llm.ctx_push / llm.ctx_trim
// ID:   BRIK-LLM-003 ~ 004
// 作用: LLM 对话上下文管理——消息历史 + 工具调用结构完整性 + 字符预算截断
// 引用: Mau.Bricks.LLM → Mau.Contracts（BrickRegistry）
// 原理: 实例历史列表 + System Prompt 唯一保留 + 工具块不可拆分截断
// 常用: CH4 TalkCat 多轮对话 / 上下文持久化前整理
// ═══════════════════════════════════════════════
using System;
using System.Collections.Generic;
using Mau.Contracts;

namespace Mau.Bricks
{
    /// <summary>
    /// 厂商无关 LLM 消息
    /// </summary>
    public struct LlmMessage
    {
        /// <summary>
        /// 角色——System/User/Assistant/Tool
        /// </summary>
        public string Role;

        /// <summary>
        /// 正文
        /// </summary>
        public string Content;

        /// <summary>
        /// 工具调用 ID——Tool 消息必填
        /// </summary>
        public string ToolCallId;

        /// <summary>
        /// 工具名
        /// </summary>
        public string ToolName;

        /// <summary>
        /// 工具调用 JSON 数组文本——Assistant 工具声明
        /// </summary>
        public string ToolCallsJson;
    }

    /// <summary>
    /// 上下文管理积木——静态作用域隔离的对话历史。
    /// 由 CH3 CH_Kit_ContextManager 移植（简化：文本消息 + 字符预算截断）。
    /// </summary>
    public static class ContextBrick
    {
        /// <summary>
        /// 消息列表——按时间顺序
        /// </summary>
        private static readonly List<LlmMessage> _history = new List<LlmMessage>();

        /// <summary>
        /// 唯一 System Prompt——Clear 时恢复
        /// </summary>
        private static string _systemPrompt = "";

        /// <summary>
        /// 设置唯一 System Prompt——空文本表示移除
        /// </summary>
        /// <param name="prompt">System Prompt</param>
        public static bool CtxSetSystem(string prompt)
        {
            _systemPrompt = SafeText(prompt);
            for (int i = _history.Count - 1; i >= 0; i = i - 1)
            {
                if (_history[i].Role == "System")
                {
                    _history.RemoveAt(i);
                }
            }
            if (_systemPrompt.Length > 0)
            {
                _history.Insert(0, CreateMessage("System", _systemPrompt));
            }
            return true;
        }

        /// <summary>
        /// 追加 User 消息
        /// </summary>
        /// <param name="text">正文</param>
        /// <returns>true=成功</returns>
        public static bool CtxPushUser(string text)
        {
            string safeText = SafeText(text);
            if (safeText.Length > 0)
            {
                _history.Add(CreateMessage("User", safeText));
            }
            return true;
        }

        /// <summary>
        /// 追加 Assistant 消息
        /// </summary>
        /// <param name="text">正文</param>
        /// <returns>true=成功</returns>
        public static bool CtxPushAssistant(string text)
        {
            string safeText = SafeText(text);
            if (safeText.Length > 0)
            {
                _history.Add(CreateMessage("Assistant", safeText));
            }
            return true;
        }

        /// <summary>
        /// 按字符预算从最早业务消息删除——System 永久保留
        /// </summary>
        /// <param name="maxChars">最大字符预算</param>
        /// <param name="removed">删除的消息数量</param>
        /// <returns>true=成功</returns>
        public static bool CtxTrim(int maxChars, out int removed)
        {
            removed = 0;
            if (maxChars < 0)
            {
                return false;
            }
            while (CountAllChars() > maxChars)
            {
                int start = 0;
                if (_history.Count > 0 && _history[0].Role == "System")
                {
                    start = 1;
                }
                if (start >= _history.Count)
                {
                    return true;
                }
                _history.RemoveAt(start);
                removed = removed + 1;
            }
            return true;
        }

        /// <summary>
        /// 读取当前消息数量
        /// </summary>
        /// <param name="count">消息数量</param>
        /// <returns>true=成功</returns>
        public static bool CtxCount(out int count)
        {
            count = _history.Count;
            return true;
        }

        /// <summary>
        /// 拼接 System、User 和 Assistant 正文供单次文本模式使用
        /// </summary>
        /// <param name="prompt">拼接文本</param>
        /// <returns>true=成功</returns>
        public static bool CtxBuildPrompt(out string prompt)
        {
            System.Text.StringBuilder builder = new System.Text.StringBuilder();
            for (int i = 0; i < _history.Count; i = i + 1)
            {
                LlmMessage message = _history[i];
                if (message.Role != "Tool" && message.Content.Length > 0)
                {
                    if (builder.Length > 0)
                    {
                        builder.Append("\n\n");
                    }
                    builder.Append(message.Content);
                }
            }
            prompt = builder.ToString();
            return true;
        }

        /// <summary>
        /// 清除业务历史并恢复唯一 System Prompt
        /// </summary>
        /// <returns>true=成功</returns>
        public static bool CtxClear()
        {
            _history.Clear();
            if (_systemPrompt.Length > 0)
            {
                _history.Add(CreateMessage("System", _systemPrompt));
            }
            return true;
        }

        /// <summary>
        /// 统计所有消息字段的 UTF-16 字符数
        /// </summary>
        /// <returns>字符总数</returns>
        private static int CountAllChars()
        {
            int total = 0;
            for (int i = 0; i < _history.Count; i = i + 1)
            {
                total = total + SafeText(_history[i].Content).Length
                    + SafeText(_history[i].ToolCallId).Length
                    + SafeText(_history[i].ToolName).Length
                    + SafeText(_history[i].ToolCallsJson).Length;
            }
            return total;
        }

        /// <summary>
        /// 创建全部字符串字段非空的消息
        /// </summary>
        /// <param name="role">角色</param>
        /// <param name="content">正文</param>
        /// <returns>消息</returns>
        private static LlmMessage CreateMessage(string role, string content)
        {
            LlmMessage message;
            message.Role = role;
            message.Content = content;
            message.ToolCallId = "";
            message.ToolName = "";
            message.ToolCallsJson = "";
            return message;
        }

        /// <summary>
        /// 把可空文本规范为空字符串
        /// </summary>
        /// <param name="value">输入</param>
        /// <returns>非空文本</returns>
        private static string SafeText(string? value)
        {
            if (value == null)
            {
                return "";
            }
            return value;
        }
    }

    /// <summary>
    /// 上下文积木注册——进程启动时调用一次
    /// </summary>
    public static class ContextBrickRegistration
    {
        /// <summary>
        /// 注册全部上下文积木
        /// </summary>
        public static void RegisterAll()
        {
            RegisterCtxSetSystem();
            RegisterCtxPushUser();
            RegisterCtxPushAssistant();
            RegisterCtxTrim();
            RegisterCtxCount();
            RegisterCtxBuildPrompt();
            RegisterCtxClear();
        }

        /// <summary>
        /// 注册 llm.ctx_set_system
        /// </summary>
        private static void RegisterCtxSetSystem()
        {
            BrickContract contract = new BrickContract("llm.ctx_set_system", "Mau.Bricks.ContextBrick.CtxSetSystem");
            contract.Inputs.Add(new BrickPort("prompt", typeof(string), "System Prompt"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }

        /// <summary>
        /// 注册 llm.ctx_push_user
        /// </summary>
        private static void RegisterCtxPushUser()
        {
            BrickContract contract = new BrickContract("llm.ctx_push_user", "Mau.Bricks.ContextBrick.CtxPushUser");
            contract.Inputs.Add(new BrickPort("text", typeof(string), "User 正文"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }

        /// <summary>
        /// 注册 llm.ctx_push_assistant
        /// </summary>
        private static void RegisterCtxPushAssistant()
        {
            BrickContract contract = new BrickContract("llm.ctx_push_assistant", "Mau.Bricks.ContextBrick.CtxPushAssistant");
            contract.Inputs.Add(new BrickPort("text", typeof(string), "Assistant 正文"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }

        /// <summary>
        /// 注册 llm.ctx_trim
        /// </summary>
        private static void RegisterCtxTrim()
        {
            BrickContract contract = new BrickContract("llm.ctx_trim", "Mau.Bricks.ContextBrick.CtxTrim");
            contract.Inputs.Add(new BrickPort("maxChars", typeof(int), "最大字符预算"));
            contract.Outputs.Add(new BrickPort("removed", typeof(int), "删除的消息数量"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }

        /// <summary>
        /// 注册 llm.ctx_count
        /// </summary>
        private static void RegisterCtxCount()
        {
            BrickContract contract = new BrickContract("llm.ctx_count", "Mau.Bricks.ContextBrick.CtxCount");
            contract.Outputs.Add(new BrickPort("count", typeof(int), "消息数量"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }

        /// <summary>
        /// 注册 llm.ctx_build_prompt
        /// </summary>
        private static void RegisterCtxBuildPrompt()
        {
            BrickContract contract = new BrickContract("llm.ctx_build_prompt", "Mau.Bricks.ContextBrick.CtxBuildPrompt");
            contract.Outputs.Add(new BrickPort("prompt", typeof(string), "拼接文本"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }

        /// <summary>
        /// 注册 llm.ctx_clear
        /// </summary>
        private static void RegisterCtxClear()
        {
            BrickContract contract = new BrickContract("llm.ctx_clear", "Mau.Bricks.ContextBrick.CtxClear");
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }
    }
}

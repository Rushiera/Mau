// ═══════════════════════════════════════════════
// 积木: cmd.register / cmd.unregister / cmd.consume / cmd.set / cmd.clean
// ID:   BRIK-CMD-001 ~ 005
// 作用: 指令总线机制积木——语料声明指令拓扑（注册 key / 消费邮件 / 投递指令），宿主注入 CommandBus 实例
// 引用: Mau.Bricks.Standard → Mau.Runtime（ICommandBus/CommandPack）· Mau.Contracts
// 原理: 静态宿主桥 Configure(ICommandBus) 注入单例；积木方法包装 Mau.Runtime.CommandBus 操作
// 常用: talkcat_fsm.mau 指令入口——CH4 P2.2 TalkCat 核心
// ═══════════════════════════════════════════════
using System;
using Mau.Contracts;
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 指令机制积木——语料变迁动作的 CommandBus 操作通道。宿主启动时注入实例。
    /// 线程约束：consume/set 为 main（邮件冻结与分发在主线程）；register/clean 兼容任意线程。
    /// </summary>
    public static class CmdBrick
    {
        /// <summary>
        /// 宿主注入的 CommandBus 实例——宿主启动时 Configure；未注入时全部返回 false
        /// </summary>
        private static ICommandBus? _instance;

        /// <summary>
        /// 注入 CommandBus 实例——宿主启动时调用一次
        /// </summary>
        /// <param name="bus">指令总线</param>
        public static void Configure(ICommandBus bus)
        {
            _instance = bus;
        }

        /// <summary>
        /// 注册模块的指令 key 列表——key 必须三段式（Category_Module_Name）
        /// </summary>
        /// <param name="ownerId">模块全局 ID</param>
        /// <param name="cmdKeys">指令 key 数组</param>
        /// <returns>true=注册成功</returns>
        public static bool Register(long ownerId, string[] cmdKeys)
        {
            ICommandBus? bus = _instance;
            if (bus == null)
            {
                return false;
            }
            if (cmdKeys == null)
            {
                cmdKeys = new string[0];
            }
            bus.Register(ownerId, cmdKeys);
            return true;
        }

        /// <summary>
        /// 注销模块并清理残留指令
        /// </summary>
        /// <param name="ownerId">模块全局 ID</param>
        /// <returns>true=注销成功</returns>
        public static bool Unregister(long ownerId)
        {
            ICommandBus? bus = _instance;
            if (bus == null)
            {
                return false;
            }
            bus.Unregister(ownerId);
            return true;
        }

        /// <summary>
        /// 消费指定模块的指令邮件——无新指令（模板邮件）返回 false，让语料进入等待分支
        /// </summary>
        /// <param name="ownerId">模块全局 ID</param>
        /// <param name="hasCommands">是否有实际写入的指令</param>
        /// <param name="cmdKeys">指令 key 数组（与注册对齐）</param>
        /// <param name="cmdValues">int payload 数组</param>
        /// <param name="cmdTexts">string payload 数组</param>
        /// <param name="text">主值端口——第一个非空 CmdText（无则空串），数组→标量绑定用</param>
        /// <returns>true=有实际指令；false=无新指令（模板邮件）或未注入</returns>
        public static bool Consume(long ownerId, out bool hasCommands,
            out string[] cmdKeys, out int[] cmdValues, out string[] cmdTexts,
            out string text)
        {
            hasCommands = false;
            cmdKeys = new string[0];
            cmdValues = new int[0];
            cmdTexts = new string[0];
            text = "";
            ICommandBus? bus = _instance;
            if (bus == null)
            {
                return false;
            }
            CommandPack mail = bus.GetCommandEmail(ownerId);
            hasCommands = mail.HasCommands;
            cmdKeys = mail.CmdKeys;
            cmdValues = mail.CmdValues;
            cmdTexts = mail.CmdTexts;
            if (!hasCommands)
            {
                return false;
            }
            if (cmdTexts != null)
            {
                for (int i = 0; i < cmdTexts.Length; i = i + 1)
                {
                    if (!string.IsNullOrEmpty(cmdTexts[i]))
                    {
                        text = cmdTexts[i];
                        break;
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// 投递指令——int 与 text 双轨；text 非空时同时写入文本池
        /// </summary>
        /// <param name="key">指令 key（必须已注册）</param>
        /// <param name="value">int 指令值</param>
        /// <param name="text">文本指令（空=不写文本）</param>
        /// <returns>true=投递完成（未注册 key 静默拒绝，与 CommandBus 语义一致）</returns>
        public static bool Set(string key, int value, string text)
        {
            ICommandBus? bus = _instance;
            if (bus == null)
            {
                return false;
            }
            bus.Set(key, value);
            if (!string.IsNullOrEmpty(text))
            {
                bus.SetText(key, text);
            }
            return true;
        }

        /// <summary>
        /// 清空指定模块在池中的所有残留指令
        /// </summary>
        /// <param name="ownerId">模块全局 ID</param>
        /// <returns>true=清理成功</returns>
        public static bool Clean(long ownerId)
        {
            ICommandBus? bus = _instance;
            if (bus == null)
            {
                return false;
            }
            bus.Clean(ownerId);
            return true;
        }
    }

    /// <summary>
    /// 指令积木注册——进程启动时调用一次
    /// </summary>
    public static class CmdBrickRegistration
    {
        /// <summary>
        /// 注册全部指令积木
        /// </summary>
        public static void RegisterAll()
        {
            RegisterCmdRegister();
            RegisterCmdUnregister();
            RegisterCmdConsume();
            RegisterCmdSet();
            RegisterCmdClean();
        }

        /// <summary>
        /// 注册 cmd.register
        /// </summary>
        private static void RegisterCmdRegister()
        {
            BrickContract contract = new BrickContract("cmd.register", "Mau.Bricks.CmdBrick.Register");
            contract.Inputs.Add(new BrickPort("ownerId", typeof(long), "模块全局 ID"));
            contract.Inputs.Add(new BrickPort("cmdKeys", typeof(string[]), "指令 key 数组（三段式）"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }

        /// <summary>
        /// 注册 cmd.unregister
        /// </summary>
        private static void RegisterCmdUnregister()
        {
            BrickContract contract = new BrickContract("cmd.unregister", "Mau.Bricks.CmdBrick.Unregister");
            contract.Inputs.Add(new BrickPort("ownerId", typeof(long), "模块全局 ID"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }

        /// <summary>
        /// 注册 cmd.consume
        /// </summary>
        private static void RegisterCmdConsume()
        {
            BrickContract contract = new BrickContract("cmd.consume", "Mau.Bricks.CmdBrick.Consume");
            contract.Inputs.Add(new BrickPort("ownerId", typeof(long), "模块全局 ID"));
            contract.Outputs.Add(new BrickPort("hasCommands", typeof(bool), "是否有实际写入的指令"));
            contract.Outputs.Add(new BrickPort("cmdKeys", typeof(string[]), "指令 key 数组"));
            contract.Outputs.Add(new BrickPort("cmdValues", typeof(int[]), "int payload 数组"));
            contract.Outputs.Add(new BrickPort("cmdTexts", typeof(string[]), "string payload 数组"));
            contract.Outputs.Add(new BrickPort("text", typeof(string), "主值端口——第一个非空文本"));
            contract.MainOutput = "text";
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }

        /// <summary>
        /// 注册 cmd.set
        /// </summary>
        private static void RegisterCmdSet()
        {
            BrickContract contract = new BrickContract("cmd.set", "Mau.Bricks.CmdBrick.Set");
            contract.Inputs.Add(new BrickPort("key", typeof(string), "指令 key（必须已注册）"));
            contract.Inputs.Add(new BrickPort("value", typeof(int), "int 指令值"));
            contract.Inputs.Add(new BrickPort("text", typeof(string), "文本指令（空=不写）"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }

        /// <summary>
        /// 注册 cmd.clean
        /// </summary>
        private static void RegisterCmdClean()
        {
            BrickContract contract = new BrickContract("cmd.clean", "Mau.Bricks.CmdBrick.Clean");
            contract.Inputs.Add(new BrickPort("ownerId", typeof(long), "模块全局 ID"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }
    }
}

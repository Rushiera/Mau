using System;
using System.IO;
using Mau.Runtime;
using Xunit;

namespace Mau.Runtime.Tests
{
    /// <summary>
    /// M1 基座下沉测试——ConfigStore / VersionInfo / CommandPump
    /// 隔离：临时文件用 Guid 唯一命名 + finally 清理；CommandBus 测试 finally Unregister
    /// </summary>
    public sealed class M1BaseServicesTests
    {
        // ── ConfigStore ──

        /// <summary>
        /// 缺文件加载——空配置 + Get 返回默认值
        /// </summary>
        [Fact]
        public void ConfigStore_LoadMissingFile_ReturnsDefaults()
        {
            string path = Path.Combine(Path.GetTempPath(), "cfg_missing_" + Guid.NewGuid().ToString("N") + ".cfg");
            try
            {
                ConfigStore store = ConfigStore.Load(path);
                Assert.Equal("def", store.Get("nope", "def"));
                Assert.False(store.TryGet("nope", out string _));
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        /// <summary>
        /// 正常文件加载——注释/空行/坏行跳过，键值读取
        /// </summary>
        [Fact]
        public void ConfigStore_LoadFile_SkipsCommentsAndBadLines()
        {
            string path = Path.Combine(Path.GetTempPath(), "cfg_ok_" + Guid.NewGuid().ToString("N") + ".cfg");
            try
            {
                File.WriteAllText(path, "# 注释行\napi_key=sk-123\n\nendpoint = http://x\n坏行\n");
                ConfigStore store = ConfigStore.Load(path);
                Assert.Equal("sk-123", store.Get("api_key", ""));
                Assert.Equal("http://x", store.Get("endpoint", ""));
                Assert.Equal("def", store.Get("nope", "def"));
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        /// <summary>
        /// Set + Save 落盘 → 重新 Load 读回（往返一致）
        /// </summary>
        [Fact]
        public void ConfigStore_SetSave_ReloadRoundtrip()
        {
            string path = Path.Combine(Path.GetTempPath(), "cfg_round_" + Guid.NewGuid().ToString("N") + ".cfg");
            try
            {
                ConfigStore store = ConfigStore.Load(path);
                store.Set("api_key", "sk-456");
                store.Set("endpoint", "https://api.test");
                store.Save();
                Assert.True(File.Exists(path));
                ConfigStore reloaded = ConfigStore.Load(path);
                Assert.Equal("sk-456", reloaded.Get("api_key", ""));
                Assert.Equal("https://api.test", reloaded.Get("endpoint", ""));
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
                if (File.Exists(path + ".tmp"))
                {
                    File.Delete(path + ".tmp");
                }
            }
        }

        /// <summary>
        /// TryGet 存在返回 true + 值，不存在返回 false
        /// </summary>
        [Fact]
        public void ConfigStore_TryGet_MissingReturnsFalse()
        {
            ConfigStore store = ConfigStore.Load("");
            string value;
            Assert.False(store.TryGet("nope", out value));
            store.Set("k", "v");
            Assert.True(store.TryGet("k", out value));
            Assert.Equal("v", value);
        }

        // ── VersionInfo ──

        /// <summary>
        /// 入口程序集名非空
        /// </summary>
        [Fact]
        public void VersionInfo_EntryName_NonEmpty()
        {
            Assert.False(string.IsNullOrEmpty(VersionInfo.GetEntryName()));
        }

        /// <summary>
        /// 入口程序集版本非空
        /// </summary>
        [Fact]
        public void VersionInfo_EntryVersion_NonEmpty()
        {
            Assert.False(string.IsNullOrEmpty(VersionInfo.GetEntryVersion()));
        }

        // ── CommandPump ──

        /// <summary>
        /// PushText——冻结后邮件携带文本 payload
        /// </summary>
        [Fact]
        public void CommandPump_PushText_DeliversToRegisteredOwner()
        {
            ThreadGuard guard = new ThreadGuard();
            CommandBus bus = new CommandBus(guard);
            CommandPump pump = new CommandPump(bus);
            long owner = 9001;
            bus.Register(owner, new string[] { "chat_talk_msg" });
            try
            {
                pump.PushText("chat_talk_msg", "你好");
                bus.BeginTickInput();
                CommandPack mail = bus.GetCommandEmail(owner);
                Assert.True(mail.HasCommands);
                Assert.Equal("chat_talk_msg", mail.CmdKeys[0]);
                Assert.Equal("你好", mail.CmdTexts[0]);
            }
            finally
            {
                bus.Unregister(owner);
            }
        }

        /// <summary>
        /// Push——冻结后邮件携带 int payload
        /// </summary>
        [Fact]
        public void CommandPump_Push_DeliversIntValue()
        {
            ThreadGuard guard = new ThreadGuard();
            CommandBus bus = new CommandBus(guard);
            CommandPump pump = new CommandPump(bus);
            long owner = 9002;
            bus.Register(owner, new string[] { "chat_talk_msg" });
            try
            {
                pump.Push("chat_talk_msg", 42);
                bus.BeginTickInput();
                CommandPack mail = bus.GetCommandEmail(owner);
                Assert.True(mail.HasCommands);
                Assert.Equal("chat_talk_msg", mail.CmdKeys[0]);
                Assert.Equal(42, mail.CmdValues[0]);
            }
            finally
            {
                bus.Unregister(owner);
            }
        }
    }
}

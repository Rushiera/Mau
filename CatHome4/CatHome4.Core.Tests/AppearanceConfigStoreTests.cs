using System;
using System.IO;
using Mau.Runtime;
using Xunit;

namespace CatHome4.Core.Tests
{
    /// <summary>
    /// 外观配置池测试（design-ch4-appearance.md）——池 CRUD / 唯一默认语义 / 校验拒绝 / 落盘往返 / 排序。
    /// 落盘产物根参数化到临时目录（Dispose 清理）。
    /// </summary>
    public class AppearanceConfigStoreTests : IDisposable
    {
        /// <summary>测试临时目录——落盘往返用（Dispose 清理）</summary>
        private readonly string _dir;

        /// <summary>建立测试环境——独立临时目录。</summary>
        public AppearanceConfigStoreTests()
        {
            _dir = Path.Combine(Path.GetTempPath(),
                "ch4-appearance-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        /// <summary>清理——删临时目录（失败不掩盖测试结论）。</summary>
        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_dir))
                {
                    Directory.Delete(_dir, true);
                }
            }
            catch (IOException)
            {
                // 清理失败不改变测试结论（临时目录可延时回收）
            }
        }

        /// <summary>构造一条完整条目（随机身份）。</summary>
        private static CH_AppearanceConfig MakeConfig(string name, string petKind,
            string ttsStyle, bool roundSummary, bool summaryAudio)
        {
            return MakeConfigWithId(Guid.NewGuid(), name, petKind, ttsStyle,
                roundSummary, summaryAudio);
        }

        /// <summary>构造一条完整条目（指定身份——排序 / 引用断言用）。</summary>
        private static CH_AppearanceConfig MakeConfigWithId(Guid id, string name,
            string petKind, string ttsStyle, bool roundSummary, bool summaryAudio)
        {
            CH_AppearanceConfig config = new CH_AppearanceConfig();
            config.AppearanceId = id;
            config.DisplayName = name;
            config.PetKind = petKind;
            config.TtsStyle = ttsStyle;
            config.RoundSummary = roundSummary;
            config.SummaryAudio = summaryAudio;
            return config;
        }

        /// <summary>新建一个绑定测试目录的池。</summary>
        private CH_AppearanceConfigStore NewStore()
        {
            return new CH_AppearanceConfigStore(_dir);
        }

        /// <summary>空池——GetAll 返空数组、ResolveDefault 返 null（不造值、不静默回退）。</summary>
        [Fact]
        public void EmptyPool_ReturnsEmptyAndNull()
        {
            CH_AppearanceConfigStore store = NewStore();
            Assert.Empty(store.GetAll());
            Assert.Null(store.ResolveDefault());
            Assert.False(store.Delete(Guid.NewGuid()));
            Assert.False(store.SetDefault(Guid.NewGuid()));
        }

        /// <summary>池空时首个条目标默认——唯一外观必然被跟随默认的猫消费。</summary>
        [Fact]
        public void Save_FirstEntry_AutoBecomesDefault()
        {
            CH_AppearanceConfigStore store = NewStore();
            store.Save(MakeConfig("first", "", "", true, false));
            CH_AppearanceConfig[] all = store.GetAll();
            Assert.Single(all);
            Assert.True(all[0].IsDefault);
        }

        /// <summary>第二条不自动标默认——默认切换只走 SetDefault 显式操作。</summary>
        [Fact]
        public void Save_SecondEntry_KeepsExistingDefault()
        {
            CH_AppearanceConfigStore store = NewStore();
            store.Save(MakeConfig("first", "", "", true, false));
            store.Save(MakeConfig("second", "", "", false, false));
            CH_AppearanceConfig[] all = store.GetAll();
            Assert.Equal(2, all.Length);
            Assert.Equal(1, CountDefault(all));
        }

        /// <summary>SetDefault 唯一默认语义——清其余、置目标。</summary>
        [Fact]
        public void SetDefault_ClearsOthers()
        {
            CH_AppearanceConfigStore store = NewStore();
            Guid first = Guid.NewGuid();
            Guid second = Guid.NewGuid();
            store.Save(MakeConfigWithId(first, "first", "", "", true, false));
            store.Save(MakeConfigWithId(second, "second", "", "", false, false));
            Assert.True(store.SetDefault(second));
            CH_AppearanceConfig[] all = store.GetAll();
            Assert.Equal(1, CountDefault(all));
            CH_AppearanceConfig resolved = store.ResolveDefault();
            Assert.NotNull(resolved);
            Assert.Equal(second, resolved.AppearanceId);
        }

        /// <summary>按身份读取——命中返完整字段副本，未命中返空配置。</summary>
        [Fact]
        public void TryGet_RoundTripsAllFields()
        {
            CH_AppearanceConfigStore store = NewStore();
            Guid id = Guid.NewGuid();
            store.Save(MakeConfigWithId(id, "work", "greencat", "gentle", true, true));
            CH_AppearanceConfig got;
            Assert.True(store.TryGet(id, out got));
            Assert.Equal("work", got.DisplayName);
            Assert.Equal("greencat", got.PetKind);
            Assert.Equal("gentle", got.TtsStyle);
            Assert.True(got.RoundSummary);
            Assert.True(got.SummaryAudio);
            CH_AppearanceConfig missing;
            Assert.False(store.TryGet(Guid.NewGuid(), out missing));
            Assert.Equal(Guid.Empty, missing.AppearanceId);
        }

        /// <summary>落盘往返——新实例读同一目录还原全部条目（含默认标记）。</summary>
        [Fact]
        public void Save_ThenReload_IsPersisted()
        {
            CH_AppearanceConfigStore store = NewStore();
            Guid id = Guid.NewGuid();
            store.Save(MakeConfigWithId(id, "persisted", "pet", "tts", true, false));
            CH_AppearanceConfigStore reloaded = NewStore();
            CH_AppearanceConfig[] all = reloaded.GetAll();
            Assert.Single(all);
            Assert.Equal(id, all[0].AppearanceId);
            Assert.True(all[0].IsDefault);
            Assert.Equal("pet", all[0].PetKind);
        }

        /// <summary>删除——存在即移除；删默认后 ResolveDefault 归 null。</summary>
        [Fact]
        public void Delete_RemovesEntry()
        {
            CH_AppearanceConfigStore store = NewStore();
            Guid id = Guid.NewGuid();
            store.Save(MakeConfigWithId(id, "only", "", "", true, false));
            Assert.True(store.Delete(id));
            Assert.Empty(store.GetAll());
            Assert.Null(store.ResolveDefault());
            Assert.False(store.Delete(id));
        }

        /// <summary>校验拒绝——空显示名与零值身份不得落盘。</summary>
        [Fact]
        public void Save_RejectsInvalidEntry()
        {
            CH_AppearanceConfigStore store = NewStore();
            CH_AppearanceConfig blankName = MakeConfig("", "", "", false, false);
            Assert.Throws<InvalidDataException>(delegate { store.Save(blankName); });
            CH_AppearanceConfig zeroId = MakeConfig("named", "", "", false, false);
            zeroId.AppearanceId = Guid.Empty;
            Assert.Throws<InvalidDataException>(delegate { store.Save(zeroId); });
            Assert.Empty(store.GetAll());
        }

        /// <summary>字段归一——显示名 / 桌宠 / TTS 前后空白被修剪。</summary>
        [Fact]
        public void Save_TrimsTextFields()
        {
            CH_AppearanceConfigStore store = NewStore();
            store.Save(MakeConfig("  spaced  ", "  pet  ", "  tts  ", false, false));
            CH_AppearanceConfig[] all = store.GetAll();
            Assert.Equal("spaced", all[0].DisplayName);
            Assert.Equal("pet", all[0].PetKind);
            Assert.Equal("tts", all[0].TtsStyle);
        }

        /// <summary>排序——GetAll 按 AppearanceId 升序（稳定输出，不依赖写入序）。</summary>
        [Fact]
        public void GetAll_SortsByIdentity()
        {
            CH_AppearanceConfigStore store = NewStore();
            Guid low = new Guid("00000000-0000-0000-0000-000000000001");
            Guid high = new Guid("ffffffff-ffff-ffff-ffff-ffffffffffff");
            store.Save(MakeConfigWithId(high, "high", "", "", false, false));
            store.Save(MakeConfigWithId(low, "low", "", "", false, false));
            CH_AppearanceConfig[] all = store.GetAll();
            Assert.Equal(2, all.Length);
            Assert.Equal(low, all[0].AppearanceId);
            Assert.Equal(high, all[1].AppearanceId);
        }

        /// <summary>同身份重写——不新增条目，字段被覆盖。</summary>
        [Fact]
        public void Save_SameIdentity_ReplacesInPlace()
        {
            CH_AppearanceConfigStore store = NewStore();
            Guid id = Guid.NewGuid();
            store.Save(MakeConfigWithId(id, "before", "", "", true, false));
            store.Save(MakeConfigWithId(id, "after", "newpet", "", false, true));
            CH_AppearanceConfig[] all = store.GetAll();
            Assert.Single(all);
            Assert.Equal("after", all[0].DisplayName);
            Assert.Equal("newpet", all[0].PetKind);
            Assert.False(all[0].RoundSummary);
            Assert.True(all[0].SummaryAudio);
            Assert.True(all[0].IsDefault);
        }

        /// <summary>计数默认项——断言唯一默认语义用。</summary>
        private static int CountDefault(CH_AppearanceConfig[] configs)
        {
            int count = 0;
            for (int i = 0; i < configs.Length; i = i + 1)
            {
                if (configs[i].IsDefault)
                {
                    count = count + 1;
                }
            }
            return count;
        }
    }
}

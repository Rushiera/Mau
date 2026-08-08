using System;
using System.IO;
using Mau.Runtime;
using Xunit;

namespace Mau.Runtime.Tests
{
    /// <summary>
    /// M2 基座测试——Dog（通用工单载体）+ Pet（常驻服务）三分类基座
    /// 隔离：临时文件 Guid 唯一 + finally 清理；Dog 注册后 finally UnregisterFlow
    /// </summary>
    public sealed class M2BaseTests
    {
        /// <summary>
        /// 测试用 Pet——Tick 计数
        /// </summary>
        private sealed class CountingPet : PetBase
        {
            /// <summary>
            /// Tick 次数
            /// </summary>
            public int TickCount;

            /// <summary>
            /// 构造计数 Pet
            /// </summary>
            public CountingPet(string name) : base(name)
            {
            }

            /// <summary>
            /// Pet 类型名
            /// </summary>
            public override string PetType
            {
                get { return "CountingPet"; }
            }

            /// <summary>
            /// 每帧驱动计数
            /// </summary>
            public override void Tick()
            {
                TickCount = TickCount + 1;
            }
        }

        /// <summary>
        /// 组装 FlowRunner——全部机制注入
        /// </summary>
        /// <returns>Runner + OA</returns>
        private static FlowRunner CreateRunner(out OA oa)
        {
            ThreadGuard guard = new ThreadGuard();
            oa = new OA(guard);
            CommandBus cmd = new CommandBus(guard);
            IdAllocator ids = new IdAllocator();
            return new FlowRunner(guard, oa, cmd, ids);
        }

        /// <summary>
        /// 注册 Dog 并 Post 建单
        /// </summary>
        /// <param name="runner">帧序宿主</param>
        /// <param name="oa">OA</param>
        /// <param name="name">Dog 名字</param>
        /// <returns>Dog 实例</returns>
        private static DogBase RegisterDog(FlowRunner runner, OA oa, string name)
        {
            DogBase dog = new DogBase(oa, name);
            long dogId = runner.RegisterFlow(dog, name);
            dog.BindId(dogId);
            return dog;
        }

        // ── Dog 生命周期 ──

        /// <summary>
        /// 闭环——Post → 写载荷 → 执行方认领完成 → 轮询 Closed → Collect 回执
        /// </summary>
        [Fact]
        public void Dog_Lifecycle_ClosedCollect()
        {
            FlowRunner runner;
            OA oa;
            runner = CreateRunner(out oa);
            DogBase dog = RegisterDog(runner, oa, "Dog1");
            try
            {
                Assert.True(dog.Post("Demo", "搬运", 100));
                Assert.True(dog.SetStr("text", "把这段搬到 A"));
                Assert.True(dog.SetInt("count", 3));
                Assert.Equal(DogPhase.Waiting, dog.Phase);
                Assert.True(dog.OfficeId > 0);

                // 执行方（模拟 Cat）认领 + 完成
                long executor = 9000;
                Office office = oa.GetOffice(dog.OfficeId);
                OfficeData result = OfficeData.Empty();
                result.Strs["moved"] = "A";
                System.Collections.Generic.List<Office> claimed = oa.ClaimBatch(executor, new long[] { office.OfficeId });
                Assert.Single(claimed);
                oa.Complete(office.OfficeId, executor, result);

                // 全局 Tick 驱动 Dog 轮询
                runner.Tick();
                Assert.Equal(DogPhase.PickingUp, dog.Phase);
                Assert.True(dog.IsClosed());
                Assert.False(dog.IsTimeout());

                OfficeData collected;
                Assert.True(dog.Collect(out collected));
                Assert.Equal("A", collected.Strs["moved"]);
                Assert.Equal(DogPhase.Done, dog.Phase);
            }
            finally
            {
                runner.UnregisterFlow(dog.DogId);
            }
        }

        /// <summary>
        /// 超时——短超时 → 全局 Tick 结算 → Dog 轮询到 TimeOut → Collect 空回执
        /// </summary>
        [Fact]
        public void Dog_Lifecycle_Timeout()
        {
            FlowRunner runner;
            OA oa;
            runner = CreateRunner(out oa);
            DogBase dog = RegisterDog(runner, oa, "DogT");
            try
            {
                Assert.True(dog.Post("Demo", "没人干", 1));
                // 跑 3 帧——OA 结算超时 + Dog 轮询
                runner.Tick();
                runner.Tick();
                runner.Tick();
                Assert.Equal(DogPhase.PickingUp, dog.Phase);
                Assert.True(dog.IsTimeout());
                Assert.False(dog.IsClosed());

                OfficeData collected;
                Assert.True(dog.Collect(out collected));
                Assert.True(collected.IsEmpty());
                Assert.Equal(DogPhase.Done, dog.Phase);
            }
            finally
            {
                runner.UnregisterFlow(dog.DogId);
            }
        }

        /// <summary>
        /// 持久化恢复——Waiting Dog 落盘 → 新 OA + TryLoad → 重建单 + 载荷 → 执行方再完成 → 恢复 Dog 收回执
        /// </summary>
        [Fact]
        public void Dog_Persist_RestoreWaiting()
        {
            string path = Path.Combine(Path.GetTempPath(), "dog_" + Guid.NewGuid().ToString("N") + ".json");
            FlowRunner runner;
            OA oa;
            runner = CreateRunner(out oa);
            DogBase dog = RegisterDog(runner, oa, "DogP");
            try
            {
                Assert.True(dog.Post("Demo", "持久搬运", 100));
                Assert.True(dog.SetStr("text", "跨重启文本"));
                Assert.True(dog.Save(path));
                Assert.True(File.Exists(path));
            }
            finally
            {
                runner.UnregisterFlow(dog.DogId);
            }

            // 新进程环境——新 OA/Runner
            FlowRunner runner2;
            OA oa2;
            runner2 = CreateRunner(out oa2);
            IDog? restored = dog.TryLoad(path, oa2);
            Assert.NotNull(restored);
            try
            {
                long newId = runner2.RegisterFlow(restored, restored.DogName);
                restored.BindId(newId);
                Assert.Equal(DogPhase.Waiting, restored.Phase);
                Assert.True(restored.OfficeId > 0);

                // 载荷已写回——执行方读到原载荷
                string text;
                Assert.True(oa2.GetStr(restored.OfficeId, "text", out text));
                Assert.Equal("跨重启文本", text);

                // 执行方完成 → 恢复 Dog 收到回执
                OfficeData result = OfficeData.Empty();
                result.Strs["moved"] = "B";
                System.Collections.Generic.List<Office> claimed = oa2.ClaimBatch(9100, new long[] { restored.OfficeId });
                Assert.Single(claimed);
                oa2.Complete(restored.OfficeId, 9100, result);
                runner2.Tick();
                Assert.True(restored.IsClosed());
                OfficeData collected;
                Assert.True(restored.Collect(out collected));
                Assert.Equal("B", collected.Strs["moved"]);
            }
            finally
            {
                runner2.UnregisterFlow(restored.DogId);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        /// <summary>
        /// 自动闭合——PickingUp 停留超阈值 → 自动 Done → FlowRunner 帧末回收（未闭合兜底）
        /// </summary>
        [Fact]
        public void Dog_AutoClose_RecyclesAfterPickUpStall()
        {
            FlowRunner runner;
            OA oa;
            runner = CreateRunner(out oa);
            DogBase dog = RegisterDog(runner, oa, "DogAuto");
            try
            {
                Assert.True(dog.Post("Demo", "没人收", 1));
                // 跑 3 帧——OA 结算超时 + Dog 轮询到 TimeOut → PickingUp
                runner.Tick();
                runner.Tick();
                runner.Tick();
                Assert.Equal(DogPhase.PickingUp, dog.Phase);
                Assert.True(dog.IsTimeout());

                // 模拟发单方崩溃——不 finish，继续 Tick 到自动闭合阈值
                for (int i = 0; i < DogBase.AutoCloseFrames + 10; i = i + 1)
                {
                    runner.Tick();
                }
                // 自动闭合 → Done → FlowRunner 帧末回收
                Assert.True(dog.Phase == DogPhase.Done || runner.GetFlow(dog.DogId) == null);
                if (runner.GetFlow(dog.DogId) != null)
                {
                    // 若恰好在闭合帧——再跑一帧让回收执行
                    runner.Tick();
                }
                Assert.Null(runner.GetFlow(dog.DogId));
            }
            finally
            {
                runner.UnregisterFlow(dog.DogId);
            }
        }

        // ── Pet 基座 ──

        /// <summary>
        /// Pet——注册进 FlowRunner 由全局 Tick 驱动
        /// </summary>
        [Fact]
        public void Pet_Registered_DrivenByGlobalTick()
        {
            FlowRunner runner;
            OA oa;
            runner = CreateRunner(out oa);
            CountingPet pet = new CountingPet("ConsoleView");
            long petId = runner.RegisterFlow(pet, "ConsoleView");
            pet.BindId(petId);
            try
            {
                runner.Tick();
                runner.Tick();
                Assert.Equal(2, pet.TickCount);
                Assert.Equal("CountingPet", pet.PetType);
                Assert.Equal(petId, pet.PetId);
            }
            finally
            {
                runner.UnregisterFlow(petId);
            }
        }

        /// <summary>
        /// Registry——种类分类（is 推断：Dog→dog / Pet→pet / 普通 Flow→flow）
        /// </summary>
        [Fact]
        public void Registry_Kind_ClassifiesDogPetFlow()
        {
            FlowRunner runner;
            OA oa;
            runner = CreateRunner(out oa);

            DogBase dog = RegisterDog(runner, oa, "KindDog");
            CountingPet pet = new CountingPet("KindPet");
            long petId = runner.RegisterFlow(pet, "KindPet");
            pet.BindId(petId);
            FlowRunnerTests_CountingFlow plain = new FlowRunnerTests_CountingFlow();
            long plainId = runner.RegisterFlow(plain, "KindFlow");

            try
            {
                FlowEntry[] entries = runner.GetStatus().Flows!;
                string dogKind = "";
                string petKind = "";
                string flowKind = "";
                for (int i = 0; i < entries.Length; i = i + 1)
                {
                    if (entries[i].Id == dog.DogId)
                    {
                        dogKind = entries[i].Kind;
                    }
                    else if (entries[i].Id == petId)
                    {
                        petKind = entries[i].Kind;
                    }
                    else if (entries[i].Id == plainId)
                    {
                        flowKind = entries[i].Kind;
                    }
                }
                Assert.Equal("dog", dogKind);
                Assert.Equal("pet", petKind);
                Assert.Equal("flow", flowKind);
            }
            finally
            {
                runner.UnregisterFlow(dog.DogId);
                runner.UnregisterFlow(petId);
                runner.UnregisterFlow(plainId);
            }
        }

        /// <summary>
        /// 测试用普通 Flow——复用 FlowRunnerTests 的计数流
        /// </summary>
        private sealed class FlowRunnerTests_CountingFlow : IFlow
        {
            /// <summary>
            /// 每帧驱动
            /// </summary>
            public void Tick()
            {
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Threading;
using Mau.Runtime;
using Xunit;

namespace Mau.Runtime.Tests
{
    /// <summary>
    /// DataBox 中台测试——接口逻辑 + 纠错 + 并发压力
    /// 隔离：每个测试 finally ClearAll 恢复现场（静态全局配置测试隔离——空串不覆盖是静默陷阱）
    /// 串行：AuditSerial——CommandBus C 类埋点（LogStore 写 DataBox "log" scope）并行污染（2026-08-10）
    /// </summary>
    [Collection("AuditSerial")]
    public sealed class DataBoxTests
    {
        /// <summary>
        /// 测试服务接口
        /// </summary>
        private interface ITestService
        {
            /// <summary>
            /// 服务名
            /// </summary>
            string Name { get; }
        }

        /// <summary>
        /// 测试服务实现
        /// </summary>
        private sealed class TestService : ITestService
        {
            /// <summary>
            /// 服务名
            /// </summary>
            public string Name
            {
                get { return "test"; }
            }
        }

        /// <summary>
        /// 测试数据类
        /// </summary>
        private sealed class TestState
        {
            /// <summary>
            /// 计数
            /// </summary>
            public int Count;
        }

        // ── 接口逻辑 ──

        /// <summary>
        /// 服务绑定与解析——Bind 后 TryResolve 同实例
        /// </summary>
        [Fact]
        public void BindResolve_ReturnsSameInstance()
        {
            try
            {
                DataBox.Bind<ITestService>(new TestService());
                ITestService resolved;
                Assert.True(DataBox.TryResolve<ITestService>(out resolved));
                Assert.Equal("test", resolved.Name);
                ITestService again;
                Assert.True(DataBox.TryResolve<ITestService>(out again));
                Assert.Same(resolved, again);
            }
            finally
            {
                DataBox.ClearAll();
            }
        }

        /// <summary>
        /// 重复绑定 = 替换——最后绑定生效
        /// </summary>
        [Fact]
        public void BindTwice_LastWins()
        {
            try
            {
                DataBox.Bind<ITestService>(new TestService());
                DataBox.Bind<ITestService>(new TestService());
                ITestService resolved;
                Assert.True(DataBox.TryResolve<ITestService>(out resolved));
            }
            finally
            {
                DataBox.ClearAll();
            }
        }

        /// <summary>
        /// 解绑后解析失败
        /// </summary>
        [Fact]
        public void Unbind_ResolveFails()
        {
            try
            {
                DataBox.Bind<ITestService>(new TestService());
                DataBox.Unbind<ITestService>();
                ITestService resolved;
                Assert.False(DataBox.TryResolve<ITestService>(out resolved));
                Assert.Null(resolved);
            }
            finally
            {
                DataBox.ClearAll();
            }
        }

        /// <summary>
        /// 获取或创建——同 scope+key 返回同一实例；不同 key 隔离
        /// </summary>
        [Fact]
        public void GetOrCreate_SameKeySameInstance_IsolatedByKey()
        {
            try
            {
                TestState a = DataBox.GetOrCreate<TestState>("log", "a");
                TestState b = DataBox.GetOrCreate<TestState>("log", "a");
                Assert.Same(a, b);
                TestState c = DataBox.GetOrCreate<TestState>("log", "c");
                Assert.NotSame(a, c);
            }
            finally
            {
                DataBox.ClearAll();
            }
        }

        /// <summary>
        /// 写入与读取——Set 后 TryGet 同值；覆盖生效
        /// </summary>
        [Fact]
        public void SetGet_RoundTrip_AndOverwrite()
        {
            try
            {
                TestState state = new TestState();
                state.Count = 42;
                DataBox.Set<TestState>("ctx", "session", state);
                TestState read;
                Assert.True(DataBox.TryGet<TestState>("ctx", "session", out read));
                Assert.Equal(42, read.Count);
                TestState other = new TestState();
                other.Count = 7;
                DataBox.Set<TestState>("ctx", "session", other);
                Assert.True(DataBox.TryGet<TestState>("ctx", "session", out read));
                Assert.Equal(7, read.Count);
            }
            finally
            {
                DataBox.ClearAll();
            }
        }

        /// <summary>
        /// 删除与清空——Remove 后 TryGet false；ClearScope 后全部消失
        /// </summary>
        [Fact]
        public void RemoveAndClearScope_EmptyReads()
        {
            try
            {
                DataBox.Set<int>("s", "k", 1);
                Assert.True(DataBox.TryGet<int>("s", "k", out _));
                Assert.True(DataBox.Remove("s", "k"));
                Assert.False(DataBox.TryGet<int>("s", "k", out _));
                Assert.False(DataBox.Remove("s", "k"));

                DataBox.Set<int>("s", "a", 1);
                DataBox.Set<int>("s", "b", 2);
                DataBox.ClearScope("s");
                Assert.False(DataBox.TryGet<int>("s", "a", out _));
                Assert.False(DataBox.TryGet<int>("s", "b", out _));
            }
            finally
            {
                DataBox.ClearAll();
            }
        }

        /// <summary>
        /// 快照——服务与数据全量可见
        /// </summary>
        [Fact]
        public void Capture_ContainsServicesAndData()
        {
            // 开头清理——防静态污染（CommandBus C 类埋点写入 LogStore 后 log scope 有 sync/entries；2026-08-10）
            DataBox.ClearAll();
            try
            {
                DataBox.Bind<ITestService>(new TestService());
                DataBox.Set<int>("log", "count", 3);
                DataBoxSnapshot snapshot = DataBox.Capture();
                Assert.Single(snapshot.Services);
                Assert.Contains(snapshot.Services,
                    s => s.TypeName.Contains("ITestService", StringComparison.Ordinal));
                Assert.Single(snapshot.Data);
                Assert.Equal("log", snapshot.Data[0].Scope);
                Assert.Equal("count", snapshot.Data[0].Key);
                Assert.Equal(3, (int)snapshot.Data[0].Value!);
            }
            finally
            {
                DataBox.ClearAll();
            }
        }

        // ── 纠错 ──

        /// <summary>
        /// 空 scope/key 拒绝——严格封装
        /// </summary>
        [Fact]
        public void EmptyKeys_ThrowArgumentException()
        {
            try
            {
                Assert.Throws<ArgumentException>(delegate { DataBox.GetOrCreate<TestState>("", "k"); });
                Assert.Throws<ArgumentException>(delegate { DataBox.GetOrCreate<TestState>("s", ""); });
                Assert.Throws<ArgumentException>(delegate { DataBox.GetOrCreate<TestState>(null!, "k"); });
                Assert.Throws<ArgumentException>(delegate { DataBox.GetOrCreate<TestState>("s", null!); });
                Assert.Throws<ArgumentException>(delegate { DataBox.Set<int>("", "k", 1); });
                Assert.Throws<ArgumentException>(delegate { DataBox.TryGet<int>("s", " ", out _); });
                Assert.Throws<ArgumentException>(delegate { DataBox.Remove("", "k"); });
                Assert.Throws<ArgumentException>(delegate { DataBox.ClearScope(" "); });
            }
            finally
            {
                DataBox.ClearAll();
            }
        }

        /// <summary>
        /// 绑定 null 拒绝
        /// </summary>
        [Fact]
        public void BindNull_ThrowsArgumentException()
        {
            try
            {
                Assert.Throws<ArgumentException>(delegate { DataBox.Bind<ITestService>(null!); });
            }
            finally
            {
                DataBox.ClearAll();
            }
        }

        /// <summary>
        /// 写入 null 拒绝
        /// </summary>
        [Fact]
        public void SetNull_ThrowsArgumentException()
        {
            try
            {
                Assert.Throws<ArgumentException>(delegate { DataBox.Set<string>("s", "k", null!); });
            }
            finally
            {
                DataBox.ClearAll();
            }
        }

        /// <summary>
        /// 未绑定解析 false；未知数据 false
        /// </summary>
        [Fact]
        public void UnboundAndUnknown_ReturnFalse()
        {
            try
            {
                ITestService resolved;
                Assert.False(DataBox.TryResolve<ITestService>(out resolved));
                TestState state;
                Assert.False(DataBox.TryGet<TestState>("nope", "none", out state));
                Assert.Null(state);
            }
            finally
            {
                DataBox.ClearAll();
            }
        }

        /// <summary>
        /// 类型不匹配 TryGet false——不跨类型读取
        /// </summary>
        [Fact]
        public void TypeMismatch_TryGetFalse()
        {
            try
            {
                DataBox.Set<int>("s", "k", 5);
                string wrong;
                Assert.False(DataBox.TryGet<string>("s", "k", out wrong));
                Assert.Null(wrong);
                int right;
                Assert.True(DataBox.TryGet<int>("s", "k", out right));
                Assert.Equal(5, right);
            }
            finally
            {
                DataBox.ClearAll();
            }
        }

        // ── 并发压力 ──

        /// <summary>
        /// 并发绑定与解析——多线程 Bind/Unbind/TryResolve 无异常
        /// </summary>
        [Fact]
        public void ConcurrentBindResolve_NoException()
        {
            try
            {
                const int threadCount = 16;
                const int iterations = 500;
                Exception? failure = null;
                List<Thread> threads = new List<Thread>();
                for (int t = 0; t < threadCount; t++)
                {
                    Thread thread = new Thread(delegate ()
                    {
                        try
                        {
                            for (int i = 0; i < iterations; i++)
                            {
                                DataBox.Bind<ITestService>(new TestService());
                                ITestService resolved;
                                DataBox.TryResolve<ITestService>(out resolved);
                                DataBox.Unbind<ITestService>();
                            }
                        }
                        catch (Exception ex)
                        {
                            Interlocked.CompareExchange(ref failure, ex, null);
                        }
                    });
                    thread.IsBackground = true;
                    threads.Add(thread);
                }
                for (int i = 0; i < threads.Count; i++)
                {
                    threads[i].Start();
                }
                for (int i = 0; i < threads.Count; i++)
                {
                    threads[i].Join();
                }
                Assert.Null(failure);
            }
            finally
            {
                DataBox.ClearAll();
            }
        }

        /// <summary>
        /// 并发数据读写——多线程不同 scope 并行 GetOrCreate/Set/Get 无异常且值隔离
        /// </summary>
        [Fact]
        public void ConcurrentScopedData_IsolatedAndConsistent()
        {
            try
            {
                const int threadCount = 16;
                const int iterations = 500;
                Exception? failure = null;
                List<Thread> threads = new List<Thread>();
                for (int t = 0; t < threadCount; t++)
                {
                    string scope = "scope-" + t.ToString();
                    Thread thread = new Thread(delegate ()
                    {
                        try
                        {
                            for (int i = 0; i < iterations; i++)
                            {
                                TestState state = DataBox.GetOrCreate<TestState>(scope, "state");
                                lock (state)
                                {
                                    state.Count++;
                                }
                                DataBox.Set<int>(scope, "seq", i);
                                int read;
                                DataBox.TryGet<int>(scope, "seq", out read);
                                if (read != i)
                                {
                                    throw new InvalidOperationException("seq mismatch");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Interlocked.CompareExchange(ref failure, ex, null);
                        }
                    });
                    thread.IsBackground = true;
                    threads.Add(thread);
                }
                for (int i = 0; i < threads.Count; i++)
                {
                    threads[i].Start();
                }
                for (int i = 0; i < threads.Count; i++)
                {
                    threads[i].Join();
                }
                Assert.Null(failure);
                // 最终一致——每 scope 计数 = iterations（隔离验证）
                for (int t = 0; t < threadCount; t++)
                {
                    TestState state;
                    Assert.True(DataBox.TryGet<TestState>("scope-" + t.ToString(), "state", out state));
                    Assert.Equal(iterations, state.Count);
                }
            }
            finally
            {
                DataBox.ClearAll();
            }
        }

        /// <summary>
        /// 并发同 scope 不同 key——锁粒度验证（scope 内串行，不跨 scope 阻塞）
        /// </summary>
        [Fact]
        public void ConcurrentSameScopeDifferentKeys_AllSurvive()
        {
            try
            {
                const int keyCount = 64;
                const int iterations = 200;
                Exception? failure = null;
                List<Thread> threads = new List<Thread>();
                for (int k = 0; k < keyCount; k++)
                {
                    string key = "key-" + k.ToString();
                    Thread thread = new Thread(delegate ()
                    {
                        try
                        {
                            for (int i = 0; i < iterations; i++)
                            {
                                TestState state = DataBox.GetOrCreate<TestState>("shared", key);
                                lock (state)
                                {
                                    state.Count++;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Interlocked.CompareExchange(ref failure, ex, null);
                        }
                    });
                    thread.IsBackground = true;
                    threads.Add(thread);
                }
                for (int i = 0; i < threads.Count; i++)
                {
                    threads[i].Start();
                }
                for (int i = 0; i < threads.Count; i++)
                {
                    threads[i].Join();
                }
                Assert.Null(failure);
                for (int k = 0; k < keyCount; k++)
                {
                    TestState state;
                    Assert.True(DataBox.TryGet<TestState>("shared", "key-" + k.ToString(), out state));
                    Assert.Equal(iterations, state.Count);
                }
            }
            finally
            {
                DataBox.ClearAll();
            }
        }
    }
}

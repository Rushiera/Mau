using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CH4;
using Mau.Runtime;
using Xunit;

namespace CatHome4.Core.Tests
{
    /// <summary>
    /// 全局工具注册面并发契约测试——整表替换语义（并发写不炸 / 并发读写安全 / 全量重建无陈旧残留）。
    /// 挂 GlobalToolState 集合：与其它触碰全局注册表的测试类串行（单向数据流——全局单例访问面排队）。
    /// </summary>
    [Collection("GlobalToolState")]
    public class ToolRegistryConcurrencyTests
    {
        /// <summary>构造单工具声明表——并发测试用最小载荷。</summary>
        /// <param name="name">工具名</param>
        /// <returns>声明数组</returns>
        private static ToolSpec[] Specs(string name)
        {
            return new ToolSpec[] { new ToolSpec(name, name, "") };
        }

        /// <summary>并发初始化——多线程同时 Init 不再抛并发写异常（整表替换：各建各的 + 原子换引用）。</summary>
        [Fact]
        public void ConcurrentInit_DoesNotThrow()
        {
            ToolSpec[] a = Specs("a-tool");
            ToolSpec[] b = Specs("b-tool");
            Exception captured = null;
            Parallel.For(0, 200, i =>
            {
                try
                {
                    if (i % 2 == 0)
                    {
                        ToolRegistry.Init(a, null, null);
                    }
                    else
                    {
                        ToolRegistry.Init(b, null, null);
                    }
                }
                catch (Exception ex)
                {
                    captured = ex;
                }
            });
            Assert.Null(captured);
            ToolSpec[] specs = ToolRegistry.BuildSpecs();
            Assert.Single(specs);
            Assert.True(specs[0].Name == "a-tool" || specs[0].Name == "b-tool");
        }

        /// <summary>并发读 + 写——读侧取局部快照，遍历不被换引用打断（不抛集合已修改、不读半更新表）。</summary>
        [Fact]
        public async Task ConcurrentReadDuringInit_NoThrow()
        {
            ToolSpec[] a = Specs("a-tool");
            ToolSpec[] b = Specs("b-tool");
            Exception captured = null;
            // 基线——读侧前提 = 注册表已初始化（生产语义：读发生在宿主 Init 之后）；
            // 否则读者首读可能落在写者首次 Init 之前，读到初始空表（干净进程必现——2026-09-25 判例）
            ToolRegistry.Init(a, null, null);
            CancellationToken token = TestContext.Current.CancellationToken;
            Task writer = Task.Run(() =>
            {
                try
                {
                    for (int i = 0; i < 400; i = i + 1)
                    {
                        if (i % 2 == 0)
                        {
                            ToolRegistry.Init(a, null, null);
                        }
                        else
                        {
                            ToolRegistry.Init(b, null, null);
                        }
                    }
                }
                catch (Exception ex)
                {
                    captured = ex;
                }
            }, token);
            Task reader = Task.Run(() =>
            {
                try
                {
                    for (int i = 0; i < 400; i = i + 1)
                    {
                        ToolSpec[] specs = ToolRegistry.BuildSpecs();
                        if (specs.Length != 1)
                        {
                            throw new InvalidOperationException("读到半更新表：长度 " + specs.Length.ToString());
                        }
                    }
                }
                catch (Exception ex)
                {
                    captured = ex;
                }
            }, token);
            await Task.WhenAll(writer, reader);
            Assert.Null(captured);
        }

        /// <summary>全量重建——新表不含的旧工具随旧表淘汰（无陈旧残留）。</summary>
        [Fact]
        public void Init_DropsStaleEntries()
        {
            ToolRegistry.Init(new ToolSpec[] { new ToolSpec("keep-tool", "K", ""), new ToolSpec("drop-tool", "D", "") }, null, null);
            ToolRegistry.Init(Specs("keep-tool"), null, null);
            Assert.NotNull(ToolRegistry.Find("keep-tool"));
            Assert.Null(ToolRegistry.Find("drop-tool"));
        }

        /// <summary>工具池重建——新快照整体替换旧快照，被删工具淘汰（陈旧残留归零）。</summary>
        [Fact]
        public void ToolPoolRebuild_ReplacesSnapshot()
        {
            ToolPool.RebuildAll(null, null, "{\"group\":\"\",\"tools\":[{\"name\":\"x-tool\",\"description\":\"X\"}]}");
            Assert.Contains("x-tool", ToolPool.AllNames());
            ToolPool.RebuildAll(null, null, "{\"group\":\"\",\"tools\":[{\"name\":\"y-tool\",\"description\":\"Y\"}]}");
            string[] names = ToolPool.AllNames();
            Assert.Contains("y-tool", names);
            Assert.DoesNotContain("x-tool", names);
        }
    }
}

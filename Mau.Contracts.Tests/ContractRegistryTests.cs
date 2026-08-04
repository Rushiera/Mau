using System;
using Mau.Contracts;
using Xunit;

namespace Mau.Contracts.Tests
{
    /// <summary>
    /// 契约层单元测试——注册表/端口/契约条目/导出标记（L0 层）
    /// </summary>
    public sealed class ContractRegistryTests
    {
        /// <summary>
        /// 注册后 TryGet 命中且字段完整往返
        /// </summary>
        [Fact]
        public void RegisterThenTryGetReturnsMatchingContract()
        {
            string name = "test.roundtrip_" + Guid.NewGuid().ToString("N");
            BrickContract contract = new BrickContract(name, "Mau.Test.Sample.Run");
            contract.Inputs.Add(new BrickPort("input", typeof(string), "输入"));
            contract.Outputs.Add(new BrickPort("result", typeof(bool), "结果"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";

            BrickRegistry.Register(contract);

            BrickContract? found;
            bool hit = BrickRegistry.TryGet(name, out found);
            Assert.True(hit);
            Assert.NotNull(found);
            Assert.Equal(name, found.Name);
            Assert.Equal("Mau.Test.Sample.Run", found.Implementation);
            Assert.Single(found.Inputs);
            Assert.Single(found.Outputs);
            Assert.Equal("input", found.Inputs[0].Name);
            Assert.Equal(typeof(string), found.Inputs[0].Type);
            Assert.Equal("输入", found.Inputs[0].Description);
            Assert.Equal(BrickReturnKind.Bool, found.Return);
            Assert.Equal(BrickDuration.Sync, found.Duration);
            Assert.Equal("main", found.Thread);
        }

        /// <summary>
        /// 同名二次注册抛 InvalidOperationException——名称唯一性是注册表铁律
        /// </summary>
        [Fact]
        public void RegisterDuplicateNameThrows()
        {
            string name = "test.duplicate_" + Guid.NewGuid().ToString("N");
            BrickRegistry.Register(new BrickContract(name, "Mau.Test.Sample.One"));

            Assert.Throws<InvalidOperationException>(delegate
            {
                BrickRegistry.Register(new BrickContract(name, "Mau.Test.Sample.Two"));
            });
        }

        /// <summary>
        /// 未注册名称 TryGet 返回 false 且 out 为空
        /// </summary>
        [Fact]
        public void TryGetUnknownNameReturnsFalse()
        {
            string name = "test.missing_" + Guid.NewGuid().ToString("N");

            BrickContract? found;
            bool hit = BrickRegistry.TryGet(name, out found);

            Assert.False(hit);
            Assert.Null(found);
        }

        /// <summary>
        /// 新契约默认值——Bool/Sync/any/空端口列表
        /// </summary>
        [Fact]
        public void NewContractHasSaneDefaults()
        {
            BrickContract contract = new BrickContract("test.defaults_" + Guid.NewGuid().ToString("N"), "Mau.Test.Sample.Run");

            Assert.Equal(BrickReturnKind.Bool, contract.Return);
            Assert.Equal(BrickDuration.Sync, contract.Duration);
            Assert.Equal("any", contract.Thread);
            Assert.Empty(contract.Inputs);
            Assert.Empty(contract.Outputs);
        }

        /// <summary>
        /// 端口两构造变体——无描述默认空串，带描述原样保留
        /// </summary>
        [Fact]
        public void PortConstructorVariantsBehave()
        {
            BrickPort plain = new BrickPort("name", typeof(int));
            Assert.Equal("name", plain.Name);
            Assert.Equal(typeof(int), plain.Type);
            Assert.Equal("", plain.Description);

            BrickPort described = new BrickPort("name", typeof(int), "端口描述");
            Assert.Equal("端口描述", described.Description);
        }

        /// <summary>
        /// 导出标记只允许方法目标、禁止多重、禁止继承
        /// </summary>
        [Fact]
        public void ExportAttributeUsageIsMethodOnly()
        {
            AttributeUsageAttribute? usage = (AttributeUsageAttribute?)Attribute.GetCustomAttribute(
                typeof(MauExportAttribute), typeof(AttributeUsageAttribute));

            Assert.NotNull(usage);
            Assert.True((usage.ValidOn & AttributeTargets.Method) != 0);
            Assert.False(usage.AllowMultiple);
            Assert.False(usage.Inherited);
        }

        /// <summary>
        /// 带导出标记的方法可被反射检索——导出清单机制可用
        /// </summary>
        [Fact]
        public void ExportAttributeRetrievableFromMethod()
        {
            System.Reflection.MethodInfo? method = typeof(ExportSample).GetMethod("ExportedRun");

            Assert.NotNull(method);
            object[] attrs = method.GetCustomAttributes(typeof(MauExportAttribute), false);
            Assert.Single(attrs);
        }

        /// <summary>
        /// Count 随注册数相对增长——不影响其他测试的绝对计数
        /// </summary>
        [Fact]
        public void CountReflectsRegistrations()
        {
            int before = BrickRegistry.Count;
            string name = "test.count_" + Guid.NewGuid().ToString("N");
            BrickRegistry.Register(new BrickContract(name, "Mau.Test.Sample.Run"));

            Assert.Equal(before + 1, BrickRegistry.Count);
        }

        /// <summary>
        /// All 集合包含本次注册的契约
        /// </summary>
        [Fact]
        public void AllContainsRegisteredContract()
        {
            string name = "test.all_" + Guid.NewGuid().ToString("N");
            BrickRegistry.Register(new BrickContract(name, "Mau.Test.Sample.Run"));

            bool contained = false;
            foreach (BrickContract item in BrickRegistry.All)
            {
                if (item.Name == name)
                {
                    contained = true;
                }
            }

            Assert.True(contained);
        }

        /// <summary>
        /// 导出标记测试载体——方法级应用
        /// </summary>
        private sealed class ExportSample
        {
            /// <summary>
            /// 被标记的导出方法
            /// </summary>
            [MauExport]
            public static void ExportedRun()
            {
            }
        }
    }
}

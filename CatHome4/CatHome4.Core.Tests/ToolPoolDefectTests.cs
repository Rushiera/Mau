using System;
using System.Collections.Generic;
using CH4;
using Mau.Runtime;
using Xunit;

namespace CatHome4.Core.Tests
{
    /// <summary>
    /// 工具组定义缺陷可见性测试（A107——design-ch4-tools §三·十三）。
    /// 覆盖面：四条静默路径（parse / throw / shape / empty）逐一出声 + 合法空组不误报 + 重建换代清零。
    /// 静态池 → GlobalToolState 集合串行（测试隔离纪律）。
    /// </summary>
    [Collection("GlobalToolState")]
    public class ToolPoolDefectTests : IDisposable
    {
        /// <summary>合法内置源 JSON——空内置组（避免内置缺陷污染用例断言）</summary>
        private const string BuiltinJson = "{\"group\":\"\",\"tools\":[]}";

        /// <summary>
        /// 用例收尾——重建空池（避免污染同集合其他测试）。
        /// </summary>
        public void Dispose()
        {
            ToolPool.RebuildAll(null, null, BuiltinJson);
        }

        /// <summary>
        /// 假 Flow——自曝文本可控（可指定抛异常），用于构造四种败因。
        /// </summary>
        private sealed class FakeFlow : IFlow
        {
            /// <summary>自曝工具定义文本</summary>
            public string ToolsJson = "";

            /// <summary>true=自曝调用面抛异常（throw 阶段用例）</summary>
            public bool ThrowOnTools = false;

            /// <summary>帧驱动（测试空实现）</summary>
            /// <param name="frame">帧号</param>
            public void Tick(int frame)
            {
            }

            /// <summary>元数据自曝（测试空实现）</summary>
            /// <returns>空对象 JSON</returns>
            public string GetMetaJson()
            {
                return "{}";
            }

            /// <summary>工具定义自曝</summary>
            /// <returns>配置文本（ThrowOnTools=true 时抛异常）</returns>
            public string GetToolsJson()
            {
                if (ThrowOnTools)
                {
                    throw new InvalidOperationException("测试桩：自曝失败");
                }
                return ToolsJson;
            }
        }

        /// <summary>
        /// 组装工具组来源——单一组。
        /// </summary>
        /// <param name="name">组名</param>
        /// <param name="flow">Flow 实例</param>
        /// <returns>来源列表</returns>
        private static List<ToolGroupSource> Sources(string name, IFlow flow)
        {
            List<ToolGroupSource> list = new List<ToolGroupSource>();
            ToolGroupSource source = new ToolGroupSource();
            source.Name = name;
            source.Flow = flow;
            list.Add(source);
            return list;
        }

        /// <summary>
        /// 缺陷查找——按组名取首条（无 = null）。
        /// </summary>
        /// <param name="group">组名</param>
        /// <returns>缺陷条目或 null</returns>
        private static ToolDefect FindDefect(string group)
        {
            ToolDefect[] defects = ToolPool.Defects();
            for (int i = 0; i < defects.Length; i = i + 1)
            {
                if (defects[i].Group == group)
                {
                    return defects[i];
                }
            }
            return null;
        }

        /// <summary>
        /// JSON 非法 → parse 缺陷（判例本体：漏项间逗号整组静默消失）。
        /// </summary>
        [Fact]
        public void InvalidJsonRecordedAsParseDefect()
        {
            FakeFlow flow = new FakeFlow();
            flow.ToolsJson = "{\"group\":\"BadCat\",\"tools\":[{\"name\":\"a\"} {\"name\":\"b\"}]}";
            ToolPool.RebuildAll(Sources("BadCat", flow), null, BuiltinJson);
            ToolDefect defect = FindDefect("BadCat");
            Assert.NotNull(defect);
            Assert.Equal("parse", defect.Stage);
            Assert.Contains("JSON 非法", defect.Reason);
            Assert.Empty(ToolPool.ByGroup("BadCat"));
        }

        /// <summary>
        /// 自曝抛异常 → throw 缺陷（组名由调用点带入）。
        /// </summary>
        [Fact]
        public void ThrowRecordedAsThrowDefect()
        {
            FakeFlow flow = new FakeFlow();
            flow.ThrowOnTools = true;
            ToolPool.RebuildAll(Sources("ThrowCat", flow), null, BuiltinJson);
            ToolDefect defect = FindDefect("ThrowCat");
            Assert.NotNull(defect);
            Assert.Equal("throw", defect.Stage);
        }

        /// <summary>
        /// 缺 tools 数组 → shape 缺陷（原为静默 return）。
        /// </summary>
        [Fact]
        public void MissingToolsArrayRecordedAsShapeDefect()
        {
            FakeFlow flow = new FakeFlow();
            flow.ToolsJson = "{\"group\":\"ShapeCat\"}";
            ToolPool.RebuildAll(Sources("ShapeCat", flow), null, BuiltinJson);
            ToolDefect defect = FindDefect("ShapeCat");
            Assert.NotNull(defect);
            Assert.Equal("shape", defect.Stage);
        }

        /// <summary>
        /// 合法空组声明（tools:[]）不判缺陷——QuickCat 范式（显式空组合法）。
        /// </summary>
        [Fact]
        public void ExplicitEmptyGroupNotReported()
        {
            FakeFlow flow = new FakeFlow();
            flow.ToolsJson = "{\"group\":\"QuickCat\",\"tools\":[]}";
            ToolPool.RebuildAll(Sources("QuickCat", flow), null, BuiltinJson);
            Assert.Null(FindDefect("QuickCat"));
        }

        /// <summary>
        /// 空产出兜底——来源已登记但 Flow 未挂（零产出且无更具体缺陷）→ empty。
        /// </summary>
        [Fact]
        public void ZeroOutputGroupRecordedAsEmptyDefect()
        {
            ToolGroupSource source = new ToolGroupSource();
            source.Name = "GhostCat";
            source.Flow = null;
            List<ToolGroupSource> list = new List<ToolGroupSource>();
            list.Add(source);
            ToolPool.RebuildAll(list, null, BuiltinJson);
            ToolDefect defect = FindDefect("GhostCat");
            Assert.NotNull(defect);
            Assert.Equal("empty", defect.Stage);
        }

        /// <summary>
        /// 正常组——工具入池 + 零缺陷（防误报）。
        /// </summary>
        [Fact]
        public void HealthyGroupProducesToolsAndNoDefect()
        {
            FakeFlow flow = new FakeFlow();
            flow.ToolsJson = "{\"group\":\"GoodCat\",\"tools\":[{\"name\":\"good-tool\",\"description\":\"d\",\"parameters\":{\"type\":\"object\"}}]}";
            ToolPool.RebuildAll(Sources("GoodCat", flow), null, BuiltinJson);
            Assert.Null(FindDefect("GoodCat"));
            Assert.Contains("good-tool", ToolPool.AllNames());
        }

        /// <summary>
        /// 重建换代——坏定义修好后缺陷清零（不留陈旧；与池同代）。
        /// </summary>
        [Fact]
        public void RebuildClearsDefects()
        {
            FakeFlow bad = new FakeFlow();
            bad.ToolsJson = "{\"group\":\"FixCat\",\"tools\":[}";
            ToolPool.RebuildAll(Sources("FixCat", bad), null, BuiltinJson);
            Assert.NotNull(FindDefect("FixCat"));
            FakeFlow good = new FakeFlow();
            good.ToolsJson = "{\"group\":\"FixCat\",\"tools\":[{\"name\":\"fix-tool\"}]}";
            ToolPool.RebuildAll(Sources("FixCat", good), null, BuiltinJson);
            Assert.Null(FindDefect("FixCat"));
            Assert.Contains("fix-tool", ToolPool.AllNames());
        }
    }
}

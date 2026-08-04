using Mau.Contracts;
using Mau.Translator;
using Xunit;

namespace Mau.Translator.Tests
{
    /// <summary>
    /// 静态验证器测试——MauValidator 11 项检查行为验证
    /// </summary>
    public class MauValidatorTests
    {
        /// <summary>
        /// 测试夹具——注册 file.convert 积木
        /// </summary>
        public MauValidatorTests()
        {
            TestBrickRegistration.Ensure();
        }

        /// <summary>
        /// 标准 file_convert 文档验证通过
        /// </summary>
        [Fact]
        public void ValidateFileConvertPasses()
        {
            MauDocument doc = ParseOk(FileConvertSource());
            var diags = MauValidator.Validate(doc);

            Assert.Empty(diags);
        }

        /// <summary>
        /// 未注册积木——E001
        /// </summary>
        [Fact]
        public void UnknownBrickReportsE001()
        {
            string source = "Mau 0.1\n" +
                "命题:\n  P_X 信号\n" +
                "变迁 T_X:\n  前置: P_X\n  动作: no.such_brick\n  后置: P_X\n";
            MauDocument doc = ParseOk(source);

            var diags = MauValidator.Validate(doc);
            Assert.Contains(diags, d => d.Code == "E001");
        }

        /// <summary>
        /// 参数端口不匹配——E002
        /// </summary>
        [Fact]
        public void BadPortReportsE002()
        {
            string source = "Mau 0.1\n" +
                "命题:\n  P_X 信号\n" +
                "变迁 T_X:\n  前置: P_X\n  动作: file.convert\n  参数: wrong_port\n  后置: P_X\n";
            MauDocument doc = ParseOk(source);

            var diags = MauValidator.Validate(doc);
            Assert.Contains(diags, d => d.Code == "E002");
        }

        /// <summary>
        /// 引用未声明命题——E003
        /// </summary>
        [Fact]
        public void MissingPropositionReportsE003()
        {
            string source = "Mau 0.1\n" +
                "命题:\n  P_X 信号\n" +
                "变迁 T_X:\n  前置: P_X\n  动作: file.convert\n  后置: P_Ghost / P_X\n";
            MauDocument doc = ParseOk(source);

            var diags = MauValidator.Validate(doc);
            Assert.Contains(diags, d => d.Code == "E003");
        }

        /// <summary>
        /// 无界环——E004
        /// </summary>
        [Fact]
        public void CycleReportsE004()
        {
            string source = "Mau 0.1\n" +
                "命题:\n" +
                "  P_A 信号\n" +
                "  P_B 事实\n" +
                "变迁 T_A:\n  前置: P_A\n  动作: file.convert\n  后置: P_B / P_A\n" +
                "变迁 T_B:\n  前置: P_B\n  动作: file.convert\n  后置: P_A / P_B\n";
            MauDocument doc = ParseOk(source);

            var diags = MauValidator.Validate(doc);
            Assert.Contains(diags, d => d.Code == "E004");
        }

        /// <summary>
        /// 后置缺失——E012
        /// </summary>
        [Fact]
        public void MissingPostReportsE012()
        {
            string source = "Mau 0.1\n" +
                "命题:\n  P_X 信号\n" +
                "变迁 T_X:\n  前置: P_X\n  动作: file.convert\n";
            MauDocument doc = ParseOk(source);

            var diags = MauValidator.Validate(doc);
            Assert.Contains(diags, d => d.Code == "E012");
        }

        /// <summary>
        /// worker 无 inbox——E008
        /// </summary>
        [Fact]
        public void WorkerWithoutInboxReportsE008()
        {
            string source = "Mau 0.1\n" +
                "命题:\n  P_X 信号\n" +
                "变迁 T_X:\n  前置: P_X\n  动作: file.convert\n  线程: worker\n  后置: P_X\n";
            MauDocument doc = ParseOk(source);

            var diags = MauValidator.Validate(doc);
            Assert.Contains(diags, d => d.Code == "E008");
        }

        /// <summary>
        /// 基座不匹配——E010
        /// </summary>
        [Fact]
        public void BaseMismatchReportsE010()
        {
            string source = "Mau 0.1\n" +
                "基座: Godot/v9.9\n" +
                "命题:\n  P_X 信号\n" +
                "变迁 T_X:\n  前置: P_X\n  动作: file.convert\n  后置: P_X\n";
            MauDocument doc = ParseOk(source);

            var diags = MauValidator.Validate(doc);
            Assert.Contains(diags, d => d.Code == "E010");
        }

        /// <summary>
        /// 非法时限模式——E006（绕过解析器，手工构造非法值）
        /// </summary>
        [Fact]
        public void BadTimeoutModeReportsE006()
        {
            MauDocument doc = new MauDocument();
            IrProposition p = new IrProposition("P_X", PropositionKind.Signal, 1);
            doc.Propositions.Add(p);
            IrTransition t = new IrTransition("T_X", 2);
            t.HasTimeout = true;
            t.TimeoutMode = "Bogus";
            t.TimeoutFrames = 0;
            t.BrickName = "file.convert";
            t.Preconditions.Add("P_X");
            t.PostOk.Add("P_X");
            doc.Transitions.Add(t);

            var diags = MauValidator.Validate(doc);
            Assert.Contains(diags, d => d.Code == "E006");
        }

        /// <summary>
        /// 配额值非法——E007（配额为0）
        /// </summary>
        [Fact]
        public void ZeroQuotaReportsE007()
        {
            string source = "Mau 0.1\n" +
                "命题:\n  P_X 信号\n" +
                "变迁 T_X:\n  前置: P_X\n  动作: file.convert\n  后置: P_X\n" +
                "资源 R_Pool:\n  配额: 0\n";
            MauDocument doc = ParseOk(source);

            var diags = MauValidator.Validate(doc);
            Assert.Contains(diags, d => d.Code == "E007");
        }

        /// <summary>
        /// 组合引用未声明变迁——E009
        /// </summary>
        [Fact]
        public void CompositionMissingTransitionReportsE009()
        {
            string source = "Mau 0.1\n" +
                "命题:\n  P_X 信号\n" +
                "变迁 T_X:\n  前置: P_X\n  动作: file.convert\n  后置: P_X\n" +
                "组合 FL_Main:\n  序列: T_Ghost\n";
            MauDocument doc = ParseOk(source);

            var diags = MauValidator.Validate(doc);
            Assert.Contains(diags, d => d.Code == "E009");
        }

        /// <summary>
        /// 非法通道类型——E011
        /// </summary>
        [Fact]
        public void BadChannelTypeReportsE011()
        {
            string source = "Mau 0.1\n" +
                "命题:\n  P_X 信号\n" +
                "变迁 T_X:\n  前置: P_X\n  动作: file.convert\n  后置: P_X\n" +
                "通道 C_Link:\n  源: a.b\n  目标: c.d\n  类型: 魔法\n";
            MauDocument doc = ParseOk(source);

            var diags = MauValidator.Validate(doc);
            Assert.Contains(diags, d => d.Code == "E011");
        }

        /// <summary>
        /// 解析并断言无语法错误
        /// </summary>
        /// <param name="source">源文本</param>
        /// <returns>文档</returns>
        private static MauDocument ParseOk(string source)
        {
            ParseResult result = MauParser.Parse(source);
            Assert.Empty(result.Diagnostics);
            return result.Document;
        }

        /// <summary>
        /// file_convert 标准源文本
        /// </summary>
        /// <returns>源文本</returns>
        private static string FileConvertSource()
        {
            return "Mau 0.1\n" +
                "基座: Mau.Runtime/v0.1\n" +
                "\n" +
                "命题:\n" +
                "  P_Input   信号\n" +
                "  P_Done    事实\n" +
                "  P_Failed  事实\n" +
                "\n" +
                "变迁 T_Convert:\n" +
                "  前置: P_Input\n" +
                "  动作: file.convert\n" +
                "  参数: input, output\n" +
                "  时限: 300帧\n" +
                "  后置: P_Done / P_Failed\n";
        }
    }
}

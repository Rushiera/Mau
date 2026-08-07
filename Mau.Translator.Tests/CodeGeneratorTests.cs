using Mau.Contracts;
using Mau.Translator;
using Xunit;

namespace Mau.Translator.Tests
{
    /// <summary>
    /// 生成器测试——确定性输出验证
    /// </summary>
    public class CodeGeneratorTests
    {
        /// <summary>
        /// 测试夹具——注册 file.convert 积木
        /// </summary>
        public CodeGeneratorTests()
        {
            TestBrickRegistration.Ensure();
        }

        /// <summary>
        /// 生成结构完整——类名/命题/Cube/Fire/Tick/查询
        /// </summary>
        [Fact]
        public void GenerateFileConvertProducesExpectedStructure()
        {
            string code = GenerateOk("FileConvert");

            Assert.Contains("public sealed class FL_FileConvert", code);
            Assert.Contains("private bool P_Input;", code);
            Assert.Contains("private bool P_Done;", code);
            Assert.Contains("private bool P_Failed;", code);
            Assert.Contains("private Cube T_Convert_Cube;", code);
            Assert.Contains("public void FireInput(string input, string output)", code);
            Assert.Contains("public void Tick()", code);
            Assert.Contains("public bool IsDone()", code);
            Assert.Contains("public bool IsFailed()", code);
            Assert.Contains("Mau.Bricks.BRIK_FILE_001.Convert(_input, _output)", code);
            Assert.Contains("P_Input = false;", code);
            Assert.Contains("P_Done = true;", code);
            Assert.Contains("P_Failed = true;", code);
        }

        /// <summary>
        /// 确定性——无时间戳，两次生成字节一致
        /// </summary>
        [Fact]
        public void GenerateIsDeterministicAndTimestampFree()
        {
            string first = GenerateOk("FileConvert");
            string second = GenerateOk("FileConvert");

            Assert.Equal(first, second);
            Assert.DoesNotContain("生成时间", first);
            Assert.DoesNotContain("20", first.Substring(0, 120));
        }

        /// <summary>
        /// 无时限变迁——不生成 Cube
        /// </summary>
        [Fact]
        public void GenerateWithoutTimeoutHasNoCube()
        {
            string source = "Mau 0.1\n" +
                "命题:\n" +
                "  P_Go 信号\n" +
                "  P_Done 事实\n" +
                "变迁 T_Go:\n" +
                "  前置: P_Go\n" +
                "  动作: file.convert\n" +
                "  参数: input, output\n" +
                "  后置: P_Done / P_Go\n";
            ParseResult parsed = MauParser.Parse(source);
            Assert.Empty(parsed.Diagnostics);
            string code = CodeGenerator.Generate(parsed.Document, "Go");

            Assert.DoesNotContain("Cube", code);
        }

        /// <summary>
        /// 编译并返回生成代码
        /// </summary>
        /// <param name="flowName">流程名</param>
        /// <returns>生成代码</returns>
        private static string GenerateOk(string flowName)
        {
            string source = "Mau 0.1\n" +
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
            ParseResult parsed = MauParser.Parse(source);
            Assert.Empty(parsed.Diagnostics);
            var diags = MauValidator.Validate(parsed.Document);
            Assert.Empty(diags);
            return CodeGenerator.Generate(parsed.Document, flowName);
        }
/// <summary>
/// 实现接口声明——生成类追加接口（全限定名）
/// </summary>
[Fact]
public void GenerateWithInterfaceDeclarationAppendsInterface()
{
    string source = "Mau 0.1\n" + "实现: CH4.Contracts.ICat\n" + "命题:\n" + "  P_Go 信号\n" + "  P_Done 事实\n" + "变迁 T_Go:\n" + "  前置: P_Go\n" + "  动作: file.convert\n" + "  参数: input, output\n" + "  后置: P_Done / P_Go\n";
    ParseResult parsed = MauParser.Parse(source);
    Assert.Empty(parsed.Diagnostics);
    string code = CodeGenerator.Generate(parsed.Document, "HelloCat");
    Assert.Contains("public sealed class FL_HelloCat : IObservableFlow, CH4.Contracts.ICat", code);
} 
/// <summary>
/// 输出端口公开属性——宿主只读
/// </summary>
 [ Fact ]  public  void  GenerateOutputPortProducesPublicProperty ( ) {  string  source  =  "Mau 0.1\n" + "命题:\n" + "  P_Go 信号\n" + "  P_Done 事实\n" + "变迁 T_Go:\n" + "  前置: P_Go\n" + "  动作: file.read\n" + "  参数: path\n" + "  后置: P_Done / P_Go\n" ;  ParseResult  parsed  =  MauParser . Parse ( source ) ;  Assert . Empty ( parsed . Diagnostics ) ;  string  code  =  CodeGenerator . Generate ( parsed . Document ,  "ReadCat" ) ;  Assert . Contains ( "public string content" ,  code ) ;  Assert . Contains ( "get { return _content; }" ,  code ) ;  }

    }
}

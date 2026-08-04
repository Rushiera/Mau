using System;
using System.IO;
using Mau.Generated.Flows;
using Xunit;

namespace Mau.Runtime.Tests
{
    /// <summary>
    /// L5 真实运行测试——生成物 headless 真跑 + 必然结果断言
    /// </summary>
    public class FileConvertFlowTests
    {
        /// <summary>
        /// 成功路径——有效文件转换完成
        /// </summary>
        [Fact]
        public void FireInputSuccessCompletesDone()
        {
            string input = Path.Combine(Path.GetTempPath(), "mau_test_input_" + Guid.NewGuid().ToString("N") + ".txt");
            string output = Path.Combine(Path.GetTempPath(), "mau_test_output_" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(input, "你好，Mau");

            FL_FileConvert flow = new FL_FileConvert();
            flow.FireInput(input, output);

            for (int i = 0; i < 300; i++)
            {
                flow.Tick();
                if (flow.IsDone() || flow.IsFailed())
                {
                    break;
                }
            }

            Assert.True(flow.IsDone());
            Assert.False(flow.IsFailed());
            Assert.True(File.Exists(output));

            File.Delete(input);
            File.Delete(output);
        }

        /// <summary>
        /// 失败路径——输入文件不存在，错误后置注册
        /// </summary>
        [Fact]
        public void FireInputMissingFileFails()
        {
            string input = Path.Combine(Path.GetTempPath(), "mau_test_missing_" + Guid.NewGuid().ToString("N") + ".txt");
            string output = Path.Combine(Path.GetTempPath(), "mau_test_out_missing_" + Guid.NewGuid().ToString("N") + ".txt");

            FL_FileConvert flow = new FL_FileConvert();
            flow.FireInput(input, output);

            for (int i = 0; i < 300; i++)
            {
                flow.Tick();
                if (flow.IsDone() || flow.IsFailed())
                {
                    break;
                }
            }

            Assert.False(flow.IsDone());
            Assert.True(flow.IsFailed());
            Assert.False(File.Exists(output));
        }

        /// <summary>
        /// 信号消费——触发后 P_Input 复位，不会重复触发
        /// </summary>
        [Fact]
        public void FireInputConsumesSignalOnce()
        {
            string input = Path.Combine(Path.GetTempPath(), "mau_test_once_" + Guid.NewGuid().ToString("N") + ".txt");
            string output = Path.Combine(Path.GetTempPath(), "mau_test_out_once_" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(input, "x");

            FL_FileConvert flow = new FL_FileConvert();
            flow.FireInput(input, output);

            // 第一次触发
            flow.Tick();
            Assert.True(flow.IsDone());

            // 后续 Tick 不重复执行（信号已消费，Cube Idle）
            flow.Tick();
            flow.Tick();
            Assert.True(flow.IsDone());

            File.Delete(input);
            File.Delete(output);
        }
    }
}

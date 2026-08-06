// ═══════════════════════════════════════════════
// 积木: math.is_all_digits / math.format_size / math.result_preview
// ID:   BRIK-MATH-001 ~ 003
// 作用: 确定性纯文本数学——数字校验 / B-K-M 数量格式化 / 结果预览
// 引用: Mau.Bricks.Standard → Mau.Contracts（BrickRegistry）
// 依赖: 无
// 原理: 静态方法 + BrickContract 注册——文化无关格式化（InvariantCulture）
// 常用: CH4 工具系统参数校验 / 日志大小展示 / 结果摘要
// ═══════════════════════════════════════════════
using System;
using System.Globalization;
using Mau.Contracts;

namespace Mau.Bricks
{
    /// <summary>
    /// 数学积木——标准积木库数学类。确定性纯文本操作，文化无关。
    /// </summary>
    public static class MathBrick
    {
        /// <summary>
        /// 判断非空文本是否全部由 ASCII 数字组成
        /// </summary>
        /// <param name="text">待检查文本</param>
        /// <returns>是否全为数字</returns>
        public static bool IsAllDigits(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }
            for (int i = 0; i < text.Length; i = i + 1)
            {
                if (text[i] < '0' || text[i] > '9')
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 按十进制 B/K/M 阈值格式化数量
        /// </summary>
        /// <param name="bytes">数量</param>
        /// <returns>稳定、文化无关的格式文本</returns>
        public static string FormatSize(long bytes)
        {
            if (bytes >= 1000000)
            {
                return (bytes / 1000000.0).ToString("F2", CultureInfo.InvariantCulture) + " M";
            }
            if (bytes >= 1000)
            {
                return (bytes / 1000.0).ToString("F2", CultureInfo.InvariantCulture) + " K";
            }
            return bytes.ToString(CultureInfo.InvariantCulture) + " B";
        }

        /// <summary>
        /// 生成单行、最多十五 UTF-16 字符头部的结果预览
        /// </summary>
        /// <param name="result">完整工具结果</param>
        /// <returns>OK 或空结果返回空字符串，否则返回预览</returns>
        public static string ResultPreview(string? result)
        {
            if (string.IsNullOrEmpty(result))
            {
                return "";
            }
            if (result == "OK" || result.StartsWith("OK\n", StringComparison.Ordinal)
                || result.StartsWith("OK\r", StringComparison.Ordinal))
            {
                return "";
            }
            int headLength = result.Length;
            if (headLength > 15)
            {
                headLength = 15;
                if (headLength < result.Length && headLength > 0
                    && char.IsHighSurrogate(result[headLength - 1])
                    && char.IsLowSurrogate(result[headLength]))
                {
                    headLength = headLength + 1;
                }
            }
            string head = result.Substring(0, headLength).Replace('\r', ' ').Replace('\n', ' ');
            return "（" + head + "…总" + FormatSize(result.Length) + "）";
        }
    }

    /// <summary>
    /// 数学积木注册——进程启动时调用一次
    /// </summary>
    public static class MathBrickRegistration
    {
        /// <summary>
        /// 注册全部数学积木
        /// </summary>
        public static void RegisterAll()
        {
            RegisterIsAllDigits();
            RegisterFormatSize();
            RegisterResultPreview();
        }

        /// <summary>
        /// 注册 math.is_all_digits
        /// </summary>
        private static void RegisterIsAllDigits()
        {
            BrickContract contract = new BrickContract("math.is_all_digits", "Mau.Bricks.MathBrick.IsAllDigits");
            contract.Inputs.Add(new BrickPort("text", typeof(string), "待检查文本"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }

        /// <summary>
        /// 注册 math.format_size
        /// </summary>
        private static void RegisterFormatSize()
        {
            BrickContract contract = new BrickContract("math.format_size", "Mau.Bricks.MathBrick.FormatSize");
            contract.Inputs.Add(new BrickPort("bytes", typeof(long), "数量"));
            contract.Outputs.Add(new BrickPort("formatted", typeof(string), "格式文本"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }

        /// <summary>
        /// 注册 math.result_preview
        /// </summary>
        private static void RegisterResultPreview()
        {
            BrickContract contract = new BrickContract("math.result_preview", "Mau.Bricks.MathBrick.ResultPreview");
            contract.Inputs.Add(new BrickPort("result", typeof(string), "完整结果文本"));
            contract.Outputs.Add(new BrickPort("preview", typeof(string), "预览文本"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }
    }
}

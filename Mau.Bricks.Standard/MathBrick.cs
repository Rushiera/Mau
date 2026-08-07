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
        /// <param name="formatted">格式文本</param>
        /// <returns>true=成功</returns>
        public static bool FormatSize(long bytes, out string formatted)
        {
            if (bytes >= 1000000)
            {
                formatted = (bytes / 1000000.0).ToString("F2", CultureInfo.InvariantCulture) + " M";
                return true;
            }
            if (bytes >= 1000)
            {
                formatted = (bytes / 1000.0).ToString("F2", CultureInfo.InvariantCulture) + " K";
                return true;
            }
            formatted = bytes.ToString(CultureInfo.InvariantCulture) + " B";
            return true;
        }

        /// <summary>
        /// 生成单行、最多十五 UTF-16 字符头部的结果预览
        /// </summary>
        /// <param name="result">完整工具结果</param>
        /// <param name="preview">预览文本——OK 或空结果返回空字符串，否则返回预览</param>
        /// <returns>true=成功</returns>
        public static bool ResultPreview(string? result, out string preview)
        {
            if (string.IsNullOrEmpty(result))
            {
                preview = "";
                return true;
            }
            if (result == "OK" || result.StartsWith("OK\n", StringComparison.Ordinal)
                || result.StartsWith("OK\r", StringComparison.Ordinal))
            {
                preview = "";
                return true;
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
            string sizeText = "";
            FormatSize(result.Length, out sizeText);
            preview = "（" + head + "…总" + sizeText + "）";
            return true;
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

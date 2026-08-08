// ═══════════════════════════════════════════════════
// 积木: mau.build
// ID:   BRIK-MAU-001
// 类别: MAU
// 作用: Mau CLI 指令封装——输出带参指令（verify/gen/build/test/check/bricks/run/debug/publish/serve/ps）
// 依赖: 无
// 引用: System
// 原理: 白名单命令校验 + 参数拼接 → 输出 "mau <command> <args>" 指令字符串（指令行中介）
// 常用: MauCat 工具 Cat——Mau CLI 带参运行（M2c 六+一域 Mau 域）
// ═══════════════════════════════════════════════════
using System;
using System.Text;

namespace Mau.Bricks
{
    /// <summary>
    /// Mau 积木——mau.build CLI 指令封装（指令行中介：参数筛选 + 准确报错）
    /// </summary>
    public static class MauBuildBrick
    {
        /// <summary>
        /// 白名单命令表
        /// </summary>
        private static readonly string[] AllowedCommands = new string[]
        {
            "verify", "gen", "build", "test", "check",
            "bricks", "run", "debug", "publish", "serve",
            "ps", "status", "snapshot", "kill", "checksum"
        };

        /// <summary>
        /// 封装 Mau CLI 指令——白名单命令校验 + 参数拼接（带参工具）
        /// </summary>
        /// <param name="command">白名单命令（verify/gen/build/test/...）</param>
        /// <param name="args">参数串（如 "file.mau" 或 "--update"）</param>
        /// <param name="result">指令文本（"mau &lt;command&gt; &lt;args&gt;"）或错误</param>
        /// <returns>true=指令构造成功</returns>
        public static bool Build(string command, string args, out string result)
        {
            result = "";
            if (string.IsNullOrWhiteSpace(command))
            {
                result = "ERR|MAU_NO_COMMAND|用法: mau <verify|gen|build|test|check|bricks|run|debug|publish|serve|ps|status|snapshot|kill|checksum> [args]";
                return false;
            }
            string safeCmd = command.Trim();
            bool allowed = false;
            for (int i = 0; i < AllowedCommands.Length; i = i + 1)
            {
                if (AllowedCommands[i] == safeCmd)
                {
                    allowed = true;
                    break;
                }
            }
            if (!allowed)
            {
                result = "ERR|MAU_UNKNOWN_COMMAND|" + safeCmd
                    + "|允许: " + string.Join("/", AllowedCommands);
                return false;
            }
            StringBuilder sb = new StringBuilder();
            sb.Append("mau ");
            sb.Append(safeCmd);
            if (!string.IsNullOrWhiteSpace(args))
            {
                sb.Append(' ');
                sb.Append(args.Trim());
            }
            result = sb.ToString();
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:61F5E4DB901B792C55CE5E9E346F5FAB76634667D96DAB8F75C19C49EA049381

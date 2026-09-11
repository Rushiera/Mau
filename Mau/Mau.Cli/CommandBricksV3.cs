using System;
using System.Collections.Generic;
using System.IO;
using Mau.Development;
using Mau.Translator;

namespace Mau.Cli
{
    /// <summary>
    /// mau bricks（v3）——积木索引命令：list / index --update / index --verify。
    /// 源码驱动（v2 模式）：文件头十字段 + Roslyn 静态签名 = 契约唯一真相源。
    /// </summary>
    public static class CommandBricksV3
    {
        /// <summary>
        /// 执行 bricks——子命令路由（指令编号化）
        /// </summary>
        /// <param name="args">子命令参数（bricks 之后的全部）</param>
        /// <returns>退出码——0 成功</returns>
        public static int Run(string[] args)
        {
            if (args.Length == 0)
            {
                PrintUsage();
                return 1;
            }

            int subId;
            if (!CommandIds.BricksSub.TryResolve(args[0], out subId))
            {
                Console.WriteLine("未知子命令: " + args[0]);
                PrintUsage();
                return 1;
            }

            switch (subId)
            {
                case CommandIds.BricksSub.List:
                    if (args.Length > 1)
                    {
                        return CliSupport.ArgError("未知参数: " + args[1], "mau bricks list");
                    }

                    return RunList();
                case CommandIds.BricksSub.Index:
                    return RunIndex(args);
                default:
                    Console.WriteLine("未知子指令编号: " + subId);
                    return 1;
            }
        }

        /// <summary>
        /// list——BrickIndex 枚举（机器索引查询）
        /// </summary>
        /// <returns>退出码</returns>
        private static int RunList()
        {
            Dictionary<string, BrickIndexEntry> index = BrickIndex.All();
            List<string> names = new List<string>(index.Keys);
            names.Sort(StringComparer.Ordinal);
            for (int i = 0; i < names.Count; i++)
            {
                BrickIndexEntry entry = index[names[i]];
                Console.WriteLine(entry.Name + " → " + entry.Implementation + "（in " + entry.InputTypes.Count + " / out " + entry.OutputCount + "）");
            }
            Console.WriteLine("积木总数: " + names.Count);
            return 0;
        }

        /// <summary>
        /// index——--update 重建 / --verify 一致性校验
        /// </summary>
        /// <param name="args">子命令参数</param>
        /// <returns>退出码</returns>
        private static int RunIndex(string[] args)
        {
            string? root = CliSupport.FindWorkspaceRoot();
            if (root == null)
            {
                root = "";
            }

            if (root.Length == 0)
            {
                Console.WriteLine("FAIL: 未找到 Mau.sln——请在 Mau workspace 下运行 mau bricks index");
                return 1;
            }

            string bricksRoot = Path.Combine(root, "Bricks");
            if (!Directory.Exists(bricksRoot))
            {
                Console.WriteLine("FAIL: Bricks 目录不存在——" + bricksRoot);
                return 1;
            }

            string usage = "mau bricks index --update | mau bricks index --verify";
            bool update = false;
            bool verify = false;
            for (int i = 1; i < args.Length; i++)
            {
                if (args[i] == "--update")
                {
                    update = true;
                }
                else if (args[i] == "--verify")
                {
                    verify = true;
                }
                else
                {
                    return CliSupport.ArgError("未知参数: " + args[i], usage);
                }
            }

            if (!update && !verify)
            {
                Console.WriteLine("用法: " + usage);
                return 1;
            }

            if (update)
            {
                return RunUpdate(bricksRoot);
            }

            return RunVerify(bricksRoot);
        }

        /// <summary>
        /// index --update——扫描提取 + 校验尾重算 + index.json/INDEX.md 重建
        /// </summary>
        /// <param name="bricksRoot">Bricks 目录</param>
        /// <returns>退出码</returns>
        private static int RunUpdate(string bricksRoot)
        {
            List<BrickSpecEntry> entries = BrickSpecExtractor.Scan(bricksRoot);
            if (entries.Count == 0)
            {
                Console.WriteLine("FAIL: 未扫描到积木（BRIK-*.cs）");
                return 1;
            }
            // [段1] 校验尾重算——每个积木源文件
            int checksumCount = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                string fullPath = Path.Combine(bricksRoot, entries[i].Path.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(fullPath))
                {
                    BrickSpecExtractor.RewriteChecksum(fullPath);
                    checksumCount = checksumCount + 1;
                }
            }
            // [段2] 重建索引
            List<string> packages;
            string json = BrickSpecExtractor.BuildJson(entries, out packages);
            string markdown = BrickSpecExtractor.BuildMarkdown(entries);
            File.WriteAllText(Path.Combine(bricksRoot, "index.json"), json, new System.Text.UTF8Encoding(true));
            File.WriteAllText(Path.Combine(bricksRoot, "INDEX.md"), markdown, new System.Text.UTF8Encoding(true));
            Console.WriteLine("=== MAU_BRICKS_INDEX 汇总 ===");
            Console.WriteLine("积木 " + entries.Count + " 件 / 校验尾重算 " + checksumCount + " 件 / 包 " + packages.Count + " 个");
            Console.WriteLine("index.json + INDEX.md 已重建");
            return 0;
        }

        /// <summary>
        /// index --verify——V1 一致性校验（文件存在 / 数量一致 / 名称唯一）
        /// </summary>
        /// <param name="bricksRoot">Bricks 目录</param>
        /// <returns>退出码——0 全过</returns>
        private static int RunVerify(string bricksRoot)
        {
            int fail = 0;
            // [段1] 扫描实际积木文件
            List<BrickSpecEntry> actual = BrickSpecExtractor.Scan(bricksRoot);
            // [段2] 索引条目 vs 实际文件
            Dictionary<string, BrickIndexEntry> index = BrickIndex.All();
            List<string> names = new List<string>(index.Keys);
            for (int i = 0; i < names.Count; i++)
            {
                BrickIndexEntry entry = index[names[i]];
                string fullPath = Path.Combine(bricksRoot, entry.Path.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(fullPath))
                {
                    fail = fail + 1;
                    Console.WriteLine("FAIL: 索引条目文件缺失——" + entry.Path);
                }
            }
            // [段3] 实际文件 vs 索引条目（数量 + 名称覆盖）
            for (int i = 0; i < actual.Count; i++)
            {
                if (!index.ContainsKey(actual[i].Name))
                {
                    fail = fail + 1;
                    Console.WriteLine("FAIL: 实际积木未登记——" + actual[i].Name + "（运行 mau bricks index --update）");
                }
            }
            if (index.Count != actual.Count)
            {
                fail = fail + 1;
                Console.WriteLine("FAIL: 数量漂移——索引 " + index.Count + " vs 实际 " + actual.Count);
            }
            if (fail == 0)
            {
                Console.WriteLine("MAU_BRICKS_VERIFY_OK（" + actual.Count + " 件一致）");
                return 0;
            }
            Console.WriteLine("MAU_BRICKS_VERIFY_FAIL（" + fail + " 项）");
            return 1;
        }

        /// <summary>
        /// 用法提示
        /// </summary>
        private static void PrintUsage()
        {
            Console.WriteLine("mau bricks list                    积木索引枚举");
            Console.WriteLine("mau bricks index --update           源码扫描 + 校验尾重算 + 索引重建");
            Console.WriteLine("mau bricks index --verify           索引与实际文件一致性校验");
        }
    }
}

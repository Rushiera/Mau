using System;
using System.IO;

namespace SetUp
{
    /// <summary>
    /// SetUp prepare 模式——重建全发布链（= README 6 步序自动化 + 收尾构建服务器清理）。
    /// 目标：配置好 public/ + Mau-public/（就地自举）+ 零隐式后台进程残留。
    /// 规范：design-ch4-release.md §二 / design-ch4-deploy.md §三 / README §3.2
    /// </summary>
    public static partial class Program
    {
        /// <summary>
        /// prepare 模式入口——按序执行 6 步（依赖序不可跳），末尾无论成败清理构建服务器。
        /// 清理铁律：dotnet build/test/publish 会驻留隐式 MSBuild/VBCSCompiler 后台进程（无窗口、删目录被锁）——
        /// 不允许前台不可见的后台驻留，prepare 结束前统一 shutdown（try/finally 保证失败路径也清理）。
        /// </summary>
        /// <param name="repoRoot">仓库根</param>
        /// <returns>退出码（0=成功，非0=失败）</returns>
        private static int Prepare(string repoRoot)
        {
            Console.WriteLine("[SetUp] prepare —— 重建全发布链");
            Console.WriteLine("  " + repoRoot);

            try
            {
                // [段1] 步1：dotnet build Mau.sln（构建 + NuGet 还原——0 错误 0 警告底线）
                if (!RunProcess("dotnet", "build Mau.sln", repoRoot))
                {
                    return Fail("步1 dotnet build Mau.sln 失败——构建未通过，中止。");
                }

                // [段2] 步2：dotnet test Mau.sln（全量测试——三测试项目）
                if (!RunProcess("dotnet", "test Mau.sln", repoRoot))
                {
                    return Fail("步2 dotnet test Mau.sln 失败——测试未通过，中止。");
                }

                // [段3] 步3：dotnet publish Mau\Mau.Cli -c Debug -o Mau-public（基座部署区——FL 组引用源，缺失则步4报 M3245）
                if (!RunProcess("dotnet", "publish Mau\\Mau.Cli\\Mau.Cli.csproj -c Debug -o Mau-public", repoRoot))
                {
                    return Fail("步3 publish Mau.Cli 失败——基座部署区未生成，中止。");
                }

                // [段4] 步4：mau proj ×8（工作目录=仓库根——黄金对比/积木索引依赖相对路径；经 Mau-public\Mau.exe 调用）
                string mauExe = Path.Combine(repoRoot, "Mau-public", "Mau.exe");
                if (!File.Exists(mauExe))
                {
                    return Fail("步4 前置缺失：" + mauExe + " 不存在——基座 CLI 未生成。");
                }
                string[] groups = new string[]
                {
                    "QuickCat\\quick_cat.mauproj QuickCat",
                    "TextCat\\text_cat.mauproj TextCat",
                    "MauCat\\mau_cat.mauproj MauCat",
                    "CsCat\\cs_cat.mauproj CsCat",
                    "ConfigCat\\config_cat.mauproj ConfigCat",
                    "SearchCat\\search_cat.mauproj SearchCat",
                    "VisionCat\\vision_cat.mauproj VisionCat",
                    "TempToolCat\\temp_tool_cat.mauproj TempToolCat",
                };
                for (int i = 0; i < groups.Length; i = i + 1)
                {
                    string[] parts = groups[i].Split(' ');
                    string proj = "corpus\\ch4\\" + parts[0];
                    string outDir = "public\\src\\" + parts[1];
                    string args = "proj " + proj + " -o " + outDir + " --build";
                    Console.WriteLine("[SetUp] 步4/" + (i + 1) + "/" + groups.Length + "：" + parts[1]);
                    if (!RunProcess(mauExe, args, repoRoot))
                    {
                        return Fail("步4 组翻译失败：" + parts[1] + "——中止。");
                    }
                }

                // [段5] 步5：dotnet publish CatHome4 -c Debug -o public\app（宿主部署区）
                if (!RunProcess("dotnet", "publish CatHome4\\CatHome4.csproj -c Debug -o public\\app", repoRoot))
                {
                    return Fail("步5 publish CatHome4 失败——宿主部署区未生成，中止。");
                }

                // [段6] 步6：宿主自检——CLI 全链（主线程直执 + 进程自退；8080 单例——验证前确认无宿主占用）
                string hostExe = Path.Combine(repoRoot, "public", "app", "CatHome4.exe");
                if (!File.Exists(hostExe))
                {
                    return Fail("步6 前置缺失：" + hostExe + " 不存在——宿主未生成。");
                }
                if (!RunProcess(hostExe, "--run \"session count\"", repoRoot))
                {
                    return Fail("步6 宿主自检失败——CLI 全链未通过，中止。");
                }

                Console.WriteLine("[SetUp] prepare 完成——public/ + Mau-public/ 已就绪。");
                return 0;
            }
            finally
            {
                // [段7] 收尾清理（无论成败必执行）：shutdown 构建服务器——不留前台不可见的隐式后台驻留
                // dotnet build/test/publish 每次调用会驻留 MSBuild/VBCSCompiler 进程（无窗口、锁目录）
                // 不允许这种负优化——结束前统一 shutdown；下一轮 prepare 冷启动，代价是首轮稍慢但零残留
                Console.WriteLine("[SetUp] 清理构建服务器（dotnet build-server shutdown）…");
                if (!RunProcess("dotnet", "build-server shutdown", repoRoot))
                {
                    Console.WriteLine("[SetUp] 警告：build-server shutdown 未正常退出——可能有隐式构建进程残留，建议手动 dotnet build-server shutdown。");
                }
            }
        }

        /// <summary>
        /// 失败收尾——红字提示 + 返回非0。
        /// </summary>
        /// <param name="message">失败信息</param>
        /// <returns>退出码 1</returns>
        private static int Fail(string message)
        {
            Console.WriteLine("[SetUp] ❌ " + message);
            return 1;
        }
    }
}

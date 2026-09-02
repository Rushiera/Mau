using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using Mau.Runtime;
using Mau.Providers;

namespace CH4
{
    /// <summary>
    /// Program 路径解析面分部——仓库根探测/数据根解析/HTTP 端口解析。
    /// P7b partial 拆分——自 Program.cs 原样搬移，逻辑零改动。
    /// </summary>
    public static partial class Program
    {
        /// <summary>
        /// 仓库根探测——从当前目录向上找含 Mau.sln 的目录（部署区定位用；CatHome4 可从任意工作目录启动）
        /// </summary>
        /// <param name="startDir">起始目录</param>
        /// <returns>仓库根或空字符串</returns>
        private static string FindRepoRoot(string startDir)
        {
            string dir = new DirectoryInfo(startDir).FullName;
            while (true)
            {
                if (File.Exists(Path.Combine(dir, "Mau.sln")))
                {
                    return dir;
                }
                string parent = Directory.GetParent(dir)?.FullName;
                if (parent == null)
                {
                    return "";
                }
                dir = parent;
            }
        }

        /// <summary>
        /// 数据根解析——三级锚定（design-ch4-release.md §三）：
        /// 1. CH4_DATA_ROOT 环境变量（显式覆盖——多实例/CI/自定义落位逃逸口）
        /// 2. 仓库根（找到 Mau.sln → 测试区 Data 隔离，可自由清）
        /// 3. %LOCALAPPDATA%/CatHome4（部署实例持久数据——secrets 不进漫游域）
        /// 返回值为 Data 的父级目录——调用方以 Path.Combine(dataRoot, "Data", ...) 拼实际路径。
        /// </summary>
        /// <returns>数据根目录</returns>
        private static string ResolveDataRoot()
        {
            // [段1] 显式覆盖——CH4_DATA_ROOT 环境变量（行业标准逃逸口）
            string envRoot = Environment.GetEnvironmentVariable("CH4_DATA_ROOT");
            if (envRoot != null && envRoot.Length > 0)
            {
                return envRoot;
            }
            // [段2] 测试区隔离——仓库根（Data 挂仓库根，可自由清）
            string root = FindRepoRoot(AppContext.BaseDirectory);
            if (root.Length > 0)
            {
                return root;
            }
            // [段3] 机器级默认——部署实例持久数据
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CatHome4");
        }

        /// <summary>
        /// HTTP 端口解析——http.port 配置项（协议 §5b 预留）；缺省/非法/越界回退 8080
        /// </summary>
        /// <param name="config">配置存储</param>
        /// <returns>监听端口</returns>
        private static int ResolveHttpPort(ConfigStore config)
        {
            string raw = config.Get("http.port", "8080");
            int port;
            if (!int.TryParse(raw, out port))
            {
                port = 0;
            }
            if (port < 1024 || port > 65535)
            {
                Console.WriteLine("[CatHome4] http.port 配置非法(" + raw + ")——回退 8080");
                port = 8080;
            }
            return port;
        }

        /// <summary>
        /// 回收站根——数据根在受控根内且可写则用数据根；否则第一个可写根（回收站必须可写——P8.5 配置群）
        /// </summary>
        /// <param name="workspace">工作区配置</param>
        /// <param name="dataRoot">数据根</param>
        /// <returns>回收站基底目录</returns>
        private static string WorkspaceRecycleRoot(WorkspaceConfig workspace, string dataRoot)
        {
            for (int i = 0; i < workspace.Roots.Length; i++)
            {
                WorkspaceConfig.RootEntry entry = workspace.Roots[i];
                if (entry.Writable && string.Equals(entry.Path, dataRoot, StringComparison.OrdinalIgnoreCase))
                {
                    return entry.Path;
                }
            }
            for (int i = 0; i < workspace.Roots.Length; i++)
            {
                WorkspaceConfig.RootEntry entry = workspace.Roots[i];
                if (entry.Writable)
                {
                    return entry.Path;
                }
            }
            return dataRoot;
        }

        /// <summary>
        /// 可写根条目数组——cs.* 编码工具只碰可写根（只读知识根不参与项目扫描——P8.5 配置群；保留 id 供命名空间寻址）
        /// </summary>
        /// <param name="workspace">工作区配置</param>
        /// <returns>可写根条目数组</returns>
        private static WorkspaceConfig.RootEntry[] WritableRootPaths(WorkspaceConfig workspace)
        {
            List<WorkspaceConfig.RootEntry> entries = new List<WorkspaceConfig.RootEntry>();
            for (int i = 0; i < workspace.Roots.Length; i++)
            {
                if (workspace.Roots[i].Writable)
                {
                    entries.Add(workspace.Roots[i]);
                }
            }
            return entries.ToArray();
        }
    }
}
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
        /// 数据根解析——部署跟随运行环境（稳定分支即运行基座）：exe 所在目录向上找 Mau.sln 仓库根，Data 挂仓库根；找不到回退当前工作目录
        /// </summary>
        /// <returns>数据根目录</returns>
        private static string ResolveDataRoot()
        {
            string root = FindRepoRoot(AppContext.BaseDirectory);
            if (root.Length > 0)
            {
                return root;
            }
            return Directory.GetCurrentDirectory();
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
        /// 可写根路径数组——cs.* 编码工具只碰可写根（只读知识根不参与项目扫描——P8.5 配置群）
        /// </summary>
        /// <param name="workspace">工作区配置</param>
        /// <returns>可写根路径数组</returns>
        private static string[] WritableRootPaths(WorkspaceConfig workspace)
        {
            List<string> paths = new List<string>();
            for (int i = 0; i < workspace.Roots.Length; i++)
            {
                if (workspace.Roots[i].Writable)
                {
                    paths.Add(workspace.Roots[i].Path);
                }
            }
            return paths.ToArray();
        }
    }
}
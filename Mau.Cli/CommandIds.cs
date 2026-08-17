using System;
using System.Collections.Generic;

namespace Mau.Cli
{
    /// <summary>
    /// 指令编号——命令名是外观层，编号是内核（与 TokenId 同构：改名零逻辑改动）。
    /// 匹配表是命令名唯一入口——内部路由只认编号。
    /// </summary>
    public static class CommandIds
    {
        /// <summary>帮助（无参数默认）</summary>
        public const int Help = 0;

        /// <summary>verify——全链编译不产出</summary>
        public const int Verify = 1;

        /// <summary>gen——全链编译 + C# 生成物输出</summary>
        public const int Gen = 2;

        /// <summary>build——全链编译 + Roslyn Emit</summary>
        public const int Build = 3;

        /// <summary>test——分层门禁（翻译器 + Runtime + 黄金）</summary>
        public const int Test = 4;

        /// <summary>check——语法谱 + 负例谱 + 关键路径报告</summary>
        public const int Check = 5;

        /// <summary>debug——四柱状态表 + 单步 + 状态断点</summary>
        public const int Debug = 6;

        /// <summary>bricks——积木索引（list / index --update / index --verify）</summary>
        public const int Bricks = 7;

        /// <summary>proj——组工程统一构筑链（翻译落盘 + dotnet build；design-ch4-deploy §三）</summary>
        public const int Proj = 8;

        /// <summary>
        /// 命令名 → 编号匹配表——唯一入口（名称可后续改，编号不可变）
        /// </summary>
        private static readonly Dictionary<string, int> CommandIds_Map = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            { "verify", Verify },
            { "gen", Gen },
            { "build", Build },
            { "test", Test },
            { "check", Check },
            { "debug", Debug },
            { "bricks", Bricks },
            { "proj", Proj },
        };

        /// <summary>
        /// 命令名解析——匹配表查询
        /// </summary>
        /// <param name="name">命令名</param>
        /// <param name="id">输出编号（未命中 = Help）</param>
        /// <returns>true=命中</returns>
        public static bool TryResolve(string name, out int id)
        {
            return CommandIds_Map.TryGetValue(name, out id);
        }

        /// <summary>
        /// bricks 子命令编号——list / index
        /// </summary>
        public static class BricksSub
        {
            /// <summary>list——索引枚举</summary>
            public const int List = 0;

            /// <summary>index——索引重建/校验（--update / --verify）</summary>
            public const int Index = 1;

            /// <summary>
            /// 子命令名 → 编号匹配表
            /// </summary>
            private static readonly Dictionary<string, int> BricksSub_Map = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                { "list", List },
                { "index", Index },
            };

            /// <summary>
            /// 子命令名解析
            /// </summary>
            /// <param name="name">子命令名</param>
            /// <param name="id">输出编号（未命中 = -1）</param>
            /// <returns>true=命中</returns>
            public static bool TryResolve(string name, out int id)
            {
                if (BricksSub_Map.TryGetValue(name, out id))
                {
                    return true;
                }
                id = -1;
                return false;
            }
        }
    }
}

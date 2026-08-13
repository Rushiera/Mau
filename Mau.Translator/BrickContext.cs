using System;
using System.Collections.Generic;

namespace Mau.Translator
{
    /// <summary>
    /// 编译上下文——基座索引（静态 BrickIndex）+ 产品积木视图列表（BrickIndexView）。
    /// 产品积木机制（design-mau-boundary.md §五）：项目自持积木目录（mauproj bricks: 声明），
    /// 构筑期内存合并查询——基座与产品全名唯一，重名冲突构筑期报错（E225）。
    /// ctx = null 的调用方仅用基座索引（Mau 基座自证路径）。
    /// </summary>
    public sealed class BrickContext
    {
        /// <summary>
        /// 产品积木视图列表（0..N）
        /// </summary>
        private readonly List<BrickIndexView> _products = new List<BrickIndexView>();

        /// <summary>
        /// 产品目录加载失败诊断——E225 数据源（非空 = 编译失败，fail closed）
        /// </summary>
        public string ProductError = "";
/// <summary>
/// 隔离模式——产品构筑时基座积木不可见（零跨仓引用铁律 design-mau-boundary.md §五.5）。
/// 默认 true——产品构筑一律隔离；仅测试/内部场景显式关闭。
/// </summary>
public bool IsolateBase = true;
        /// <summary>
        /// 从产品积木目录列表构造上下文——逐目录加载 + 基座重名冲突检测
        /// </summary>
        /// <param name="dirs">产品 Bricks/ 目录（绝对路径，0..N）</param>
        /// <returns>上下文（ProductError 非空 = 加载失败）</returns>
        public static BrickContext FromProductDirs(List<string> dirs)
{
            BrickContext ctx = new BrickContext();
            ctx.IsolateBase = true; // 零跨仓引用铁律（§五.5）——产品构筑一律隔离基座积木
            for (int i = 0; i < dirs.Count; i++)
            {
                string dir = dirs[i];
                BrickIndexView view = new BrickIndexView();
                if (!view.Load(dir))
                {
                    ctx.ProductError = "产品积木目录无效——index.json 缺失或解析失败: " + dir
                        + "（请先在该目录运行 mau bricks index --update --bricks <path>）";
                    return ctx;
                }
                // 冲突检测——产品积木名与基座重名 = 拒绝（全名唯一，防止产品覆盖基座语义）
                foreach (BrickIndexEntry entry in view.All)
                {
                    BrickIndexEntry? baseEntry;
                    if (BrickIndex.TryGet(entry.Name, out baseEntry))
                    {
                        ctx.ProductError = "产品积木名与基座冲突——'" + entry.Name + "'（产品目录 "
                            + dir + "；基座 " + BrickIndex.ResolveSourcePath(baseEntry) + "）";
                        return ctx;
                    }
                }
                ctx._products.Add(view);
            }
            // 依赖预检（E227 场景 b——加载期 fail closed）——产品积木依赖基座积木 = 跨仓引用禁止
            // （零跨仓引用铁律：产品积木库完全自持；依赖基座桥的迁出资产需模板复制自持副本）
            for (int v = 0; v < ctx._products.Count; v++)
            {
                foreach (BrickIndexEntry entry in ctx._products[v].All)
                {
                    for (int d = 0; d < entry.Dependencies.Count; d++)
                    {
                        string dep = entry.Dependencies[d];
                        if (dep.EndsWith(".cs", StringComparison.Ordinal))
                        {
                            continue; // 支撑文件——非积木依赖
                        }
                        BrickIndexEntry? baseDep;
                        if (BrickIndex.TryGet(dep, out baseDep))
                        {
                            ctx.ProductError = "产品积木跨仓依赖禁止——'" + entry.Name + "' 依赖基座积木 '"
                                + dep + "'（零跨仓引用铁律：产品积木库完全自持；依赖基座桥需模板复制自持副本）";
                            return ctx;
                        }
                    }
                }
            }
            return ctx;
        }
        /// <summary>
        /// 查询积木——先基座后产品
        /// </summary>
        /// <param name="name">积木名</param>
        /// <param name="entry">命中条目</param>
        /// <param name="sourceView">出处视图——null=基座，非 null=产品视图（E226 依据 + 源文件路径根）</param>
        /// <returns>是否命中</returns>
        public bool TryGet(string name, out BrickIndexEntry entry, out BrickIndexView? sourceView)
{
            sourceView = null;
            entry = null!;
            // 隔离模式——产品构筑时基座积木不可见（零跨仓引用铁律：跨仓引用由验证层分类报 E227）
            if (IsolateBase)
            {
                for (int i = 0; i < _products.Count; i++)
                {
                    if (_products[i].TryGet(name, out entry!))
                    {
                        sourceView = _products[i];
                        return true;
                    }
                }
                return false;
            }
            if (BrickIndex.TryGet(name, out entry!))
            {
                return true;
            }
            for (int i = 0; i < _products.Count; i++)
            {
                if (_products[i].TryGet(name, out entry!))
                {
                    sourceView = _products[i];
                    return true;
                }
            }
            return false;
        }        /// <summary>
        /// 产品积木总数
        /// </summary>
        public int ProductCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _products.Count; i++)
                {
                    n = n + _products[i].Count;
                }
                return n;
            }
        }

        /// <summary>
        /// 产品视图列表——产品积木谱枚举用
        /// </summary>
        public List<BrickIndexView> Products
        {
            get { return _products; }
        }

        /// <summary>
        /// 解析条目源码绝对路径——基座条目走基座根，产品条目走产品视图根
        /// </summary>
        /// <param name="entry">条目</param>
        /// <param name="sourceView">出处视图（null=基座）</param>
        /// <returns>绝对路径（未命中返回空串）</returns>
        public string ResolveSourcePath(BrickIndexEntry entry, BrickIndexView? sourceView)
        {
            if (sourceView == null)
            {
                return BrickIndex.ResolveSourcePath(entry);
            }
            return sourceView.ResolveSourcePath(entry);
        }
    }
}

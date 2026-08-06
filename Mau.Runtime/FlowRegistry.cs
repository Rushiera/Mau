using System;
using System.Collections.Generic;

namespace Mau.Runtime
{
    /// <summary>
    /// 实体注册表——通用 Flow 注册/查找/回收（机制层，不感知产品类型）。
    /// 裁决落点：CH4.Core Cat/Dog 字典机制下沉（boundary-map 缺① D2）。
    /// </summary>
    public sealed class FlowRegistry
    {
        /// <summary>
        /// 线程归属守卫——注册/查找/回收仅主线程
        /// </summary>
        private readonly ThreadGuard _guard;

        /// <summary>
        /// 全局 ID 分配器——注册时分配实体 ID
        /// </summary>
        private readonly IdAllocator _ids;

        /// <summary>
        /// 实体字典——ID 到 Flow 实例
        /// </summary>
        private readonly Dictionary<long, IFlow> _flows = new Dictionary<long, IFlow>();

        /// <summary>
        /// 名册——ID 到名字
        /// </summary>
        private readonly Dictionary<long, string> _names = new Dictionary<long, string>();

        /// <summary>
        /// 类型名册——ID 到实现类型名
        /// </summary>
        private readonly Dictionary<long, string> _typeNames = new Dictionary<long, string>();

        /// <summary>
        /// 类型工厂表——类型键 → 实例工厂（基座按参数生成实体的通道）
        /// </summary>
        private readonly Dictionary<string, Func<IFlow>> _factories = new Dictionary<string, Func<IFlow>>(StringComparer.Ordinal);

        /// <summary>
        /// 构造注册表
        /// </summary>
        /// <param name="guard">线程归属守卫——构造于宿主主线程</param>
        /// <param name="ids">全局 ID 分配器</param>
        public FlowRegistry(ThreadGuard guard, IdAllocator ids)
        {
            if (guard == null)
            {
                throw new ArgumentNullException("guard");
            }
            if (ids == null)
            {
                throw new ArgumentNullException("ids");
            }
            _guard = guard;
            _ids = ids;
        }

        /// <summary>
        /// 注册实体——分配全局 ID，登记实例/名字/类型
        /// </summary>
        /// <param name="flow">Flow 实例</param>
        /// <param name="name">实体名字</param>
        /// <returns>分配的全局 ID</returns>
        public long Register(IFlow flow, string name)
        {
            _guard.AssertMainThread("FlowRegistry.Register");
            if (flow == null)
            {
                throw new ArgumentNullException("flow");
            }
            if (name == null)
            {
                throw new ArgumentNullException("name");
            }
            int typeId;
            long id = _ids.Alloc("Flow", out typeId);
            _flows[id] = flow;
            _names[id] = name;
            _typeNames[id] = flow.GetType().Name;
            return id;
        }

        /// <summary>
        /// 回收实体——从字典移除（生命周期 Shutdown 由宿主负责——本层只管理注册）
        /// </summary>
        /// <param name="id">实体 ID</param>
        /// <returns>true=回收成功（存在且移除）</returns>
        public bool Unregister(long id)
        {
            _guard.AssertMainThread("FlowRegistry.Unregister");
            if (!_flows.ContainsKey(id))
            {
                return false;
            }
            _flows.Remove(id);
            _names.Remove(id);
            _typeNames.Remove(id);
            return true;
        }

        /// <summary>
        /// 按 ID 查找实体
        /// </summary>
        /// <param name="id">实体 ID</param>
        /// <returns>Flow 实例，不存在返回 null</returns>
        public IFlow? Get(long id)
        {
            _guard.AssertMainThread("FlowRegistry.Get");
            IFlow? flow;
            if (_flows.TryGetValue(id, out flow))
            {
                return flow;
            }
            return null;
        }

        /// <summary>
        /// 按 ID 取名字
        /// </summary>
        /// <param name="id">实体 ID</param>
        /// <returns>名字，不存在返回空串</returns>
        public string GetName(long id)
        {
            _guard.AssertMainThread("FlowRegistry.GetName");
            string? name;
            if (_names.TryGetValue(id, out name) && name != null)
            {
                return name;
            }
            return "";
        }

        /// <summary>
        /// 注册类型工厂——宿主加载业务模块时登记（类型键全局唯一）
        /// </summary>
        /// <param name="typeKey">类型键——业务模块声明的实体类型（如 DogType）</param>
        /// <param name="factory">实例工厂</param>
        /// <returns>true=注册成功；false=键重复或参数非法</returns>
        public bool RegisterFactory(string typeKey, Func<IFlow> factory)
        {
            _guard.AssertMainThread("FlowRegistry.RegisterFactory");
            if (string.IsNullOrWhiteSpace(typeKey) || factory == null)
            {
                return false;
            }
            if (_factories.ContainsKey(typeKey))
            {
                return false;
            }
            _factories[typeKey] = factory;
            return true;
        }

        /// <summary>
        /// 按类型键生成实体——工厂实例化 → 注册 → 返回全局 ID（基座生成通道）
        /// </summary>
        /// <param name="typeKey">类型键</param>
        /// <param name="name">实体名字</param>
        /// <returns>全局 ID；工厂不存在返回 -1</returns>
        public long Create(string typeKey, string name)
        {
            _guard.AssertMainThread("FlowRegistry.Create");
            Func<IFlow>? factory;
            if (!_factories.TryGetValue(typeKey, out factory) || factory == null)
            {
                return -1;
            }
            IFlow flow = factory();
            if (flow == null)
            {
                return -1;
            }
            return Register(flow, name);
        }

        /// <summary>
        /// 已注册工厂数量
        /// </summary>
        public int FactoryCount
        {
            get
            {
                _guard.AssertMainThread("FlowRegistry.FactoryCount");
                return _factories.Count;
            }
        }

        /// <summary>
        /// 全部实体 ID（冻结快照——迭代期间增删安全）
        /// </summary>
        public long[] Ids
        {
            get
            {
                _guard.AssertMainThread("FlowRegistry.Ids");
                long[] ids = new long[_flows.Count];
                _flows.Keys.CopyTo(ids, 0);
                return ids;
            }
        }

        /// <summary>
        /// 全部实体条目（冻结快照——深复制）
        /// </summary>
        public FlowEntry[] Entries
        {
            get
            {
                _guard.AssertMainThread("FlowRegistry.Entries");
                long[] ids = Ids;
                FlowEntry[] entries = new FlowEntry[ids.Length];
                for (int i = 0; i < ids.Length; i++)
                {
                    FlowEntry entry = new FlowEntry();
                    entry.Id = ids[i];
                    string? name;
                    if (_names.TryGetValue(ids[i], out name) && name != null)
                    {
                        entry.Name = name;
                    }
                    string? typeName;
                    if (_typeNames.TryGetValue(ids[i], out typeName) && typeName != null)
                    {
                        entry.TypeName = typeName;
                    }
                    entries[i] = entry;
                }
                return entries;
            }
        }

        /// <summary>
        /// 已注册数量
        /// </summary>
        public int Count
        {
            get
            {
                _guard.AssertMainThread("FlowRegistry.Count");
                return _flows.Count;
            }
        }

        /// <summary>
        /// 清空全部——宿主 Shutdown 时调用
        /// </summary>
        public void Clear()
        {
            _guard.AssertMainThread("FlowRegistry.Clear");
            _flows.Clear();
            _names.Clear();
            _typeNames.Clear();
        }
    }

    /// <summary>
    /// 实体条目——注册表透明化 DTO（冻结快照，纯数据）
    /// </summary>
    public sealed class FlowEntry
    {
        /// <summary>
        /// 实体全局 ID
        /// </summary>
        public long Id;

        /// <summary>
        /// 实体名字
        /// </summary>
        public string Name = null!;

        /// <summary>
        /// 实现类型名
        /// </summary>
        public string TypeName = null!;
    }
}

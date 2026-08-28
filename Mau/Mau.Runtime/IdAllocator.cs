using System;
using System.Collections.Generic;

namespace Mau.Runtime
{
    /// <summary>
    /// 全局 ID 分配器——分配顺序只由主线程生命周期操作决定，同一输入序列得到稳定实例顺序
    /// </summary>
    public sealed class IdAllocator
    {
        /// <summary>
        /// 下一个可分配的全局 ID
        /// </summary>
        private long _next;

        /// <summary>
        /// 各逻辑类型的累计分配数
        /// </summary>
        private readonly Dictionary<string, int> _typeCount;

        /// <summary>
        /// 创建从 1 开始的 ID 分配器
        /// </summary>
        public IdAllocator()
        {
            _next = 1;
            _typeCount = new Dictionary<string, int>();
        }

        /// <summary>
        /// 分配全局 ID 并返回当前类型实例序号
        /// </summary>
        /// <param name="typeName">逻辑类型名称</param>
        /// <param name="typeId">该类型本次分配后的序号</param>
        /// <returns>全局唯一 ID</returns>
        public long Alloc(string typeName, out int typeId)
        {
            long longId = _next;
            int oldCount;

            _next = _next + 1;
            if (!_typeCount.TryGetValue(typeName, out oldCount))
            {
                oldCount = 0;
            }
            typeId = oldCount + 1;
            _typeCount[typeName] = typeId;
            return longId;
        }

        /// <summary>
        /// 下一个可分配的全局 ID——透明度暴露
        /// </summary>
        public long NextId
        {
            get { return _next; }
        }

        /// <summary>
        /// 复制类型累计计数，避免外部持有可变内部字典
        /// </summary>
        /// <returns>类型累计计数快照</returns>
        public IReadOnlyDictionary<string, int> GetTypeCountSnapshot()
        {
            return new Dictionary<string, int>(_typeCount);
        }
    }
}

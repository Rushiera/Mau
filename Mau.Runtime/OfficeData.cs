using System.Collections.Generic;

namespace Mau.Runtime
{
    /// <summary>
    /// OA 单双字典载荷——模块间通信的唯一数据通道（design-mau-module.md §二）。
    /// int 走 int；非 int 全部走 str（ID/时间戳/JSON/bool→0/1）；数组用 名_len + 名_0..N-1。
    /// </summary>
    public struct OfficeData
    {
        /// <summary>
        /// int 类数据字典
        /// </summary>
        public Dictionary<string, int> Ints;

        /// <summary>
        /// str 类数据字典——非 int 全部走这里
        /// </summary>
        public Dictionary<string, string> Strs;

        /// <summary>
        /// 创建空载荷——双字典随单生成
        /// </summary>
        /// <returns>空载荷</returns>
        public static OfficeData Empty()
        {
            OfficeData data;
            data.Ints = new Dictionary<string, int>(System.StringComparer.Ordinal);
            data.Strs = new Dictionary<string, string>(System.StringComparer.Ordinal);
            return data;
        }

        /// <summary>
        /// 深拷贝——快照/回执副本防篡改
        /// </summary>
        /// <returns>独立副本</returns>
        public OfficeData Copy()
        {
            OfficeData copy = Empty();
            string[] intKeys = new string[Ints.Count];
            Ints.Keys.CopyTo(intKeys, 0);
            for (int i = 0; i < intKeys.Length; i = i + 1)
            {
                copy.Ints[intKeys[i]] = Ints[intKeys[i]];
            }
            string[] strKeys = new string[Strs.Count];
            Strs.Keys.CopyTo(strKeys, 0);
            for (int i = 0; i < strKeys.Length; i = i + 1)
            {
                copy.Strs[strKeys[i]] = Strs[strKeys[i]];
            }
            return copy;
        }

        /// <summary>
        /// 是否为空载荷
        /// </summary>
        /// <returns>双字典均空为真</returns>
        public bool IsEmpty()
        {
            return Ints.Count == 0 && Strs.Count == 0;
        }
    }
}

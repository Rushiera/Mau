namespace Mau.Runtime
{
    /// <summary>
    /// 静态值盒（程序级）——boxId 作用域存储经 DataBox scope 映射（BRIK 唯一数据协议）
    /// 单值 scope "box:{boxId}"；数据包 scope "boxdic:{boxId}"
    /// </summary>
    public static class BoxStore
    {
        /// <summary>
        /// 原子递增锁（Inc 读改写互斥——跨线程安全）
        /// </summary>
        private static readonly object IncLock = new object();
        /// <summary>
        /// 写入单值
        /// </summary>
        /// <param name="boxId">作用域 ID</param>
        /// <param name="key">键</param>
        /// <param name="value">值</param>
        /// <returns>true=成功</returns>
        public static bool Set(string boxId, string key, int value)
        {
            try
            {
                DataBox.Set<int>("box:" + boxId, key, value);
                return true;
            }
            catch
            {
                // 积木薄壳零异常契约——DataBox 异常折返 false 不抛出（语料面不感知异常）
                return false;
            }
        }
        /// <summary>
        /// 读取单值
        /// </summary>
        /// <param name="boxId">作用域 ID</param>
        /// <param name="key">键</param>
        /// <param name="defaultValue">默认值</param>
        /// <param name="value">读取值</param>
        /// <returns>true=成功</returns>
        public static bool Get(string boxId, string key, int defaultValue, out int value)
        {
            value = defaultValue;
            try
            {
                int v;
                if (DataBox.TryGet<int>("box:" + boxId, key, out v))
                {
                    value = v;
                }
                return true;
            }
            catch
            {
                // 积木薄壳零异常契约——DataBox 异常折返 false 不抛出（语料面不感知异常）
                return false;
            }
        }
        /// <summary>
        /// 写入数据包（JSON 文本——对象/数组；G.9 放宽：纯文本暂存——回滚显示用户输入数据源 2026-08-11）
        /// </summary>
        /// <param name="boxId">作用域 ID</param>
        /// <param name="packetKey">包名</param>
        /// <param name="dataJson">JSON 对象/数组文本，或纯文本</param>
        /// <returns>true=成功</returns>
        public static bool SetDic(string boxId, string packetKey, string dataJson)
        {
            // 三分支同义收拢（R1-P3-04）：JSON 对象/数组 与 纯文本 均按原文暂存（G.9 用户输入原文）；原 JsonDocument.Parse 仅为判别、判别不改变行为故省略
            DataBox.Set<string>("boxdic:" + boxId, packetKey, dataJson);
            return true;
        }
        /// <summary>
        /// 读取数据包（JSON 文本）
        /// </summary>
        /// <param name="boxId">作用域 ID</param>
        /// <param name="packetKey">包名</param>
        /// <param name="dataJson">JSON 对象文本</param>
        /// <returns>true=成功</returns>
        public static bool GetDic(string boxId, string packetKey, out string dataJson)
        {
            dataJson = "{}";
            try
            {
                string json;
                if (DataBox.TryGet<string>("boxdic:" + boxId, packetKey, out json))
                {
                    dataJson = json;
                }
                return true;
            }
            catch
            {
                // 积木薄壳零异常契约——DataBox 异常折返 false 不抛出（语料面不感知异常）
                return false;
            }
        }
        /// <summary>
        /// 清空一个作用域
        /// </summary>
        /// <param name="boxId">作用域 ID</param>
        /// <returns>true=成功</returns>
        public static bool Clear(string boxId)
        {
            try
            {
                DataBox.ClearScope("box:" + boxId);
                DataBox.ClearScope("boxdic:" + boxId);
                return true;
            }
            catch
            {
                // 积木薄壳零异常契约——DataBox 异常折返 false 不抛出（语料面不感知异常）
                return false;
            }
        }
        /// <summary>
        /// 原子递增——box[boxId, key] += 1（锁内读改写——跨线程安全；T4 模块谱并发计数新增 2026-08-13）
        /// </summary>
        /// <param name="boxId">作用域 ID</param>
        /// <param name="key">键</param>
        /// <returns>true=成功</returns>
        public static bool Inc(string boxId, string key)
        {
            lock (IncLock)
            {
                int v;
                Get(boxId, key, 0, out v);
                return Set(boxId, key, v + 1);
            }
        }
    }
}

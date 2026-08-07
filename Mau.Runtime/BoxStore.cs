namespace Mau.Runtime
{
    /// <summary>
    /// 静态值盒（程序级）——boxId 作用域存储经 DataBox scope 映射（BRIK 唯一数据协议）
    /// 单值 scope "box:{boxId}"；数据包 scope "boxdic:{boxId}"
    /// </summary>
    public static class BoxStore
    {
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
                return false;
            }
        }

        /// <summary>
        /// 写入数据包（JSON 文本）
        /// </summary>
        /// <param name="boxId">作用域 ID</param>
        /// <param name="packetKey">包名</param>
        /// <param name="dataJson">JSON 对象文本</param>
        /// <returns>true=成功</returns>
        public static bool SetDic(string boxId, string packetKey, string dataJson)
        {
            try
            {
                using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(dataJson);
                if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
                {
                    return false;
                }
                DataBox.Set<string>("boxdic:" + boxId, packetKey, dataJson);
                return true;
            }
            catch
            {
                return false;
            }
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
                return false;
            }
        }
    }
}

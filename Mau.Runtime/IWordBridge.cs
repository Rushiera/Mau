namespace Mau.Runtime
{
    /// <summary>
    /// Word 桥——外部包能力接口（PACK 类，IPackBridge 特化）。
    /// 实现：Mau.Office.OfficeBridge（DocumentFormat.OpenXml 封装）；依赖者宿主选装 Bind。
    /// </summary>
    public interface IWordBridge : IPackBridge
    {
    }
}

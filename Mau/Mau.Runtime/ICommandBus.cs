using System.Collections.Generic;

namespace Mau.Runtime
{
    /// <summary>
    /// 指令总线接口。key-value 模型——模块预注册指令 key，外部通过 Set 写入 value。
    /// 由 CommandBus 实现，解耦宿主层对具体类的依赖。
    /// </summary>
    public interface ICommandBus
    {
        /// <summary>
        /// 当前指令池快照（只读）
        /// </summary>
        IReadOnlyDictionary<string, int> CmdCache { get; }

        /// <summary>
        /// 注册模块的指令 key 列表
        /// </summary>
        /// <param name="ownerLongId">模块全局 ID</param>
        /// <param name="cmdKeys">指令 key 数组</param>
        void Register(long ownerLongId, string[] cmdKeys);

        /// <summary>
        /// 注销模块并清理残留指令
        /// </summary>
        /// <param name="ownerLongId">模块全局 ID</param>
        void Unregister(long ownerLongId);

        /// <summary>
        /// 向指令池写入一条 int 指令
        /// </summary>
        /// <param name="key">指令 key，必须已被注册</param>
        /// <param name="value">指令值</param>
        /// <param name="source">投递来源标识（ui/brick/pipe/cli/test 等——C 类 Log 携带，区分输入方）</param>
        void Set(string key, int value, string source);

        /// <summary>
        /// 向文本池写入一条 string 指令
        /// </summary>
        /// <param name="key">指令 key，必须已被注册</param>
        /// <param name="text">指令文本</param>
        /// <param name="source">投递来源标识（ui/brick/pipe/cli/test 等——C 类 Log 携带，区分输入方）</param>
        void SetText(string key, string text, string source);

        /// <summary>
        /// 获取指定模块的指令邮件并清空其在池中的指令
        /// </summary>
        /// <param name="ownerLongId">模块全局 ID</param>
        CommandPack GetCommandEmail(long ownerLongId);

        /// <summary>
        /// 清空指定模块在池中的所有残留指令
        /// </summary>
        /// <param name="ownerLongId">模块全局 ID</param>
        void Clean(long ownerLongId);

        /// <summary>
        /// 获取已注册模块清单，用于调试面板展示
        /// </summary>
        string[] GetKeyDic();

        /// <summary>
        /// 获取当前指令池内容，用于调试面板展示
        /// </summary>
        string[] GetCommandPool();

        /// <summary>
        /// 获取 Command 域独立摘要——透明度暴露
        /// </summary>
        /// <returns>注册/待消费/拒绝统计快照</returns>
        CommandSnapshot GetSnapshot();
/// <summary>
/// 冻结输入——宿主每帧 Tick 开始处调用，此前到达的 Set 进入可消费池
/// </summary>
void BeginTickInput();    }
}

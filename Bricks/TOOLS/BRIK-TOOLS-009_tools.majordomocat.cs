// ═══════════════════════════════════════════════════
// 积木: tools.majordomocat
// ID:   BRIK-TOOLS-009
// 类别: TOOLS
// 作用: 返回 Majordomo 工具组全部工具的 OpenAI 兼容定义 JSON（宿主工具池拼装原料——design-ch4-tools-pool）
// 依赖: 无
// 引用: 无（纯文本常量）
// 原理: GetToolsJson() 返回本组工具定义整包 JSON——改描述只动本文件，mau proj 重建即生效
//       命名铁律：工具定义积木约定名 = tools.<flowName 小写>——flowName 由语料文件名派生
//       （majordomo_cat.mau → MajordomoCat → tools.majordomocat）；名不符则静默跳过（池内无定义）
// 常用: FL_MajordomoCat.GetToolsJson() 自曝调用面（IFlow 接口）
// ═══════════════════════════════════════════════════
namespace Mau.Bricks
{
    /// <summary>
    /// 工具定义积木——Majordomo 组工具定义 JSON（OpenAI 兼容拼装原料）。
    /// 特权组：仅默认会话（catId=majordomo）可见可调（design-ch4-host-restart §二）。
    /// </summary>
    public static class ToolsMajordomocatBrick
    {
        /// <summary>
        /// 获取本组工具定义 JSON——{"group":"MajordomoCat","tools":[{name,description,parameters}]}
        /// </summary>
        /// <returns>工具定义 JSON 字符串</returns>
        public static string GetToolsJson()
        {
            return "{\"group\":\"MajordomoCat\",\"privileged\":true,\"tools\":[" +
                "{\"name\":\"majordomo-restart\",\"description\":\"宿主自更新：部署当前源码（prepare + 原子切换）并重启宿主，重启完成后结果回执自动注入本会话（majordomo）。小改动自测用——改语料/积木/文档后一条调用完成'改-部署-重启-看结果'闭环。调用后本轮对话将被强制中断（已完成内容与前文已落盘），新宿主起来后会带回部署结论（版本/成败/产物时间戳）。架构级变更（Runtime 机制/协议/签名）不要用本工具。\",\"parameters\":{\"type\":\"object\",\"properties\":{\"target\":{\"type\":\"string\",\"description\":\"目标运行区绝对路径（缺省=受控根 mauout）\"},\"push\":{\"type\":\"string\",\"description\":\"重启后附加说明串（随部署报告一起注入本会话，可空）\"}},\"required\":[]}}," +
                "{\"name\":\"majordomo-cmd\",\"description\":\"宿主管理指令执行——把一行管理指令交给内核执行（与前端管理面板同源单内核）。允许前缀：cat.*（cat.list 全猫清单 / cat.new 新建 / cat.start 启动 / cat.stop 停止 / cat.delete 销毁 / cat.pause 中止回合 / cat.chat 投递对话 / cat.cfg.get 读每猫配置 / cat.cfg.set 写每猫配置）与 catcfg.*（catcfg.apply 配置生效）。错 key 回执附可用指令清单。特权面——仅主干会话（majordomo）可调。\",\"parameters\":{\"type\":\"object\",\"properties\":{\"cmd\":{\"type\":\"string\",\"description\":\"一行管理指令（如 cat.list / cat.cfg.get CH4Coder / cat.cfg.set CH4Coder persona 新角色段）\"}},\"required\":[\"cmd\"]}}" +
                ",{\"name\":\"majordomo-catinfo\",\"description\":\"全猫状态统计——一次调用取全部猫：运行态（是否运行 / 监听端口 / 主干标记）、会话流式态（四相环 + 七态细分）、前文长度（请求级实时 tokens 与前文条数）、最近活跃时间（最近一次前文变动时刻）、轮次 / 消息数 / 待处理 / Note 标记。返回体 = 整块分类 JSON（缩进 + 中文直显，同 info 规格；无「头 + 正文」两段）。特权面——仅主干会话（majordomo）可调。\",\"parameters\":{\"type\":\"object\",\"properties\":{},\"required\":[]}}" +
                "]}";
        }
    }
}
// #MAU_CHECKSUM:SHA256:CF02944D897A397612A153306813AAFB3A03135FFDC51B3D852BE3DC0A3BC891

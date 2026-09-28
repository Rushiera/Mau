// ═══════════════════════════════════════════════════
// 积木: tools.filecat
// ID:   BRIK-TOOLS-011
// 类别: TOOLS
// 作用: 返回 FileCat 工具组全部工具的 OpenAI 兼容定义 JSON（宿主工具池拼装原料——A67 拆分）
// 依赖: 无
// 引用: 无（纯文本常量）
// 原理: GetToolsJson() 返回本组工具定义整包 JSON——改描述只动本文件，mau proj 重建即生效
// 常用: FL_FileCat.GetToolsJson() 自曝调用面（IFlow 接口）
// ═══════════════════════════════════════════════════
namespace Mau.Bricks
{
    /// <summary>
    /// 工具定义积木——FileCat 组工具定义 JSON（OpenAI 兼容拼装原料）
    /// </summary>
    public static class ToolsFilecatBrick
    {
        /// <summary>
        /// 获取本组工具定义 JSON——{"group":"FileCat","tools":[{name,description,parameters}]}
        /// </summary>
        /// <returns>工具定义 JSON 字符串</returns>
        public static string GetToolsJson()
        {
            return "{\"group\":\"FileCat\",\"tools\":[" +
                "{\"name\":\"file-tree\",\"description\":\"目录树——广度优先列目录结构（同层出完再进下一层）；超限时末行附 [截断]（含总条数）；path 可用根名（如 CCBP）= 该根根目录\",\"parameters\":{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"目录路径（受控根 id 前缀 / 根名 / 绝对路径）\"},\"depth\":{\"type\":\"integer\",\"description\":\"输出层数（默认 2，1–10；1=只列直属子项）\"},\"limit\":{\"type\":\"integer\",\"description\":\"最大条数（默认 500；触顶时末行给出总条数）\"}},\"required\":[\"path\"]}}," +
                "{\"name\":\"file-find\",\"description\":\"文件名 glob 搜索——按文件名模式找文件（**/ 前缀=递归全部子目录；recursive 默认 true）\",\"parameters\":{\"type\":\"object\",\"properties\":{\"dir\":{\"type\":\"string\",\"description\":\"搜索根目录（受控根 id 前缀 / 根名 / 绝对路径）\"},\"pattern\":{\"type\":\"string\",\"description\":\"文件名模式（默认 *；**/ 前缀=递归全部子目录）\"},\"recursive\":{\"type\":\"boolean\",\"description\":\"是否递归（默认 true）\"},\"limit\":{\"type\":\"integer\",\"description\":\"最大结果（默认 500；触顶时末行给出总条数）\"}},\"required\":[\"dir\"]}}," +
                "{\"name\":\"file-move\",\"description\":\"移动/重命名——文件与目录均支持（目录=整棵子树移动）；自动创建目标父目录；目标已存在拒绝\",\"parameters\":{\"type\":\"object\",\"properties\":{\"src\":{\"type\":\"string\",\"description\":\"源路径\"},\"dest\":{\"type\":\"string\",\"description\":\"目标路径\"}},\"required\":[\"src\",\"dest\"]}}," +
                "{\"name\":\"file-delete\",\"description\":\"软删除——移入受控回收站（可恢复）；支持文件与目录（含非空目录=整棵子树）\",\"parameters\":{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"要删除的文件/目录路径\"}},\"required\":[\"path\"]}}," +
                "{\"name\":\"file-copy\",\"description\":\"复制——文件与目录均支持（目录=整棵子树复制）；自动创建目标父目录；目标已存在拒绝\",\"parameters\":{\"type\":\"object\",\"properties\":{\"src\":{\"type\":\"string\",\"description\":\"源路径\"},\"dest\":{\"type\":\"string\",\"description\":\"目标路径\"}},\"required\":[\"src\",\"dest\"]}}," +
                "{\"name\":\"file-version\",\"description\":\"读取 PE 文件（exe/dll）版本信息——版本三件（Informational/File/Product）+ 修改时间 + 大小；非 PE 文件明确报错（部署产物版本与编译时刻核对）\",\"parameters\":{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"文件路径（受控根 id 前缀 / 根名 / 绝对路径）\"}},\"required\":[\"path\"]}}" +
                "]}";
        }
    }
}
// #MAU_CHECKSUM:SHA256:7A802A1BDA679EEC547F2B5ECDDCE55466EE412DB8ED3C335AF8B669FB09B016

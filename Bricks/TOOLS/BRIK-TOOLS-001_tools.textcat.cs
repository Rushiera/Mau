// ═══════════════════════════════════════════════════
// 积木: tools.textcat
// ID:   BRIK-TOOLS-001
// 类别: TOOLS
// 作用: 返回 TextCat 工具组全部工具的 OpenAI 兼容定义 JSON（宿主工具池拼装原料——design-ch4-tools-pool）
// 依赖: 无
// 引用: 无（纯文本常量）
// 原理: GetToolsJson() 返回本组工具定义整包 JSON——改描述只动本文件，mau proj 重建即生效
// 常用: FL_TextCat.GetToolsJson() 自曝调用面（IFlow 接口）
// ═══════════════════════════════════════════════════
namespace Mau.Bricks
{
    /// <summary>
    /// 工具定义积木——TextCat 组工具定义 JSON（OpenAI 兼容拼装原料）
    /// </summary>
    public static class ToolsTextcatBrick
    {
        /// <summary>
        /// 获取本组工具定义 JSON——{"group":"TextCat","tools":[{name,description,parameters}]}
        /// </summary>
        /// <returns>工具定义 JSON 字符串</returns>
        public static string GetToolsJson()
        {
            return "{\"group\":\"TextCat\",\"tools\":[" +
                "{\"name\":\"text-read\",\"description\":\"读取 UTF-8 文本文件（受控根内；路径支持受控根 id 前缀寻址或绝对路径），返回完整内容\",\"parameters\":{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"要读取的文件路径（受控根 id 前缀或绝对路径）\"}},\"required\":[\"path\"]}}," +
                "{\"name\":\"text-write\",\"description\":\"覆写文件（含新建）——整文件替换为 content（路径支持受控根 id 前缀或绝对路径）\",\"parameters\":{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"目标文件路径\"},\"content\":{\"type\":\"string\",\"description\":\"完整新内容\"}},\"required\":[\"path\",\"content\"]}}," +
                "{\"name\":\"text-append\",\"description\":\"追加文本到文件末尾（文件不存在则新建；路径支持受控根 id 前缀或绝对路径）\",\"parameters\":{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"目标文件路径\"},\"content\":{\"type\":\"string\",\"description\":\"要追加的文本\"}},\"required\":[\"path\",\"content\"]}}," +
                "{\"name\":\"text-replace\",\"description\":\"替换文本——old 全部出现处替换为 new，返回替换数量；未找到报错（路径支持受控根 id 前缀或绝对路径）\",\"parameters\":{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"目标文件路径\"},\"old\":{\"type\":\"string\",\"description\":\"要查找的旧文本\"},\"new\":{\"type\":\"string\",\"description\":\"替换后的新文本\"}},\"required\":[\"path\",\"old\",\"new\"]}}," +
                "{\"name\":\"text-read_lines\",\"description\":\"按行号区间读取文本（start 起 / end 止，1 起；end 省略读至文件尾；编码自动探测）——大文件省 token\",\"parameters\":{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"文件路径\"},\"start\":{\"type\":\"integer\",\"description\":\"起始行（1 起，默认 1）\"},\"end\":{\"type\":\"integer\",\"description\":\"结束行（默认文件尾）\"}},\"required\":[\"path\"]}}," +
                "{\"name\":\"text-read_between\",\"description\":\"锚点区间读取——str1 与 str2 之间内容（str1 空=文件头 / str2 空=文件尾；锚点须全文唯一；编码自动探测）——大文件精确取段\",\"parameters\":{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"文件路径\"},\"str1\":{\"type\":\"string\",\"description\":\"起始锚点（空=文件头）\"},\"str2\":{\"type\":\"string\",\"description\":\"结束锚点（空=文件尾）\"}},\"required\":[\"path\"]}}," +
                "{\"name\":\"text-tree\",\"description\":\"目录树——列目录结构（depth 层级 / limit 条数上限；稳定排序）\",\"parameters\":{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"目录路径\"},\"depth\":{\"type\":\"integer\",\"description\":\"递归深度（默认 2，≤10）\"},\"limit\":{\"type\":\"integer\",\"description\":\"最大条数（默认 500）\"}},\"required\":[\"path\"]}}," +
                "{\"name\":\"text-find\",\"description\":\"文件名 glob 搜索——按文件名模式找文件（**/ 前缀=递归全部子目录；recursive 默认 true）\",\"parameters\":{\"type\":\"object\",\"properties\":{\"dir\":{\"type\":\"string\",\"description\":\"搜索根目录\"},\"pattern\":{\"type\":\"string\",\"description\":\"文件名模式（默认 *；**/ 前缀=递归全部子目录）\"},\"recursive\":{\"type\":\"boolean\",\"description\":\"是否递归（默认 true）\"},\"limit\":{\"type\":\"integer\",\"description\":\"最大结果（默认 500）\"}},\"required\":[\"dir\"]}}," +
                "{\"name\":\"text-grep\",\"description\":\"内容关键词搜索——目录内递归扫文本文件，返回 相对路径:行号:上下文（前后 ≤10 字符）——定位代码/文档关键词\",\"parameters\":{\"type\":\"object\",\"properties\":{\"dir\":{\"type\":\"string\",\"description\":\"搜索根目录\"},\"keyword\":{\"type\":\"string\",\"description\":\"搜索关键词（大小写敏感）\"},\"pattern\":{\"type\":\"string\",\"description\":\"文件名过滤（默认 *）\"},\"limit\":{\"type\":\"integer\",\"description\":\"最大结果（默认 200）\"}},\"required\":[\"dir\",\"keyword\"]}}," +
                "{\"name\":\"text-move\",\"description\":\"移动/重命名——文件与目录均支持（目录=整棵子树移动）；自动创建目标父目录；目标已存在拒绝\",\"parameters\":{\"type\":\"object\",\"properties\":{\"src\":{\"type\":\"string\",\"description\":\"源路径\"},\"dest\":{\"type\":\"string\",\"description\":\"目标路径\"}},\"required\":[\"src\",\"dest\"]}}," +
                "{\"name\":\"text-delete\",\"description\":\"软删除——移入受控回收站（可恢复）；支持文件与空目录\",\"parameters\":{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"要删除的文件/空目录路径\"}},\"required\":[\"path\"]}}" +
                "]}";
        }
    }
}
// #MAU_CHECKSUM:SHA256:960C8A8CE235664B871745339F9691AB984595F84394BC336939A2AC47F01F47

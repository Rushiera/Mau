# codestat —— 代码体量统计

一次扫描给出：文件数 / 总行 / 空行 / 注释 / 有效代码 + 注释率，按**扩展名**与**目录**两个维度聚合，产物为文本表。

## 用法

```
& tools\codestat\code_stat.ps1                          # 统计仓库根（默认 = 本脚本上两级），产物落 <根>\CatTemp\code_stat.txt
& tools\codestat\code_stat.ps1 -Root D:\other\repo      # 换统计根
& tools\codestat\code_stat.ps1 -Out .\stat.txt          # 换产物路径
& tools\codestat\code_stat.ps1 -Ext .cs,.mau            # 换扩展名集合
& tools\codestat\code_stat.ps1 -ExcludeDirs bin,obj     # 换排除目录集合（按路径段全匹配）
& tools\codestat\code_stat.ps1 -GroupDepth 1            # 换目录聚合深度（默认 2 级）
```

控制台回执两行：`DONE files=… total=… blank=… comment=… code=…` 与 `OUT=<产物路径>`。

## 参数

| 参数 | 默认 | 说明 |
|:--|:--|:--|
| `-Root` | 脚本上两级目录（仓库根） | 统计根，递归扫描 |
| `-Out` | `<Root>\CatTemp\code_stat.txt` | 产物路径（目录不存在则创建） |
| `-Ext` | `.cs .mau .mauproj .html .js .css .ts .ps1 .json` | 参与统计的扩展名 |
| `-ExcludeDirs` | `bin obj node_modules .git public Mau-public Data CatTemp MauOut .vs` | 排除目录（路径段全匹配，任意层级命中即排除） |
| `-GroupDepth` | `2` | 目录聚合深度（1 = 顶层，2 = 两级） |

## 口径

- 空行独立计数；**行首注释**计入注释行，**行内尾注释**随代码计入代码行；块注释跨行累计
- 注释语言：`.cs` / `.js` / `.ts`（`//` + 块注释）· `.css`（块注释）· `.html`（HTML 注释）· `.ps1`（`#` + 尖括号块注释）· `.mau` / `.mauproj`（`//`）；其余扩展名不识别注释
- 注释率 = 注释 /（注释 + 有效代码）——分母不含空行
- 脚本自身在统计面内（`tools/` 不被排除）

## 基线（2026-10-02）

413 文件 / 101,937 行 —— 空行 4,834 · 注释 21,435 · 有效代码 75,668 · 注释率 22.1%。

分层参考：Mau 基座（生产）25.0% · CH4 宿主（生产）20.8% · Bricks 31.7% · corpus 46.8% · 前端 html 11.4%（注释集中于契约与声明面）。

## 维护提示

脚本头部为块注释，**块注释内不得出现字面量块注释结束符**——写 `.ps1` 注释语法说明时需绕开该符号（本脚本已因此改写过一次：注释提前结束 → `param` 被当普通语句 → 全脚本解析失败）。

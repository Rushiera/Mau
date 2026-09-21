# Mau —— 会自己长大的系统

> 埃及语"猫"（mau/mjw）。古埃及太阳神 Ra 亦被称为"伟大的猫 Mau"。
> 仓库历史一直在——Mau 这个名字是象征。

---

## 一、项目简介

**我们在造一个 AI 用自己的语言、写自己的宿主、自己的工具、自己的家。**

不是"让 AI 帮人写代码"——是让 AI 从零搭建一套自己能持续进化的运行时：

```
LLM 写 .mau 语料 → 门禁验真（词法/解析/验证/E2xx 引用/E3xx 图论/E4xx 积木——失败拒绝生成）
  → 翻译 C# → 编译 dll → 热重载加载（宿主不重启）→ 观测（帧号可溯）→ 迭代
```

**自举循环：** 业务代码 100% 由语料表达——改语料、重编译、即生效。AI 自己闭环自己。

**哲学：** LLM 负责表达，确定性代码负责验真。语言为 LLM 输出稳定性设计（符号化 + 白名单词法）；分析门禁（图论验证）填补 LLM 的逻辑盲区——表达自由，验证严格。

**定位：** 私人方言，只服务一个消费者——从不为陌生用户建防护，不为未来产品留接口。不考虑市场与通用性，只把自己的家修好。

**双区块（同仓推进）：**

| 区块 | 是什么 | 位置 |
|:--|:--|:--|
| **Mau 基座** | 私人方言语言内核 + 数字电路运行时 + 门禁工具链 + 积木原子能力 | `Mau/`（Mau.sln） |
| **CH4 宿主** | 自举循环宿主——agent 循环基建走 C#，业务 100% 语料化热重载 | `CatHome4/`（CatHome4.sln）· `corpus/ch4/` |

**版本线：** 基座 `v3.x` · 宿主 `v1.x`——同仓推进。版本与规格的唯一真相源在 CCBP 知识库（`Project/Mau/`、`Project/CH4/`）；本仓库只承载代码与语料。

---

## 二、部署与运行

### 2.1 前置

- Windows + .NET 8 SDK（`dotnet --version` ≥ 8.0——部署全程需要 SDK，运行需要 Runtime）
- 取仓库：

```
git clone git@gitee.com:wu_lisha/mau.git
cd mau
```

### 2.2 图形界面部署（推荐——首次使用走这条）

1. **双击仓库根目录下的 `SetUp.exe`** —— 合法位置即 Mau 仓库根（同目录需能见到 `Mau.sln`，否则会拒绝运行）。
2. 界面先做环境检测（.NET 8 Runtime / SDK）。点 **prepare** —— 一键重建全发布链：构建 → 测试 → 基座部署区 → 9 组语料编译 → 宿主发布。全程有日志，等它跑完。
3. 点 **deploy**，把运行实体发布到 **mau 仓库目录之外**的目标目录 —— 例如 `C:\MauTest\MauOut`。
   🔴 **必须部署到仓库之外**：部署目录是正式运行实体，与仓库解耦；此后重建/删除仓库都不影响它。
4. 部署完成后，**部署目录根下的 `CatHome4.exe` 就是程序入口** —— 双击即启动。

命令行等价（与图形界面同链，二选一）：

```
SetUp.exe prepare                     ← 重建全发布链
SetUp.exe deploy C:\MauTest\MauOut    ← 发布正式运行实体到仓库外
```

| 模式 | 做什么 | 产物 |
|:--|:--|:--|
| **prepare** | 重建全发布链——构建/测试/基座部署区/语料编译/宿主发布 | 仓库内 `public/` + `Mau-public/`（就地自举） |
| **deploy** | 把可运行版本发布到外部目标目录 | 仓库外的正式运行实体 |

### 2.3 首次运行

1. 启动部署目录下的 **`CatHome4.exe`** —— 宿主自动加载语料 Flows（9 个工具组）并开启 HTTP 外观层。
2. 浏览器打开 **http://127.0.0.1:8080** —— 主面板（部署区主端口；开发区实例为 8079）。对话页按猫走独立端口（端口段内分配）。
3. 首次启动会自动生成配置与数据目录；LLM 端点与密钥在面板或数据目录中填写。

**运行实体的数据目录（部署实例持久区）：**

```
%LOCALAPPDATA%\CatHome4\Data
```

| 子目录 | 主要内容 |
|:--|:--|
| `config/` | 配置群 —— `app.cfg`（全局）、`cat-default.cfg`（每猫默认）、`llm.cfg` / `llm-api.json`（LLM）、`ui.cfg`、`search.cfg`、`vision.cfg`、`qqbot.json`、`workspace.json`（受控根表） |
| `secrets/` | 密钥 —— `llm-api.cfg`（LLM 端点与密钥）、`qqbot.cfg` |
| `sessions/` | 每猫一个目录 —— 会话状态 `<会话号>.json`、视图 `<会话号>.view.json`、该猫配置 `cat.cfg` |
| `runs/` | 每次运行一个目录 —— `log_all.txt` / `oa_all.txt` / `err_all.txt` / `frame.txt`（帧号可溯的观测面；**帧体无变化只落帧号行**——缺席字段沿用上一完整帧） |
| `qqbot-files/` | QQ 通道收发的文件缓存 |

**Data 三级锚定：** `CH4_DATA_ROOT` 环境变量（显式覆盖）→ 仓库根 `Data/`（就地开发与跑测的隔离区）→ `%LOCALAPPDATA%\CatHome4\Data`（部署实例的持久区）。

🔴 正式实例的数据恒在第三级 —— **重建或删除仓库都不会丢**；仓库根那一级只服务就地跑测，可随时清空。

---

### 2.4 手动部署链（等价明细 —— 顺序不可跳）

```
1. dotnet build Mau.sln
2. dotnet test Mau.sln
3. dotnet publish Mau\Mau.Cli -c Debug -o Mau-public
4. mau proj corpus\ch4\<Flow>\<组>.mauproj -o public\src\<Flow> --build   （×9——见下方清单）
5. dotnet publish CatHome4 -c Debug -o public\app
6. public\app\CatHome4.exe --run "session count"
```

| 步 | 做什么 | 验收 |
|:--|:--|:--|
| 1 | 构建 + NuGet 还原（唯一真相源 = git） | 0 错误 0 警告 |
| 2 | 全量测试 | 全绿（`mau test` → `MAU_CHECKS_OK` 为门禁底线） |
| 3 | **基座部署区** —— 组构建的引用源（缺此步 `mau proj` 报 M3245 找不到 Mau.Runtime） | `Mau-public/Mau.Runtime.dll` 存在 |
| 4 | 语料 → 组翻译 → 编译 dll（9 个工具组 Flow） | `public/app/Flows/FL_<组>.dll` 存在 |
| 5 | 宿主部署 | `public/app/CatHome4.exe` 存在 |
| 6 | 宿主自检 —— CLI 全链（主线程直执 + 进程自退） | 各 Cat 注册 + 会话消息数正常 |

**步骤 4 的 9 个 mauproj（`corpus/ch4` 每个 Flow 一个）：**

```
mau proj corpus\ch4\QuickCat\quick_cat.mauproj        -o public\src\QuickCat --build
mau proj corpus\ch4\TextCat\text_cat.mauproj          -o public\src\TextCat --build
mau proj corpus\ch4\MauCat\mau_cat.mauproj            -o public\src\MauCat --build
mau proj corpus\ch4\CsCat\cs_cat.mauproj              -o public\src\CsCat --build
mau proj corpus\ch4\ConfigCat\config_cat.mauproj      -o public\src\ConfigCat --build
mau proj corpus\ch4\SearchCat\search_cat.mauproj      -o public\src\SearchCat --build
mau proj corpus\ch4\VisionCat\vision_cat.mauproj      -o public\src\VisionCat --build
mau proj corpus\ch4\TempToolCat\temp_tool_cat.mauproj -o public\src\TempToolCat --build
mau proj corpus\ch4\PsCat\ps_cat.mauproj              -o public\src\PsCat --build
```

> 步骤 3-4 是硬约束：改基座源码后必须重跑 3-4（`Mau-public/` 是编译与运行时的同源点）。语料层随时可重建（`public/app/Flows/*.dll` 运行中热重载、不锁文件）；宿主自身 publish 前先停进程。

**SetUp 自身更新（🔴 改动 `SetUp/` 源码后必做）：** `SetUp.exe` 是发布产物、不是源码 —— git 提交的是构建出的单文件 exe。改源码后必须重新发布并同次提交：

```
dotnet publish SetUp\SetUp.csproj -c Release -o <临时目录>   ← 1. 重新发布
复制 SetUp.exe 覆盖仓库根（旧版先移 CatTemp 备份）           ← 2. 替换
SetUp.exe prepare                                           ← 3. 全链验证（步 4 组扫描全收录）
git add SetUp/ SetUp.exe && commit && push                  ← 4. 源码 + exe 同次提交
```

---

### 2.5 常用指令（工作目录 = 仓库根）

**mau CLI —— 构筑与门禁：**

| 指令 | 用途 |
|:--|:--|
| `mau verify <file.mau>` | 全链检查（词法→解析→验证→分析，不产出） |
| `mau gen <file.mau> -o <dir>` | 全链编译 + C# 生成物 |
| `mau proj <组.mauproj> -o <srcDir> [--build]` | 统一构筑链 —— 组翻译落盘；`--build` 走 dotnet build → `Flows/FL_<组>.dll` |
| `mau build <组.mauproj>` | 统一链路由（转发 `mau proj --build`） |
| `mau test` | 三段门禁 —— L2 翻译器+Runtime / L3 黄金哈希 / L4 积木谱 → `MAU_CHECKS_OK` |
| `mau check` | 语法谱 + 负例谱 + 关键路径报告 → `MAU_CHECK_OK` |
| `mau debug <file.mau> [--ticks N] [--step]` | 四柱状态表 + 单步 + 状态断点 |
| `mau bricks list / index --update / index --verify` | 积木枚举 / 契约提取重算索引（唯一写入通道）/ 索引校验 |

**CLI 参数纪律：** 未知参数、缺值、非法值一律报错退出（不静默忽略、不回落默认值）—— 入口接收面与用法声明必须一致。

**dotnet —— 标准入口：**

| 指令 | 用途 |
|:--|:--|
| `dotnet build Mau.sln` | 基座全量构建 —— 0 错误 0 警告是提交底线 |
| `dotnet test Mau.sln` | 基座测试入口（三测试项目全量） |
| `dotnet build CatHome4.sln` | 宿主全量构建 —— 同上底线 |
| `dotnet test CatHome4.sln` | 宿主测试入口 |

**宿主（CatHome4）：**

| 指令 | 用途 |
|:--|:--|
| `CatHome4.exe`（无参） | 程序入口 —— 扫描 `Flows/` 加载语料 + HTTP 外观层（端口按区段：开发区 `http://127.0.0.1:8079` / 部署区 `http://127.0.0.1:8080`） |
| `CatHome4.exe --run "<指令>"` | CLI 全链 —— 主线程直执指令 + 进程自退（脚本化跑测通道；**非交互模式不启外观层与常驻附属——端口零占用**，与常驻实例并存） |

**git 约定：**

| 约定 | 内容 |
|:--|:--|
| 提交信息 | `vX.Y: 变更摘要`（≤30 字，动词开头；一次提交一个主题） |
| 双版本线 | 基座 `v3.x` 与宿主 `v1.x` 同仓推进；历史唯一真相 = CCBP 双 CHANGELOG |
| SetUp.exe | 跟随 git 提交的单文件发布产物 —— 改源码必须重发同次提交（见 2.4） |

**规格权威：** CCBP 知识库 `Project/Mau/`（基座设计）与 `Project/CH4/`（宿主设计、部署架构、发布规范）。

---

## 许可证

MIT License · 详见 [LICENSE](./LICENSE)

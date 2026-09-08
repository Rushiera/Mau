# Mau —— 会自己长大的系统

> 埃及语"猫"（mau/mjw）。古埃及太阳神 Ra 亦被称为"伟大的猫 Mau"。
> 仓库历史一直在——Mau 这个名字是象征。

---

## 一、立意与目标

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
| **Mau 基座** | 私人方言语言内核 + 数字电路运行时（Tick/Inbox/OA/CommandBus/DataBox/ALC）+ 门禁工具链 + 积木原子能力 | `Mau/`（Mau.sln 9 项目） |
| **CH4 宿主** | 自举循环宿主——agent 循环基建走 C#，业务 100% 语料化热重载；S1-S8 拆分后按域多程序集 | `CatHome4/`（CatHome4.sln 8 项目）· `corpus/ch4/` |

**现状：** 自举循环真实运转——LLM 经宿主工具（text-* 文件操作 / mau-* 语料自查 / cs-* Roslyn 编码 / config-* 配置自改 / web-search 联网 / image-analyze 识图 / temp-* 万能接口 / powershell 执行）完成"发现问题 → 修语料 → 门禁通过"闭环。多猫并发（每猫独立会话/端口/配置）+ 工具组 Flow 化（ToolRegistry 动态注册/热重载/退役）+ qqbot 接入（QQ Bot 池 + 双向桥接 + 强匹配指令）已落地；可用性三件（工具组事实压测 / LLM 重试与断点续传 / 坏前文最小化修复）+ D 部署（SetUp 启动器 / Data 三级锚定 / 纯净部署验收）已完成。v0.75 起工具面全链：PsCat PowerShell（免转义/UTF-8 内建/写拦截）+ Flow 装配扫描化（新增 Flow 零代码）+ 工具定义禀赋化（统一工具池）+ 配置作用域（猫级 FS/根 id 不敏感）+ 会话视图与前文真实 usage + P6a 中止（cat.pause）+ P6b 会话回滚/分支（session.rollback/fork）+ P1 托盘自启 + P8/P9 QQBot 消息/文件通道（MD 切分 + msg_id 被动回复 + [文件:path] 内嵌标记）+ SetUp --report 确定性部署 + 前端三改（reason 流式展开/roundsum 命中率/工具并发编号）。版本与里程碑唯一真相源：CCBP `Project/CH4/CHANGELOG.md` 与 `Project/Mau/CHANGELOG.md`；规格-实现总账与设计路由：CCBP `Project/CH4/design-status.md`。

---

## 二、仓库结构（现行——v0.88.8，文档治理轮 2026-09-08）

```
mau/
├── Mau.sln / CatHome4.sln     双解决方案——基座与宿主各自独立构建
├── SetUp/ + SetUp.exe         D1 启动器（仓库根单文件——prepare 重建发布链 / deploy 产出正式实例 / 无参=WinForms UI）
├── Mau/                       基座 9 项目（Mau.sln）
│   ├── Mau.Contracts          内核契约（TokenIds/ISymbolAppearance/BrickContract）
│   ├── Mau.Runtime            数字电路机制（Tick/Cube/Inbox/OA/CommandBus/DataBox/FlowALC/FlowHandle/ThreadGuard/LogStore/FileSystemService）
│   ├── Mau.Translator         语料翻译器（词法→解析→验证→分析→生成）
│   ├── Mau.Cli                mau 命令行（verify/gen/proj/test/check/debug/bricks）
│   ├── Mau.Development        Roslyn 开发工具（口袋编译/契约提取/组翻译共享服务/MauRoslynBridge）
│   ├── Mau.Providers          LLM 供应商实现（DeepSeek 系列——LLM/WebSearch/Vision）
│   └── Mau.*.Tests            三测试项目（Runtime/Translator/Development）
├── CatHome4/                  宿主 8 项目（CatHome4.sln）
│   ├── CatHome4              入口壳（Program.*.cs——纯 composition root 组装）
│   ├── CatHome4.Contracts    域接口（IHostPush/IHttpRouteSink/IHtmlRootProvider/IQqTargetCollector）
│   ├── CatHome4.Core         会话协调（ChatSession×3/ToolRegistry/工具批收敛/Note）
│   ├── CatHome4.Http         Kestrel 外观层（快照/SSE/指令/配置/历史通道）
│   ├── CatHome4.QQ           QQ 管理器（WS 协议资产：鉴权/心跳 ACK/Resume/重连/输入路由/输出转发）
│   ├── CatHome4.Admin        管理 API 处理器 + cat.* 指令族
│   ├── CatHome4.Observe      观测面（快照/帧流/审计）
│   └── CatHome4.Core.Tests   Core 测试（相位环/工具批收敛/Note 全链）
├── corpus/ch4/                业务语料——每工具组一个 Flow 目录（.mau + .mauproj）
│   ├── QuickCat/              问答消费者（主 Cat）
│   ├── TextCat/ MauCat/ CsCat/ ConfigCat/   工具组 Flow（text-*/mau-*/cs-*/config-*）
│   ├── SearchCat/ VisionCat/ 工具组 Flow（web-search/image-analyze）
│   └── TempToolCat/           万能接口 Flow（temp-info/temp-exec——改积木即换临时工具）
├── Bricks/                    积木文本资产库（index.json 机器索引唯一真相源）
├── Mau-public/                基座部署区（publish 平铺——FL csproj 引用源铁律）
├── public/                    宿主部署区（src=翻译中间产物 / app=CatHome4.exe 平铺 + Flows/）
├── config/                    配置模板（*.template / *.example——真实配置本地持有）
└── docs/                      设计审计（P7_AUDIT.md 等）
```

**架构分层：**

| 层 | 形态 | 变更方式 |
|:--|:--|:--|
| Runtime 层 | Mau.Runtime 固定 C#——数字电路机制 | 编译期变更 |
| 业务层 | .mau 语料 → 翻译 C# → 编译 dll——全部功能 | **热重载**（FlowHandle/FlowALC——改语料重编译即生效，宿主不重启） |
| 工具组层 | 每工具组独立 .mau Flow——独立注册/热重载/退役；ToolRegistry 动态注册面（内置/OA 双轨） | **热重载**（改单组不动全局） |

**当前能力（工具面 38 件——33 OA + 5 内置，ToolRegistry 单一真相源）：**

| 域 | 工具 | 执行器 |
|:--|:--|:--|
| 文本（11） | text-read / text-write / text-append / text-replace / text-read_lines / text-read_between / text-tree / text-find / text-grep / text-move / text-delete | TextCat Flow → FileSystemService（受控根 + 锚点三态 + 编码内建 + 软删除） |
| Mau 自查（3） | mau-verify / mau-gen / mau-proj | MauCat Flow → MauCompilerV3 进程内直调 + MauProjFile 组翻译 |
| 编码（10） | cs-check / cs-build / cs-list / cs-read / cs-find_ref / cs-patch / cs-member / cs-comment / cs-dead / cs-comment_check | CsCat Flow → ICSharpBridge + MauRoslynBridge（磁盘权威 + 项目键缓存池） |
| 配置（4） | config-list / config-get / config-set / config-reset | ConfigCat Flow → ConfigStore SetChecked/ResetToDefault |
| 联网（1） | web-search | SearchCat Flow → IWebSearchService |
| 识图（1） | image-analyze | VisionCat Flow → IVisionService |
| 万能接口（2） | temp-info / temp-exec | TempToolCat Flow → TempRegistry（LLM 可改区——改积木即换临时工具） |
| PowerShell（1） | powershell | PsCat Flow → ps.exec 积木 → IPsService（EncodedCommand 免转义/UTF-8 内建/写文件拦截/超时杀树） |
| 内置（5） | host-reload / time / random / info / Note | 会话内/宿主直执 |

**多猫并发（P9）：** 单进程多会话——每猫独立 ChatSession + 端口级路由 + cat.cfg 独立配置（persona / toolNames / injectList / apiConfigId / qqbotId）；默认猫 majordomo + cat.* 指令族管理（cat.new / start / stop / delete / chat）。

**工具组 Flow 化（R0.2）：** 每工具组独立 .mau Flow（TextCat / MauCat / CsCat / ConfigCat / SearchCat / VisionCat / TempToolCat / PsCat）——独立注册 / 热重载 / 退役；ToolRegistry 动态注册面（内置 / OA 双轨）。

**qqbot 接入（R2.3——附属功能组件 / 全局插件，非 toolcall）：**

- **QQ Bot 配置池**——对齐 LLM API 池（qqbot.json 明文零 secret + qqbot.cfg secrets + CRUD 端点 + 前端管理页）
- **每猫配置**——cat.cfg qqbotId / qqbotEnable（多 Cat 可绑同一 Bot，多对一）
- **QQ 管理器**——注册即建 WS 连接（CH1 协议资产：鉴权 / 心跳 ACK / Resume / 重连）；跟随宿主启动
- **输入路由**——消息广播注入所有绑定且启用的 Cat（`[来自QQ]` 前缀）；未绑定返回「无猫」/ 未启用返回「目标 Cat 未启用 qqbot 转发功能」
- **输出转发**——游标增量轮询，回复块带 `Cat名：` 前缀转发（无条件转发——前端对话回复也同步）；失败 L2 留痕
- **强匹配指令**——`/ping` `/info` 等 / 开头指令代码直执（不走 LLM），返回 QQ 管理器服务状态
- **LLM 与 Talk 无感**——输入前缀 LLM 可见；输出由管理器主动拉取，ChatSession 零改动

---

## 三、第一次部署（从空仓库）+ 常用指令

### 3.1 前置

- Windows + .NET 8 Runtime/SDK（`dotnet --version` ≥ 8.0）
- 取仓库：

```
git clone git@gitee.com:wu_lisha/mau.git
cd mau
```

### 3.2 部署（D1 SetUp 启动器——推荐）

仓库根 `SetUp.exe` 单文件跟随 git——clone 即用。运行前置检测：.NET 8 Runtime/SDK + 同目录找到 `Mau.sln`（合法位置 = 仓库根）。

```
SetUp.exe prepare            ← 一键重建全发布链（build Mau.sln → test → publish Mau-public → mau proj ×9 → publish public\app）
SetUp.exe deploy <目标目录>   ← 产出正式运行实例（外部目录；Data 走 %LOCALAPPDATA%/CatHome4/Data）
SetUp.exe                    ← 无参 = WinForms UI（环境检测 + 全流程日志 + 两种模式按钮）
```

| 模式 | 做什么 | 目标 |
|:--|:--|:--|
| **prepare** | 重建全发布链——构建/测试/部署区/语料编译/宿主发布 | 配置好 `public/` + `Mau-public/`（就地自举） |
| **deploy** | 把可运行版本发布到外部目标目录 | 产出正式运行实例（就地运行可跳过） |

**Data 三级锚定：** `CH4_DATA_ROOT` 环境变量（显式覆盖）→ 仓库根 `Data/`（就地测试隔离）→ `%LOCALAPPDATA%/CatHome4/Data`（部署实例持久）。开发跑仓库用仓库 Data；正式实例持久数据永不随删库丢失。

**手动链（等价明细——唯一次序，顺序不可跳）：**

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
| 3 | **基座部署区**——组构建的引用源（无此步 mau proj 报 M3245 找不到 Mau.Runtime） | `Mau-public/Mau.Runtime.dll` 存在 |
| 4 | 语料 → 组翻译 → 编译 dll（9 个工具组 Flow） | `public/app/Flows/FL_<组>.dll` 存在 |
| 5 | 宿主部署 | `public/app/CatHome4.exe` 存在 |
| 6 | 宿主自检——CLI 全链（主线程直执 + 进程自退） | 各 Cat 注册 + 会话消息数正常 |

**步骤 4 的 9 个 mauproj（corpus/ch4 每个 Flow 一个）：**

```
mau proj corpus\ch4\QuickCat\quick_cat.mauproj      -o public\src\QuickCat --build
mau proj corpus\ch4\TextCat\text_cat.mauproj        -o public\src\TextCat --build
mau proj corpus\ch4\MauCat\mau_cat.mauproj          -o public\src\MauCat --build
mau proj corpus\ch4\CsCat\cs_cat.mauproj            -o public\src\CsCat --build
mau proj corpus\ch4\ConfigCat\config_cat.mauproj    -o public\src\ConfigCat --build
mau proj corpus\ch4\SearchCat\search_cat.mauproj    -o public\src\SearchCat --build
mau proj corpus\ch4\VisionCat\vision_cat.mauproj    -o public\src\VisionCat --build
mau proj corpus\ch4\TempToolCat\temp_tool_cat.mauproj -o public\src\TempToolCat --build
mau proj corpus\ch4\PsCat\ps_cat.mauproj            -o public\src\PsCat --build
```

> 步骤 3-4 的顺序是硬约束：改基座源码后必须重跑 3-4（`Mau-public/` 是编译/运行时同源点）。语料层随时可重建（`public/app/Flows/*.dll` 运行中热重载不锁文件）；宿主自身 publish 前先停进程。规格权威：CCBP `Project/CH4/design-ch4-deploy.md`（部署架构）+ `design-ch4-release.md`（发布规范）。

**SetUp 自身更新（🔴 SetUp/ 源码变更后必做）：** SetUp.exe 是发布产物，不是源码——git 里提交的是构建出的单文件 exe。改 SetUp/ 源码后必须重新发布并同次提交，否则删库重拉会拿到旧二进制（判例：2026-09-03 PsCat 漏网——v0.77.0 源码扫描化但 exe 未重发，prepare 只识别 8 组）：

```
dotnet publish SetUp\SetUp.csproj -c Release -o <临时目录>   ← 1. 重新发布
复制 SetUp.exe 覆盖仓库根（旧版先移 CatTemp 备份）           ← 2. 替换
SetUp.exe prepare                                            ← 3. 全链验证（步4 组扫描全收录）
git add SetUp/ SetUp.exe && commit && push                   ← 4. 源码+exe 同次提交
```

### 3.3 常用指令（工作目录 = 仓库根）

**mau CLI（构筑与门禁）：**

| 指令 | 用途 |
|:--|:--|
| `mau verify <file.mau>` | 全链检查（词法→解析→验证→分析，不产出） |
| `mau gen <file.mau> -o <dir>` | 全链编译 + C# 生成物 |
| `mau proj <组.mauproj> -o <srcDir> [--build]` | 统一构筑链——组翻译落盘；--build 走 dotnet build → Flows/FL_&lt;组&gt;.dll |
| `mau build <组.mauproj>` | 统一链路由（转发 mau proj --build） |
| `mau test [--update]` | 三段门禁——[1/3] L2 翻译器+Runtime [2/3] L3 黄金哈希 [3/3] L4 积木谱 → `MAU_CHECKS_OK` |
| `mau check` | 语法谱 5/5 + 负例谱 10/10 + 关键路径报告 → `MAU_CHECK_OK` |
| `mau debug <file.mau> [--ticks N] [--step] [--pause-on S_X=Y]` | 四柱状态表 + 单步 + 状态断点 |
| `mau bricks list / index --update / index --verify` | 积木枚举 / 契约提取重算索引（唯一写入通道）/ 索引校验 |

**dotnet（标准入口）：**

| 指令 | 用途 |
|:--|:--|
| `dotnet build Mau.sln` | 基座全量构建——0/0 是提交底线 |
| `dotnet test Mau.sln` | 基座测试入口（三测试项目全量） |
| `dotnet build CatHome4.sln` | 宿主全量构建——0/0 是提交底线 |
| `dotnet test CatHome4.sln` | 宿主测试入口（CatHome4.Core.Tests） |

**宿主（CatHome4）：**

| 指令 | 用途 |
|:--|:--|
| `CatHome4.exe`（无参） | 程序入口——扫描 Flows/ 加载语料 + HTTP 外观层 http://127.0.0.1:8080 |
| `CatHome4.exe --run "<指令>"` | CLI 全链——主线程直执指令 + 进程自退（脚本化跑测通道） |

**git：**

| 约定 | 内容 |
|:--|:--|
| 提交信息 | `vX.Y: 变更摘要`（≤30 字，动词开头；一次提交一个主题） |
| 双版本线 | 基座 `v3.x` 与业务 `v0.xx` 同仓推进；历史唯一真相 = CCBP 双 CHANGELOG |
| 黄金文件 | `Mau.Snapshots/golden-sha-v3.txt` 变更必须在 commit 信息中说明原因 |

---

## 许可证

MIT License · 详见 [LICENSE](./LICENSE)

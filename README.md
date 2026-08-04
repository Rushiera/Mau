# Mau

> 埃及语"猫"（mau/mjw）。古埃及太阳神 Ra 亦被称为"伟大的猫 Mau"。

**Mau** 是一门架在 .NET 8 之上的声明式方言环境——把程序逻辑的定义权从框架转移到语句，由约定协议规定程序最顶层的设计和时序关系。

---

## 核心理念

```
公式层（.mau 声明）— 系统是什么
    ↓ 构筑：解析 → IR → 静态验证
翻译层（C#）— 解析器 → 验证 → 代码生成
    ↓ 生成 C# 源码
积木层（C#）— 语义原语（叶子黑盒）
    ↓ Roslyn Emit
运行时（CH 内核）— Tick / ALC / OA / 快照
```

- **公式层是宪法，C# 是执行者** —— 系统结构真相全部由 Mau 承载
- **翻译层归我们所有** —— 翻译器和积木用 C# 实现，可重写可移植
- **只在基座下有效** —— 语义精确到函数级

---

## 五逻辑单元

| 单元 | 含义 |
|:--|:--|
| 命题 Proposition | "什么成立"——状态：条件/信号/事实 |
| 变迁 Transition | "什么触发什么"——前置→动作→双后置 |
| 通道 Channel | "什么流向什么"——跨线程/跨进程 |
| 组合 Composition | "什么与什么并列/串/选择/重试" |
| 资源 Resource | "什么被消耗/独占"——令牌/引用/配额 |

---

## 工程结构

| 项目 | 定位 |
|:--|:--|
| `Mau.Runtime` | 基座——Cube/IFlow/Inbox/IClock/MauTrace |
| `Mau.Contracts` | 积木契约——注册表/端口/契约条目 |
| `Mau.Translator` | 翻译器——解析→IR→静态验证→代码生成 |
| `Mau.Bricks.Standard` | 标准积木——file.convert 等 |
| `Mau.Cli` | 命令行——verify/gen/test |
| `Mau.Host` | 入口壳——引导/组装/启动 |
| `Mau.*.Tests` | 测试——翻译器/运行时/积木/契约/E2E |
| `Mau.Snapshots` | 黄金文件——语料+预期生成物 |

---

## 快速开始

```bash
# 构筑
dotnet build Mau.sln

# 运行门禁
dotnet run --project Mau.Cli test

# 翻译 .mau 文件
dotnet run --project Mau.Cli gen --input cases/demo.mau
```

---

## 许可证

MIT License · 详见 [LICENSE](./LICENSE)

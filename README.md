# Files

C# (.NET 10 / WPF) 桌面文件管理器。

## 文档

| 文档 | 内容 |
| --- | --- |
| [docs/BUILD.md](docs/BUILD.md) | **构建说明**：前置条件、一键构建、参数、产物、常见问题 |
| [docs/design/README.md](docs/design/README.md) | **功能设计索引**：现状与缺口、下一步实施顺序 |
| [docs/PRD.md](docs/PRD.md) | 产品需求：定位、多栏+多标签模型、Win7 功能覆盖矩阵、性能预算、架构约束、风险 |
| [docs/DEV-PLAN.md](docs/DEV-PLAN.md) | 开发计划：M1–M5 任务分解、估时、退出判据、测试矩阵、阻塞项 |
| [docs/PERF-BASELINE.md](docs/PERF-BASELINE.md) | 性能基线实测：5 万/10 万项目录枚举数据与结论 |
| [docs/DEPLOY.md](docs/DEPLOY.md) | 分发与运行：数据位置、SmartScreen、权限要求 |
| [docs/USER-TEST.md](docs/USER-TEST.md) | 用户测试清单 |
| [docs/adr/ADR-001](docs/adr/ADR-001-runtime-and-ui-framework.md) | 运行时与 UI 框架选型（含 Win7 兼容性事实核查）— 已接受 |

### 功能设计（详细规格，未实现）

| 文档 | 内容 |
| --- | --- |
| [SETTINGS.md](docs/design/SETTINGS.md) | 设置体系：30+ 项清单、设置窗口交互、单实例、schema 迁移 |
| [NAVIGATION-AND-PANES.md](docs/design/NAVIGATION-AND-PANES.md) | 导航窗格（树）、详情窗格、预览窗格 |
| [SEARCH.md](docs/design/SEARCH.md) | 筛选（AQS 子集）与递归搜索 |

## 解决方案结构

| 路径 | 说明 |
| --- | --- |
| `src/Files.Core` | 与 UI、与平台都无关的核心逻辑（模型、路径、诊断、布局） |
| `src/Files.Interop` | Win32 / Shell 互操作（`FindFirstFileExW` 目录枚举等） |
| `src/Files.App` | WPF 桌面应用 |
| `tests/Files.Core.Tests` | MSTest 单元测试 |
| `bench/Files.Bench` | 目录枚举性能基准台 |

## 环境要求

- .NET SDK 10.0.100（见 `global.json`，允许向前滚动到最新功能带）
- Windows 10 22H2+ / Windows 11，**仅 x64**（WPF 目标为 `net10.0-windows`）

## 常用命令

```powershell
# 一键构建（推荐）：还原 + 构建 + 测试 + 发布到 artifacts/
.\scripts\build.ps1

# 双击构建（不熟悉命令行时）
scripts\build.cmd

# 只跑测试
.\scripts\test.ps1

# 性能基准（10 万项目录枚举，含预算判定）
.\scripts\bench.ps1 -Count 100000 -Gate

# 启动应用并显示性能 HUD（FPS / 最差帧 / 内存）
dotnet run --project src/Files.App -- --perf
```

构建细节与常见问题见 [docs/BUILD.md](docs/BUILD.md)。

## 构建说明

- 依赖包缓存在仓库内的 `.dotnet/packages`，不污染用户的全局 NuGet 缓存。
- 测试项目启用了 Microsoft.Testing.Platform 自包含运行器（`EnableMSTestRunner`），
  因此可以直接执行测试可执行文件，无需 VSTest 外部宿主。
- 构建统一使用单节点 + 禁用构建服务器（`-m:1 --disable-build-servers`）：
  受限环境下多节点与编译服务器会因进程间通信失败而报错。项目规模小，速度差异可忽略。

## 许可

待定。

# Files

C# (.NET 10 / WPF) 桌面文件管理器。

## 文档

| 文档 | 内容 |
| --- | --- |
| [docs/PRD.md](docs/PRD.md) | 产品需求：定位、多栏模型、Win7 功能覆盖矩阵、性能预算、架构约束、风险 |
| [docs/DEV-PLAN.md](docs/DEV-PLAN.md) | 开发计划：M1–M5 任务分解、估时、退出判据、测试矩阵、阻塞项 |
| [docs/adr/ADR-001](docs/adr/ADR-001-runtime-and-ui-framework.md) | 运行时与 UI 框架选型（含 Win7 兼容性事实核查）— **待签字** |

## 解决方案结构

| 路径 | 说明 |
| --- | --- |
| `src/Files.Core` | 与 UI 无关的核心逻辑（文件系统抽象、模型） |
| `src/Files.App` | WPF 桌面应用 |
| `tests/Files.Core.Tests` | MSTest 单元测试 |

## 环境要求

- .NET SDK 10.0.100（见 `global.json`，允许向前滚动到最新功能带）
- Windows（WPF 目标为 `net10.0-windows`）

## 常用命令

```powershell
# 还原依赖（首次需要联网）
dotnet restore Files.slnx

# 构建
dotnet build Files.slnx

# 运行测试（本仓库使用自包含测试运行器）
.\scripts\test.ps1
```

## 构建说明

- 依赖包缓存在仓库内的 `.dotnet/packages`，不污染用户的全局 NuGet 缓存。
- 测试项目启用了 Microsoft.Testing.Platform 自包含运行器（`EnableMSTestRunner`），
  因此可以直接执行测试可执行文件，无需 VSTest 外部宿主。
- 在受限/沙箱化环境中构建时，MSBuild 的多节点模式与编译服务器可能因进程间通信受限而失败，
  此时可用 `dotnet build Files.slnx -m:1 --disable-build-servers`。

## 许可

待定。

# ADR-001：运行时与 UI 框架选型

- 状态：待确认（Blocking，需产品负责人签字）
- 日期：2026-10-21
- 决策编号：D1

---

## 1. 背景

需求原文：

> 本应用计划提供 Windows 7 资源管理器的功能，并支持左右分栏 / 上下分栏 / 分四栏。希望有最高的 UI 性能和运行效率。

这句话存在两种读法，且两种读法导向**完全不同**的技术栈：

| 读法 | 含义 | 后果 |
| --- | --- | --- |
| **A. 功能复刻** | 做一个功能上对标 Win7 资源管理器的现代文件管理器（面包屑、库、详情窗格、Ribbon、搜索、复制冲突对话框…），运行在 Windows 10/11 上 | 可选任何现代技术栈 |
| **B. 平台兼容** | 应用必须能安装并运行在 Windows 7 SP1 上 | 技术栈被彻底锁死，且与"最高性能"部分冲突 |

本文档同时评估，并给出推荐。

---

## 2. 硬约束（已核实的事实）

### 2.1 Microsoft 官方支持矩阵

来自 [.NET Windows 安装与支持文档](https://learn.microsoft.com/en-us/dotnet/core/install/windows)（页面更新于 2026-06-02）：

| 操作系统 | .NET 10 | .NET 9 | .NET 8 |
| --- | --- | --- | --- |
| Windows 11 | ✔️ x64/x86/Arm64 | ✔️ | ✔️ |
| Windows 10（LTSC / Enterprise 指定版本） | ✔️ | ✔️ | ✔️ |
| Windows 8.1 | ❌ | ❌ | ❌ |
| **Windows 7 SP1** | **❌** | **❌** | **❌** |

文档原文：**"There is no longer a version of .NET that's supported on Windows 7 and Windows 8.1. The last supported releases was .NET 6 and support ended on November 12, 2024."**

即：现代 .NET 路线在 Win7 上**没有任何受支持的版本**。最后一个可能的选择是已 EOL 的 .NET 6，或 **.NET Framework 4.8**（Win7 SP1 仍在 4.8 的官方支持列表内）。

### 2.2 Win7 会连带取消的能力

若选读法 B，以下能力必须放弃或降级：

| 能力 | Win7 SP1 状况 |
| --- | --- |
| Per-Monitor V2 DPI 感知 | ❌ 需要 Win10 1703+。Win7 只有系统级 DPI，4K 屏上分栏界面会模糊 |
| .NET 10 运行时 / JIT / GC 改进 | ❌ 只能退回 .NET Framework 4.8，GC、TieredCompilation、ReadyToRun 均较弱 |
| Windows Search / 索引导出 | ⚠️ Win7 有，但 API 面更窄 |
| 现代打包（MSIX） | ❌ MSIX 需 Win10 1809+，Win7 只能走 MSI/绿色包 |
| 长路径（>260 字符）原生支持 | ❌ 需 Win10 1607+ 且需 manifest + API 手写 |
| 交付风险 | ❌ 本机无 VS/无目标包，需另装 Build Tools；社区已报告 [.NET 10 WPF 在 Win7 随机崩溃](https://github.com/dotnet/wpf/issues/11304) |

### 2.3 开发机现状（本次实测）

```
dotnet --list-sdks            → 10.0.100
WindowsDesktop runtime        → 10.0.0
.NET Framework (HKLM)         → Release 534296 / Version 4.8.10008（4.8.1 量级，仅运行时）
Reference Assemblies 目标包    → 不存在
vswhere.exe                   → 不存在（未安装任何 Visual Studio / Build Tools）
```

结论：**当前这台机器无法编译 .NET Framework 项目**（缺少 4.8 目标包与 WinFX MSBuild targets，需额外安装 VS Build Tools 工作负载）。现代 .NET 10 路线已完全可用并已验证构建通过。

---

## 3. UI 框架候选评估

评估维度对齐"最高的 UI 性能和运行效率"：冷启动、滚动帧率、内存工作集、大目录（10 万项）表现、开发成本。

| 方案 | 冷启动 | 空闲内存 | 10 万项滚动 | Win7 | 富 UI 能力 | 开发成本 | 评价 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| **WPF / .NET 10** | 中（200–400ms） | 中（80–150MB） | 优（虚拟化+回收可达 60fps） | ❌ | 优（矢量缩放、模板、动画、DPI） | 基准 | **推荐** |
| WinForms / .NET 10 | 优（100–200ms） | 优（40–70MB） | 优（VirtualMode 原生极快） | ❌ | 弱（GDI 光栅、DPI 缩放模糊、主题困难） | 低-中 | 极致轻量备选 |
| WinUI 3 / Windows App SDK | 差（600ms+） | 差（150MB+） | 中（ItemsRepeater 好，但整体重） | ❌ | 优 | 中 | ❌ 不选：启动/内存最差，坑多 |
| .NET Framework 4.8 + WPF | 中 | 中-差 | 良 | ✅ | 优 | 中 | 仅在强制 B 时选 |
| C++/Win32 + Direct2D | 优（<100ms） | 优（<40MB） | 理论最优 | ✅ | 需自建全部控件 | **3–4×** | 理论最优，成本不可接受 |
| Avalonia / Flutter | 中 | 差 | 中 | ❌/部分 | 优 | 中 | ❌ 非原生，与"效率最高"矛盾 |

### 3.1 关于"最高性能"的一个必要澄清

真正最快的文件管理器（Total Commander、Directory Opus）是 **原生 C++**。若把"最高 UI 性能"当作压倒一切的硬指标，托管方案都不是最优解。

我的判断：**WPF + .NET 10 在"性能可达标 × 开发可交付"之间是唯一合理的交点**，前提是执行第 4 节的工程纪律。经验上，文件管理器 80% 的可感知卡顿来自三个具体原因，且都与框架无关：

1. 逐文件调用 `Icon.ExtractAssociatedIcon` —— 单个调用可达数毫秒，1000 项即卡死主线程；
2. 绑定链路上挂 `IValueConverter` 做文件大小/时间格式化 —— 虚拟化每滚一次就重算；
3. 用 `FileSystemWatcher` + 全量重扫做刷新 —— 大目录下引发风暴。

只要这三条被制度性禁止（见 §4），WPF 完全能做出比资源管理器更跟手的滚动与切换。

---

## 4. 推荐决策

### 主推荐

```
运行时    : .NET 10（net10.0-windows），x64
UI 框架   : WPF
支持系统  : Windows 10 22H2+ / Windows 11（首选 64 位；ARM64 二期）
DPI       : PerMonitorV2
文件操作  : Shell COM IFileOperation（非托管拷贝，见 PRD §6.4）
发布      : 自包含（self-contained）+ ReadyToRun + 单文件
```

### 若必须支持 Win7（读法 B）

```
运行时    : .NET Framework 4.8（不要用 4.8.1 作为目标——4.8.1 要求 Windows 10 1803+ / Server 2019，
            不随 Win7 分发；4.8 才是 Win7 SP1 上的受支持版本）
UI 框架   : WPF（同套 XAML 可大部分复用）
代价      : 失去 PMv2 DPI / 长路径 / MSIX / 现代 GC；
            需在 CI 上维护双目标；本机需先装 Build Tools；
            代码须分裂为 net48 与 net10 双 TFM，interop 层差异显著
```

### 决策影响面

| 选 A（推荐） | 选 B（Win7） |
| --- | --- |
| 单一 TFM，interop 层可用现代 C#（`default interface member`、`stackalloc`、`Unsafe`、`Span` 直读 PIDL） | 双 TFM 维护，interop 层退化为手写 COM 包装 |
| 高 DPI 多屏无模糊 | 混合 DPI 环境下必然模糊 |
| 可直接用本机现有 SDK 开发 | 必须先装 VS Build Tools + .NET 4.8 目标包 |

---

## 5. 请确认

请在以下三项中选择，我据此冻结架构：

- **D1-1 运行平台**：`仅 Win10/11（推荐）` / `必须含 Win7 SP1` / `Win7 作为降级构建（同一代码库双目标）`
- **D1-2 UI 框架**：`WPF（推荐）` / `WinForms（若把内存与启动放第一位，接受视觉朴素）`
- **D1-3 性能红线**：请确认第 4 节性能预算表中的数值是否为验收线（冷启动 ≤400ms、10 万项 60fps、四栏工作集 ≤150MB）。

> 未确认前，`PRD.md` 与 `DEV-PLAN.md` 均按 **D1-1 = 仅 Win10/11，D1-2 = WPF** 编写，所有受此假设影响的条目已就地标注 `（依赖 D1）`。

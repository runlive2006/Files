# 功能设计：设置体系

- 版本：v1.1（已实现基础版）
- 日期：2026-10-24
- 状态：**核心已实现**（见 §9 实现说明）
- 前置阅读：[PRD §5.5](../PRD.md)、[DEV-PLAN](../DEV-PLAN.md)

---

## 1. 现状与问题

当前 `AppSettings` 只有 6 个用户可见项（`ViewMode`、`SortColumn`、`SortDescending`、`LastPath`、`UseSystemIcons`、布局相关），
「设置」菜单只有**一项**：使用系统图标。

这带来三个问题：

| 问题 | 影响 |
| --- | --- |
| 隐藏文件无法显示 | 用户看不到 `AppData`、`.git` 等，无法完成日常操作 |
| 扩展名无法控制 | Win7 默认隐藏扩展名，本产品直接显示，与用户习惯冲突 |
| 无任何行为开关 | 删除确认、双击行为、启动位置都无法调整 |

**目标**：把设置做成一个完整、可持久化、可导入导出的体系，覆盖资源管理器用户会去改的绝大多数项。

---

## 2. 设计原则

1. **每一项都必须立即生效**，不需要重启（唯一例外见 §5.3 缓存类设置）。
2. **默认值对齐 Windows 资源管理器**，让 Win7 迁移用户不需要改任何东西。
3. **设置项必须可枚举**：名称、类型、默认值、分组、说明都由元数据驱动，供 UI 自动生成、导入导出、以及未来的策略管控使用（PRD §5.5 已提出该要求）。
4. **不提供"危险开关"**：不暴露可能破坏数据的选项（如"永久删除不确认"默认关闭且需二次确认才能打开）。

---

## 3. 设置项清单

分组对应设置界面左侧导航。`键` 是 `settings.json` 中的字段名。

### 3.1 常规（General）

| 键 | 类型 | 默认 | 说明 |
| --- | --- | --- | --- |
| `Startup.Mode` | enum | `LastSession` | 启动时打开什么：`LastSession`（恢复上次）/ `Home`（用户目录）/ `FixedPath`（指定目录） |
| `Startup.FixedPath` | string | `""` | `Startup.Mode=FixedPath` 时使用 |
| `Startup.RestoreTabs` | bool | `true` | 是否恢复各栏标签；关闭时只恢复每栏的活动标签 |
| `Startup.SingleInstance` | bool | `true` | 是否单实例运行。多实例时新进程把路径转交给已有窗口（见 §5.1） |
| `Startup.CheckDefaultOnLaunch` | bool | `false` | 启动时检查是否为默认文件管理器（**仅提示，不自动改注册表**） |

### 3.2 视图（View）

| 键 | 类型 | 默认 | 说明 |
| --- | --- | --- | --- |
| `View.DefaultMode` | enum | `Details` | 新标签/新栏的默认视图：`Details` / `List` / `Thumbnails` |
| `View.IconSize` | int | `16` | 详情/列表视图的图标尺寸：`16` / `20` / `24` |
| `View.ThumbnailSize` | int | `128` | 缩略图单元格边长，范围 96–256，步进 16 |
| `View.ShowHidden` | bool | `false` | 显示隐藏文件与系统文件（两者合并为一个开关，与 Explorer 一致） |
| `View.ShowExtensions` | bool | `true` | 显示文件扩展名。**默认 `true`**（与 Explorer 相反，理由见 §5.2） |
| `View.ShowStatusBar` | bool | `true` | 显示底部状态栏 |
| `View.RowHeight` | enum | `Normal` | `Compact` / `Normal` / `Comfortable` |
| `View.AlternateRowColor` | bool | `false` | 奇偶行交替背景色（大表格可读性） |
| `View.FolderFirst` | bool | `true` | 文件夹排在文件之前。关闭后完全按排序键混合排列 |
| `View.ShowGridLines` | bool | `false` | 详情视图显示网格线 |
| `View.InheritFolderView` | bool | `true` | 记住每个文件夹各自的视图模式（Explorer 行为）；关闭则全局统一 |

### 3.3 文件操作（FileOperations）

| 键 | 类型 | 默认 | 说明 |
| --- | --- | --- | --- |
| `FileOps.ConfirmDelete` | bool | `true` | 删除到回收站前确认 |
| `FileOps.ConfirmPermanentDelete` | bool | `true` | 永久删除前确认。**不可通过设置关闭**（强制 `true`，见 §5.4） |
| `FileOps.DeleteToRecycleBin` | bool | `true` | `Del` 键默认进回收站；关闭则直接永久删除（需二次确认） |
| `FileOps.ConfirmDragMove` | bool | `false` | 跨卷/跨栏拖放移动前确认 |
| `FileOps.OpenWithSingleClick` | bool | `false` | 单击打开（Web 式）；默认关闭，双击打开 |
| `FileOps.DoubleClickOnFolder` | enum | `Open` | `Open`（进入）/ `OpenInNewTab`（新标签打开） |
| `FileOps.AutoRefreshInterval` | int | `0` | 自动刷新间隔（秒），`0` 表示依赖文件系统通知 |

### 3.4 标签与栏（TabsPanes）

| 键 | 类型 | 默认 | 说明 |
| --- | --- | --- | --- |
| `Panes.ShowTabStripAlways` | bool | `false` | 是否始终显示标签条（默认只在 ≥2 标签时显示，省 24px） |
| `Panes.NewTabInheritsPath` | bool | `true` | 新标签继承当前路径（关闭则打开用户目录） |
| `Panes.CloseWindowWithLastTab` | bool | `false` | 关闭最后一个标签时是否关闭窗口 |
| `Panes.MaxClosedTabHistory` | int | `10` | `Ctrl+Shift+T` 可恢复的标签数，范围 0–50 |

### 3.5 性能（Performance）

| 键 | 类型 | 默认 | 说明 |
| --- | --- | --- | --- |
| `Perf.IconCacheSize` | int | `512` | 图标缓存条目上限，范围 128–4096 |
| `Perf.EnableThumbnailDecoding` | bool | `false` | 是否解码真实图片缩略图（当前未实现，见 §5.3） |
| `Perf.ShowPerfHud` | bool | `false` | 是否显示性能 HUD（等价于命令行 `--perf`） |

### 3.6 高级（Advanced）

| 键 | 类型 | 默认 | 说明 |
| --- | --- | --- | --- |
| `Advanced.LogLevel` | enum | `Warning` | `Error` / `Warning` / `Info` / `Debug` |
| `Advanced.KeepCrashLogs` | int | `10` | 保留的崩溃日志份数 |
| `Advanced.EnableShellExtensions` | bool | `false` | 是否加载第三方右键扩展（实验性，需进程隔离） |
| `Advanced.ConfigDirectory` | string | `""` | 配置目录覆盖；空表示 `<exe>\data`（见 §5.5） |

---

## 4. 设置界面设计

### 4.1 形态

**独立窗口**（非模态亦可），从「设置」菜单打开。

```
┌─ 设置 ─────────────────────────────────────┐
│ ┌──────────┐ ┌───────────────────────────┐ │
│ │ 常规      │ │ 启动                      │ │
│ │ 视图      │ │  ○ 恢复上次会话           │ │
│ │ 文件操作  │ │  ○ 打开用户目录           │ │
│ │ 标签与栏  │ │  ○ 打开指定位置 [____][…] │ │
│ │ 性能      │ │                           │ │
│ │ 高级      │ │ ☑ 恢复各栏标签            │ │
│ │          │ │ ☑ 单实例运行              │ │
│ │          │ │                           │ │
│ │          │ │ 外观                      │ │
│ │          │ │ 显示隐藏文件      [ 关 ]  │ │
│ │          │ │ 显示扩展名        [ 开 ]  │ │
│ └──────────┘ └───────────────────────────┘ │
│                    [导入] [导出] [恢复默认] │
│                              [取消] [确定] │
└────────────────────────────────────────────┘
```

### 4.2 交互规则

| 规则 | 说明 |
| --- | --- |
| 即时预览 | 视图类设置（隐藏文件、扩展名、行高）改动后**立即作用于所有已打开窗口**，无需点确定 |
| 取消语义 | 「取消」只回滚尚未即时生效的项（如启动行为），并提示"部分设置已生效" |
| 搜索 | 设置窗口顶部提供搜索框，按名称/说明过滤（34 项时仍有必要） |
| 恢复默认 | 按分组重置，不是全量重置；需确认 |
| 导入导出 | 单文件 `.filesconfig`（ZIP：`settings.json` + `keybindings.json`），带 `schemaVersion` |
| 冲突处理 | 导入时提供「合并 / 覆盖 / 另存为」，默认**合并** |
| 无效值 | 手工编辑 JSON 产生的越界值在加载时被夹紧到合法范围，并记 Warning 日志，**不阻塞启动** |

---

## 5. 关键设计决策

### 5.1 单实例与路径转交

默认开启单实例。第二个进程启动时不打开新窗口，而是把命令行路径通过**命名管道**转交给已有实例，然后退出。

- 管道名：`Files-{用户SID}-{可执行文件路径哈希}`，避免不同副本互相干扰
- 转交失败（超时 2s）时降级为**打开新窗口**，不报错
- `--new-window` 参数强制新开窗口，跳过转交

> 这解释了为什么"双击文件夹 → 在本程序新标签打开"需要它：没有单实例，每次都是新进程。

### 5.2 为什么「显示扩展名」默认开

Windows 资源管理器默认**隐藏**扩展名，这是历史上大量"双击 .txt.exe 中招"的根源。本产品默认显示扩展名：

- 安全收益明确（用户能看见真实类型）；
- 对 Win7 迁移用户是**可见的行为差异**，因此需要在首次启动时用一次性提示说明，并提供"改回隐藏"的直达链接。

### 5.3 缩略图解码（`Perf.EnableThumbnailDecoding`）

当前「缩略图」视图只是**大图标网格**，不解码图片内容。开启该项后将：

- 对图片文件调用 `IShellItemImageFactory::GetImage`（系统缩略图缓存，比自己解码快且支持视频/PDF）
- 异步、可取消、按视口优先级加载
- 失败回退为类型图标

**该项默认关闭**，因为：系统缩略图缓存首次访问较慢，且需要处理大量边界（损坏文件、超大图、无缩略图提供者）。这是独立特性，应单独排期。

### 5.4 为什么「永久删除确认」不可关闭

`FileOps.ConfirmPermanentDelete` 暴露在设置里但**强制为 `true`**，UI 显示为只读并附说明。理由：不可撤销的数据丢失不应提供一键关闭的开关。用户若确实要跳过，可用 `Shift+Del` 后仍会确认——这是刻意的摩擦。

### 5.5 配置目录

沿用现有约束（PRD §5.5、DEPLOY §1）：默认 `<exe>\data`，不写 `%APPDATA%`。

`Advanced.ConfigDirectory` 允许指向别处，用于：多份配置隔离、放到只读目录时改指向可写位置。

> 若该路径不可写，启动时明确报错退出，而不是静默回退到 `%APPDATA%`。

### 5.6 设置变更的传播

```
SettingsService (单例)
   ↓ 事件 SettingsChanged(changedKeys)
ShellViewModel
   ↓ 分发
PaneViewModel (×N) → TabViewModel (×M) → 重新应用视图参数
```

按**代价**分三档处理，不是所有项都能"零成本即时生效"：

| 档位 | 设置项 | 处理方式 | 感知延迟 |
| --- | --- | --- | --- |
| **A. 纯视觉** | 行高、交替行色、网格线、状态栏、标签条常显 | 直接改依赖属性，不动数据 | 立即（< 16ms） |
| **B. 需重排** | 文件夹优先、默认视图模式、图标尺寸 | 重新排序/重设模板，不重新枚举 | 立即（< 50ms） |
| **C. 需重枚举** | **显示隐藏文件**、显示扩展名 | 必须重新读目录（`FindFirstFileEx` 才能拿到隐藏属性） | 后台执行，期间列表显示旧内容 + 状态栏"正在刷新…" |

> **C 档不是"即时"**：PRD 要求"即时生效，不重扫全目录"是针对**扩展名**的（扩展名只需改显示文本，
> 见 §6.3），而**隐藏文件必须重枚举**——因为隐藏是文件属性，只有枚举时才知道。
> 这里明确区分，避免实现时误以为两者都能零成本切换。

- C 档走已有的 `LatestTaskRunner`，天然防抖（连续切换开关只跑最后一次）
- C 档期间保持滚动位置与选择集（按路径恢复）

---

## 6. 数据模型

```csharp
public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 2;   // 本次升级到 2

    public GeneralSettings General { get; set; } = new();
    public ViewSettings View { get; set; } = new();
    public FileOperationSettings FileOperations { get; set; } = new();
    public PaneSettingsGroup Panes { get; set; } = new();
    public PerformanceSettings Performance { get; set; } = new();
    public AdvancedSettings Advanced { get; set; } = new();

    // 保留：窗口几何与布局（结构不变）
    public WindowSettings Window { get; set; } = new();
    public string LayoutMode { get; set; } = "Single";
    public List<PaneSettings> PanesLayout { get; set; } = [];
    public List<double> SplitRatios { get; set; } = [];
}
```

**迁移**：`SchemaVersion 1 → 2` 时，把旧的顶层 `ViewMode`/`UseSystemIcons`/`SortColumn` 映射到新分组，其余取默认值。旧文件必须能无损升级（PRD §5.5 要求"无法升级时拒绝导入并保留原配置"）。

---

## 7. 验收标准

| # | 标准 |
| --- | --- |
| 1 | 全部 34 项可读写，重启后保持 |
| 2 | A/B 档设置改动后立即生效（< 50ms），无需重启；C 档（隐藏文件）后台刷新期间界面可用 |
| 3 | 手工改坏 `settings.json` 后能正常启动（回落默认 + 保留 `.corrupt`） |
| 4 | 旧版 `settings.json`（v1）能自动迁移到 v2，用户无感知 |
| 5 | 导入/导出往返一致；冲突时默认合并且可回滚 |
| 6 | 「永久删除确认」无法被关闭 |
| 7 | 设置搜索能命中名称与说明 |
| 8 | 配置目录不可写时给出明确提示并退出，不写 `%APPDATA%` |

---

## 8. 未决问题

| # | 问题 | 建议 |
| --- | --- | --- |
| Q1 | 是否需要「便携模式」标记文件（`files.portable`） | 是，成本低，与"数据只写程序目录"天然契合 |
| Q2 | 设置是否要按用户区分（同一份 exe 多人使用） | 否，当前是绿色便携模型 |
| Q3 | 快捷键自定义是否纳入本轮 | 否，单独设计（涉及冲突检测与 UI 提示同步） |
| Q4 | 是否提供命令行覆盖设置（`--setting View.ShowHidden=true`） | 建议提供，便于自动化测试 |

---

## 9. 实现说明（2026-10-24）

### 9.1 已完成

| 项 | 说明 |
| --- | --- |
| 设置模型 | 6 个分组类 + `AppSettings` 聚合，`SchemaVersion` 升至 2 |
| 元数据目录 | `SettingCatalog` 共 34 项，含分组、类型、范围、枚举选项、只读标记 |
| 迁移 | v1 → v2：顶层 `ViewMode` 映射到 `View.DefaultMode`；`LastPath` 在 `Panes` 为空时兜底为单栏 |
| 值夹紧 | 越界整数夹紧到合法范围并记警告；非法枚举重置为默认 |
| 安全项强制 | `ConfirmPermanentDelete` 即使被配置文件改成 `false` 也强制回 `true` |
| 原子写 | 临时文件 + `Move(overwrite)`；损坏时另存 `.corrupt` 并回落默认 |
| 设置窗口 | 由元数据自动生成界面，按类型切换 CheckBox / Slider / ComboBox / TextBox；支持搜索、按组恢复默认、打开配置目录 |
| 即时生效 | `View.ShowHidden` 触发重枚举；其余视图项重设视图参数 |

### 9.2 验证方式

- 单元测试 **189 个**全部通过，其中设置相关 16 个（迁移、夹紧、只读拒绝、通知、往返）
- 端到端：写 `ShowHidden=false` 启动 → 列表只显示 `visible.txt`（1 项）；
  改为 `true` → 显示 `hidden.txt` + `visible.txt`（2 项）。证明设置真正驱动了枚举过滤

### 9.3 已接线（行为生效）

| 设置项 | 生效方式 |
| --- | --- |
| `View.ShowHidden` | 驱动枚举过滤（`Win32DirectoryEnumerator.IncludeHidden`） |
| `View.ShowExtensions` | 列表名称显示 |
| `View.ShowStatusBar` | 状态栏可见性 |
| `View.ShowDetailsPane` | 底部详情窗格 |
| `View.ShowNavigationPane` | 左侧导航树 |
| `View.NavigationPaneWidth` | 导航树宽度，拖动分隔条回写 |
| `View.RowHeight` | 列表行高（紧凑/标准/宽松） |
| `View.UseSystemIcons` | 真实系统图标 vs 彩色方块 |
| 窗口几何（左/上/宽/高/最大化） | 启动时恢复，含跨屏可见性校验 |
| 布局模式与各栏路径/视图模式/分隔条比例 | 启动时恢复 |

### 9.4 尚未接线（能存不能用）

| 项 | 影响 |
| --- | --- |
| `FileOps.ConfirmDelete` / `DeleteToRecycleBin` | 删除前确认与回收站选择尚未接到操作路径 |
| `View.IconSize` | 图标尺寸未生效 |
| `View.AlternateRowColor` / `ShowGridLines` | 外观未生效 |
| `Startup.SingleInstance` | 单实例与路径转交未实现 |
| `Startup.StartupMode` | 启动时未按它选择初始路径 |
| `Panes.ShowTabStripAlways` / `CloseWindowWithLastTab` | 未生效 |
| `Perf.IconCacheSize` / `ShowPerfHud` | 未接到图标缓存与 HUD |
| `Perf.EnableThumbnailDecoding` | 只读展示，真实缩略图解码未实现 |
| 导入 / 导出 `.filesconfig` | 未做 |
| 设置搜索高亮 | 已支持过滤，未做高亮 |
| 快捷键自定义 | 按设计不属于本轮 |

> 这些项的设置已能读写与持久化，只是行为尚未接线。逐项接入即可，不需要再改数据模型。
> 接线顺序见 [DEV-PLAN.md](../DEV-PLAN.md) §3 第二波。

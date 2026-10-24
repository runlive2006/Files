# Files —— 开发计划（DEV-PLAN）

- 版本：v0.2（决策 D1 已冻结，多栏+多标签纳入范围）
- 日期：2026-10-21
- 前置阅读：[ADR-001 选型](adr/ADR-001-runtime-and-ui-framework.md) · [PRD](PRD.md)
- 里程碑口径：以**周**为估算单位，按 1 名全职开发折算。每个里程碑有可运行交付物与退出判据。
- 已冻结前提（D1）：WPF / .NET 10 / **仅 x64** / 仅 Win10-11；Win7 仅为功能基线。

---

## 0. 已完成基线（M0）

| 项 | 状态 |
| --- | --- |
| Git 仓库 + `main` 跟踪 `origin` | ✅ 已推送 `9d73880` |
| `Files.slnx`（Core / App / Tests 三项目） | ✅ |
| 构建通路 | ✅ `dotnet build Files.slnx -m:1` 0 警告 0 错误 |
| 测试通路 | ✅ `scripts/test.ps1`，MSTest 自包含运行器，3/3 通过 |
| 依赖隔离 | ✅ `NuGet.config` → `.dotnet/packages`，已 `.gitignore` |
| 最小可用代码 | ✅ `IDirectoryLister` + `FileSystemDirectoryLister` + 单栏 ListView |

> ⚠️ 本机构建注意事项（已写入 PRD/README，CI 必须复刻）：受限环境下需 `-m:1 --disable-build-servers`；`dotnet test` 的 VSTest 目标与 .NET 10 SDK + Microsoft.Testing.Platform 不兼容，测试走 `scripts/test.ps1`。

---

## 1. 总体时间线

```
M0 基线       ██                        已完成
M1 单栏可用   ████████                  6 周   → Alpha-1
M2 多栏+标签  ████████████              6 周   → Alpha-2
M3 Shell 化         ████████            6 周   → Beta-1
M4 完整 Ribbon         ███████          5 周   → Beta-2
M5 打磨             ████                3 周   → RC → 1.0
                      ─────────────────────────
                                合计 ≈ 26 周
```

关键路径：`M1 虚拟化性能` → `M2 布局树 × 标签` → `M3 IFileOperation`。M3 的 interop 是最可能超支的部分，因此把它放在性能门禁已经建立之后（避免"性能返工 × interop 返工"叠加）。

> **v0.2 变更**：因 D1 确认"多栏与多标签都要"，M2 由 4 周扩为 6 周（+2 周标签子系统），总工期 24 → 26 周。

---

## 2. M1：单栏可用（6 周）

**目标**：一个不带动画、但**快且正确**的单栏浏览器。对标 Win7 Explorer 的单窗口核心体验。

### 2.1 任务分解

| # | 任务 | PRD 覆盖 | 估时 | 产出 |
| --- | --- | --- | --- | --- |
| 1.1 | 枚举层重写：`FindFirstFileEx` + `SafeFindHandle`，替换 `Directory.EnumerateFileSystemInfos` | §6.3 | 3d | `Files.Interop.Win32Find` |
| 1.2 | `EntryRow` 扁平视图模型（预格式化字符串，无转换器） | §6.5-1 | 2d | App/ViewModels |
| 1.3 | 虚拟化列表：`VirtualizingStackPanel` + Recycling + 项级滚动 | §6.5-2,3 | 3d | 视图模式：详情/小图标/大图标 |
| 1.4 | **性能探针**：帧计时器 + 工作集采样 + 启动打点，接 ETW/日志 | §5.1 | 3d | `Files.Core.Diagnostics` |
| 1.5 | **性能基准测试台**：生成 10 万假文件目录，跑滚动/进入用例 | §5.1 | 3d | `bench/`（可独立运行） |
| 1.6 | 异步命令基础设施：`AsyncRelayCommand`、`ITaskQueue`、取消传播 | — | 4d | Core |
| 1.7 | 导航：面包屑 + 可编辑地址栏 + 后退/前进/向上 + 历史栈 | NAV-01..03 | 5d | App |
| 1.8 | 选择模型：`Ctrl`/`Shift`/框选/全选/反选 | ORG-06,07 | 4d | App（含单元测试） |
| 1.9 | 基础操作：新建/重命名/删除（回收站）/复制剪切粘贴（先用 `File.Copy` 占位） | ORG-01..04 | 5d | Core + App |
| 1.10 | 排序与分组（后台线程产出） | VIEW-02,03 | 4d | Core |
| 1.11 | 列系统与列宽持久化 | VIEW-04 | 3d | App |
| 1.12 | 即时搜索（当前目录，异步可中断） | SRC-01,04 | 4d | Core + App |
| 1.13 | 设置子系统：三层配置（默认/机器/用户）+ `schemaVersion` 迁移 + 原子写 + 可枚举的设置元数据 | §5.5 | 4d | Core |
| 1.14 | 路径正确性库：长 UNC/保留名/正规化 | §5.4 | 4d | Core（重测试） |
| 1.15 | 标签状态模型（`TabState`：路径/选择/滚动/排序/历史），先单栏单标签跑通 | §2.2,§2.3 | 3d | Core + App |

### 2.2 进度（2026-10-21 更新）

| 任务 | 状态 | 说明 |
| --- | --- | --- |
| 1.1 枚举层重写 | ✅ 完成 | `Files.Interop` + `FindFirstFileExW` + `SafeFindHandle`；已替换 UI 使用的实现 |
| 1.2 EntryRow | ✅ 完成 | `FileSystemEntry` 改 `readonly struct`，视图行预格式化，无绑定转换器 |
| 1.3 虚拟化列表 | ✅ 完成 | 三种视图均虚拟化；缩略图用自研 `VirtualizingWrapPanel`，23,246 项实测只生成 36 个容器 |
| 1.4 性能探针 | ✅ 完成 | `StartupProbe`、`WorkingSetSampler`、`FrameTimer`；`--perf` 显示 HUD |
| 1.5 性能基准台 | ✅ 完成 | `bench/Files.Bench` + `scripts/bench.ps1`，支持 `--gate` |
| 1.6 异步命令基础设施 | 🟡 部分 | `LatestTaskRunner`（只保留最后一次请求）已完成；`AsyncRelayCommand` 待做 |
| 1.7 导航 | ✅ 完成 | 地址栏（可编辑、支持环境变量）、后退/前进/向上、`NavigationHistory` 深度 50 |
| 1.8 选择模型 | 🟡 部分 | `Ctrl+A` / `Ctrl+I` 反选、`Shift` 范围与 `Ctrl` 加选（由 ListView 提供）；框选待做 |
| 1.9 基础操作 | ✅ 完成 | 新建文件夹/新建文件/重命名/删除（回收站与永久）/复制/剪切/粘贴；删除走 `SHFileOperation` + `FOF_ALLOWUNDO`。M3 换 `IFileOperation` |
| 1.10 排序与分组 | 🟡 部分 | 四列可排序、后台排序、文件夹优先、降序保持分组；分组视图待做 |
| 1.11 列系统 | ✅ 完成 | 列宽可调并持久化到设置 |
| 1.12 即时搜索 | ⬜ 未开始 | || 1.13 设置子系统 | ✅ 完成 | 原子写、损坏自愈（`.corrupt`）、schema 迁移、窗口几何与跨屏可见性校验 |
| 1.14 路径正确性库 | ✅ 完成 | `PathUtility`（长路径/UNC/保留名/Unicode NFC）+ 测试 |
| 1.15 标签状态模型 | ✅ 完成 | `TabState` / `PaneState` / `LayoutNode` / `LayoutFactory` + 测试 |

**已取得性能基线**（详见 [PERF-BASELINE.md](PERF-BASELINE.md)）：

| 规模 | 枚举 | 枚举+映射+排序 | 每项内存 | 预算 | 判定 |
| --- | --- | --- | --- | --- | --- |
| 50,000 项 | 30.4ms | 69.7ms | — | 600ms | 达标 |
| 100,000 项 | 55.8ms | 131.1ms | 427 B → **248 B** | 1200ms | 达标 |

> **结论：数据层不是瓶颈**，10 万项只占首屏预算的 22%，风险集中在 WPF 视图实例化。
> 内存优化后每项 248 B、10 万项 23.6MB，已能支撑"四栏各 1000 项"的预算；
> 但"四栏各 10 万项"仍需依赖 §4.2 的非活动标签不留视图约束。

#### M1 文件操作的已知限制（M3 解决）

| 限制 | 原因 | 影响 |
| --- | --- | --- |
| 不支持超过 MAX_PATH 的路径 | `SHFileOperation` 不接受 `\\?\` 前缀 | 深层路径操作会失败，M3 换 `IFileOperation` 后解决 |
| 进度与冲突使用系统自带界面 | 同上 | 不满足 PRD DLG-01/DLG-02 的"聚合式自定义对话框" |
| 长操作不可取消 | `SHFileOperation` 是同步阻塞调用 | 大批量复制时只能等；已放到后台线程，界面不冻结 |
| 重命名是对话框而非内联 | 内联需行级 `INotifyPropertyChanged`，10 万项目录下内存代价高 | 交互多一步；M2 引入"仅活动行可变"后改内联 |

> 已具备的正确语义：删除进回收站（`FOF_ALLOWUNDO`）、跨卷自动降级为复制、
> 重名冲突由系统提示而非静默覆盖、复制后源文件保留、移动后源文件消失。

### 2.3 退出判据（全部满足才进 M2）
- [ ] 10 万项目录：进入首屏 ≤600ms，滚动 ≥58fps，工作集 ≤90MB（基准台实测，非估算）
- [ ] 冷启动 ≤400ms
- [ ] 路径正确性测试集全绿（含 `\\?\`、UNC、`CON.txt`、尾随点、组合等价 Unicode）
- [ ] 删除默认进回收站且资源管理器可见"删除日期/原位置"
- [ ] 空闲 CPU ≤1%
- [ ] 无一处绑定值转换器做格式化（CI 静态检查）
- **若性能门禁不达标 → 触发 R3 预案：预研自绘面板（P-1），M2 延后 2 周**

### 2.3 架构骨架（M1 建立，后续复用）

```
Files.Core/
  Abstractions/     IDirectoryLister*, IFileOperator, IPathResolver, IIconProvider
  Models/           FileSystemEntry, EntryKind, LayoutNode, PaneState, TabState
  Services/         SearchService, SortService, HistoryService, SettingsService, TaskQueue
  Diagnostics/      PerfProbe, FrameTimer, WorkingSetSampler
Files.Interop/      ← 新项目（M1.1 建立）
  Win32Find.cs      IFileOperation.cs  ShellIcon.cs  KnownFolders.cs  FileSystemWatcherEx.cs
Files.App/
  Views/            MainWindow, PaneView, TabStrip, BreadcrumbBar, DetailsList, IconGrid
  ViewModels/       ShellViewModel, PaneViewModel, TabViewModel, EntryRow
  Behaviors/        DragDrop, InlineRename, KeyboardSelection
```

`Files.Core` 不得引用 `PresentationFramework`。这条用 `Directory.Build.props` + 自定义 MSBuild target 在 CI 强制。

---

## 3. M2：多栏 + 多标签（6 周）

**目标**：4 种布局 × 栏内标签页，性能不因栏数与标签数线性劣化。

> 这是 v0.2 的最大变更。设计核心：**标签是状态容器，不是预创建的视图**。非活动标签只保留 `TabState`（≤2KB），视图与项容器在切换时重建（切换目标 ≤80ms，见 PRD §5.1）。若做成"每标签一个常驻可视化树"，四栏 × 5 标签就会直接击穿内存预算。

### 3.0 多栏进度（2026-10-21）

| 任务 | 状态 | 说明 |
| --- | --- | --- |
| 2.1 布局树模型 | ✅ 完成 | `LayoutNode` / `SplitNode` / `PaneNode`；`LayoutFactory.Create` 支持**复用焦点栏实例**，其余栏克隆，切换布局不丢当前目录 |
| 2.2 布局渲染与分隔条 | ✅ 完成 | 按树递归生成 `Grid` + `GridSplitter`；拖动后回写 `FirstRatio` |
| 2.5 焦点管理 | 🟡 部分 | `F6`/`Shift+F6` 循环、焦点栏描边、状态栏指示已完成；`Ctrl+Tab` 标签切换待标签子系统 |
| 2.8 栏级状态隔离 | ✅ 完成 | 每栏独立的 `PaneViewModel`（路径/排序/视图模式/历史/选择） |
| 2.9 布局持久化 | ✅ 完成 | `settings.json` 保存布局模式、各栏路径与视图模式、分隔条比例；恢复时校验路径存在 |
| 2.3 标签组控件 | ✅ 完成 | 标签条仅在 ≥2 标签时显示；活动标签高亮、关闭按钮、标题消歧（同名目录带父目录名） |
| 2.4 标签状态生命周期 | ✅ 完成 | **栏内只有一个 `ListView`**，切换标签替换 `ItemsSource` 让 WPF 回收旧容器；滚动位置与选择集按标签保存/恢复 |
| 2.6 标签跨栏拖动 | ⬜ 未开始 | |
| 2.7 跨栏拖放 | ✅ 完成 | 系统 `FileDrop` 格式：栏间互拖、拖入拖出资源管理器；悬停文件夹即投放该文件夹；同卷移动/跨卷复制（`DropPolicy`，11 个测试）；目标栏高亮 |
| 2.10 多栏×多标签性能门禁 | 🟡 部分 | 实测四栏×多标签可正常启动（WS 170MB）；尚未做 5 标签×10 万项的极限门禁 |
| 2.11 镜像/同步跟随 | ⬜ 未开始 | |
| 2.12 布局预设 | ⬜ 未开始 | |
| 2.13 关闭确认 | ⬜ 未开始 | |

### 3.1 UI 与图标（2026-10-21 追加）

| 项 | 说明 |
| --- | --- |
| 应用图标 | `Assets/Files.ico`，7 种尺寸（16–256），**自绘原创**，无第三方素材版权问题；由 `scripts/make-icon.ps1` 可重现生成 |
| 双击打开文件 | 文件夹进入；文件交给系统默认程序（`Process.Start` + `UseShellExecute`） |
| 焦点栏切换 | 点击栏内**任意区域**（标题/地址栏/标签条/列表空白）都切换焦点栏。仅靠 `GotKeyboardFocus` 不够——焦点本就在栏内时不会再触发，已在最外层 Border 监听 `PreviewMouseDown` |
| 工具栏 | 改为下拉菜单：**分栏方式**（单栏/左右/上下/四栏）、**操作**（复制/粘贴/剪切 + 新建文件夹）、**视图**（详情/列表/缩略图）、**设置** |
| 缩略图视图 | 使用自研 `VirtualizingWrapPanel` 排列为大图标网格，已虚拟化 |
| 系统图标 | `ShellIcon` 用 `SHGFI_USEFILEATTRIBUTES` 按扩展名取图标（**零磁盘访问**）；`IconProvider` 按"扩展名 + 尺寸"缓存（几百个键覆盖十万项）；设置菜单可开关，关闭时回退为按类型着色方块 |

> 已知取舍：系统图标按扩展名获取，因此 `.exe`、`.lnk` 这类"每文件图标不同"的项会显示通用图标。
> 精确到文件需要按路径取图标，会产生每项一次磁盘访问，与 10 万项的性能目标冲突。

### 3.2 缩略图视图的已知限制（重要）

缩略图视图使用自研 `VirtualizingWrapPanel`，**已实现虚拟化**。

**实测数据**（`tools/PanelCheck`，可复跑）：

| 场景 | 结果 |
| --- | --- |
| 100,000 项 / 视口 979×696 | 只生成 **56** 个容器 |
| 滚到底部 | 容器数 **47**，偏移 1,485,048（总高 1,485,744） |
| 真实应用 / 23,246 项（WinSxS） | 窗口 3.1 秒出现，渲染 **36** 个列表项，进程响应正常 |

### 实现要点（两次失败后总结）

1. **`ScrollViewer.CanContentScroll` 必须为 `true`**：否则按像素滚动，
   面板的 `IScrollInfo` 不被使用，虚拟化完全不生效。
2. **`MeasureOverride` 绝不能返回 `Infinity`**：会让父级永远不再重新测量，
   表现为列表空白。视口尺寸退化时必须用有限值兜底。
3. **必须提供可重复的验证手段**：`tools/PanelCheck` 在真实 WPF 消息循环里
   加载 10 万项并统计容器数。此前两次失败都是因为只能靠肉眼看界面，
   无法判断"到底有没有虚拟化"。
4. **布局需要多轮**：面板注入 → 测量 → 生成容器，单次 `UpdateLayout()` 不够，
   验证代码要循环等待。曾因此误判为 `realized=0`。

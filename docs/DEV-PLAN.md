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

### 2.2 退出判据（全部满足才进 M2）

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

| # | 任务 | PRD | 估时 |
| --- | --- | --- | --- |
| 2.1 | 布局树模型：`LayoutNode`（`Split(dir, a, b)` / `Leaf(pane)`），可序列化、可折叠为单栏 | §2.1 | 4d |
| 2.2 | 布局渲染与 `GridSplitter`，比例存储（相对而非像素，DPI 无关） | §2.1 | 3d |
| 2.3 | **标签组控件**：标签条（≥2 才显示）、活动标签、溢出滚动 + 下拉列表 | §2.2, TAB-01/03 | 5d |
| 2.4 | **标签状态生命周期**：非活动标签释放视图、切换时重建、滚动/选择还原 | §2.3, §4.2 约束 | 5d |
| 2.5 | 焦点管理：`F6`/`Shift+F6` 栏间循环、`Ctrl+Tab` 标签间切换、独立快捷键上下文 | §2.2, §5.6 | 3d |
| 2.6 | 标签跨栏拖动 + 标签标题去歧义 | TAB-02, TAB-04 | 4d |
| 2.7 | 跨栏拖放（含 Ctrl/Shift 修饰、跨卷降级、悬停回焦展开） | PANE-02 | 5d |
| 2.8 | 栏/标签级状态隔离：视图模式/排序/筛选/历史各自独立 | PANE-03 | 3d |
| 2.9 | 布局 + 标签持久化与恢复（含"恢复上次会话"开关） | §2.5 | 4d |
| 2.10 | **多栏×多标签性能门禁**：四栏×5 标签 ≤220MB；标签切换 ≤80ms；图标缓存跨栏跨标签命中 | §5.1 | 4d |
| 2.11 | 镜像/同步跟随模式 | PANE-04 | 3d |
| 2.12 | 布局预设（命名保存/加载，含整栏标签组） | PANE-06, TAB-06 | 3d |
| 2.13 | 关闭操作中标签的确认流程 | TAB-05, §5.7 | 2d |

退出判据：

- 四栏各 10 万项工作集 ≤150MB；**四栏 × 5 标签各 1000 项 ≤220MB**；
- 标签切换 ≤80ms（含视图重建）；
- 跨栏拖放含 1000 文件批次无 UI 卡顿；
- 布局树 + 标签组单测覆盖分裂/合并/序列化往返/损坏恢复；
- 关掉 20 标签再开新会话，内存回到基线（无泄漏）。

---

## 4. M3：Shell 化（6 周）⚠️ 最高技术风险

**目标**：真实的 Windows 文件操作与图标语义。这一里程碑把 M1 的占位实现全部替换掉。

| # | 任务 | PRD | 估时 | 风险 |
| --- | --- | --- | --- | --- |
| 3.1 | `IFileOperation` interop：vtable、`SetOperationFlags`、进度 sink、`IFileOperationProgressSink` 封送 | §6.4 | 8d | 高 |
| 3.2 | 统一进度对话框（整批聚合、取消 200ms 内响应） | DLG-02 | 4d | |
| 3.3 | 冲突对话框：枚举全部冲突后一次性呈现 + 应用于全部 | DLG-01 | 4d | |
| 3.4 | 提权路径处理（`IFileOperation` 内置 UAC）与失败指引 | §5.4 | 2d | |
| 3.5 | 真实图标：`IImageList`/`SHGetFileInfo`，DPI 刻度感知，LRU 2000 | §6.3-5 | 5d | |
| 3.6 | 缩略图队列：优先级 + 可取消 + 同扩展名共享位图 | §6.3 | 5d | 高（R5） |
| 3.7 | 扩展属性：`IPropertyStore` 读 `PKEY_*`，视口 ±3 屏惰性填充 | VIEW-10,§6.3-3 | 5d | |
| 3.8 | 重解析点/符号链接/junction 语义正确显示与操作 | §5.4 | 3d | 高 |
| 3.9 | `FileSystemWatcher` 精准刷新（合并风暴、去抖、只更新受影响区间） | §1.2-3 | 4d | |
| 3.10 | 回收站视图（`ShellFolder` 枚举 + 还原/永久删除） | NAV-10 | 4d | |
| 3.11 | 特殊文件夹/`::{GUID}` 解析 + 已知文件夹 PIDL | NAV-11 | 3d | |

退出判据：复制/移动/删除/重命名全部经 `IFileOperation`（grep 确认无 `File.Copy`/`Directory.Move` 残留在生产路径）；10 万项 + 真实图标滚动仍 ≥58fps；符号链接目录不被"跟随穿透"造成误删。

---

## 5. M4：完整 Ribbon 与体验（5 周）

| # | 任务 | PRD | 估时 |
| --- | --- | --- | --- |
| 4.1 | 导航窗格树（懒展开、展开态持久化、库/设备/网络节点） | NAV-05 | 5d |
| 4.2 | 库（`.library-ms` 解析与聚合视图） | NAV-06 | 4d |
| 4.3 | 此电脑视图（容量、文件系统、可移动卷） | NAV-07 | 3d |
| 4.4 | Ribbon 主页 + 上下文工具选项卡 | RIB-01 | 5d |
| 4.5 | 详情窗格（含多选聚合统计） | VIEW-05 | 4d |
| 4.6 | 预览窗格（文本/图片/Markdown） | VIEW-06 | 4d |
| 4.7 | 属性对话框（摘要/安全 ACL 只读起步） | DLG-03 | 4d |
| 4.8 | 右键菜单自家命令 + 打开方式 | CTX-01,03 | 4d |
| 4.9 | Shell 扩展隔离进程 PoC（32/64 位双宿主） | §6.6, CTX-02 | 6d ⚠️ |
| 4.10 | AQS 筛选器语法子集 | SRC-02 | 4d |
| 4.11 | **管理员身份重启入口**：`ShellExecute runas` + 只传工作区状态文件路径 + 管理员模式下的扩展禁用与 UI 标识 | §5.4 | 4d |

4.9 是**预研性质任务**：若 PoC 无法稳定隔离，降级为"仅自家菜单 + 显式实验开关"，并在 PRD 把 CTX-02 移出 1.0 范围。

4.11 的验收要点（安全敏感）：

- [ ] 重启只传状态文件路径，不接受任意命令行参数透传
- [ ] 管理员模式下第三方扩展自动禁用，且界面明确告知（不得静默失效）
- [ ] UAC 取消/被策略禁止时给出可操作指引，不崩溃、不静默降级
- [ ] 非管理员日常使用路径完全不触发 UAC（`IFileOperation` 按需提权不受影响）

---

## 6. M5：打磨与发布（3 周）

| # | 任务 | 估时 |
| --- | --- | --- |
| 5.1 | 命令面板（`Ctrl+P` 模糊匹配所有命令） | 3d |
| 5.2 | 可访问性：键盘全可达、Narrator、高对比 | 4d |
| 5.3 | 本地化资源外置 + 中英校对 | 3d |
| 5.4 | 性能回归门禁进 CI（基准台跑硬阈值，超标即红） | 3d |
| 5.5 | 发布：自包含 + ReadyToRun + 单文件；包体与启动实测 | 3d |
| 5.6 | 崩溃日志与恢复（`workspace.json` 损坏时可重建） | 2d |
| 5.7 | 文档：用户手册、快捷键表、已知问题 | 2d |
| 5.8 | 兼容性矩阵验证：Win10 22H2 / 21H2 LTSC / Win11 23H2·24H2·25H2，**x64**，100/125/150/200% DPI | 4d |
| 5.9 | **设置导入导出与漫游**：`.filesconfig` 打包/解包、schema 迁移、合并/覆盖/另存为、漫游目录重定向、便携模式 | §5.5 | 4d |
| 5.10 | 快捷键完整验收：逐项比对 Explorer 键位表，并验证重映射后的 UI 提示同步 | §5.6 | 2d |

---

## 7. 贯穿性工作流（每个里程碑都要做）

### 7.1 测试矩阵

| 层 | 工具 | 覆盖重点 | 门槛 |
| --- | --- | --- | --- |
| 单元 | MSTest（已就位） | 路径解析、排序、搜索语法、布局树序列化 | 核心逻辑行覆盖 ≥80% |
| 契约 | 单元 + 真实 FS | 枚举、操作、图标在长路径/UNC/保留名/权限不足下行为 | 100% 边界用例 |
| 性能 | `bench/` 基准台 | 启动、工作集、滚动帧率、大目录、多栏 | **硬阈值，超标阻断合并** |
| UI | FlaUI 或 WinAppDriver（M2 引入） | 键盘导航、拖放、选择 | P0 交互全绿 |
| 静态 | 自定义 Roslyn analyzer（M1） | 禁止绑定转换器格式化、禁止 UI 线程 IO、Core 不得引用 WPF | 0 违规 |

性能基准台是本项目**最重要的资产**：它让"最高 UI 性能"从口号变成 CI 上的数字。M1.5 就建立，之后每个 PR 都跑。

### 7.2 CI/CD

- 本机无 Visual Studio，全链路走 `dotnet CLI` —— 这是好事，保证不依赖 IDE。
- GitHub Actions：`windows-latest`，`dotnet build` → `scripts/test.ps1` → `bench` 阈值检查 → 上传产物。
- ⚠️ 当前限制（本机 hosts 屏蔽 + 沙箱令牌）导致 `git push` 需在不受限模式执行；自动化推送前需先解决 hosts 或改用 SSH over 443。见 §8。

### 7.3 代码约定

`Nullable=enable`、`ImplicitUsings=enable`、`TreatWarningsAsErrors=true`（Release），文件名=类型名，`ConfigureAwait(false)` 在 Core 层统一，UI 层禁 `async void`（事件处理器除外）。

---

## 8. 环境与依赖问题（阻塞项，需处理）

| 问题 | 现状 | 处理 |
| --- | --- | --- |
| hosts 屏蔽 `github.com` 等 31 域名 → `127.0.0.1` | 已确认存在，10-15 修改 | 暂不处理：联网操作统一走不受限授权；如需 CI 自动推送再议 |
| 沙箱低完整性令牌阻断所有 TLS | 受限模式下 `git push`/`dotnet restore` 失败 | 网络操作统一走不受限授权 |
| `dotnet test` + VSTest 在 .NET 10 SDK 不可用 | 已改用 MTP 自包含运行器 | 已解决（`scripts/test.ps1`） |
| 多节点 MSBuild 在沙箱失败 | 需 `-m:1` | 已写进脚本与 README |
| NuGet scratch 锁目录权限 | 已用 `.dotnet/` 内临时目录绕开 | 已解决 |

**远程操作约定**：GitHub 操作使用 `gh`（已确认登录为 `runlive2006`，token 含 `repo`、`workflow` 权限）。但 `gh` 与 `git` 同样受 hosts 屏蔽与沙箱 TLS 限制，联网操作需在不受限模式下执行。**决策与待办一律记录在本地文档，不在 GitHub 建 Issue。**

---

## 9. 决策状态

D1 已确认（见 [ADR-001 §5](adr/ADR-001-runtime-and-ui-framework.md)），架构已冻结，可开工。全部决策记录在本地文档，**不使用 GitHub Issue 跟踪**。

| 事项 | 状态 |
| --- | --- |
| D1-1 运行平台 = Win10/11，仅 x64 | ✅ 已确认 |
| D1-2 UI 框架 = WPF / .NET 10 | ✅ 已确认 |
| D1-3 性能红线 = PRD §5.1 数值 | ✅ 已确认（实测后允许上调并附数据） |
| 多栏 + 多标签 | ✅ 已确认两者都要 |
| 管理员身份入口 | ✅ 已确认需要 |
| 快捷键跟随 Explorer | ✅ 已确认 |
| 设置漫游 / 导入导出 | ✅ 已确认需要 |
| 第三方右键 Shell 扩展是否进 1.0 | ⏳ 待定（4.9 PoC 后决定） |
| 双栏对比同步（Q8） | ⏳ 待定（M4 后评估） |

---

## 10. 下一步（可立即开工）

第一个 Sprint（M1 第 1 周，对应任务 1.1 / 1.4 / 1.5 / 1.14 / 1.15）：

1. 新建 `Files.Interop` 项目并接入解决方案；
2. `FindFirstFileEx` 枚举替换现有 `FileSystemDirectoryLister`；
3. 建立 `bench/` 基准台骨架 + 10 万项生成器；
4. 把性能探针（启动/工作集/帧计时）接到现有窗口，**先拿到今天这个骨架的真实性能基线**；
5. 路径正确性测试集（这是唯一比功能更优先的事，因为它决定后续返工量）；
6. `TabState` 骨架（单栏单标签），为 M2 的标签子系统铺路。

有了第 4 步的数字，才知道 WPF 路线离 400ms/58fps 有多远，也才知道 R3 要不要现在就启用。

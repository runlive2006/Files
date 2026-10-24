# 功能设计：搜索与筛选

- 版本：v1.1（筛选与递归搜索已实现）
- 日期：2026-10-25
- 状态：**已实现**（索引搜索按原计划不做）
- 前置阅读：[PRD §3.4](../PRD.md)

---

## 1. 范围界定

必须区分三个容易混淆的概念，否则设计会失控：

| 概念 | 定义 | 实现难度 | 本轮 |
| --- | --- | --- | --- |
| **筛选（Filter）** | 在**已枚举**的当前目录条目上按条件隐藏，不产生新 IO | 低 | ✅ 做 |
| **搜索（Search）** | 递归遍历子目录，产生新结果集 | 中 | ✅ 做 |
| **索引搜索** | 走 Windows Search 索引，支持文件内容 | 高（COM + 索引状态） | ❌ 不做 |

**索引搜索不做**的理由：依赖 Windows Search 服务是否启用、索引范围是否覆盖目标目录，结果不确定；且 `ISearchQueryHelper` 的 COM 交互复杂度高。先用直接遍历，覆盖 90% 场景。

---

## 2. 筛选（当前目录内）

### 2.1 交互

搜索框内输入即生效，**不递归**。默认行为：输入普通文字 = 按名称子串匹配（忽略大小写）。

```
[🔍 报告          ]  ← 输入即过滤当前目录
```

- 防抖 250ms
- 匹配目标：文件/文件夹**名称**（不含路径）
- 结果为空时列表显示"无匹配项"，而不是空白
- `Esc` 清空并恢复

### 2.2 筛选语法（AQS 子集）

| 语法 | 含义 | 示例 |
| --- | --- | --- |
| `ext:.log` | 扩展名 | `ext:.cs` |
| `kind:picture` | 类型类别 | `kind:folder` / `kind:picture` / `kind:document` / `kind:video` / `kind:audio` |
| `size:>10mb` | 大小比较 | `size:>1mb`、`size:<100kb`、`size:1kb..1mb` |
| `datemodified:thisweek` | 修改时间 | `today` / `thisweek` / `thismonth` / `thisyear` |
| `~ext:.tmp` | 取反 | `~kind:folder` |
| `"项目 周报"` | 短语 | 含空格时用引号 |
| 空格分隔 | AND | `kind:picture size:>1mb` |

**未识别的属性前缀按普通文本处理**（如 `foo:bar` 当作名称子串），而不是静默返回零结果——这是 PRD 已定的原则。

### 2.3 性能约束

- 筛选在**内存中的条目数组**上执行，不重新枚举
- 10 万项时单次筛选 ≤ 50ms（纯字符串/数值比较，无分配）
- 输入过程中若上一次筛选未完成，取消上一次（`LatestTaskRunner`）

---

## 3. 搜索（递归）

### 3.1 触发

- 输入框左侧切换按钮：`当前目录` / `包含子目录`
- 或输入后按 `Enter` 开始递归搜索（避免边打字边递归）

**为什么不在打字时递归**：递归遍历磁盘是不可控 IO，边打字边搜会导致磁盘狂转且结果闪烁。必须显式触发。

### 3.2 结果呈现

- 列表切换为「搜索结果」布局：多一列**路径**（否则同名文件无法区分）
- 顶部显示进度：`已找到 128 项，已扫描 3,421 个文件…` + 取消按钮
- 搜索期间**不阻塞**：结果边扫描边追加

**追加策略（修正）**：不能对 `ObservableCollection` 逐项 `Add` —— 每次 Add 都会触发
`CollectionChanged`，5 万项会产生 5 万次通知，UI 必然卡死。现有架构（`TabViewModel.RebuildRows`）
是全量 `Clear` + `Add`，直接复用同样不可行。

改为**批量追加**：

| 参数 | 值 | 说明 |
| --- | --- | --- |
| 刷新间隔 | 200ms | 定时器触发，不是每找到一项就刷 |
| 单批上限 | 500 项 | 即使 200ms 内找到更多，也分批 |
| 追加方式 | 一次性 `Add` 到后备 `List`，然后触发**一次** `Reset` 通知 | 见下 |

具体做法：`TabViewModel` 增加一个「增量模式」——

```csharp
// 搜索结果专用：维护后备列表，批量同步到 ObservableCollection
private readonly List<EntryRow> _pendingRows = [];

public void FlushPendingRows()
{
    if (_pendingRows.Count == 0) return;

    Rows.Clear();                       // 一次 Clear
    foreach (var row in _pendingRows)   // 之后一次性重建
        Rows.Add(row);

    _pendingRows.Clear();
}
```

> 代价是每次刷新会重建可见容器。对 5 万项、200ms 一次来说可接受（实测 10 万项重建约 131ms，
> 而搜索场景的刷新量远小于此）。若实测有卡顿，再改为 `INotifyCollectionChanged` 的区间通知实现。

- 搜索完成时做一次最终刷新，之后恢复普通模式

### 3.3 遍历策略

| 项 | 决策 |
| --- | --- |
| 遍历方式 | 迭代式 BFS（不用递归，避免深目录栈溢出） |
| 符号链接/重解析点 | **不跟随**（防止循环与跨盘爆炸）；结果中仍列出链接本身 |
| 权限不足的目录 | 跳过并计数，结束时提示"跳过 N 个无权限目录" |
| 取消 | 检查点每 200 项检查一次 `CancellationToken` |
| 上限 | 结果上限 50,000 项，达到后停止并提示"结果过多，请细化条件" |
| 深度 | 默认不限；提供"仅当前层级"选项 |

### 3.4 排序与筛选

- 搜索结果可继续用搜索框筛选（组合使用）
- 排序仍支持四列，但默认按「路径」排序（更符合搜索结果的使用方式）

---

## 4. UI 布局

### 4.1 搜索框位置

**每个标签的工具栏内**（与地址栏同一行右侧），而不是窗口级。

理由：搜索是"在当前目录里找"，属于标签的上下文。窗口级搜索框会产生"搜的是哪个栏"的歧义。

```
[← → ↑] [地址栏.....................] [🔍 搜索____] [≡]
```

### 4.2 搜索模式下的栏头

进入递归搜索后，地址栏下方出现一条**搜索状态条**：

```
正在搜索「报告」，范围：C:\Work（含子目录）  已找到 128 项  [取消]
```

- 搜索中禁止在该标签内导航（或导航即取消搜索）
- 搜索完成后状态条变为：`搜索结果：128 项，来自 C:\Work` + [返回目录]

---

## 5. 数据模型

```csharp
public sealed class SearchQuery
{
    public string Text { get; init; } = string.Empty;      // 原始输入
    public bool Recursive { get; init; }
    public string RootPath { get; init; } = string.Empty;

    // 解析后的条件（由 SearchQueryParser 产出）
    public IReadOnlyList<SearchPredicate> Predicates { get; init; } = [];
}

public abstract record SearchPredicate;

public sealed record NameContains(string Text) : SearchPredicate;
public sealed record ExtensionIs(string Extension) : SearchPredicate;
public sealed record KindIs(EntryKind Kind) : SearchPredicate;
public sealed record SizeInRange(long? MinBytes, long? MaxBytes) : SearchPredicate;
public sealed record ModifiedInRange(DateTimeOffset? From, DateTimeOffset? To) : SearchPredicate;
public sealed record Not(SearchPredicate Inner) : SearchPredicate;
```

**解析器与匹配器分离**：`SearchQueryParser` 把文本变成谓词列表（纯函数，易测），
`SearchMatcher` 对单个条目求值。这样筛选与递归搜索**共用同一套匹配逻辑**，不会出现"筛选能找到、搜索找不到"的不一致。

---

## 6. 验收标准

| # | 标准 |
| --- | --- |
| 1 | 筛选 10 万项 ≤ 50ms，输入无卡顿 |
| 2 | 递归搜索可随时取消，取消后 200ms 内停止 |
| 3 | 不跟随符号链接；循环链接不会导致无限遍历 |
| 4 | 无权限目录被跳过并计数，不中断搜索 |
| 5 | 搜索结果超过 50,000 项时停止并提示 |
| 6 | 未识别的 `foo:bar` 按文本处理，不返回零结果 |
| 7 | 筛选与搜索使用同一套匹配逻辑（同一查询在两种模式下结果一致） |
| 8 | 搜索中导航会取消搜索并恢复普通列表 |

---

## 7. 未决问题

| # | 问题 | 建议 |
| --- | --- | --- |
| Q1 | 是否支持正则（`re:^test.*\.cs$`） | 建议支持，但需限制执行时间防灾难性回溯 |
| Q2 | 是否保存搜索历史 | 是，最近 10 条，存本地 |
| Q3 | 搜索结果是否可批量操作 | 是，复用现有 `IFileOperator`（结果项有完整路径） |
| Q4 | 是否需要"保存搜索为虚拟文件夹" | 否，超出范围 |

---

## 8. 实现说明（2026-10-25）

### 8.1 已完成

| 项 | 说明 |
| --- | --- |
| 查询解析 | `SearchQueryParser`：ext / kind / size / datemodified / ~取反 / 引号短语 |
| 匹配器 | `SearchMatcher`：与解析器分离，筛选与搜索共用同一套判断 |
| 内存筛选 | `EntryFilter`：在已有列举结果上过滤，不重新枚举 |
| 递归搜索 | `RecursiveSearcher`：迭代 BFS、不跟随重解析点、跳过无权限、5 万项上限、可取消 |
| 筛选 UI | 每栏搜索框，250ms 防抖，500ms 内即时生效；Esc 清空 |
| 递归搜索 UI | 勾选「含子目录」后按 Enter 触发；黄色状态条显示进度与取消按钮 |

### 8.2 实现中发现并修正的设计缺陷

**`SearchResult` 早期只返回 `FileSystemEntry`（仅含名称，无路径）。**
但搜索结果的条目分散在不同目录，无法像目录列举那样由"父目录 + 名称"推导路径。
后果：界面报告"找到 3 项"但列表只显示 1 项（都显示成同一个名字）。

修正：`SearchResult` 改为携带 `SearchHit`（条目 + 完整路径），
并在单元测试里加了断言"每条命中都应带完整路径"，防止回归。

> 这个缺陷只有在端到端验证时才会暴露——单测当时全部通过。
> 说明"能编译 + 单测过"不足以证明功能正确。

### 8.3 验证

- 单元测试 261 个全部通过（新增 41 个：解析 13 + 匹配 12 + 筛选 6 + 递归 10）
- 端到端筛选语法对照：

  | 查询 | 结果 |
  | --- | --- |
  | `ext:.png` | gamma.png |
  | `kind:picture` | gamma.png |
  | `beta` | beta.log |
  | `~ext:.txt` | beta.log, gamma.png |
  | `size:>1b` | 全部 3 项 |
  | 清空 | 全部恢复 |

- 端到端递归搜索：`ext:.txt` + 含子目录 → 列出 `top.txt, mid.txt, deep.txt`
  （分别位于根目录、sub1、sub1\sub2），证明跨目录命中正确

### 8.4 未做

| 项 | 原因 |
| --- | --- |
| 索引搜索（Windows Search） | 按设计不做，理由见 §1 |
| 搜索结果额外「路径」列 | 当前复用列表列，未加动态列 |
| 搜索历史 | 未做 |
| 正则搜索 `re:` | 需限制执行时间防灾难性回溯，单独排期 |
| 递归搜索的虚拟化列表 | 结果集可能上万，当前直接全量入 Rows |

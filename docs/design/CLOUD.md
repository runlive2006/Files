# 功能设计：云盘与网络位置

- 版本：v1.0（WPS 云盘已实现）
- 日期：2026-10-25
- 状态：**WPS 云盘已实现**；其他云盘与网络位置待定
- 前置阅读：[NAVIGATION-AND-PANES.md](NAVIGATION-AND-PANES.md)

---

## 1. 关键调研结论（决定了整个方案）

在动手前先查清了 WPS 云盘在系统中的真实存在形式，这直接决定了实现成本：

| 调研项 | 结果 |
| --- | --- |
| 是否映射为盘符 | ❌ 不是。`DriveInfo.GetDrives()` 只有 C: 和 D: |
| 注册方式 | ✅ Shell 命名空间扩展，CLSID `{EEEEFCF7-867B-4FA2-9ABD-884CF531B612}`，名称「WPS云盘」 |
| 是否有真实本地路径 | ✅ **有**：`D:\Docs\WPS\WPS Cloud\WPSDrive\<uid>\WPS云盘` |
| 能否用普通文件 API 访问 | ✅ 可以枚举、可以进入子目录 |
| 目录属性 | ⚠️ 全部带 `ReparsePoint`（云占位符机制） |

**结论：不需要 `IShellFolder` / PIDL interop。**

这是本次最重要的判断。如果是"纯虚拟文件夹"（如「网络位置」「控制面板」，`Self.Path` 为空或 `::{CLSID}` 形式），
就必须实现完整的 Shell 命名空间枚举，工作量是数量级的差异。WPS 云盘属于前者，因此：
**用 Shell 拿到真实路径，之后全部复用现有文件系统实现。**

---

## 2. 方案

```
ShellCloudLocationProvider（Files.Interop）
    ↓ Shell.Application.Namespace("::{CLSID}").Self.Path
CloudLocation(DisplayName, Path, Provider)
    ↓ 注入
NavigationTreeBuilder
    ↓ 生成「云盘」分组节点
导航树 → 点击 → 普通目录枚举（复用现有实现）
```

### 2.1 为什么探测放 Interop、抽象放 Core

- Core 定位是平台无关；`Type.GetTypeFromProgID` 只在 Windows 可用（会触发 CA1416）
- 抽象 `ICloudLocationProvider` 留在 Core，便于单元测试注入假数据

### 2.2 必须校验"路径真实存在"

`folder.Self.Path` 对纯虚拟文件夹会返回空或 `::{CLSID}`。因此探测时强制校验：

```csharp
if (string.IsNullOrWhiteSpace(path)
    || path.StartsWith("::", StringComparison.Ordinal)
    || !Directory.Exists(path))
{
    return null;   // 放弃该命名空间
}
```

不加这一条，「控制面板」之类的虚拟位置会被误当成目录，点进去必然失败。

---

## 3. 重解析点策略（本次最关键的技术点）

### 3.1 问题

云盘目录**全部带 `ReparsePoint` 属性**。而递归搜索为了防循环，默认不跟随重解析点：

```csharp
// 修改前
if (isDirectory && !isReparse) { pending.Enqueue(...); }
```

直接沿用会导致：**云盘里的文件完全搜不到**（这正是实现后第一时间要验证的点）。

### 3.2 区分办法

实测发现两者可区分：

| 类型 | `ReparsePoint` | `ResolveLinkTarget` 结果 |
| --- | --- | --- |
| 真实 junction / 符号链接 | ✅ | **非 null**（能解析出目标） |
| 云盘占位符 | ✅ | **null**（驱动层挂载标记，无目标） |

据此制定策略（`ReparsePointPolicy`）：

- 能解析出目标 → 真实链接 → **不跟随**（防循环、防跨盘爆炸）
- 解析不出目标 → 云占位符 → **可进入**（它就是普通目录）

### 3.3 验证

- 单测：junction 用例断言"真实链接不可进入"；普通目录断言"可进入"
- 端到端：在 WPS 云盘根目录递归搜索 `ext:.docx` → **找到 5,329 项**
  （若策略未生效会是 0 项）

---

## 4. 已实现范围

| 项 | 说明 |
| --- | --- |
| WPS 云盘探测 | Shell 命名空间 → 真实路径，失败静默返回空 |
| 导航树节点 | 「云盘」分组下显示「WPS云盘」，点击可进入 |
| 浏览 | 复用现有枚举，实测列出 9 个子目录 |
| 递归搜索 | 可穿透云盘（重解析点策略） |
| 多个云盘 | 结构支持；当前只注册了 WPS |

---

## 5. 未实现 / 待定

| 项 | 说明 |
| --- | --- |
| 其他云盘 | OneDrive（`{018D5C66-...}`）已探测到，但本环境未登录，因此未接入 |
| 网络位置（工作组/共享） | 需 `WNetEnumResource`，枚举可能很慢且失败形态复杂，未做 |
| 纯虚拟文件夹 | 如「控制面板」需完整 `IShellFolder` interop，不在范围 |
| 云盘文件状态图标 | 同步中/已同步/仅云端等 overlay，需 `IShellIconOverlay` |
| 上传/下载进度 | 云盘自己的客户端负责，本程序不接管 |

---

## 6. 扩展其他云盘的做法

在 `ShellCloudLocationProvider.KnownClouds` 里加一行即可：

```csharp
("onedrive", "{018D5C66-4533-4307-9B53-224DE2ED1FE6}", "OneDrive"),
```

前提是该云盘在 `Shell.Application.Namespace` 下能取到非空的 `Self.Path` 且目录存在。
若某云盘是纯虚拟文件夹（无真实路径），本方案不适用，需要单独实现 Shell 枚举。

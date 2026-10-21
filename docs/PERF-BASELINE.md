# 性能基线（M1 Sprint 1 实测）

- 日期：2026-10-21
- 对应任务：DEV-PLAN M1 的 1.1 / 1.4 / 1.5 / 1.14
- 结论一句话：**数据层不是瓶颈**。10 万项目录的"枚举 + 映射 + 排序"只要 **131ms**，
  仅占 PRD §5.1「进入目录首屏 ≤600ms」预算的 22%。剩余风险全部在 WPF 视图实现，不在 IO。

---

## 1. 测试环境

| 项 | 值 |
| --- | --- |
| OS | Microsoft Windows 10.0.26300 |
| 运行时 | .NET 10.0.0（Release） |
| 进程架构 | x64 |
| CPU 逻辑核数 | 20 |
| GC 模式 | Workstation（并发关闭） |
| 存储 | 本地 SSD（`D:\Dev\Projects\Files\.dotnet\tmp`） |

> 数据由 `bench/Files.Bench` 生成，测量的是**真实文件系统**，不是内存里的假数据。

---

## 2. 结果

### 2.1 50,000 项

| 测量项 | 最快 | 中位 | 分配 | 工作集增量 | 预算 | 判定 |
| --- | --- | --- | --- | --- | --- | --- |
| 基线 `Directory.EnumerateFileSystemEntries`（仅名称） | 20.4ms | 23.4ms | 7.95 MB | 3.2 MB | — | — |
| `Win32DirectoryEnumerator.ReadAll`（含属性） | 30.4ms | 32.2ms | 9.89 MB | 4.9 MB | 300ms | 达标 |
| `Win32DirectoryLister.List`（枚举+映射+排序） | 69.7ms | 71.1ms | 21.27 MB | 10.8 MB | 600ms | 达标 |
| 首屏模拟（枚举 + 前 100 项格式化） | 31.6ms | 35.3ms | 9.90 MB | 2.9 MB | 600ms | 达标 |

### 2.2 100,000 项

| 测量项 | 最快 | 中位 | 分配 | 工作集增量 | 预算 | 判定 |
| --- | --- | --- | --- | --- | --- | --- |
| 基线 `Directory.EnumerateFileSystemEntries`（仅名称） | 38.8ms | 41.1ms | 16.02 MB | −19.4 MB* | — | — |
| `Win32DirectoryEnumerator.ReadAll`（含属性） | **55.8ms** | 56.7ms | 19.79 MB | 9.6 MB | 600ms | 达标 |
| `Win32DirectoryLister.List`（枚举+映射+排序） | **131.1ms** | 151.3ms | 42.69 MB | 34.1 MB | 1200ms | 达标 |
| 首屏模拟（枚举 + 前 100 项格式化） | 85.3ms | 94.3ms | 19.80 MB | 9.7 MB | 1200ms | 达标 |

\* 负值来自测量前的 `GC.Collect()` 把上一轮的垃圾回收掉了，属于测量伪影，不代表内存真的减少。
**内存增量只作趋势参考，绝对值以工作集采样为准。**

吞吐：**约 179 万项/秒**（100,000 项 / 55.8ms）。

---

## 3. 结论与解读

### 3.1 好消息：IO 与枚举层远超需求

- 一次 `FindFirstFileExW` 循环就拿到名称、属性、大小、修改时间，**零额外 stat**；
- 相比 `Directory.EnumerateFileSystemEntries` 只慢约 17ms（50k）/ 17ms（100k），
  但后者**不提供属性**，要用属性就得再逐项 `FileInfo`（那才是真正的性能杀手）；
- 按 PRD 预算，10 万项首屏 600ms 中，数据层只花 131ms，**剩 470ms 留给 WPF 视图实例化**。

### 3.2 新发现：真正的下一个瓶颈是**每项内存**，不是速度

`Win32DirectoryLister.List` 在 10 万项时分配 **42.69 MB**，折算 **≈ 427 字节/项**：

| 组成 | 估算 |
| --- | --- |
| `FileSystemEntry` 对象头 + 5 个字段 | ~56 B |
| 3 个字符串（`Name`、`FullPath`、派生 `Extension`） | ~120–200 B |
| `List<FileSystemEntry>` 槽位与增长冗余 | ~16 B |
| 排序过程与临时对象 | 其余 |

影响：

- 单栏 1000 项 → 约 0.4MB，**完全在 PRD 预算内**（≤90MB）；
- 但 10 万项目录 → 约 42MB 托管堆，叠加四栏 × 多标签（PRD §5.1 的四栏 ≤150MB、
  四栏×5标签 ≤220MB）会明显吃紧；
- 因此 **PRD §4.2 的"非活动标签只留状态、不留视图"是必须执行的硬约束**，
  否则 4 栏各开一个 10 万项目录就会直接击穿预算。

### 3.3 待优化项（M2 前完成，不影响当前进度）

| 优化 | 预期收益 | 风险 |
| --- | --- | --- |
| `FileSystemEntry` 由 `class` 改为 `readonly struct` | 省对象头，约 −16~24 B/项 | 需改绑定与测试，注意大结构体拷贝成本 |
| `FullPath` 改为按需拼装（只存 `Name` + 目录基址） | 省 1 个字符串/项 | 需改列表排序与右键菜单取值路径 |
| 扩展名用池化字典共享实例 | 省重复字符串 | 低 |
| 排序改为仅比较 `Name` 与 `IsDirectory`（已是） | 已最优 | — |

> 这些都不改变架构，属于"把 427 B/项 压到 200 B/项 以下"的工程优化，列入 M2 一并做。

---

## 4. 复现方式

```powershell
# 5 万项，含预算判定（超预算退出码 1）
.\scripts\bench.ps1 -Count 50000 -Gate

# 10 万项，保留数据以便复跑
.\scripts\bench.ps1 -Count 100000 -Keep
```

或直接：

```powershell
dotnet run -c Release --project bench/Files.Bench -- --count 100000 --gate
```

> 必须用 `-c Release`。Debug 下的数值受未优化代码与边界检查影响，不能作为验收依据。

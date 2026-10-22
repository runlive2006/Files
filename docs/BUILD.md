# 构建说明

面向需要自行构建本程序的用户。全程只需一条命令。

---

## 1. 前置条件

| 项 | 要求 |
| --- | --- |
| 操作系统 | Windows 10 22H2+ / Windows 11，**仅 x64** |
| .NET SDK | **10.x**（项目 `global.json` 要求 10.0.100，允许向上滚动） |
| 磁盘 | 首次构建约 500 MB（SDK 缓存 + 中间产物） |
| 网络 | 首次构建需要联网还原 NuGet 依赖 |

安装 SDK：<https://dotnet.microsoft.com/download/dotnet/10.0>

验证安装：

```powershell
dotnet --version
```

应输出 `10.0.x`。若提示找不到命令，需重新打开终端（安装程序修改了 PATH）。

---

## 2. 一键构建

### 方式 A：双击（推荐给不熟悉命令行的用户）

双击 `scripts\build.cmd`，等待结束，窗口会显示产物路径。

### 方式 B：命令行

```powershell
.\scripts\build.ps1
```

### 方式 C：自动化 / CI

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\build.ps1
```

> `build.cmd` 结束时会暂停等待按键，**CI 里请直接调用 `build.ps1`**。

---

## 3. 脚本做了什么

```
[1] 检查构建环境      dotnet 是否存在、SDK 主版本是否匹配 global.json
[2] 确定版本号        读取 csproj 的 <Version>，或用 -Version 覆盖
[3] 检查运行实例      输出目录被正在运行的 Files.exe 占用时提前报错
[4] 还原依赖          固定缓存到 .dotnet/packages，不污染全局缓存
[5] 构建              Release，单节点，禁用构建服务器
[6] 运行单元测试      175 个用例，失败则中止发布
[7] 清理输出目录      只清内容不删目录；失败则降级为覆盖发布
[8] 发布              框架依赖，win-x64
[9] 汇总              产物路径、版本、文件数、总大小
```

任何一步失败都会以**非零退出码**结束，并给出可操作的提示。

---

## 4. 参数

| 参数 | 说明 | 示例 |
| --- | --- | --- |
| `-Configuration` | `Release`（默认）/ `Debug` | `-Configuration Debug` |
| `-Version` | 覆盖版本号 | `-Version 0.2.0` |
| `-SkipTests` | 跳过单元测试，只构建发布 | `-SkipTests` |
| `-OutputDirectory` | 自定义输出目录 | `-OutputDirectory D:\out` |

示例：

```powershell
# 快速出包，不跑测试，版本号 0.2.0
.\scripts\build.ps1 -SkipTests -Version 0.2.0

# 输出到指定目录
.\scripts\build.ps1 -OutputDirectory D:\Temp\FilesBuild
```

---

## 5. 产物

```
artifacts\Files-<版本>-win-x64\
├─ Files.exe                ← 双击运行
├─ Files.dll
├─ Files.Core.dll
├─ Files.Interop.dll
├─ *.pdb                    调试符号
├─ Files.deps.json
└─ Files.runtimeconfig.json
```

**这是「框架依赖」形态**：目标机器需要安装 .NET 10 桌面运行时。

刻意不提供：

- **不打包 zip** —— 直接交付目录，替换单个 exe 即可做增量测试；
- **不生成自包含单文件** —— 单文件自解压会写 `%TEMP%\.net`，与「数据只写程序目录」的约束冲突。

运行时数据全部写在 exe 同级的 `data\` 目录（首次运行自动创建）。

---

## 6. 常见问题

### 提示"未找到 dotnet 命令"

未安装 SDK，或安装后没有重开终端。安装地址见上文。

### 提示"需要 .NET SDK 10.x"

机器上只有其他主版本的 SDK。安装 10.x 即可，多个版本可共存。

### 提示"检测到正在运行的 Files 进程"

先关闭已打开的 Files 窗口。输出目录里的 exe 被占用时无法复制新文件。

### 提示"依赖还原失败"

- 检查网络与代理；
- 若在公司网络，可能需要配置 NuGet 源或证书。

### 构建成功但运行报 SmartScreen 拦截

未签名程序的正常行为。点「更多信息」→「仍要运行」，或先执行包内 `unblock.cmd`。
彻底解决需要代码签名证书，详见 [DEPLOY.md](DEPLOY.md)。

### 构建很慢

首次构建要还原依赖，之后增量构建约 20 秒。脚本使用单节点构建（`-m:1`）以保证在受限环境下也能成功，这是刻意的可靠性取舍。

---

## 7. 其他脚本

| 脚本 | 用途 |
| --- | --- |
| `scripts\test.ps1` | 只跑单元测试（支持 `-Configuration`） |
| `scripts\bench.ps1` | 目录枚举性能基准（`-Count 100000 -Gate`） |
| `scripts\make-icon.ps1` | 重新生成应用图标（`Assets\Files.ico`） |
| `scripts\verify-render.ps1` | 用 UI Automation 验证列表实际渲染条目数 |
| `scripts\unblock.ps1` | 解除下载文件的网络锁定标记 |

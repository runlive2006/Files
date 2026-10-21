# 分发与运行说明

- 版本：v0.1.0-m1（临时测试包）
- 日期：2026-10-21

---

## 1. 数据存放位置（强制约束）

**本程序不写 `%APPDATA%`、`%LOCALAPPDATA%`、系统临时目录或注册表。**

所有可写数据都在 exe 同级的 `data\` 目录下：

```
<exe 所在目录>\
├─ Files.exe
├─ Files.dll / Files.Core.dll / Files.Interop.dll
└─ data\
   ├─ settings.json      配置与窗口/布局状态
   ├─ logs\              崩溃日志 crash-*.log
   └─ temp\              进程临时目录（含 WPF\ 光标缓存）
```

含义：

- **绿色便携**：整个文件夹拷走即完成迁移；
- **彻底清除**：删除文件夹即无残留；
- **多份实例互不干扰**：把文件夹复制两份，各自有独立配置。

### 如果程序目录不可写

放在 `C:\Program Files` 等受保护位置时会启动失败，并明确提示：

> 无法在程序所在目录写入数据。请把程序放到你有写入权限的文件夹中（例如 `D:\Tools\Files`）。

这是刻意设计：**明确的失败优于渲染阶段的未处理异常**。

### 一点说明

`data\temp` 只收拢**本进程**的临时文件。Windows 自身的组件（如 WPF 字体缓存服务）
仍可能写入系统自己的位置，那部分不受应用控制，也不属于应用数据。

---

## 2. SmartScreen「Windows 已保护你的电脑」

### 为什么会出现

`Files.exe` **没有代码签名**。Windows 对"无签名 + 无下载信誉"的可执行文件会弹出
SmartScreen 拦截。这与程序行为无关，也无法通过修改程序本身消除。

### 三种处理方式

#### A. 临时放行（测试用，最快）

弹窗中点 **「更多信息」** → **「仍要运行」**。

#### B. 解除网络锁定标记（如果文件是从网络/聊天工具传过来的）

下载的文件会被打上 `Zone.Identifier`（Mark-of-the-Web），这是触发拦截的直接原因之一。

双击 `scripts\unblock.cmd`，或手动执行：

```powershell
Unblock-File .\Files.exe
```

> 只移除标记，不改文件内容、不改系统设置。

#### C. 彻底解决（对外分发必须做）

购买**代码签名证书**并对 exe 签名：

| 证书类型 | 效果 | 成本 |
| --- | --- | --- |
| **EV 代码签名证书** | 立即获得 SmartScreen 信誉，**不再弹窗** | 较高，需企业资质与硬件令牌 |
| OV 代码签名证书 | 签名后显示发布者名称，但仍需累积下载信誉才不弹窗 | 中 |

签名命令：

```powershell
signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /a Files.exe
```

> 自签名证书**不能**解决该问题：只会把"未知发布者"变成"不受信任的发布者"，提示更严重。

### 结论

- **本机/内部测试**：用 A 或 B 即可。
- **对外发布**：必须购买 EV 证书。这是 Windows 平台的既有规则，不是本项目的实现缺陷。

---

## 3. 普通用户启动失败（已修复）

**现象**：管理员能启动，普通用户报

```
发生未处理的错误：Access to the path 'D:\Temp\user\WPF\xxxx.lek' is denied.
```

**原因**：WPF 渲染列表列头时会把光标资源写入 `%TEMP%\WPF\*.lek`。
本机 `%TEMP%` 指向 `D:\Temp\user`，该目录对普通用户不可写，异常从
`Cursor.LoadFromStream` 抛出并终止进程。

**修复**：程序在创建任何界面元素之前，把进程 `TEMP`/`TMP` 指向
`<exe>\data\temp`，不再依赖系统临时目录。因此普通用户与管理员行为一致。

**验证方法**：普通账户启动后，确认 `data\temp\WPF` 目录被创建 —— 说明 WPF 的
光标缓存已落在程序目录内，不再触碰 `D:\Temp\user`。

---

## 4. 运行要求

| 项 | 要求 |
| --- | --- |
| 操作系统 | Windows 10 22H2+ / Windows 11，**仅 x64** |
| 运行时 | .NET 10 桌面运行时（框架依赖包） |
| 权限 | **普通用户即可**，无需管理员 |
| 磁盘 | 约 1 MB（不含数据） |

若目标机器没有 .NET 10 运行时，需要改用自包含发布（体积约 90 MB）。

---

## 5. 打包命令（开发者用）

```powershell
# 框架依赖（体积小，需目标机装 .NET 10 桌面运行时）
dotnet publish src/Files.App/Files.App.csproj -c Release -r win-x64 `
  --self-contained false -m:1 -o artifacts/Files-0.1.0-win-x64
```

打包后请检查：`artifacts/Files-0.1.0-win-x64/data/` 不应存在于交付包中
（那是运行后生成的，删除即可保持干净）。

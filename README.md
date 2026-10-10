# 隐私屏 · PrivacyScreen

> 一键让你面前的显示器**整块变黑**，从外面看就像电脑关机了。
> 而电脑**照常运行** —— 后台任务、下载、编译、远程连接都不受影响，
> **远程端看到的仍然是正常桌面**。

一个 Windows 桌面小工具，C# + WinForms 编写，单文件绿色免安装。

---

## 效果

```
┌─────────────────────────────┐
│  ▓▓ 黑色遮罩窗（置顶）        │──────→  本地物理显示器：整块全黑
└─────────────────────────────┘
┌─────────────────────────────┐
│  ░░ 你的真实桌面              │─┈┈┈┈→  远程端画面：正常桌面，可操作
└─────────────────────────────┘
```

---

## 核心特性

| 特性 | 说明 |
|---|---|
| **本地黑 / 远程正常** | 遮罩窗被设为「捕获排除」，本地物理屏全黑，而截图、录屏、远程软件拍不到它，因此远程端看到的是正常桌面 |
| **多显示器按需选择** | 启动时用图形化面板勾选「只黑哪一块 / 两块全黑」，支持记忆上次选择 |
| **黑屏中切换范围** | 无需退出黑屏，直接在托盘菜单里勾选/取消某一块屏 |
| **全局自定义快捷键** | 任意程序里都能触发，快捷键在独立线程注册，主界面卡死也不影响退出 |
| **四条独立退出通道** | 主快捷键 / 备用快捷键 / 连续双击鼠标左键 / 连按两次 Esc，任何一条都能救你出来 |
| **全黑时隐藏鼠标** | 指针也一起消失，看起来更像关机 |
| **托盘常驻** | 不占任务栏，右下角小图标随时唤起 |
| **纯本地、不联网** | 不采集任何数据，配置存 `%APPDATA%\Blackout\settings.json` |

---

## 快速开始

### 下载即用

到 [**Releases**](https://github.com/YOUTOMS/PrivacyScreen/releases/latest) 页面下载：

| 版本 | 大小 | 说明 |
|---|---|---|
| `PrivacyScreen-standalone.exe` | 约 63 MB | **推荐**。自带 .NET 运行时，任何 64 位 Windows 10/11 直接双击运行，**不用装任何东西** |
| `PrivacyScreen-lite.exe` | 约 209 KB | 精简版，需目标机已安装 .NET 8 桌面运行时 |

### 使用三步

1. **双击 exe** —— 不弹主窗口，只在右下角托盘出现一个小图标
2. **勾选要黑的显示器** —— 弹出图形化面板，按真实左右位置示意，勾完点「开始黑屏」
3. **退出时按 `Ctrl + Alt + B`**

### 默认快捷键

| 功能 | 快捷键 |
|---|---|
| 进入黑屏 | `Ctrl + Alt + M` |
| 退出黑屏（主） | `Ctrl + Alt + B` |
| 退出黑屏（备用） | `Ctrl + Alt + N` |
| 退出（鼠标） | 连续双击鼠标左键 |
| 退出（键盘） | 连按两次 `Esc` |

> **永远退不出来怎么办？**
> 按 `Ctrl + Alt + Del` 打开任务管理器结束 `PrivacyScreen` 进程即可 —— 这是 Windows 安全键，任何软件都屏蔽不了。
> 实在不行长按电源键重启。**本工具除了画一层黑图，没有修改系统任何设置，重启后干干净净。**

---

## 工作原理

核心只有一个 Windows 官方 API：

```csharp
SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);  // 0x00000011
```

它告诉系统：**这个窗口从截屏/录屏接口里"抠掉"**。于是：

- 本地物理显示器：正常绘制遮罩 → **看到全黑**
- 任何走标准捕获路径的截图/录屏/远程软件：捕获不到遮罩 → **看到遮罩后面的正常桌面**

两个配套的关键设计：

1. **遮罩窗必须鼠标穿透**（`WS_EX_TRANSPARENT` + `WM_NCHITTEST` 返回 `HTTRANSPARENT`）。
   否则远程端通过 `SendInput` 注入的鼠标事件会被顶层遮罩吃掉，远程就没法操作了。
2. **遮罩窗不抢焦点**（`WS_EX_NOACTIVATE` + `ShowWithoutActivation`）。
   让键盘输入继续流向原本的前台窗口。

---

## 系统要求

- 64 位 Windows 10 版本 2004（内部版本 19041）或更高 / Windows 11
- 独立版无需任何额外依赖；精简版需 .NET 8 桌面运行时

> 在更老的系统上程序仍可运行，但会**自动降级**：捕获排除不支持，远程端也会看到黑屏，并在托盘气泡中提示。

---

## 从源码构建

需要 .NET 8 SDK。

```bash
cd src/PrivacyScreen

# 独立版（自带运行时，约 63 MB）
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none \
  -o ../../dist-standalone

# 精简版（约 209 KB）
dotnet publish -c Release -r win-x64 --self-contained false \
  -p:PublishSingleFile=true -p:DebugType=none -o ../../dist
```

### 环境自检

程序自带诊断模式，用来排查「远程为什么没穿透」「快捷键为什么没反应」：

```bash
PrivacyScreen.exe --diag          # 弹出报告
PrivacyScreen.exe --diag --quiet  # 只写文件不弹窗
```

报告输出到 `%TEMP%\PrivacyScreen-diag.txt`，内容包括显示器列表、捕获排除 API 支持情况、快捷键占用检测、配置文件路径。

---

## 项目结构

```
PrivacyScreen/
├── PRD-隐私屏工具-v1.2.md          # 完整需求文档（15 章）
├── 使用说明.md                      # 面向普通用户的使用手册
└── src/PrivacyScreen/
    ├── PrivacyScreen.csproj
    ├── app.manifest                 # DPI 感知 / 权限声明
    ├── Program.cs                   # 入口、单实例、全局异常兜底
    ├── Diagnostics.cs               # --diag 环境自检
    ├── Native/Win32.cs              # Windows API 封装（P/Invoke）
    ├── Core/
    │   ├── AppSettings.cs           # 配置读写
    │   ├── MonitorItem.cs           # 显示器枚举与稳定标识
    │   ├── BlackoutController.cs    # 黑屏总控
    │   ├── HotKeyService.cs         # 全局快捷键（独立线程 + 消息泵）
    │   ├── EscapeHooks.cs           # 纯鼠标 / 双 Esc 逃生钩子
    │   └── CursorHider.cs           # 隐藏鼠标指针
    └── UI/
        ├── OverlayForm.cs           # 黑色遮罩窗（核心）
        ├── ScopeForm.cs             # 黑屏范围选择面板
        ├── TrayContext.cs           # 托盘与主流程编排
        └── TrayIconFactory.cs       # 托盘图标绘制
```

---

## 已知限制

- **黑屏 ≠ 锁机 ≠ 加密**。它只遮挡显示。如果对方已经能控制你的电脑，他仍然能操作。真需要防人，请配合 `Win + L` 锁屏使用。
- **全局快捷键谁按都生效**。本项目按需求**未做退出密码保护**，所以知道快捷键的人都能解除黑屏。
- **远程兼容性依赖具体远程软件**。它必须走标准屏幕捕获路径才能穿透。**已在作者本机实测通过（远程端看到正常桌面，2026-10-10）**；若你使用其它远程软件，建议先自行实测一次。
- **液晶屏背光仍然亮着**。很暗的环境下凑近看能发现屏幕其实没关。这是物理限制，软件解决不了；需要彻底请直接关显示器电源。

---

## 许可

本项目采用 [MIT 许可证](LICENSE) —— 你可以自由使用、修改、分发，只需保留版权声明。

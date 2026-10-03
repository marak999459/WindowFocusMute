<div align="right">

**简体中文** | [English](README.en.md)

</div>

# WindowFocusMute — Windows 窗口失焦自动静音

> **看视频 / 开会时，切走窗口就让指定应用自动闭嘴。**

监听 Windows 前台窗口切换：**名单内进程的窗口失去焦点时自动静音，获得焦点时自动恢复**。

WinForms 图形界面，.NET 8，C#，**零 NuGet 依赖**（纯 Win32 + COM 互操作）。

## 功能特性

- ✅ **失焦静音 / 聚焦恢复**：200ms 轮询前台窗口，名单内进程切走即静音、切回即恢复
- ✅ **图形界面**：进程列表勾选即保存生效，显示每个进程的 exe 图标、PID、窗口标题
- ✅ **任务栏标准过滤**：默认只列出任务栏上真实存在的窗口进程（可见 + 非工具窗口 + 未被 DWM 遮蔽），可切换显示全部进程
- ✅ **配置热重载**：`targets.txt` 手动编辑后自动重载，与界面勾选状态双向同步
- ✅ **智能恢复**：移出名单时自动恢复该进程声音，只切静音位、不碰音量值，系统音量不受影响
- ✅ **设备自适应**：默认音频设备切换 / COM 会话失效后自动重建控制器

## 使用

运行 `WindowFocusMute.exe`（构建后输出在项目根目录），弹出图形界面：

- **勾选进程** = 加入名单，取消勾选 = 移出名单，改动立即保存并生效。
- 列表默认只显示任务栏上有的窗口进程；勾选「显示所有进程」可看全部。
- 名单里已退出的进程仍会显示为「(未运行)」并保持勾选，方便下次启动后生效。
- 底部日志区实时显示静音/恢复动作。

进程名对照：任务管理器 →「详细信息」选项卡的「名称」列，去掉 `.exe` 后缀。

关闭窗口即退出程序。

## 配置文件

`targets.txt` 位于 exe 同目录，每行一个进程名（不含 `.exe`，大小写不敏感，`#` 开头为注释）：

```
# WindowFocusMute 目标进程名单
citizen sleeper
msedge
spotify
```

可手动编辑（保存后自动重载），也可直接在界面里勾选。

## 从源码构建

需要 .NET 8 SDK：

```bash
dotnet build -c Release
```

发布单文件 exe：

```bash
dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true -p:SelfContained=false
```

产物在 `bin\Release\net8.0-windows\publish\` 下，单独拷走 `WindowFocusMute.exe` 即可运行（目标机器需装 .NET 8 Desktop Runtime；`-p:SelfContained=true` 可打包为完全独立 exe，体积较大）。

## 代码结构

```
Program.cs            入口：[STAThread] 启动 MuteEngine + MainForm
MainForm.cs           WinForms 界面：进程勾选列表、日志区、热重载同步
MuteEngine.cs         静音引擎（纯逻辑无 UI）：名单读写、焦点轮询、静音/恢复
CoreAudioInterop.cs   WASAPI COM 互操作（全部 internal）
Win32.cs              前台窗口进程号获取 + 任务栏标准窗口枚举
app.ico               程序图标
targets.txt           运行时生成/维护的名单
```

### 实现说明

- **引擎与 UI 完全解耦**：UI 通过 `engine.Add/Remove/Targets` 操作名单，通过 `Log` / `TargetsChanged` 事件接收通知，加托盘、改界面都不用动引擎。
- **焦点检测**：200ms 轮询 `GetForegroundWindow` → `GetWindowThreadProcessId`，简单可靠，不依赖消息钩子。
- **静音**：Windows Core Audio（WASAPI）`IAudioSessionManager2` 枚举默认输出设备的全部音频会话，`IAudioSessionControl2.GetProcessId` 找到名单内进程的会话，用 `ISimpleAudioVolume.SetMute` 切换。只改该进程会话的静音位，不碰音量大小。
- **进程列表过滤**：`EnumWindows` 按任务栏标准枚举（`IsWindowVisible` + 标题非空 + 非工具窗口 + 无属主 + DWM 未遮蔽）。

## 已知边界（后续可做）

- 只作用于**默认输出设备**；一个进程的多个音频会话一并处理，无法按单个标签页精细静音。
- 以管理员权限运行的窗口可能取不到正确进程名（焦点检测会跳过）。
- 200ms 轮询在极快切换时可能有可感知延迟，可改 `SetWinEventHook` 事件驱动（更即时）。
- 托盘化（无窗口）的进程勾选后保持名单，但只有它获得过前台焦点再切走时才会触发静音。
- 待办候选：最小化到托盘、开机自启、启动时恢复名单进程的静音状态、多输出设备支持。

## 环境要求

- Windows 10/11（x64）
- .NET 8 Desktop Runtime（运行）；.NET 8 SDK（构建）

## License

MIT

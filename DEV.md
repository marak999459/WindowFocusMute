# WindowFocusMute 开发文档

> 交接文档：新工作区/新会话接手开发时先读这份。
> 项目位置：`D:\projects\WindowFocusMute`（注意是 `projects`，小写复数；D 盘另有一个 `project` 目录，不是这个项目的）

## 一、项目是什么

Windows 桌面小工具：**名单内进程的窗口失去焦点时自动静音，获得焦点时自动恢复声音**。
典型场景：看视频/开会时，切走窗口就让某个应用闭嘴。

- 形态：WinForms 图形界面（.NET 8，C#）
- 语言：界面与文档为中文
- 无任何 NuGet 依赖，纯 Win32 + COM 互操作

## 二、当前功能状态（均已实测通过）

| 功能 | 状态 |
|---|---|
| 图形界面：进程列表勾选即保存生效 | ✅ |
| 进程列表显示每个进程的 exe 图标 | ✅ |
| 列表显示 PID、窗口标题；可切换「显示所有进程」 | ✅ || targets.txt 手动编辑热重载（双向同步勾选状态） | ✅ |
| 名单内已退出进程显示「(未运行)」并保持勾选 | ✅ |
| 移出名单时自动恢复该进程声音 | ✅ |
| 程序图标（蓝色喇叭）与 exe 图标 | ✅ |
| 200ms 轮询前台窗口 + 音频设备失效自动重建 | ✅ |
| 失焦静音实测生效（曾因接口 vtable 顺序错误静音无效，已修复） | ✅ |
| 进程列表按任务栏标准过滤（可见 + 非工具窗口 + 未遮蔽 + 无属主），不再出现纯后台进程 | ✅ |
| 关闭窗口最小化到托盘（点 X 隐藏，退出必须走托盘右键菜单） | ✅ |

## 三、构建与运行

```
cd D:\projects\WindowFocusMute
dotnet build -c Release
```

- exe 直接输出到**项目根目录**（csproj 已设 `OutputPath=.\`、`AppendTargetFrameworkToOutputPath=false`）。
- 运行：双击根目录的 `WindowFocusMute.exe`，或 `.\WindowFocusMute.exe`。
- 配置文件 `targets.txt` 在 exe 同目录（即项目根目录），每行一个进程名（不含 `.exe`，`#` 开头为注释）。
- 发布单文件版：`dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true -p:SelfContained=false`。

## 四、代码结构

```
Program.cs            入口：[STAThread] 启动 MuteEngine + MainForm
MainForm.cs           WinForms 界面：ListView(图标+复选框)、日志区、勾选事件
MuteEngine.cs         静音引擎（纯逻辑无 UI）：名单读写、焦点轮询、静音/恢复
CoreAudioInterop.cs   WASAPI COM 互操作（全部 internal）
Win32.cs              GetForegroundWindow → 进程号
app.ico               程序图标（代码生成的蓝色喇叭，可随时替换）
targets.txt           运行时生成/维护的名单（根目录）
```

设计要点：

- **MuteEngine 与 UI 完全解耦**：UI 通过 `engine.Add/Remove/Targets` 操作名单，通过 `Log` / `TargetsChanged` 事件接收通知。加托盘、改界面都不用动引擎。
- **勾选链路**：ListView `ItemChecked` → `engine.Add/Remove` → 写 `targets.txt` → `TargetsChanged` 事件 → `SyncCheckStates` 回填勾选（用 `_uiReady` 标志防止事件风暴）。
- **静音链路**：`IAudioSessionManager2.GetSessionEnumerator` 枚举默认输出设备的会话 → `IAudioSessionControl2.GetProcessId` → `Process.GetProcessById` 拿进程名 → 命中名单则 `ISimpleAudioVolume.SetMute`。只切静音位，不碰音量值。
- **互操作注意**：COM 接口全用 `[PreserveSig] int` 返回 HRESULT；拿 `ISimpleAudioVolume` 要走 `Marshal.QueryInterface`（RCW 类型化接口没有直接 QI 方法，之前踩过）；接口可访问性必须一致（public 方法不能暴露 private 接口类型，之前踩过）。
- **进程列表的"任务栏标准"过滤**：`Win32.GetTaskbarWindows()` 用 `EnumWindows` 从窗口侧枚举（判定：`IsWindowVisible` + 标题非空 + 非 `WS_EX_TOOLWINDOW` 或有 `WS_EX_APPWINDOW` + 无属主窗口 + DWM 未 cloaked），再映射到进程。**不要**改回用 `Process.MainWindowHandle != 0` 判断——那会把 TextInputHost、echo-client 之类有隐藏窗口的后台进程也列出来；也不能只加 `IsWindowVisible`——ApplicationFrameHost（UWP 壳）、工具窗口仍是"可见"的但不进任务栏。勾选「显示所有进程」才回退到全进程枚举。
- **托盘最小化**：`OnFormClosing` 里用 `_realExit` 标志区分——点 X/Alt+F4（`CloseReason.UserClosing` 且非 `_realExit`）时 `e.Cancel=true` + `Hide()`；只有托盘菜单「退出」先置 `_realExit=true` 再 `Close()` 才真正退出，退出时必须 `_trayIcon.Dispose()` 否则托盘残留幽灵图标。**测试注意**：自动化测试要发 `WM_SYSCOMMAND+SC_CLOSE`（真实点 X 的路径）才算数；直接 `PostMessage(WM_CLOSE)` 会绕过 WinForms 的 UserClosing 判定直接退进程，结果误报。
- **vtable 顺序铁律（重要，曾导致"静音完全无效"的隐蔽 bug）**：`[ComImport]` 接口里方法的**声明顺序必须与 C++ 头文件里 vtable 顺序完全一致**，否则调用会落到错误的槽位——返回值可能仍是 S_OK，但 out 参数里是垃圾数据。`IAudioSessionControl2` 的扩展方法顺序是 `GetSessionIdentifier` → `GetSessionInstanceIdentifier` → `GetProcessId` → `IsSystemSoundsSession` → `SetDuckingPreference`（参考 NAudio `NAudio.Wasapi/CoreAudioApi/Interfaces/IAudioSessionControl.cs`）。声明漏掉或错序一个方法，`GetProcessId` 就会返回"像指针的巨大数字"（字符串指针低 32 位），导致按进程名匹配会话永远失败——且**没有任何异常或错误码**，日志看起来一切正常。排查此类问题：写独立小程序逐方法打印 hr 和返回值，与 NAudio/官方头文件逐槽位比对。

## 五、环境备忘（接手前必读）

1. **工作区/沙箱**：本项目曾在受限沙箱里开发，dotnet 需要写 `~\.dotnet` 和 `%APPDATA%\NuGet`，当时用 `DOTNET_CLI_HOME`/`APPDATA` 重定向绕过。**现在权限已放开，正常 `dotnet build` 即可**，不需要任何环境变量。
2. **D 盘目录大小写**：用户项目都在 `D:\projects`；`D:\project` 是别的东西（曾误建过并已删除）。
3. **旧工作区**：`C:\Users\22001\AppData\LocalLow\Jump Over the Age\Citizen Sleeper` 是游戏存档目录，项目残留已清干净，不要再往那里写东西。
4. **测试后台进程**：验证 UI 后记得 `Stop-Process -Name WindowFocusMute`，exe 被占用会导致后续 build/删除失败。
5. **截图验证 UI**：普通屏幕拷贝会截到上层窗口，用 `PrintWindow(hwnd, hdc, 2)`（`PW_RENDERFULLCONTENT`）直接从窗口取图。

## 六、实测验证方法（回归用）

1. `dotnet build -c Release` → 0 警告 0 错误。
2. 启动 exe → 窗口出现，列表显示有窗口的进程及各自图标。
3. 勾选某进程 → `targets.txt` 立刻出现该名字；取消勾选 → 立刻消失。
4. 手动编辑 `targets.txt` → 界面勾选状态自动同步。
5. 勾选进程失焦/聚焦 → 日志区出现「已静音 (失焦)」/「恢复声音 (聚焦)」，实际声音相应切换。

## 七、已知边界与后续方向

- 只作用于**默认输出设备**（eRender/eMultimedia）；换设备后 COM 会话失效会自动重建控制器，但蓝牙耳机独占等极端场景未测。
- 一个进程的多个音频会话会一并处理；无法按单个标签页/子窗口精细静音。
- 以管理员权限运行的窗口可能取不到其进程名（焦点检测会跳过）。
- 200ms 轮询在极快切换时可能有可感知延迟；可改 `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` 事件驱动。
- 待办候选：开机自启、开机后恢复名单进程的静音状态、多输出设备支持。

## 八、历史沿革（一句话版）

v1 控制台版（轮询 + WASAPI，命令行 add/del）→ 迁移到 D:\projects → v2 WinForms 勾选界面 → v2.1 进程图标 + exe 输出根目录 + 程序图标。

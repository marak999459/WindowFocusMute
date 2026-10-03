using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace WindowFocusMute;

internal sealed class MainForm : Form
{
    private readonly MuteEngine _engine;
    private readonly ListView _processList;
    private readonly ImageList _imageList;
    private readonly TextBox _logBox;
    private readonly CheckBox _showAllCheckBox;
    private readonly Label _statusLabel;
    private readonly NotifyIcon _trayIcon;
    private readonly ContextMenuStrip _trayMenu;

    /// <summary>用户点了托盘"退出"，此后关闭窗口真正退出。</summary>
    private bool _realExit;

    /// <summary>同步勾选状态期间为 true，抑制 ItemChecked 事件。</summary>
    private bool _uiReady;

    public MainForm(MuteEngine engine)
    {
        _engine = engine;

        Text = "WindowFocusMute — 失焦静音";
        Icon = new Icon("app.ico");
        Size = new Size(680, 640);
        MinimumSize = new Size(560, 500);
        StartPosition = FormStartPosition.CenterScreen;

        // ---- 顶部说明 ----
        var tip = new Label
        {
            Text = "勾选的进程：窗口失焦时自动静音，聚焦时恢复。改动立即保存并生效。",
            Dock = DockStyle.Top,
            Padding = new Padding(10, 8, 10, 4),
            Height = 32,
        };

        // ---- 进程列表（带图标 + 复选框）----
        _imageList = new ImageList
        {
            ImageSize = new Size(16, 16),
            ColorDepth = ColorDepth.Depth32Bit,
        };
        _processList = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            CheckBoxes = true,
            FullRowSelect = true,
            SmallImageList = _imageList,
            HideSelection = false,
        };
        _processList.Columns.Add("进程", 200);
        _processList.Columns.Add("PID", 60);
        _processList.Columns.Add("窗口标题", 340);
        _processList.ItemChecked += ProcessList_ItemChecked;

        // ---- 底部按钮行 ----
        var bottomPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 40,
            Padding = new Padding(10, 6, 10, 0),
            FlowDirection = FlowDirection.LeftToRight,
        };
        var refreshButton = new Button { Text = "刷新进程列表", AutoSize = true };
        refreshButton.Click += (_, _) => PopulateProcessList();

        _showAllCheckBox = new CheckBox
        {
            Text = "显示所有进程（默认只显示任务栏上有的）",
            AutoSize = true,
            Padding = new Padding(8, 4, 0, 0),
        };
        _showAllCheckBox.CheckedChanged += (_, _) => PopulateProcessList();

        bottomPanel.Controls.Add(refreshButton);
        bottomPanel.Controls.Add(_showAllCheckBox);

        // ---- 状态栏 ----
        _statusLabel = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 24,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(10, 0, 0, 0),
        };

        // ---- 日志 ----
        _logBox = new TextBox
        {
            Dock = DockStyle.Bottom,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Height = 110,
            BackColor = Color.White,
        };

        Controls.Add(_processList);
        Controls.Add(bottomPanel);
        Controls.Add(_statusLabel);
        Controls.Add(_logBox);
        Controls.Add(tip);

        // ---- 托盘图标与菜单：关窗最小化到托盘，退出必须走托盘菜单 ----
        _trayMenu = new ContextMenuStrip();
        _trayMenu.Items.Add("显示主窗口", null, (_, _) => ShowFromTray());
        _trayMenu.Items.Add(new ToolStripSeparator());
        var exitItem = new ToolStripMenuItem("退出", null, (_, _) =>
        {
            _realExit = true;
            Close();
        });
        _trayMenu.Items.Add(exitItem);

        _trayIcon = new NotifyIcon
        {
            Icon = Icon,
            Text = "WindowFocusMute — 失焦静音",
            Visible = true,
            ContextMenuStrip = _trayMenu,
        };
        _trayIcon.DoubleClick += (_, _) => ShowFromTray();

        // ---- 事件接线 ----
        _engine.Log += msg => BeginInvoke(() => AppendLog(msg));
        _engine.TargetsChanged += _ => BeginInvoke(() => SyncCheckStates());
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);

        // 托盘菜单"退出"以外的一切关闭请求（点 X、Alt+F4、任务栏关闭）都转为最小化到托盘
        if (!_realExit && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            AppendLog("已最小化到托盘 — 双击托盘图标恢复，退出请用托盘图标右键菜单。");
            return;
        }

        // 真正退出：清掉托盘图标，否则会残留到鼠标划过
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        PopulateProcessList();
        AppendLog($"配置文件: {_engine.ConfigPath}");
        _statusLabel.Text = "运行中 — 每 200ms 检测前台窗口。";
    }

    // ---------- 进程列表 ----------

    private void PopulateProcessList()
    {
        _uiReady = false;
        _processList.BeginUpdate();
        try
        {
            bool showAll = _showAllCheckBox.Checked;
            var selected = _engine.Targets.ToHashSet(StringComparer.OrdinalIgnoreCase);

            // 释放旧图标
            _imageList.Images.Clear();

            // 收集候选进程：默认按"任务栏标准"列出有可见顶层窗口的进程；
            // 勾选"显示所有进程"才回退到全进程枚举（含托盘/后台/无窗口进程）
            var rows = new List<RowInfo>();
            var iconAdded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (!showAll)
            {
                // 从窗口侧枚举：与任务栏同一套判定（可见 + 非工具窗口 + 未遮蔽 + 无属主）
                foreach (var (pid, (hwnd, windowTitle)) in Win32.GetTaskbarWindows())
                {
                    Process? p = null;
                    try
                    {
                        p = Process.GetProcessById((int)pid);
                        if (string.IsNullOrEmpty(p.ProcessName)) continue;
                        if (!seenNames.Add(p.ProcessName)) continue;

                        string title = windowTitle;
                        if (title.Length > 70) title = title[..70] + "…";
                        string imageKey = AddProcessIcon(p, iconAdded);
                        rows.Add(new RowInfo(p.ProcessName, p.Id, title, imageKey));
                    }
                    catch { /* 进程可能刚好退出 */ }
                    finally { p?.Dispose(); }
                }
            }
            else
            {
                foreach (var p in Process.GetProcesses())
                {
                    try
                    {
                        if (string.IsNullOrEmpty(p.ProcessName)) continue;
                        if (!seenNames.Add(p.ProcessName)) continue;

                        IntPtr hwnd = p.MainWindowHandle;
                        bool hasWindow = hwnd != IntPtr.Zero;
                        string title = hasWindow ? p.MainWindowTitle ?? string.Empty : "(无窗口)";
                        if (title.Length > 70) title = title[..70] + "…";

                        string imageKey = hasWindow ? AddProcessIcon(p, iconAdded) : string.Empty;
                        rows.Add(new RowInfo(p.ProcessName, p.Id, title, imageKey));
                    }
                    catch { /* 进程可能刚好退出或访问被拒 */ }
                    finally { p.Dispose(); }
                }
            }

            _processList.Items.Clear();

            foreach (var row in rows.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase))
            {
                var item = new ListViewItem(row.Name) { ImageKey = row.ImageKey, Tag = row.Name };
                item.SubItems.Add(row.Pid.ToString());
                item.SubItems.Add(row.Title);
                item.Checked = selected.Contains(row.Name);
                _processList.Items.Add(item);
            }

            // 名单里已退出但仍在名单中的进程，单独列出（无图标）
            foreach (var name in selected.Where(s => !rows.Any(r => r.Name.Equals(s, StringComparison.OrdinalIgnoreCase)))
                         .OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            {
                var item = new ListViewItem(name) { Tag = name };
                item.SubItems.Add("-");
                item.SubItems.Add("(未运行)");
                item.Checked = true;
                _processList.Items.Add(item);
            }
        }
        finally
        {
            _processList.EndUpdate();
            _uiReady = true;
        }
    }

    /// <summary>提取进程 exe 的关联图标加入 ImageList，返回 key（失败返回空串）。</summary>
    private string AddProcessIcon(Process p, HashSet<string> added)
    {
        try
        {
            string path = p.MainModule?.FileName ?? string.Empty;
            if (path.Length == 0 || !added.Add(p.ProcessName)) return string.Empty;

            using var icon = Icon.ExtractAssociatedIcon(path);
            if (icon is null) return string.Empty;
            _imageList.Images.Add(p.ProcessName, icon);
            return p.ProcessName;
        }
        catch
        {
            // 系统进程/权限不足等，取不到图标就显示默认无图标
            return string.Empty;
        }
    }

    private sealed record RowInfo(string Name, int Pid, string Title, string ImageKey);

    // ---------- 勾选处理 ----------

    private void ProcessList_ItemChecked(object? sender, ItemCheckedEventArgs e)
    {
        if (!_uiReady) return;
        string name = e.Item.Tag as string ?? string.Empty;
        if (name.Length == 0) return;

        BeginInvoke(() =>
        {
            try
            {
                if (e.Item.Checked) _engine.Add(name);
                else _engine.Remove(name);
            }
            catch (Exception ex)
            {
                AppendLog($"操作失败: {ex.Message}");
            }
        });
    }

    /// <summary>名单变化后同步勾选状态（含手动编辑 targets.txt 的热重载）。</summary>
    private void SyncCheckStates()
    {
        var selected = _engine.Targets.ToHashSet(StringComparer.OrdinalIgnoreCase);
        _uiReady = false;
        try
        {
            foreach (ListViewItem item in _processList.Items)
            {
                bool shouldCheck = selected.Contains(item.Tag as string ?? string.Empty);
                if (item.Checked != shouldCheck)
                    item.Checked = shouldCheck;
            }
        }
        finally
        {
            _uiReady = true;
        }
    }

    // ---------- 日志 ----------

    private void AppendLog(string msg)
    {
        _logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}{Environment.NewLine}");
    }
}

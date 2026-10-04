using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace WindowFocusMute;

/// <summary>
/// 静音引擎：加载 targets.txt 名单、轮询前台窗口、
/// 对名单内进程的音频会话执行失焦静音 / 聚焦恢复。
/// 纯逻辑无 UI，UI 只需订阅 Log 事件并调用 Add/Remove/Targets。
/// </summary>
internal sealed class MuteEngine : IDisposable
{
    // ---------- 名单 ----------
    private readonly HashSet<string> _targets = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>当前名单（只读副本）。</summary>
    public IReadOnlyCollection<string> Targets => _targets.ToList();

    /// <summary>日志事件：一条带时间戳的状态消息。</summary>
    public event Action<string>? Log;

    /// <summary>名单发生变化（含热重载）时触发，参数为最新名单。</summary>
    public event Action<IReadOnlyCollection<string>>? TargetsChanged;

    public string ConfigPath { get; }

    private readonly System.Timers.Timer _pollTimer;
    private DateTime _configLastWrite;
    private string? _lastConfigError;

    // 焦点状态
    private uint _lastPid;
    private string _lastProcessName = string.Empty;

    // 进程名 -> 是否已静音，避免重复设置
    private readonly Dictionary<string, bool> _mutedState = new(StringComparer.OrdinalIgnoreCase);

    // 音频控制器；设备失效时重建
    private CoreAudioInterop.AudioController? _audio;

    public MuteEngine()
    {
        ConfigPath = Path.Combine(AppContext.BaseDirectory, "targets.txt");
        EnsureConfigExists();
        LoadConfig();

        _pollTimer = new System.Timers.Timer(200) { AutoReset = true };
        _pollTimer.Elapsed += (_, _) => PollTick();
        _pollTimer.Start();
    }

    // ---------- 名单管理 ----------

    /// <summary>添加进程到名单并保存，返回是否真的新增。</summary>
    public bool Add(string processName)
    {
        processName = processName.Trim();
        if (processName.Length == 0) return false;
        if (_targets.Contains(processName)) return false;
        _targets.Add(processName);
        SaveConfig();
        OnTargetsChanged();
        return true;
    }

    /// <summary>从名单移除进程并保存，返回是否真的移除。</summary>
    public bool Remove(string processName)
    {
        if (!_targets.Remove(processName)) return false;
        // 移除时恢复该进程的声音，防止永远哑掉
        SetProcessMute(processName, muted: false, force: true);
        SaveConfig();
        OnTargetsChanged();
        return true;
    }

    private void OnTargetsChanged()
    {
        Log?.Invoke($"名单更新: {(Targets.Count == 0 ? "(空)" : string.Join(", ", Targets))}");
        TargetsChanged?.Invoke(Targets);
    }

    // ---------- 轮询与静音 ----------

    private void PollTick()
    {
        try
        {
            ReloadIfConfigChanged();

            uint pid = Win32.GetForegroundProcessId();
            string name = GetProcessName(pid);

            if (pid != _lastPid || name != _lastProcessName)
            {
                OnFocusChanged(_lastProcessName, name);
                _lastPid = pid;
                _lastProcessName = name;
            }
        }
        catch
        {
            // 单次轮询失败忽略，下轮继续
        }
    }

    private void OnFocusChanged(string previous, string current)
    {
        if (IsTarget(previous))
            SetProcessMute(previous, muted: true);

        if (IsTarget(current))
            SetProcessMute(current, muted: false);
    }

    private bool IsTarget(string processName) =>
        !string.IsNullOrEmpty(processName) && _targets.Contains(processName);

    private void SetProcessMute(string processName, bool muted, bool force = false)
    {
        if (!force && _mutedState.TryGetValue(processName, out bool was) && was == muted)
            return;

        if (!TryApplyMute(processName, muted))
        {
            // 命中 0 个会话但进程明明在运行 → 控制器是旧快照（进程比控制器晚启动），
            // 重建控制器立即重试一次；仍失败则下轮轮询再试
            Log?.Invoke($"{processName} 未找到音频会话，重建音频控制器后重试…");
            _audio = null;
            if (TryApplyMute(processName, muted))
                return;
            Log?.Invoke($"{processName} 重试后仍未找到会话（进程可能刚退出或尚无音频输出），下轮继续。");
        }
    }

    private bool TryApplyMute(string processName, bool muted)
    {
        if (_audio is null)
        {
            _audio = CoreAudioInterop.CreateController();
            if (_audio is null)
            {
                Log?.Invoke("无法访问音频设备，稍后重试。");
                return false;
            }
        }

        int hitCount = 0;
        var (total, handled) = _audio.ForEachSession((name, pid, vol) =>
        {
            if (string.Equals(name, processName, StringComparison.OrdinalIgnoreCase))
            {
                hitCount++;
                Guid empty = Guid.Empty;
                vol.SetMute(muted, ref empty);
            }
        });

        if (total < 0)
        {
            // 设备可能被拔插，丢弃控制器，下轮重建
            _audio = null;
            Log?.Invoke($"音频会话异常，{processName} 本轮未设置，将自动重试。");
            return false;
        }

        if (hitCount == 0)
            return false;

        _mutedState[processName] = muted;
        Log?.Invoke($"{processName} -> {(muted ? "已静音 (失焦)" : "恢复声音 (聚焦)")} [命中会话 {hitCount}/{total}]");
        return true;
    }

    private static string GetProcessName(uint pid)
    {
        if (pid == 0) return string.Empty;
        try
        {
            using var p = System.Diagnostics.Process.GetProcessById((int)pid);
            return p.ProcessName;
        }
        catch
        {
            return string.Empty;
        }
    }

    // ---------- 配置 ----------

    private void EnsureConfigExists()
    {
        if (File.Exists(ConfigPath)) return;
        File.WriteAllLines(ConfigPath, new[]
        {
            "# WindowFocusMute 目标进程名单",
            "# 每行一个进程名（不含 .exe），大小写不敏感。",
            "# 界面中勾选会自动写入本文件，也可手动编辑（保存后自动重载）。",
            "spotify",
        });
    }

    private void LoadConfig()
    {
        try
        {
            _configLastWrite = File.GetLastWriteTimeUtc(ConfigPath);
            var lines = File.ReadAllLines(ConfigPath)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && !l.StartsWith('#'))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            bool changed = !_targets.SetEquals(lines);
            _targets.Clear();
            foreach (var t in lines) _targets.Add(t);

            _mutedState.Clear();
            _lastConfigError = null;
            if (changed) OnTargetsChanged();
        }
        catch (IOException ex)
        {
            if (_lastConfigError != ex.Message)
            {
                Log?.Invoke($"读取配置失败: {ex.Message}");
                _lastConfigError = ex.Message;
            }
        }
    }

    /// <summary>外部（UI）调用：检查配置文件是否被手动改动，是则重载。</summary>
    public void ReloadIfConfigChanged()
    {
        try
        {
            if (File.GetLastWriteTimeUtc(ConfigPath) != _configLastWrite)
                LoadConfig();
        }
        catch { /* 文件正被编辑等，下轮再试 */ }
    }

    private void SaveConfig()
    {
        try
        {
            File.WriteAllLines(ConfigPath, Targets.OrderBy(t => t, StringComparer.OrdinalIgnoreCase));
            _configLastWrite = File.GetLastWriteTimeUtc(ConfigPath);
        }
        catch (Exception ex)
        {
            Log?.Invoke($"保存配置失败: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _pollTimer?.Dispose();
    }
}

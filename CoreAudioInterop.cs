using System;
using System.Runtime.InteropServices;

namespace WindowFocusMute;

/// <summary>
/// Windows Core Audio (WASAPI) COM 互操作：
/// 枚举默认输出设备上的音频会话，并按进程会话设置静音。
/// </summary>
internal static class CoreAudioInterop
{
    // ---------- 常量 ----------
    private const CLSCTX CLSCTX_ALL = CLSCTX.CLSCTX_INPROC_SERVER | CLSCTX.CLSCTX_INPROC_HANDLER |
                                      CLSCTX.CLSCTX_LOCAL_SERVER | CLSCTX.CLSCTX_REMOTE_SERVER;

    private enum EDataFlow { eRender, eCapture, eAll, EDataFlow_enum_count }
    private enum ERole { eConsole, eMultimedia, eCommunications, ERole_enum_count }

    [Flags]
    private enum CLSCTX : uint
    {
        CLSCTX_INPROC_SERVER = 0x1,
        CLSCTX_INPROC_HANDLER = 0x2,
        CLSCTX_LOCAL_SERVER = 0x4,
        CLSCTX_REMOTE_SERVER = 0x10,
    }

    // ---------- Coclass ----------
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorComObject { }

    // ---------- IMMDeviceEnumerator ----------
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(EDataFlow dataFlow, uint dwStateMask, out IntPtr ppDevices);
        [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppEndpoint);
        // 其余成员不使用，占位保持 vtable 顺序
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string pwstrId, out IntPtr ppDevice);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr pClient);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr pClient);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, CLSCTX dwClsCtx, IntPtr pActivationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
        // 其余成员不使用
        [PreserveSig] int OpenPropertyStore(uint stgmAccess, out IntPtr ppProperties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);
        [PreserveSig] int GetState(out uint pdwState);
    }

    // ---------- IAudioSessionManager2 ----------
    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioSessionManager2
    {
        // IAudioSessionManager
        [PreserveSig] int GetAudioSessionControl(IntPtr AudioSessionGuid, uint StreamFlags, out IntPtr SessionControl);
        [PreserveSig] int GetSimpleAudioVolume(IntPtr AudioSessionGuid, uint StreamFlags, out ISimpleAudioVolume AudioVolume);
        // IAudioSessionManager2
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator SessionEnum);
        [PreserveSig] int RegisterSessionNotification(IntPtr SessionNotification);
        [PreserveSig] int UnregisterSessionNotification(IntPtr SessionNotification);
        [PreserveSig] int RegisterDuckNotification(IntPtr sessionID, IntPtr duckNotification);
        [PreserveSig] int UnregisterDuckNotification(IntPtr duckNotification);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioSessionEnumerator
    {
        [PreserveSig] int GetCount(out int SessionCount);
        [PreserveSig] int GetSession(int SessionCount, out IAudioSessionControl Session);
    }

    [ComImport, Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioSessionControl
    {
        [PreserveSig] int GetState(out int pRetVal);
        // 其余成员不使用
        [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);
        [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string Value, ref Guid EventContext);
        [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);
        [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string Value, ref Guid EventContext);
        [PreserveSig] int GetGroupingParam(out Guid pRetVal);
        [PreserveSig] int SetGroupingParam(ref Guid Override, ref Guid EventContext);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr NewNotifications);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr NewNotifications);
    }

    [ComImport, Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl2
    {
        // IAudioSessionControl（vtable 顺序）
        [PreserveSig] int GetState(out int pRetVal);
        [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);
        [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string Value, ref Guid EventContext);
        [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);
        [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string Value, ref Guid EventContext);
        [PreserveSig] int GetGroupingParam(out Guid pRetVal);
        [PreserveSig] int SetGroupingParam(ref Guid Override, ref Guid EventContext);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr NewNotifications);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr NewNotifications);
        // IAudioSessionControl2
        // IAudioSessionControl2 扩展方法（vtable 顺序必须与 audiopolicy.h 一致！
        // GetSessionIdentifier / GetSessionInstanceIdentifier 在 GetProcessId 之前，之前写反导致 PID 全是垃圾值）
        [PreserveSig] int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);
        [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);
        [PreserveSig] int GetProcessId(out uint pdwProcessId);
        [PreserveSig] int IsSystemSoundsSession();
        [PreserveSig] int SetDuckingPreference(bool optOut);
    }

    [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ISimpleAudioVolume
    {
        [PreserveSig] int SetMasterVolume(float fLevel, ref Guid EventContext);
        [PreserveSig] int GetMasterVolume(out float pfLevel);
        [PreserveSig] int SetMute(bool bMute, ref Guid EventContext);
        [PreserveSig] int GetMute(out bool pbMute);
    }

    // ---------- 公共 API ----------

    /// <summary>拿默认输出设备的 IAudioSessionManager2。设备插拔后可能失效，失败时返回 null，调用方下次重试。</summary>
    public static AudioController? CreateController()
    {
        try
        {
            var enumerator = (IMMDeviceEnumerator)(object)new MMDeviceEnumeratorComObject();
            int hr = enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out IMMDevice device);
            if (hr != 0) return null;

            Guid iid = new Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"); // IAudioSessionManager2
            hr = device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out object mgrObj);
            if (hr != 0) return null;
            return new AudioController((IAudioSessionManager2)mgrObj);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>封装「枚举所有会话 -> 按进程名/进程号设置静音」的操作。</summary>
    internal sealed class AudioController
    {
        private readonly IAudioSessionManager2 _manager;

        internal AudioController(IAudioSessionManager2 manager) => _manager = manager;

        /// <summary>
        /// 对默认输出设备上的每个音频会话执行操作。
        /// action 参数：进程名（小写）、进程号、ISimpleAudioVolume。
        /// 返回 (会话总数, 成功拿到音量接口并处理的会话数)；COM 设备失效等情况下返回 (-1, 0)。
        /// </summary>
        internal (int total, int handled) ForEachSession(Action<string, uint, ISimpleAudioVolume> action, Action<string>? diag = null)
        {
            try
            {
                int hr = _manager.GetSessionEnumerator(out IAudioSessionEnumerator enumerator);
                if (hr != 0) return (-1, 0);

                hr = enumerator.GetCount(out int count);
                if (hr != 0) return (-1, 0);

                int ok = 0;
                for (int i = 0; i < count; i++)
                {
                    hr = enumerator.GetSession(i, out IAudioSessionControl session);
                    if (hr != 0 || session is null) continue;
                    try
                    {
                        var ctl2 = (IAudioSessionControl2)session;
                        hr = ctl2.GetProcessId(out uint pid);
                        if (hr != 0) { diag?.Invoke($"会话[{i}] GetProcessId 失败 hr=0x{hr:X8}"); continue; }

                        // QI ISimpleAudioVolume（NAudio 官方用法；会话控件对象直接支持该接口）
                        Guid volIid = new Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"); // ISimpleAudioVolume
                        IntPtr unk = Marshal.GetIUnknownForObject(ctl2);
                        int qiHr = Marshal.QueryInterface(unk, ref volIid, out IntPtr volPtr);
                        Marshal.Release(unk);
                        if (qiHr != 0) { diag?.Invoke($"会话[{i}] QI ISimpleAudioVolume 失败 hr=0x{qiHr:X8}"); continue; }
                        var volume = (ISimpleAudioVolume)Marshal.GetObjectForIUnknown(volPtr);
                        Marshal.Release(volPtr);

                        string? name = null;
                        try { using var p = System.Diagnostics.Process.GetProcessById((int)pid); name = p.ProcessName.ToLowerInvariant(); }
                        catch { /* 进程可能刚好退出 */ }

                        if (name is not null)
                        {
                            action(name, pid, volume);
                            ok++;
                        }
                        Marshal.ReleaseComObject(volume);
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(session);
                    }
                }
                return (count, ok);
            }
            catch
            {
                return (-1, 0);
            }
        }
    }
}

using System.Runtime.InteropServices;

namespace BlueRelay.Services;

/// <summary>
/// 系统互操作：IPolicyConfig 设置默认音频设备、DWM 窗口圆角。
/// </summary>
internal static class NativeMethods
{
    [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
    private class _CPolicyConfigClient { }

    /// <summary>把指定设备设为默认多媒体 + 控制台输出设备。</summary>
    public static void SetDefaultAudioPlaybackDevice(string deviceId)
    {
        var config = (IPolicyConfig)new _CPolicyConfigClient();
        config.SetDefaultEndpoint(deviceId, ERole.eMultimedia);
        config.SetDefaultEndpoint(deviceId, ERole.eConsole);
    }

    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    /// <summary>给窗口加 Win11 圆角。</summary>
    public static void ApplyRoundedCorners(IntPtr hwnd)
    {
        int pref = DWMWCP_ROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
    }
}

[ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfig
{
    [PreserveSig] int GetMixFormat(string pszDeviceName, IntPtr ppFormat);
    [PreserveSig] int GetDeviceFormat(string pszDeviceName, bool bDefault, IntPtr ppFormat);
    [PreserveSig] int ResetDeviceFormat(string pszDeviceName);
    [PreserveSig] int SetDeviceFormat(string pszDeviceName, IntPtr pEndpointFormat, IntPtr mixFormat);
    [PreserveSig] int GetProcessingPeriod(string pszDeviceName, bool bDefault, IntPtr pmftDefaultPeriod, IntPtr pmftMinimumPeriod);
    [PreserveSig] int SetProcessingPeriod(string pszDeviceName, IntPtr pmftPeriod);
    [PreserveSig] int GetShareMode(string pszDeviceName, IntPtr pMode);
    [PreserveSig] int SetShareMode(string pszDeviceName, IntPtr mode);
    [PreserveSig] int GetPropertyValue(string pszDeviceName, bool bFxStore, IntPtr key, IntPtr pv);
    [PreserveSig] int SetPropertyValue(string pszDeviceName, bool bFxStore, IntPtr key, IntPtr pv);
    [PreserveSig] int SetDefaultEndpoint(string pszDeviceName, ERole role);
    [PreserveSig] int SetEndpointVisibility(string pszDeviceName, bool bVisible);
}

internal enum ERole
{
    eConsole = 0,
    eMultimedia = 1,
    eCommunications = 2,
}

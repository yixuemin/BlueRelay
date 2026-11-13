using CommunityToolkit.Mvvm.ComponentModel;

namespace BlueRelay.Models;

/// <summary>音频输出设备信息。</summary>
public sealed partial class AudioDeviceInfo : ObservableObject
{
    // Material Design 音量图标（24x24）path data
    private const string ICON_UP = "M3 9v6h4l5 5V4L7 9H3zm13.5 3c0-1.77-1.02-3.29-2.5-4.03v8.05c1.48-.73 2.5-2.25 2.5-4.02zM14 3.23v2.06c2.89.86 5 3.54 5 6.71s-2.11 5.85-5 6.71v2.06c4.01-.91 7-4.49 7-8.77s-2.99-7.86-7-8.77z";
    private const string ICON_DOWN = "M18.5 12c0-1.77-1.02-3.29-2.5-4.03v8.05c1.48-.73 2.5-2.25 2.5-4.02zM5 9v6h4l5 5V4L9 9H5z";
    private const string ICON_MUTE = "M7 9v6h4l5 5V4l-5 5H7z";
    private const string ICON_OFF = "M16.5 12c0-1.77-1.02-3.29-2.5-4.03v2.21l2.45 2.45c.03-.2.05-.41.05-.63zm2.5 0c0 .94-.2 1.82-.54 2.64l1.51 1.51C20.63 14.91 21 13.5 21 12c0-4.28-2.99-7.86-7-8.77v2.06c2.89.86 5 3.54 5 6.71zM4.27 3L3 4.27 7.73 9H3v6h4l5 5v-6.73l4.25 4.25c-.67.52-1.42.93-2.25 1.18v2.06c1.38-.31 2.63-.95 3.69-1.81L19.73 21 21 19.73l-9-9L4.27 3zM12 4L9.91 6.09 12 8.18V4z";

    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsDefault { get; set; }

    /// <summary>设备主音量（0~100）。</summary>
    [ObservableProperty]
    private float _volume;

    /// <summary>音量图标 path data（随音量分级：静音/低/中/高）。</summary>
    [ObservableProperty]
    private string _iconData = ICON_UP;

    /// <summary>音量悬浮条是否打开（控制 Popup 开关，主窗口显示时重置为 false）。</summary>
    [ObservableProperty]
    private bool _isVolumeOpen;

    /// <summary>音量弹窗打开回调：(self)，由 ViewModel 注入以关闭其他设备的弹窗（互斥）。</summary>
    public Action<AudioDeviceInfo>? VolumeOpened;

    partial void OnIsVolumeOpenChanged(bool value)
    {
        if (value) VolumeOpened?.Invoke(this);
    }

    /// <summary>音量变化回调：(deviceId, volume)，由 ViewModel 注入以写入端点音量。</summary>
    public Action<string, float>? VolumeChanged;

    partial void OnVolumeChanged(float value)
    {
        IconData = GetIconData(value);
        VolumeChanged?.Invoke(Id, value);
    }

    private static string GetIconData(float vol)
    {
        if (vol <= 0f) return ICON_OFF;
        if (vol <= 33f) return ICON_MUTE;
        if (vol <= 66f) return ICON_DOWN;
        return ICON_UP;
    }
}

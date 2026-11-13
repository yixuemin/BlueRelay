using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BlueRelay.Models;
using BlueRelay.Services;

namespace BlueRelay.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly BluetoothService _bluetooth;
    private readonly AudioService _audio;
    private readonly SettingsService _settings;

    public ObservableCollection<BluetoothDeviceInfo> BluetoothDevices { get; } = new();
    public ObservableCollection<AudioDeviceInfo> AudioDevices { get; } = new();

    [ObservableProperty]
    private float _volume;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _lowEndMode;

    /// <summary>默认输出疑似虚拟声卡时的警告文案（null 表示无警告）。</summary>
    [ObservableProperty]
    private string? _virtualAudioWarning;

    [ObservableProperty]
    private bool _showVirtualAudioWarning;

    // 已知会拦截/吞掉音频流的虚拟声卡与音效增强软件关键词
    private static readonly string[] VirtualAudioKeywords =
    {
        "fxsound", "vb-audio", "vb-cable", "voicemeeter", "virtual",
        "nahimic", "dolby", "dts", "sonic studio", "sonic radar",
        "steam streaming", "cable input", "blackhole", "loopback", "mixdown"
    };

    public MainViewModel(BluetoothService bluetooth, AudioService audio, SettingsService settings)
    {
        _bluetooth = bluetooth;
        _audio = audio;
        _settings = settings;
        _bluetooth.AutoReconnect = settings.Current.AutoReconnect;
        _lowEndMode = settings.Current.LowEndMode;
        _bluetooth.ConnectionStateChanged += (_, _) =>
        {
            // WinRT 状态事件（如仅监听模式下手机主动接入）来自非 UI 线程，必须切回 UI 线程刷新集合
            void Update() { IsBusy = _bluetooth.AnyConnecting; _ = RefreshDevicesAsync(); }
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess()) Update();
            else dispatcher.InvokeAsync(Update);
        };
    }

    public async Task InitializeAsync()
    {
        await RefreshDevicesAsync();
        RefreshAudioDevices();
        var vol = _audio.GetA2dpSinkVolume();
        if (vol.HasValue) Volume = vol.Value;

        // 启动时自动连接上次使用的设备；同一时刻只播放一台，取最近使用的一台
        if (_settings.Current.AutoConnectOnStart && _settings.Current.LastDevices.Count > 0)
        {
            try { await _bluetooth.ConnectAsync(_settings.Current.LastDevices[0]); } catch { }
        }
    }

    /// <summary>关闭所有设备的音量悬浮条（主窗口显示前调用，避免上次残留的弹层）。</summary>
    public void CloseAllVolumePopups()
    {
        foreach (var d in AudioDevices) d.IsVolumeOpen = false;
    }

    /// <summary>请求打开设置弹窗（由 App 订阅）。</summary>
    public event EventHandler? OpenSettingsRequested;

    [RelayCommand]
    private void OpenSettings() => OpenSettingsRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>打开 Windows 蓝牙设置完成配对（与参考项目一致：应用不负责配对）。</summary>
    [RelayCommand]
    private void AddDevice()
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:bluetooth") { UseShellExecute = true }); }
        catch { }
    }

    [RelayCommand]
    private async Task RefreshDevices()
    {
        await RefreshDevicesAsync();
        RefreshAudioDevices();
    }

    /// <summary>播放系统提示音，用于确认电脑扬声器/当前输出设备正常。</summary>
    [RelayCommand]
    private void TestSound() => System.Media.SystemSounds.Exclamation.Play();

    [RelayCommand]
    private async Task ConnectAsync(BluetoothDeviceInfo? device)
    {
        if (device == null) return;
        if (_bluetooth.IsConnected(device.Id))
        {
            _bluetooth.Disconnect(device.Id);
        }
        else
        {
            var savedVolume = Volume; // 记住面板音量
            // 蓝牙协议限制同一时刻只有一台音源播放：连接新设备前先断开其他连接
            foreach (var otherId in _bluetooth.GetConnectedIds())
                if (otherId != device.Id) _bluetooth.Disconnect(otherId);
            var listenOnly = _settings.Current.ListenOnlyMode;
            var ok = await _bluetooth.ConnectAsync(device.Id, listenOnly);
            // 连接后把面板音量应用到新会话（方向：面板 → 会话，而非读会话默认值覆盖面板）
            if (ok && !listenOnly)
            {
                await ApplyVolumeWithRetryAsync(savedVolume);
                _ = AutoRepairNoSoundAsync(device.Id, savedVolume);
            }
        }
        await RefreshDevicesAsync();
    }

    /// <summary>自动修复无声：连接后每秒读默认输出峰值，10 秒完全无声则断开重连一次（可关闭）。</summary>
    private int _autoRepairGeneration;

    private async Task AutoRepairNoSoundAsync(string deviceId, float savedVolume)
    {
        if (!_settings.Current.AutoRepairNoSound) return;
        var gen = ++_autoRepairGeneration;

        for (int i = 0; i < 10; i++)
        {
            await Task.Delay(1000);
            // 用户已断开/又发起新连接，或检测到任何声音输出（含电脑自身播放），立即停止干预
            if (gen != _autoRepairGeneration || !_bluetooth.IsConnected(deviceId)) return;
            if (_audio.GetDefaultRenderPeak() > 0.001f) return;
        }

        // 10 秒始终无声：自动断开重连一次（等效手动修复）
        _bluetooth.Disconnect(deviceId);
        await Task.Delay(800);
        await _bluetooth.ConnectAsync(deviceId);
        await ApplyVolumeWithRetryAsync(savedVolume);
        await RefreshDevicesAsync();
    }

    [RelayCommand]
    private async Task SwitchAudioDeviceAsync(AudioDeviceInfo? device)
    {
        if (device == null) return;
        // 已是当前默认设备，无需切换（避免无意义的断开重连中断音频）
        if (device.IsDefault) return;

        var connectedIds = _bluetooth.GetConnectedIds();
        var savedVolume = Volume;
        _audio.SetDefaultDevice(device.Id);
        RefreshAudioDevices();

        // A2DP 音频会话绑定"连接时的默认设备"，切换默认设备不会让它迁移，
        // 必须断开重连，让新会话挂到新默认设备，音量控制才会指向正确会话。
        if (connectedIds.Count > 0)
        {
            foreach (var id in connectedIds) _bluetooth.Disconnect(id);
            await Task.Delay(800);   // 等旧会话释放
            await _bluetooth.ReconnectAllAsync(connectedIds);
            await RefreshDevicesAsync();
            // 尽快把音量应用回新会话（轮询等会话建立，避免新会话 100% 默认值爆音）
            await ApplyVolumeWithRetryAsync(savedVolume);
        }
    }

    /// <summary>把音量应用到 A2DP 会话，轮询等待会话建立（最多约 5 秒），一旦生效立即返回。</summary>
    private async Task ApplyVolumeWithRetryAsync(float volume)
    {
        // 切换设备后重连，A2DP 音频会话由系统异步建立，耗时可能超过 2 秒；
        // 每轮「设置 → 读回校验」，成功即返回。
        for (int i = 0; i < 25; i++)
        {
            _audio.SetA2dpSinkVolume(volume);
            var actual = _audio.GetA2dpSinkVolume();
            if (actual.HasValue && Math.Abs(actual.Value - volume) < 0.5f) return;
            await Task.Delay(200);
        }
    }

    partial void OnVolumeChanged(float value)
    {
        _audio.SetA2dpSinkVolume(value);
    }

    partial void OnLowEndModeChanged(bool value)
    {
        _audio.ApplyLowEndMode(value);
        _settings.Current.LowEndMode = value;
        _settings.Save();
    }

    private async Task RefreshDevicesAsync()
    {
        var devices = await _bluetooth.GetSinkDevicesAsync();
        BluetoothDevices.Clear();
        foreach (var d in devices) BluetoothDevices.Add(d);
        IsConnected = devices.Any(d => d.IsConnected);
    }

    private void RefreshAudioDevices()
    {
        var devices = _audio.GetRenderDevices();
        AudioDevices.Clear();
        foreach (var d in devices)
        {
            // 先读当前主音量（此时回调未注入，不会触发写回），再注入回调
            d.Volume = _audio.GetEndpointVolume(d.Id);
            d.VolumeChanged = (id, vol) => _audio.SetEndpointVolume(id, vol);
            d.VolumeOpened = self =>
            {
                // 互斥：打开一个设备的音量弹窗时，关闭其他设备的弹窗
                foreach (var other in AudioDevices)
                    if (other != self) other.IsVolumeOpen = false;
            };
            AudioDevices.Add(d);
        }

        // 默认输出落在虚拟声卡/音效软件上时给出警告——这是"已连接但没声音"的常见原因
        var def = AudioDevices.FirstOrDefault(d => d.IsDefault);
        ShowVirtualAudioWarning = def != null &&
            VirtualAudioKeywords.Any(k => def.Name.Contains(k, StringComparison.OrdinalIgnoreCase));
        VirtualAudioWarning = ShowVirtualAudioWarning && def != null
            ? $"当前默认输出是「{def.Name}」。虚拟声卡/音效增强软件可能拦截蓝牙音频导致无声，建议切换为物理扬声器或耳机。"
            : null;
    }
}

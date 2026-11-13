using CommunityToolkit.Mvvm.ComponentModel;
using BlueRelay.Services;

namespace BlueRelay.ViewModels;

/// <summary>设置弹窗的 ViewModel：主题、开机自启、自动重连、低端硬件模式，即时保存。</summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly BluetoothService _bluetooth;

    [ObservableProperty]
    private bool _darkTheme;

    [ObservableProperty]
    private bool _autoStart;

    [ObservableProperty]
    private bool _autoReconnect;

    [ObservableProperty]
    private bool _autoConnectOnStart;

    [ObservableProperty]
    private bool _lowEndMode;

    [ObservableProperty]
    private bool _autoRepairNoSound;

    [ObservableProperty]
    private bool _listenOnlyMode;

    [ObservableProperty]
    private bool _audioPerformanceBoost;

    /// <summary>主题变更事件（由 App 订阅以切换资源字典）。</summary>
    public event EventHandler? ThemeChanged;

    public SettingsViewModel(SettingsService settings, BluetoothService bluetooth)
    {
        _settings = settings;
        _bluetooth = bluetooth;
        _darkTheme = settings.Current.Theme != "Light";
        _autoStart = settings.Current.AutoStart;
        _autoReconnect = settings.Current.AutoReconnect;
        _autoConnectOnStart = settings.Current.AutoConnectOnStart;
        _lowEndMode = settings.Current.LowEndMode;
        _autoRepairNoSound = settings.Current.AutoRepairNoSound;
        _listenOnlyMode = settings.Current.ListenOnlyMode;
        _audioPerformanceBoost = settings.Current.AudioPerformanceBoost;
    }

    partial void OnDarkThemeChanged(bool value)
    {
        _settings.Current.Theme = value ? "Dark" : "Light";
        _settings.Save();
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnAutoStartChanged(bool value)
    {
        _settings.SetAutoStart(value);
    }

    partial void OnAutoReconnectChanged(bool value)
    {
        _settings.Current.AutoReconnect = value;
        _settings.Save();
        _bluetooth.AutoReconnect = value;
    }

    partial void OnAutoConnectOnStartChanged(bool value)
    {
        _settings.Current.AutoConnectOnStart = value;
        _settings.Save();
    }

    partial void OnLowEndModeChanged(bool value)
    {
        LowEndModeService.Apply(value);
        _settings.Current.LowEndMode = value;
        _settings.Save();
    }

    partial void OnAutoRepairNoSoundChanged(bool value)
    {
        _settings.Current.AutoRepairNoSound = value;
        _settings.Save();
    }

    partial void OnListenOnlyModeChanged(bool value)
    {
        _settings.Current.ListenOnlyMode = value;
        _settings.Save();
    }

    partial void OnAudioPerformanceBoostChanged(bool value)
    {
        _settings.Current.AudioPerformanceBoost = value;
        _settings.Save();
    }
}

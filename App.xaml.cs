using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;
using BlueRelay.Services;
using BlueRelay.ViewModels;
using BlueRelay.Views;

namespace BlueRelay;

public partial class App : System.Windows.Application
{
    private ServiceProvider? _services;
    private System.Windows.Forms.NotifyIcon? _tray;
    private MainWindow? _window;
    private MainViewModel? _vm;
    private SettingsWindow? _settingsWindow;
    private SettingsViewModel? _settingsVm;
    private SettingsService? _settings;
    private System.Drawing.Icon? _trayIconConnected;
    private System.Drawing.Icon? _trayIconDisconnected;
    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _showEvent;

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int dwProcessId);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        CrashReporter.Initialize();

        var services = new ServiceCollection();
        services.AddSingleton<SettingsService>();
        services.AddSingleton<AudioService>();
        services.AddSingleton<AudioPerformanceBoostService>();
        services.AddSingleton<BluetoothService>();
        services.AddSingleton<CliService>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<MainWindow>();
        services.AddSingleton<SettingsWindow>();
        _services = services.BuildServiceProvider();
        _settings = _services.GetRequiredService<SettingsService>();

        // 低端模式提权子进程：写注册表后退出
        if (e.Args.Length == 1 && (e.Args[0] == "--lowend-on" || e.Args[0] == "--lowend-off"))
        {
            LowEndModeService.ApplyElevated(e.Args[0] == "--lowend-on");
            Shutdown();
            return;
        }

        // --show：GUI 模式启动后立即打开面板（调试/预览用）
        bool showOnStart = e.Args.Contains("--show");
        // --uitest：自动交互测试，驱动连接/音量/切换并截图后退出
        bool uiTest = e.Args.Contains("--uitest");

        // GUI 模式单实例：二次启动通过命名事件通知已有实例弹出面板后退出；
        // CLI 模式（短生命周期、单独进程）不受限制，便于脚本化控制。
        if (e.Args.Length == 0 || showOnStart || uiTest)
        {
            _singleInstanceMutex = new Mutex(true, "BlueRelay_SingleInstance_Mutex", out bool createdNew);
            if (!createdNew)
            {
                try { EventWaitHandle.OpenExisting("BlueRelay_ShowEvent").Set(); } catch { }
                Shutdown();
                return;
            }
            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "BlueRelay_ShowEvent");
            _ = Task.Run(WaitShowEventLoop);
        }

        // CLI 模式：WinExe 无自带控制台，附加父进程控制台（cmd/PowerShell）以支持 stdout 输出；
        // 在无 SynchronizationContext 的线程执行，避免 WinRT await 死锁。
        if (e.Args.Length > 0 && !showOnStart && !uiTest)
        {
            if (AttachConsole(-1)) // ATTACH_PARENT_PROCESS
            {
                var stdout = Console.OpenStandardOutput();
                var writer = new StreamWriter(stdout) { AutoFlush = true };
                Console.SetOut(writer);
                Console.SetError(writer);
            }
            var cli = _services.GetRequiredService<CliService>();
            var code = Task.Run(() => cli.RunAsync(e.Args)).GetAwaiter().GetResult();
            Shutdown(code);
            return;
        }

        // GUI 模式（WinExe 无控制台，无需 FreeConsole）

        // 应用持久化的主题设置（App.xaml 默认深色，此处按用户设置覆盖）
        ApplyTheme(_settings.Current.Theme);

        _vm = _services.GetRequiredService<MainViewModel>();

        var assetsDir = Path.Combine(AppContext.BaseDirectory, "Assets");
        // 未连接图标 = 应用图标（蓝白，共用一个 app.ico）
        _trayIconDisconnected = LoadIcon(Path.Combine(assetsDir, "app.ico"));
        _trayIconConnected = LoadIcon(Path.Combine(assetsDir, "tray-connected.ico"));

        _tray = new System.Windows.Forms.NotifyIcon
        {
            Icon = _trayIconDisconnected ?? System.Drawing.SystemIcons.Application,
            Visible = true,
            Text = "蓝桥音频（未连接）"
        };
        _tray.MouseClick += (_, args) =>
        {
            if (args.Button == System.Windows.Forms.MouseButtons.Left)
                ShowWindow();
        };

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("打开面板", null, (_, _) => ShowWindow());
        menu.Items.Add("连接上次设备", null, (_, _) => ConnectLastDevice());
        menu.Items.Add("断开当前连接", null, (_, _) => DisconnectAllConnections());
        menu.Items.Add("设置", null, (_, _) => OpenSettingsWindow());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("蓝牙设置", null, (_, _) => OpenBluetoothSettings());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => Shutdown());
        _tray.ContextMenuStrip = menu;

        _vm.OpenSettingsRequested += (_, _) => OpenSettingsWindow();
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsConnected))
                UpdateTrayIcon();
        };

        Task initTask = _vm.InitializeAsync();

        if (showOnStart)
        {
            EnsureWindow();
            _ = ShowWhenReadyAsync(initTask);
        }

        if (uiTest)
        {
            EnsureWindow();
            _ = RunUiTestAsync(initTask);
        }
    }

    private async Task ShowWhenReadyAsync(Task init)
    {
        try { await init; } catch { }
        if (_window == null) return;
        await _window.Dispatcher.InvokeAsync(ShowWindow);
    }

    /// <summary>自动交互测试：驱动连接/音量/切换并逐帧截图，结束后恢复初始状态。</summary>
    private async Task RunUiTestAsync(Task init)
    {
        try { await init; } catch { }
        if (_window == null || _vm == null) return;

        var dir = Path.Combine(AppContext.BaseDirectory, "uitest");
        var log = new List<string> { $"=== UITest {DateTime.Now:HH:mm:ss} ===" };
        try { Directory.CreateDirectory(dir); } catch { }

        string? originalDefault = _vm.AudioDevices.FirstOrDefault(d => d.IsDefault)?.Id;
        var audio = _services!.GetRequiredService<AudioService>();
        var bluetooth = _services!.GetRequiredService<BluetoothService>();
        var originalVolume = audio.GetA2dpSinkVolume();

        try
        {
            await _window.Dispatcher.InvokeAsync(ShowWindow);
            // 动画采样：精确记录弹出动画的 scale/opacity 随时间变化
            await _window.Dispatcher.InvokeAsync(() => log.Add($"anim t0    {_window.GetAnimState()}"));
            await Task.Delay(60);
            await _window.Dispatcher.InvokeAsync(() => log.Add($"anim t60   {_window.GetAnimState()}"));
            await Task.Delay(120);
            await _window.Dispatcher.InvokeAsync(() => log.Add($"anim t180  {_window.GetAnimState()}"));
            await Task.Delay(400); // 等弹出动画完成
            await _window.Dispatcher.InvokeAsync(() => log.Add($"anim t580  {_window.GetAnimState()}"));
            await _window.Dispatcher.InvokeAsync(() => Capture(dir, "01-initial.png", log));

            // 验证第二次打开不闪烁：Hide 后重新 Show，初始 Opacity 应为 0（而非被 HoldEnd 锁在 1）
            await _window.Dispatcher.InvokeAsync(() => _window.Hide());
            await Task.Delay(100);
            await _window.Dispatcher.InvokeAsync(ShowWindow);
            await _window.Dispatcher.InvokeAsync(() => log.Add($"reopen t0   {_window.GetAnimState()}"));
            await Task.Delay(300);
            await _window.Dispatcher.InvokeAsync(() => log.Add($"reopen t300 {_window.GetAnimState()}"));

            // ===== 音量三连 bug 验证 =====
            // 设面板音量为 40，连接后应保持 40（而非会话默认 100%）
            await _window.Dispatcher.InvokeAsync(() => _vm.Volume = 40);
            await Task.Delay(300);
            await _window.Dispatcher.InvokeAsync(() => Capture(dir, "02-volume40.png", log));
            var preConnect = audio.GetA2dpSinkVolume();
            log.Add($"连接前会话音量={preConnect?.ToString("0") ?? "无会话"}（面板=40）");

            // 连接蓝牙
            var btDevice = _vm.BluetoothDevices.FirstOrDefault();
            log.Add($"待连接设备={btDevice?.Name ?? "无"}");
            await _window.Dispatcher.InvokeAsync(() =>
            {
                if (btDevice != null) _vm.ConnectCommand.Execute(btDevice);
            });
            await Task.Delay(150); // 连接中，捕捉 spinner
            await _window.Dispatcher.InvokeAsync(() => Capture(dir, "03-connecting.png", log));
            await Task.Delay(10000);
            await _window.Dispatcher.InvokeAsync(() => Capture(dir, "04-connect.png", log));
            var volAfterConnect = audio.GetA2dpSinkVolume();
            log.Add($"连接后会话音量={volAfterConnect?.ToString("0") ?? "无会话"}（期望=40，验证面板音量应用到会话）");
            if (btDevice != null)
                log.Add($"LastDevices 连接后含设备={_settings!.Current.LastDevices.Contains(btDevice.Id)}（期望True）");

            // 切换输出设备（切到非默认设备）
            var switchTarget = _vm.AudioDevices.FirstOrDefault(d => !d.IsDefault);
            log.Add($"切换目标设备={switchTarget?.Name ?? "无"}");
            await _window.Dispatcher.InvokeAsync(() =>
            {
                if (switchTarget != null) _vm.SwitchAudioDeviceCommand.Execute(switchTarget);
            });
            await Task.Delay(10000);
            await _window.Dispatcher.InvokeAsync(() => Capture(dir, "05-switch.png", log));
            var volAfterSwitch = audio.GetA2dpSinkVolume();
            log.Add($"切换后会话音量={volAfterSwitch?.ToString("0") ?? "无会话"}（期望=40，验证切换后音量保持）");

            // 切回原默认设备，设音量 60，验证音量可调
            await _window.Dispatcher.InvokeAsync(() =>
            {
                var def = _vm.AudioDevices.FirstOrDefault(d => d.Id == originalDefault);
                if (def != null && !def.IsDefault) _vm.SwitchAudioDeviceCommand.Execute(def);
            });
            await Task.Delay(10000);
            await _window.Dispatcher.InvokeAsync(() => _vm.Volume = 60);
            await Task.Delay(500);
            var volAfterSet60 = audio.GetA2dpSinkVolume();
            log.Add($"切回后设音量60，会话音量={volAfterSet60?.ToString("0") ?? "无会话"}（期望=60，验证音量可调）");

            // 端点音量读写验证：设 55 读回应为 55，再恢复
            var endpointDev = _vm.AudioDevices.FirstOrDefault();
            if (endpointDev != null)
            {
                var origEndpoint = audio.GetEndpointVolume(endpointDev.Id);
                audio.SetEndpointVolume(endpointDev.Id, 55f);
                var readEndpoint = audio.GetEndpointVolume(endpointDev.Id);
                audio.SetEndpointVolume(endpointDev.Id, origEndpoint);
                log.Add($"端点音量：{endpointDev.Name} 原{origEndpoint:0}% → 设55 → 读{readEndpoint:0}%（期望55）");
            }

            // 图标分级验证：不同音量应有不同长度的图标 path（UP≈181 / DOWN≈101 / MUTE≈35 / OFF≈281）
            foreach (var d in _vm.AudioDevices)
                log.Add($"图标分级：{d.Name} 音量={d.Volume:0}% iconLen={d.IconData.Length}");

            // 打开设置弹窗
            await _window.Dispatcher.InvokeAsync(() => _vm.OpenSettingsCommand.Execute(null));
            await Task.Delay(800);
            await _window.Dispatcher.InvokeAsync(() => CaptureWindow(_settingsWindow, dir, "06-settings.png", log));

            // 切换深色模式，验证主题实时切换
            await _window.Dispatcher.InvokeAsync(() => { if (_settingsVm != null) _settingsVm.DarkTheme = true; });
            await Task.Delay(500);
            await _window.Dispatcher.InvokeAsync(() => CaptureWindow(_settingsWindow, dir, "07-settings-dark.png", log));

            // 切回浅色，恢复初始状态
            await _window.Dispatcher.InvokeAsync(() => { if (_settingsVm != null) _settingsVm.DarkTheme = false; });
            await Task.Delay(300);
            await _window.Dispatcher.InvokeAsync(() => _settingsWindow?.Hide());
        }
        catch (Exception ex)
        {
            log.Add($"异常: {ex}");
        }

        // 恢复初始状态：断开蓝牙、恢复默认设备（同步，可靠）
        try
        {
            foreach (var id in bluetooth.GetConnectedIds())
                bluetooth.Disconnect(id);
            if (originalDefault != null)
                audio.SetDefaultDevice(originalDefault);
            if (originalVolume.HasValue)
                audio.SetA2dpSinkVolume(originalVolume.Value);
            log.Add($"恢复完成：断开蓝牙，默认设备恢复，音量恢复为 {originalVolume?.ToString("0") ?? "无会话"}");
            var lastCheckDev = _vm.BluetoothDevices.FirstOrDefault()?.Id;
            if (lastCheckDev != null)
                log.Add($"LastDevices 恢复后含设备={_settings!.Current.LastDevices.Contains(lastCheckDev)}（期望False）");
        }
        catch (Exception ex)
        {
            log.Add($"恢复异常: {ex}");
        }

        try { File.WriteAllLines(Path.Combine(dir, "log.txt"), log); } catch { }
        Shutdown();
    }

    private void Capture(string dir, string name, List<string> log)
        => CaptureWindow(_window, dir, name, log);

    private void CaptureWindow(Window? win, string dir, string name, List<string> log)
    {
        try
        {
            if (win == null) return;
            var w = (int)win.ActualWidth;
            var h = (int)win.ActualHeight;
            var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(win);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            var path = Path.Combine(dir, name);
            using var fs = File.Create(path);
            enc.Save(fs);
            log.Add($"OK {name} {w}x{h} visible={win.IsVisible} opacity={win.Opacity:0.00}");
        }
        catch (Exception ex)
        {
            log.Add($"FAIL {name}: {ex.Message}");
        }
    }

    /// <summary>单实例：后台等待二次启动发来的「显示面板」事件。</summary>
    private async Task WaitShowEventLoop()
    {
        while (_showEvent != null)
        {
            try { _showEvent.WaitOne(); }
            catch { break; }
            try { await Dispatcher.InvokeAsync(ShowWindow); }
            catch { break; }
        }
    }

    private void ConnectLastDevice()
    {
        var last = _settings?.Current.LastDevices.FirstOrDefault();
        if (last == null || _vm == null) return;
        var device = _vm.BluetoothDevices.FirstOrDefault(d => string.Equals(d.Id, last, StringComparison.OrdinalIgnoreCase));
        _vm.ConnectCommand.Execute(device);
    }

    private void DisconnectAllConnections()
    {
        var ids = _services?.GetRequiredService<BluetoothService>().GetConnectedIds();
        if (ids == null) return;
        foreach (var id in ids) _services!.GetRequiredService<BluetoothService>().Disconnect(id);
    }

    private static void OpenBluetoothSettings()
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:bluetooth") { UseShellExecute = true }); }
        catch { }
    }

    private void EnsureWindow()
    {
        if (_window != null || _services == null || _vm == null) return;
        _window = _services.GetRequiredService<MainWindow>();
        _window.DataContext = _vm;
    }

    private void ShowWindow()
    {
        EnsureWindow();
        if (_window == null || _vm == null) return;

        _window.ResetAnimationState();
        _vm.CloseAllVolumePopups();
        // 首开时窗口 Opacity=0，可在不可见状态下完成实际布局与二次定位。
        PositionWindowNearTray();
        if (!_window.IsVisible) _window.Show();
        _window.UpdateLayout();
        PositionWindowNearTray();
        _window.Activate();
        _window.PlayShowAnimation();
    }

    /// <summary>把窗口定位到光标所在屏幕（托盘所在屏）的右下角、任务栏上方。</summary>
    private void PositionWindowNearTray()
    {
        if (_window == null) return;
        var cursor = System.Windows.Forms.Cursor.Position;
        var screen = System.Windows.Forms.Screen.FromPoint(cursor);
        var dpi = VisualTreeHelper.GetDpi(_window);
        double right = screen.WorkingArea.Right / dpi.DpiScaleX;
        double bottom = screen.WorkingArea.Bottom / dpi.DpiScaleY;

        // 用缓存尺寸；未预热时 SizeToContent 尺寸未定，回退默认值（调试模式略偏可接受）
        double w = double.IsNaN(_window.ActualWidth) || _window.ActualWidth <= 0 ? 360 : _window.ActualWidth;
        double h = double.IsNaN(_window.ActualHeight) || _window.ActualHeight <= 0 ? 400 : _window.ActualHeight;
        _window.Left = right - w - 12;
        _window.Top = bottom - h - 12;
    }

    private void ApplyTheme(string theme)
    {
        var dict = new ResourceDictionary
        {
            Source = new Uri($"Themes/{theme}.xaml", UriKind.Relative)
        };
        Resources.MergedDictionaries.Clear();
        Resources.MergedDictionaries.Add(dict);
        if (_settings != null)
        {
            _settings.Current.Theme = theme;
            _settings.Save();
        }
    }

    /// <summary>打开设置弹窗（懒加载，复用窗口实例）。</summary>
    private void OpenSettingsWindow()
    {
        if (_settingsVm == null)
        {
            _settingsVm = _services!.GetRequiredService<SettingsViewModel>();
            _settingsVm.ThemeChanged += (_, _) => ApplyTheme(_settingsVm.DarkTheme ? "Dark" : "Light");
        }

        if (_settingsWindow == null)
        {
            _settingsWindow = _services!.GetRequiredService<SettingsWindow>();
            _settingsWindow.DataContext = _settingsVm;
        }

        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    /// <summary>根据连接状态切换托盘图标与提示文字。</summary>
    private void UpdateTrayIcon()
    {
        if (_tray == null) return;
        var connected = _vm?.IsConnected ?? false;
        _tray.Icon = connected
            ? (_trayIconConnected ?? System.Drawing.SystemIcons.Application)
            : (_trayIconDisconnected ?? System.Drawing.SystemIcons.Application);
        _tray.Text = connected ? "蓝桥音频（已连接）" : "蓝桥音频（未连接）";
    }

    private static System.Drawing.Icon? LoadIcon(string path)
    {
        try
        {
            if (File.Exists(path)) return new System.Drawing.Icon(path);
        }
        catch { }
        return null;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _showEvent?.Dispose();
        try { _singleInstanceMutex?.ReleaseMutex(); } catch { }
        _singleInstanceMutex?.Dispose();
        _services?.Dispose();
        base.OnExit(e);
    }
}

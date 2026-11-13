using Windows.Devices.Enumeration;
using Windows.Media.Audio;
using BlueRelay.Models;

namespace BlueRelay.Services;

/// <summary>
/// 蓝牙 A2DP Sink 连接服务：连接/断开、仅监听模式、状态管理、
/// 指数退避自动重连、批量重连。
/// </summary>
public sealed class BluetoothService
{
    private readonly Dictionary<string, AudioPlaybackConnection> _connections = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _connecting = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _retryCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _boosted = new(StringComparer.OrdinalIgnoreCase);
    private readonly SettingsService _settings;
    private readonly AudioPerformanceBoostService _boost;

    // 指数退避：2s 起步翻倍，上限 5 分钟，±20% 抖动避免多设备同时重连冲突；20 次后熔断
    private const double ReconnectBaseMs = 2000;
    private const double ReconnectMaxMs = 300000;
    private const int ReconnectMaxAttempts = 20;

    public bool AutoReconnect { get; set; }

    public event EventHandler? ConnectionStateChanged;

    /// <summary>是否有设备正在连接中（用于 UI 加载动画）。</summary>
    public bool AnyConnecting => _connecting.Count > 0;

    public BluetoothService(SettingsService settings, AudioPerformanceBoostService boost)
    {
        _settings = settings;
        _boost = boost;
    }

    /// <summary>枚举所有可作为 A2DP Sink 的蓝牙设备。</summary>
    public async Task<List<BluetoothDeviceInfo>> GetSinkDevicesAsync()
    {
        var result = new List<BluetoothDeviceInfo>();
        var selector = AudioPlaybackConnection.GetDeviceSelector();
        var devices = await DeviceInformation.FindAllAsync(selector);
        foreach (var d in devices)
        {
            object? protocolId = null;
            try { d.Properties?.TryGetValue("System.Devices.Aep.ProtocolId", out protocolId); } catch { }
            result.Add(new BluetoothDeviceInfo
            {
                Id = d.Id,
                Name = d.Name,
                Protocol = BluetoothDeviceInfo.DetectProtocol(protocolId),
                IsConnected = IsConnected(d.Id)
            });
        }
        return result;
    }

    /// <summary>是否已建立音频连接（仅监听等待中不算已连接）。</summary>
    public bool IsConnected(string deviceId) =>
        _connections.TryGetValue(deviceId, out var c) &&
        c.State == AudioPlaybackConnectionState.Opened;

    /// <summary>返回所有已连接（Opened）设备的 ID。</summary>
    public IReadOnlyCollection<string> GetConnectedIds() =>
        _connections.Where(kv => kv.Value.State == AudioPlaybackConnectionState.Opened)
                    .Select(kv => kv.Key).ToList();

    /// <summary>返回所有已注册（含仅监听等待中）设备的 ID，用于退出时清理。</summary>
    public IReadOnlyCollection<string> GetRegisteredIds() => _connections.Keys.ToList();

    /// <summary>
    /// 建立音频接收连接。
    /// listenOnly=true 时只 Start 让系统进入可连接状态，不主动 Open，由手机端发起 A2DP 连接
    /// （兼容部分手机「本机主动连接显示成功但不推流」的问题）。
    /// </summary>
    public async Task<bool> ConnectAsync(string deviceId, bool listenOnly = false)
    {
        if (IsConnected(deviceId)) return true;
        if (_connections.ContainsKey(deviceId) || !_connecting.Add(deviceId)) return true; // 等待中或连接中

        ConnectionStateChanged?.Invoke(this, EventArgs.Empty);

        try
        {
            // WinRT 异步调用放到线程池执行，避免在 WPF UI 线程的 SynchronizationContext 下 await 死锁
            var (success, connection) = await Task.Run(async () =>
            {
                var conn = AudioPlaybackConnection.TryCreateFromId(deviceId);
                if (conn == null) return (false, (AudioPlaybackConnection?)null);

                conn.StateChanged += OnStateChanged;

                await conn.StartAsync();

                if (listenOnly)
                    return (true, conn); // 仅监听：等待手机端发起，StateChanged Opened 时才算连接成功

                var result = await conn.OpenAsync();
                if (result.Status == AudioPlaybackConnectionOpenResultStatus.Success)
                    return (true, conn);

                try { conn.Dispose(); } catch { }
                return (false, (AudioPlaybackConnection?)null);
            });

            if (success && connection != null)
            {
                _connections[deviceId] = connection; // await Task.Run 后回到 UI 线程上下文，安全更新
                _retryCounts.Remove(deviceId);
                // 主动模式连接已建立；仅监听模式等手机接入（Opened 事件）后再启用增强
                if (!listenOnly) AcquireBoost(deviceId);
                RememberDevice(deviceId);
                return true;
            }
            return false;
        }
        finally
        {
            _connecting.Remove(deviceId);
            ConnectionStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Disconnect(string deviceId)
    {
        if (!_connections.TryGetValue(deviceId, out var connection)) return;
        _connections.Remove(deviceId);
        _retryCounts.Remove(deviceId); // 主动断开：清除重试计数，不再自动重连
        ReleaseBoost(deviceId);
        try { connection.Dispose(); } catch { }
        ForgetDevice(deviceId);
        ConnectionStateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>音频稳定模式：仅在设置开启且连接真正建立（Opened）时启用。</summary>
    private void AcquireBoost(string deviceId)
    {
        if (!_settings.Current.AudioPerformanceBoost) return;
        if (_boosted.Add(deviceId)) _boost.Acquire();
    }

    private void ReleaseBoost(string deviceId)
    {
        if (_boosted.Remove(deviceId)) _boost.Release();
    }

    /// <summary>断开全部连接（含仅监听等待中的），用于退出清理。</summary>
    public void DisconnectAll()
    {
        foreach (var id in _connections.Keys.ToList()) Disconnect(id);
    }

    /// <summary>记录设备到「上次连接」列表（启动时自动重连用），去重并置顶。</summary>
    private void RememberDevice(string deviceId)
    {
        var list = _settings.Current.LastDevices;
        list.Remove(deviceId);
        list.Insert(0, deviceId);
        _settings.Save();
    }

    /// <summary>从「上次连接」列表移除（用户主动断开后不再自动重连）。</summary>
    private void ForgetDevice(string deviceId)
    {
        if (_settings.Current.LastDevices.Remove(deviceId))
            _settings.Save();
    }

    /// <summary>重连设备（蓝牙协议同一时刻只播放一台，取列表中第一台）。</summary>
    public async Task ReconnectAllAsync(IEnumerable<string> deviceIds)
    {
        var first = deviceIds.FirstOrDefault();
        if (first != null)
        {
            try { await ConnectAsync(first); } catch { }
        }
    }

    private async void OnStateChanged(AudioPlaybackConnection sender, object args)
    {
        var deviceId = sender.DeviceId;

        // 手机端主动接入（仅监听模式）或连接就绪：重置重试计数并通知 UI
        if (sender.State == AudioPlaybackConnectionState.Opened)
        {
            _retryCounts.Remove(deviceId);
            AcquireBoost(deviceId); // 仅监听模式下手机主动接入时在此启用增强
            ConnectionStateChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (sender.State != AudioPlaybackConnectionState.Closed) return;

        // 只有"意外断开"（连接仍登记在案）才处理并自动重连；
        // 主动断开（Disconnect 已先移除）直接返回，避免主动断开后被自动重连。
        if (!_connections.ContainsKey(deviceId)) return;

        _connections.Remove(deviceId);
        ReleaseBoost(deviceId);
        try { sender.Dispose(); } catch { }
        ConnectionStateChanged?.Invoke(this, EventArgs.Empty);

        if (AutoReconnect)
            await ScheduleReconnectAsync(deviceId);
    }

    /// <summary>指数退避 + 抖动重连：2s/4s/8s…封顶 5 分钟，±20% 抖动，20 次熔断。</summary>
    private async Task ScheduleReconnectAsync(string deviceId)
    {
        var attempt = _retryCounts.GetValueOrDefault(deviceId);
        if (attempt >= ReconnectMaxAttempts)
        {
            _retryCounts.Remove(deviceId); // 熔断：停止重连，等用户手动处理
            return;
        }
        _retryCounts[deviceId] = attempt + 1;

        var baseMs = Math.Min(ReconnectBaseMs * Math.Pow(2, Math.Min(attempt, 7)), ReconnectMaxMs);
        var jittered = baseMs * (0.8 + Random.Shared.NextDouble() * 0.4);
        await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(1000, jittered)));

        // 等待期间用户已手动连回或被断开清理
        if (_connections.ContainsKey(deviceId)) return;

        try { await ConnectAsync(deviceId); } catch { }
    }
}

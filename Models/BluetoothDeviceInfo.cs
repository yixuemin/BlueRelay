namespace BlueRelay.Models;

/// <summary>蓝牙 A2DP 音频源设备信息。</summary>
public sealed class BluetoothDeviceInfo
{
    /// <summary>LE Audio 协议 GUID（用于设备协议前缀展示）。</summary>
    private static readonly Guid LeAudioProtocolGuid = new("0BB58923-2600-489B-9FC9-DE57A5669D6F");

    public string Id { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>A2DP / LE Audio / null（未知），仅展示用。</summary>
    public string? Protocol { get; set; }

    public bool IsConnected { get; set; }

    /// <summary>带协议前缀的展示名，如 [A2DP] Pixel 8。</summary>
    public string DisplayName => Protocol == null ? Name : $"[{Protocol}] {Name}";

    /// <summary>根据 DeviceInformation 的 System.Devices.Aep.ProtocolId 判断协议；读不到时回退 A2DP。</summary>
    public static string? DetectProtocol(object? protocolId)
    {
        if (protocolId is Guid g)
            return g == LeAudioProtocolGuid ? "LE Audio" : "A2DP";
        if (protocolId is string s && Guid.TryParse(s, out var parsed))
            return parsed == LeAudioProtocolGuid ? "LE Audio" : "A2DP";
        return "A2DP"; // 属性不可读时按 Classic A2DP 展示（绝大多数接收设备）
    }
}

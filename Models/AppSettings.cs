namespace BlueRelay.Models;

/// <summary>应用设置（持久化到 JSON）。</summary>
public sealed class AppSettings
{
    public bool AutoStart { get; set; }
    public bool AutoReconnect { get; set; } = true;
    public bool AutoConnectOnStart { get; set; }
    public bool LowEndMode { get; set; }
    /// <summary>连接后检测 10 秒，完全无声则自动断开重连一次。</summary>
    public bool AutoRepairNoSound { get; set; } = true;
    /// <summary>仅监听模式：只 Start 不 Open，由手机端发起连接（兼容"已连接但无声"机型）。</summary>
    public bool ListenOnlyMode { get; set; }
    /// <summary>音频稳定模式：连接期间启用 MMCSS Pro Audio 调度。</summary>
    public bool AudioPerformanceBoost { get; set; }
    public string Theme { get; set; } = "Dark";
    public List<string> LastDevices { get; set; } = new();
}

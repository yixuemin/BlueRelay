using NAudio.CoreAudioApi;
using BlueRelay.Models;

namespace BlueRelay.Services;

/// <summary>
/// 音频服务：枚举/切换输出设备、A2DP 会话音量、低端硬件模式（SBC bitpool）。
/// </summary>
public sealed class AudioService
{
    private readonly MMDeviceEnumerator _enumerator = new();

    public List<AudioDeviceInfo> GetRenderDevices()
    {
        var def = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        return _enumerator
            .EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
            .Select(d => new AudioDeviceInfo
            {
                Id = d.ID,
                Name = d.FriendlyName,
                IsDefault = d.ID == def?.ID
            })
            .ToList();
    }

    public void SetDefaultDevice(string id)
    {
        NativeMethods.SetDefaultAudioPlaybackDevice(id);
    }

    /// <summary>读取输出设备主音量（0~100），失败返回 100。</summary>
    public float GetEndpointVolume(string deviceId)
    {
        try
        {
            var dev = _enumerator.GetDevice(deviceId);
            return dev.AudioEndpointVolume.MasterVolumeLevelScalar * 100f;
        }
        catch
        {
            return 100f;
        }
    }

    /// <summary>读取当前默认输出设备的实时音频峰值（0~1），失败返回 0。用于无声自动修复检测。</summary>
    public float GetDefaultRenderPeak()
    {
        try
        {
            return _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
                .AudioMeterInformation.MasterPeakValue;
        }
        catch
        {
            return 0f;
        }
    }

    /// <summary>设置输出设备主音量（0~100）。</summary>
    public void SetEndpointVolume(string deviceId, float volumePercent)
    {
        try
        {
            var dev = _enumerator.GetDevice(deviceId);
            dev.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(volumePercent / 100f, 0f, 1f);
        }
        catch { }
    }

    /// <summary>在所有激活的渲染设备上查找 A2DP Sink 音频会话，优先当前默认输出设备。</summary>
    private AudioSessionControl? FindA2dpSession()
    {
        // 优先在当前默认输出设备上找（A2DP 会话应挂在默认设备上）
        try
        {
            var def = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            if (def != null)
            {
                var s = FindA2dpSessionOn(def);
                if (s != null) return s;
            }
        }
        catch { }

        // 回退：遍历所有激活设备（兼容会话尚未挂到默认设备的过渡态）
        foreach (var dev in _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            var s = FindA2dpSessionOn(dev);
            if (s != null) return s;
        }
        return null;
    }

    private static AudioSessionControl? FindA2dpSessionOn(MMDevice dev)
    {
        try
        {
            var sessions = dev.AudioSessionManager.Sessions;
            if (sessions == null) return null;
            for (int i = 0; i < sessions.Count; i++)
            {
                var s = sessions[i];
                if (s.IsSystemSoundsSession) continue;
                string name;
                try { name = s.DisplayName; } catch { continue; }
                if (name.Contains("A2DP", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("SNK", StringComparison.OrdinalIgnoreCase))
                    return s;
            }
        }
        catch { }
        return null;
    }

    /// <summary>获取 A2DP Sink 会话音量（0~100），未连接返回 null。</summary>
    public float? GetA2dpSinkVolume() => FindA2dpSession()?.SimpleAudioVolume.Volume * 100f;

    /// <summary>设置 A2DP Sink 会话音量（0~100）。</summary>
    public void SetA2dpSinkVolume(float volumePercent)
    {
        var session = FindA2dpSession();
        if (session == null) return;
        session.SimpleAudioVolume.Volume = Math.Clamp(volumePercent / 100f, 0f, 1f);
    }

    /// <summary>诊断：枚举所有渲染设备（含非激活）上的所有音频会话，返回文本列表。</summary>
    public List<string> DiagnoseSessions()
    {
        var result = new List<string>();
        foreach (var dev in _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.All))
        {
            try
            {
                var sessions = dev.AudioSessionManager.Sessions;
                if (sessions == null) continue;
                for (int i = 0; i < sessions.Count; i++)
                {
                    var s = sessions[i];
                    if (s.IsSystemSoundsSession) continue;
                    string name = "";
                    try { name = s.DisplayName; } catch { }
                    float vol = 0;
                    try { vol = s.SimpleAudioVolume.Volume * 100f; } catch { }
                    result.Add($"状态={dev.State} 设备=[{dev.FriendlyName}] 会话=[{name}] 音量={vol:0}%");
                }
            }
            catch { }
        }
        return result;
    }

    /// <summary>
    /// 低端硬件模式：降低 SBC bitpool 以稳定弱信号下的音频。
    /// 需管理员权限，实现在 LowEndModeService 中。
    /// </summary>
    public void ApplyLowEndMode(bool enabled)
    {
        LowEndModeService.Apply(enabled);
    }
}

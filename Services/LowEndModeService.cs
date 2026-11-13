using System.Diagnostics;
using Microsoft.Win32;

namespace BlueRelay.Services;

/// <summary>
/// 低端硬件模式：降低蓝牙 A2DP 的 SBC bitpool。
/// 需管理员权限写 HKLM，无权限时自动 UAC 提权。
/// </summary>
internal static class LowEndModeService
{
    private const string A2dpKeyPath = @"SYSTEM\CurrentControlSet\Services\BTHPORT\Parameters\A2DP";
    private const int DefaultBitpool = 15;

    public static void Apply(bool enabled)
    {
        if (TryWrite(enabled)) return;
        Elevate(enabled);
    }

    /// <summary>已提权进程直接写（不再尝试提权）。</summary>
    public static void ApplyElevated(bool enabled)
    {
        TryWrite(enabled);
    }

    /// <summary>直接写注册表，返回是否成功（无权限时 false）。</summary>
    private static bool TryWrite(bool enabled)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(A2dpKeyPath, writable: true);
            if (key == null) return false;
            if (enabled)
            {
                key.SetValue("DefaultBitpool", DefaultBitpool, RegistryValueKind.DWord);
                key.SetValue("MaximumBitpool", DefaultBitpool, RegistryValueKind.DWord);
            }
            else
            {
                key.DeleteValue("DefaultBitpool", throwOnMissingValue: false);
                key.DeleteValue("MaximumBitpool", throwOnMissingValue: false);
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>以管理员权限重新启动自身写注册表（触发 UAC）。</summary>
    private static void Elevate(bool enabled)
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return;
            var psi = new ProcessStartInfo(exe, enabled ? "--lowend-on" : "--lowend-off")
            {
                UseShellExecute = true,
                Verb = "runas"
            };
            Process.Start(psi);
        }
        catch
        {
            // 用户取消 UAC
        }
    }
}

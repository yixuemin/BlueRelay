using System.Text.Json;

namespace BlueRelay.Services;

/// <summary>
/// 命令行接口（apc2ctl 风格）：--list / --connect <name> / --disconnect <name> / --toggle。
/// </summary>
public sealed class CliService
{
    private readonly BluetoothService _bluetooth;
    private readonly AudioService _audio;

    public CliService(BluetoothService bluetooth, AudioService audio)
    {
        _bluetooth = bluetooth;
        _audio = audio;
    }

    public async Task<int> RunAsync(string[] args)
    {
        try
        {
            if (args.Length == 0) return 0;

            switch (args[0].ToLowerInvariant())
            {
                case "--list":
                    await ListAsync();
                    return 0;

                case "--connect":
                    if (args.Length < 2) { Console.Error.WriteLine("用法: --connect <设备名>"); return 2; }
                    return await ConnectByNameAsync(args[1]);

                case "--disconnect":
                    if (args.Length < 2) { Console.Error.WriteLine("用法: --disconnect <设备名>"); return 2; }
                    return DisconnectByName(args[1]);

                case "--toggle":
                    return await ToggleAsync();

                case "--volcheck":
                    VolCheck();
                    return 0;

                case "--switchtest":
                    await SwitchTestAsync();
                    return 0;

                case "--help":
                    PrintHelp();
                    return 0;

                default:
                    Console.Error.WriteLine($"未知命令: {args[0]}");
                    PrintHelp();
                    return 2;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"错误: {ex.Message}");
            return 1;
        }
    }

    private async Task ListAsync()
    {
        var devices = await _bluetooth.GetSinkDevicesAsync();
        var list = devices.Select(d => new
        {
            name = d.Name,
            connected = d.IsConnected
        });
        Console.WriteLine(JsonSerializer.Serialize(list));
    }

    private async Task<int> ConnectByNameAsync(string name)
    {
        var devices = await _bluetooth.GetSinkDevicesAsync();
        var dev = devices.FirstOrDefault(d =>
            d.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
        if (dev == null) { Console.Error.WriteLine($"未找到设备: {name}"); return 1; }

        var ok = await _bluetooth.ConnectAsync(dev.Id);
        Console.WriteLine(ok ? $"已连接: {dev.Name}" : $"连接失败: {dev.Name}");
        return ok ? 0 : 1;
    }

    private int DisconnectByName(string name)
    {
        var devices = _bluetooth.GetSinkDevicesAsync().GetAwaiter().GetResult();
        var dev = devices.FirstOrDefault(d =>
            d.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
        if (dev == null) { Console.Error.WriteLine($"未找到设备: {name}"); return 1; }

        _bluetooth.Disconnect(dev.Id);
        Console.WriteLine($"已断开: {dev.Name}");
        return 0;
    }

    private async Task<int> ToggleAsync()
    {
        var devices = await _bluetooth.GetSinkDevicesAsync();
        var dev = devices.FirstOrDefault(d => d.IsConnected) ?? devices.FirstOrDefault();
        if (dev == null) { Console.Error.WriteLine("无可用设备"); return 1; }

        if (dev.IsConnected) { _bluetooth.Disconnect(dev.Id); Console.WriteLine($"已断开: {dev.Name}"); }
        else { await _bluetooth.ConnectAsync(dev.Id); Console.WriteLine($"已连接: {dev.Name}"); }
        return 0;
    }

    private void VolCheck()
    {
        var vol = _audio.GetA2dpSinkVolume();
        Console.WriteLine($"A2DP 会话音量读取: {(vol.HasValue ? $"{vol.Value:0}%" : "未找到")}");
        foreach (var line in _audio.DiagnoseSessions())
            Console.WriteLine("  " + line);
    }

    private async Task SwitchTestAsync()
    {
        var devices = _audio.GetRenderDevices();
        var def = devices.FirstOrDefault(d => d.IsDefault);
        var other = devices.FirstOrDefault(d => !d.IsDefault);
        if (def == null || other == null) { Console.WriteLine("输出设备不足"); return; }

        var connectedIds = _bluetooth.GetConnectedIds();
        Console.WriteLine($"默认设备: {def.Name}，已连接蓝牙: {connectedIds.Count} 个");
        Console.WriteLine("--- 切换前会话 ---");
        foreach (var l in _audio.DiagnoseSessions()) Console.WriteLine("  " + l);

        _audio.SetDefaultDevice(other.Id);
        Console.WriteLine($"\n已切到: {other.Name}");
        if (connectedIds.Count > 0)
        {
            foreach (var id in connectedIds) _bluetooth.Disconnect(id);
            await Task.Delay(500);
            await _bluetooth.ReconnectAllAsync(connectedIds);
            Console.WriteLine("已执行断开重连，等待 2 秒");
        }
        await Task.Delay(2000);
        Console.WriteLine("--- 切换+重连后会话 ---");
        foreach (var l in _audio.DiagnoseSessions()) Console.WriteLine("  " + l);

        _audio.SetDefaultDevice(def.Id);
        Console.WriteLine($"\n已切回: {def.Name}");
    }

    private static void PrintHelp()
    {
        Console.WriteLine("BlueRelay CLI 用法:");
        Console.WriteLine("  --list                列出蓝牙音频设备");
        Console.WriteLine("  --connect <设备名>     连接设备");
        Console.WriteLine("  --disconnect <设备名>  断开设备");
        Console.WriteLine("  --toggle              切换默认设备连接状态");
    }
}

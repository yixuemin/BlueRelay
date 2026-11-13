using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using BlueRelay.Models;

namespace BlueRelay.Services;

/// <summary>
/// 设置持久化：JSON 存储 + 开机自启注册表。
/// </summary>
public sealed class SettingsService
{
    private readonly string _configDir;
    private readonly string _configPath;
    private readonly string _legacyConfigPath;
    private AppSettings _settings = new();

    public AppSettings Current => _settings;

    public SettingsService()
    {
        _configDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BlueRelay");
        _configPath = Path.Combine(_configDir, "settings.json");
        _legacyConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BTAudioTray", "settings.json");
        Load();
    }

    private void Load()
    {
        try
        {
            var migrating = !File.Exists(_configPath) && File.Exists(_legacyConfigPath);
            var path = File.Exists(_configPath) ? _configPath : _legacyConfigPath;
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                _settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                if (migrating) Save();
            }
        }
        catch { }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(_configDir);
            File.WriteAllText(_configPath, JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    public void SetAutoStart(bool enabled)
    {
        _settings.AutoStart = enabled;
        Save();

        using var key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
        var exePath = Environment.ProcessPath ?? "";
        if (key == null) return;
        key.DeleteValue("BTAudioTray", throwOnMissingValue: false);
        if (enabled)
            key.SetValue("BlueRelay", $"\"{exePath}\"");
        else
            key.DeleteValue("BlueRelay", throwOnMissingValue: false);
    }
}

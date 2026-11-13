using System.IO;

namespace BlueRelay.Services;

/// <summary>
/// 本地崩溃报告：捕获未处理异常，写入日志文件（不发送遥测）。
/// </summary>
public static class CrashReporter
{
    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BlueRelay", "logs");

    public static void Initialize()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Write("UnhandledException", e.ExceptionObject as Exception);

        System.Windows.Application.Current.DispatcherUnhandledException += (_, e) =>
        {
            Write("DispatcherUnhandledException", e.Exception);
            e.Handled = true;
        };
    }

    public static void Write(string tag, Exception? ex)
    {
        if (ex == null) return;
        try
        {
            Directory.CreateDirectory(LogDir);
            var path = Path.Combine(LogDir, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            File.WriteAllText(path,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {tag}\n{ex}\n");
        }
        catch { }
    }
}

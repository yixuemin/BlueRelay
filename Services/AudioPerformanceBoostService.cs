using System.Runtime.InteropServices;

namespace BlueRelay.Services;

/// <summary>
/// 音频稳定模式：连接期间在专用线程注册 MMCSS "Pro Audio" 调度并持有系统执行状态，
/// 减少音频卡顿。引用计数：第一个连接启用，最后一个连接断开时停止；
/// 增强线程在事件上阻塞等待，空闲时零 CPU 开销。
/// </summary>
public sealed class AudioPerformanceBoostService : IDisposable
{
    private const uint ES_CONTINUOUS = 0x80000000;
    private const uint ES_SYSTEM_REQUIRED = 0x00000001;
    private const uint ES_AWAYMODE_REQUIRED = 0x00000040;
    // 故意不使用 ES_DISPLAY_REQUIRED：绝不为音频保活而强制屏幕常亮

    [DllImport("avrt.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr AvSetMmThreadCharacteristics(string TaskName, ref uint TaskIndex);

    [DllImport("avrt.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AvRevertMmThreadCharacteristics(IntPtr AvrtHandle);

    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint esFlags);

    private readonly object _gate = new();
    private int _refCount;
    private Thread? _thread;
    private ManualResetEventSlim? _stop;

    /// <summary>获取一份增强引用（连接建立时调用）。</summary>
    public void Acquire()
    {
        lock (_gate)
        {
            _refCount++;
            if (_refCount == 1) Start();
        }
    }

    /// <summary>归还增强引用（连接断开时调用）。</summary>
    public void Release()
    {
        lock (_gate)
        {
            if (_refCount == 0) return;
            _refCount--;
            if (_refCount == 0) Stop();
        }
    }

    private void Start()
    {
        _stop = new ManualResetEventSlim(false);
        _thread = new Thread(BoostLoop) { IsBackground = true, Name = "AudioPerformanceBoost" };
        _thread.Start();
    }

    private void Stop()
    {
        _stop?.Set();
        _thread?.Join(TimeSpan.FromSeconds(2));
        _thread = null;
        _stop?.Dispose();
        _stop = null;
    }

    private void BoostLoop()
    {
        // MMCSS 要求注册与反注册在同一线程完成
        uint taskIndex = 0;
        var handle = IntPtr.Zero;
        try
        {
            handle = AvSetMmThreadCharacteristics("Pro Audio", ref taskIndex);
            SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_AWAYMODE_REQUIRED);
            _stop!.Wait();
        }
        finally
        {
            if (handle != IntPtr.Zero) AvRevertMmThreadCharacteristics(handle);
            SetThreadExecutionState(ES_CONTINUOUS);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _refCount = 0;
            Stop();
        }
    }
}

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using BlueRelay.ViewModels;

namespace BlueRelay.Views;

public partial class MainWindow : Window
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33; // Win11 系统圆角
    private const int DWMWCP_ROUND = 2;

    public MainWindow()
    {
        InitializeComponent();
        // 点击窗口外（桌面等）失去焦点时：先关闭音量弹窗，再隐藏面板
        Deactivated += (_, _) => { CloseVolumePopups(); Hide(); };
        // 点击面板非音量图标区域时关闭音量弹窗
        PreviewMouseDown += OnPreviewMouseDown;
        // 按 Esc 关闭
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Hide(); };
        // 用系统圆角替代 AllowsTransparency（透明窗口是闪烁的根源）
        SourceInitialized += (_, _) => ApplyRoundedCorners();
    }

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        // 点击音量图标按钮时，交给按钮自己切换开关，不在这里关闭
        if (e.OriginalSource is DependencyObject src && FindParent<ToggleButton>(src) != null)
            return;
        CloseVolumePopups();
    }

    private void CloseVolumePopups()
    {
        if (DataContext is MainViewModel vm) vm.CloseAllVolumePopups();
    }

    private static T? FindParent<T>(DependencyObject d) where T : DependencyObject
    {
        while (d != null)
        {
            if (d is T t) return t;
            d = VisualTreeHelper.GetParent(d);
        }
        return null;
    }

    private void ApplyRoundedCorners()
    {
        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            var pref = DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
        }
        catch { }
    }

    /// <summary>显示前重置：清除上次淡入动画（否则 HoldEnd 把 Opacity 锁在终值），回到透明。</summary>
    public void ResetAnimationState()
    {
        BeginAnimation(Window.OpacityProperty, null);
        Opacity = 0;
    }

    /// <summary>极简淡入动画（已移除缩放，避免渲染抖动）。</summary>
    public void PlayShowAnimation()
    {
        BeginAnimation(Window.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)));
    }

    /// <summary>返回当前动画状态（用于自动测试采样）。</summary>
    public string GetAnimState() => $"opacity={Opacity:0.00}";
}

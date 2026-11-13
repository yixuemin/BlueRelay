using System.Windows;
using System.Windows.Input;

namespace BlueRelay.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Hide(); };
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }
}

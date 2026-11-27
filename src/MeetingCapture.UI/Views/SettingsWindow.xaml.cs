using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using MeetingCapture.UI.ViewModels;

namespace MeetingCapture.UI.Views;

public partial class SettingsWindow : Window
{
    [DllImport("user32.dll")]
    private static extern bool SetWindowDisplayAffinity(nint hWnd, uint dwAffinity);
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

    public SettingsWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        Loaded += (s, e) =>
        {
            try
            {
                var handle = new WindowInteropHelper(this).Handle;
                if (handle != 0)
                {
                    SetWindowDisplayAffinity(handle, WDA_EXCLUDEFROMCAPTURE);
                }
            }
            catch { }
        };
    }

    private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using MeetingCapture.UI.ViewModels;

namespace MeetingCapture.UI.Views;

public partial class MainWindow : Window
{
    [DllImport("user32.dll")]
    private static extern bool SetWindowDisplayAffinity(nint hWnd, uint dwAffinity);
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

    private readonly MainViewModel _viewModel;
    private readonly IServiceProvider _serviceProvider;
    private Storyboard? _pulsingStoryboard;

    public MainWindow(MainViewModel viewModel, IServiceProvider serviceProvider)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _serviceProvider = serviceProvider;
        DataContext = _viewModel;

        Loaded += (s, e) =>
        {
            try
            {
                var handle = new WindowInteropHelper(this).Handle;
                if (handle != 0)
                {
                    // Masque complètement l'application MeetingCapture de toutes les captures d'écran et flux vidéo
                    SetWindowDisplayAffinity(handle, WDA_EXCLUDEFROMCAPTURE);
                }
            }
            catch { }
        };

        _viewModel.RequestOpenHistory += OnOpenHistory;
        _viewModel.RequestOpenSettings += OnOpenSettings;

        _viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsRecording))
            {
                Dispatcher.Invoke(() =>
                {
                    if (_viewModel.IsRecording)
                    {
                        StartPulsing();
                    }
                    else
                    {
                        StopPulsing();
                    }
                });
            }
        };
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void StartPulsing()
    {
        _pulsingStoryboard = (Storyboard)FindResource("RecPulsingAnimation");
        _pulsingStoryboard?.Begin(this, true);
    }

    private void StopPulsing()
    {
        _pulsingStoryboard?.Stop(this);
    }

    private void OnOpenHistory()
    {
        var historyWindow = new HistoryWindow(_serviceProvider);
        historyWindow.Topmost = false;
        historyWindow.Show();
    }

    private void OnOpenSettings()
    {
        var settingsWindow = new SettingsWindow(_viewModel);
        settingsWindow.Topmost = false;
        settingsWindow.ShowDialog();
    }
}

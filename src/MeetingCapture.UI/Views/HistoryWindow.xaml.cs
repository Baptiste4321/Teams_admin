using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using MeetingCapture.Core.Interfaces;
using MeetingCapture.Core.Models;
using MeetingCapture.UI.ViewModels;

namespace MeetingCapture.UI.Views;

public partial class HistoryWindow : Window
{
    [DllImport("user32.dll")]
    private static extern bool SetWindowDisplayAffinity(nint hWnd, uint dwAffinity);
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

    private readonly HistoryViewModel _vm;
    private readonly DispatcherTimer _playbackTimer;
    private readonly System.Windows.Media.MediaPlayer _audioPlayer = new();
    private bool _isUserDraggingSlider;

    private bool _isFullscreen;
    private WindowState _prevWindowState;
    private WindowStyle _prevWindowStyle;
    private ResizeMode _prevResizeMode;

    public HistoryWindow(IServiceProvider serviceProvider)
    {
        InitializeComponent();
        var storage = serviceProvider.GetRequiredService<IMeetingStorageService>();
        var transService = serviceProvider.GetService<ITranscriptionService>();
        _vm = new HistoryViewModel(storage, transService);
        DataContext = _vm;

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

        KeyDown += HistoryWindow_KeyDown;

        // Wire media events for audio player
        _audioPlayer.MediaOpened += (s, e) =>
        {
            if (PlayerMediaElement.Source == null && _audioPlayer.NaturalDuration.HasTimeSpan)
            {
                _vm.TotalDurationSeconds = _audioPlayer.NaturalDuration.TimeSpan.TotalSeconds;
            }
        };

        _audioPlayer.MediaEnded += (s, e) =>
        {
            if (PlayerMediaElement.Source == null)
            {
                _playbackTimer.Stop();
                _vm.IsPlaying = false;
                _vm.CurrentPlaybackSeconds = 0;
                _audioPlayer.Stop();
            }
        };

        // Wire playback events
        _vm.RequestPlay += () =>
        {
            bool hasVideo = PlayerMediaElement.Source != null;
            bool hasAudio = _audioPlayer.Source != null;

            if (hasVideo) PlayerMediaElement.Play();
            if (hasAudio) _audioPlayer.Play();

            if (hasVideo || hasAudio)
            {
                _playbackTimer.Start();
            }
        };

        _vm.RequestPause += () =>
        {
            if (PlayerMediaElement.Source != null) PlayerMediaElement.Pause();
            if (_audioPlayer.Source != null) _audioPlayer.Pause();
            _playbackTimer.Stop();
        };

        _vm.RequestStop += () =>
        {
            if (PlayerMediaElement.Source != null) PlayerMediaElement.Stop();
            if (_audioPlayer.Source != null) _audioPlayer.Stop();
            _playbackTimer.Stop();
        };

        _vm.RequestSeek += pos =>
        {
            if (PlayerMediaElement.Source != null) PlayerMediaElement.Position = pos;
            if (_audioPlayer.Source != null) _audioPlayer.Position = pos;
        };

        _vm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(_vm.SelectedMeeting) || e.PropertyName == nameof(_vm.SelectedMediaUri))
            {
                try
                {
                    // 1. Video stream
                    var videoPath = _vm.SelectedMeeting?.ExistingVideoFilePath;
                    if (!string.IsNullOrEmpty(videoPath) && System.IO.File.Exists(videoPath))
                    {
                        PlayerMediaElement.Source = new Uri(videoPath);
                        PlayerMediaElement.Volume = 0; // Pure video playback, audio handled by _audioPlayer
                    }
                    else
                    {
                        PlayerMediaElement.Source = null;
                    }

                    // 2. Audio stream
                    var audioPath = _vm.SelectedMeeting?.ExistingAudioFilePath;
                    if (!string.IsNullOrEmpty(audioPath) && System.IO.File.Exists(audioPath))
                    {
                        _audioPlayer.Open(new Uri(audioPath));
                        _audioPlayer.Volume = _vm.Volume;
                    }
                    else
                    {
                        _audioPlayer.Close();
                    }
                }
                catch
                {
                    PlayerMediaElement.Source = null;
                    _audioPlayer.Close();
                }
            }
        };

        // DispatcherTimer for high precision playback tracking
        _playbackTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _playbackTimer.Tick += (s, e) =>
        {
            if (!_isUserDraggingSlider)
            {
                if (PlayerMediaElement.Source != null && PlayerMediaElement.NaturalDuration.HasTimeSpan)
                {
                    _vm.CurrentPlaybackSeconds = PlayerMediaElement.Position.TotalSeconds;
                }
                else if (_audioPlayer.Source != null && _audioPlayer.NaturalDuration.HasTimeSpan)
                {
                    _vm.CurrentPlaybackSeconds = _audioPlayer.Position.TotalSeconds;
                }

                // Auto-scroll the transcript list to keep current segment visible
                if (_vm.ActiveTranscriptSegment != null)
                {
                    SyncTranscriptList.ScrollIntoView(_vm.ActiveTranscriptSegment);
                }
            }
        };

        Closing += (s, e) =>
        {
            _playbackTimer.Stop();
            PlayerMediaElement.Stop();
            PlayerMediaElement.Close();
            _audioPlayer.Stop();
            _audioPlayer.Close();
        };
    }

    private void PlayerMediaElement_MediaOpened(object sender, RoutedEventArgs e)
    {
        if (PlayerMediaElement.NaturalDuration.HasTimeSpan)
        {
            _vm.TotalDurationSeconds = PlayerMediaElement.NaturalDuration.TimeSpan.TotalSeconds;
        }
    }

    private void PlayerMediaElement_MediaEnded(object sender, RoutedEventArgs e)
    {
        _playbackTimer.Stop();
        _vm.IsPlaying = false;
        _vm.CurrentPlaybackSeconds = 0;
        PlayerMediaElement.Stop();
        _audioPlayer.Stop();
    }

    private void BtnPlay_Click(object sender, RoutedEventArgs e)
    {
        _vm.PlayCommand.Execute(null);
    }

    private void BtnPause_Click(object sender, RoutedEventArgs e)
    {
        _vm.PauseCommand.Execute(null);
    }

    private void BtnStop_Click(object sender, RoutedEventArgs e)
    {
        _vm.StopCommand.Execute(null);
    }

    private void PlaybackSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _isUserDraggingSlider = true;
    }

    private void PlaybackSlider_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        _isUserDraggingSlider = false;
        var pos = TimeSpan.FromSeconds(PlaybackSlider.Value);
        if (PlayerMediaElement.Source != null) PlayerMediaElement.Position = pos;
        if (_audioPlayer.Source != null) _audioPlayer.Position = pos;
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_audioPlayer != null)
        {
            _audioPlayer.Volume = e.NewValue;
        }
        if (PlayerMediaElement != null && _audioPlayer?.Source == null)
        {
            PlayerMediaElement.Volume = e.NewValue;
        }
    }

    private void SyncTranscriptList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SyncTranscriptList.SelectedItem is TranscriptSegment seg)
        {
            var pos = TimeSpan.FromSeconds(seg.Start);
            if (PlayerMediaElement.Source != null) PlayerMediaElement.Position = pos;
            if (_audioPlayer.Source != null) _audioPlayer.Position = pos;

            if (!_vm.IsPlaying)
            {
                _vm.CurrentPlaybackSeconds = seg.Start;
            }
        }
    }

    private void BtnToggleFullscreen_Click(object sender, RoutedEventArgs e)
    {
        ToggleFullscreen();
    }

    private void PlayerMediaElement_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleFullscreen();
            e.Handled = true;
        }
    }

    private void HistoryWindow_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _isFullscreen)
        {
            ToggleFullscreen();
            e.Handled = true;
        }
    }

    private void ToggleFullscreen()
    {
        if (!_isFullscreen)
        {
            // Entrée en plein écran
            _prevWindowState = WindowState;
            _prevWindowStyle = WindowStyle;
            _prevResizeMode = ResizeMode;

            TopHeaderBar.Visibility = Visibility.Collapsed;
            LeftMeetingsBorder.Visibility = Visibility.Collapsed;
            ColMeetingsList.Width = new GridLength(0);

            TranscriptBorder.Visibility = Visibility.Collapsed;
            ColTranscript.Width = new GridLength(0);

            BtnExitFullscreenFloating.Visibility = Visibility.Visible;
            BtnFullscreen.Content = "⛶ Quitter plein écran";

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;

            _isFullscreen = true;
        }
        else
        {
            // Sortie du plein écran
            TopHeaderBar.Visibility = Visibility.Visible;
            LeftMeetingsBorder.Visibility = Visibility.Visible;
            ColMeetingsList.Width = new GridLength(310);

            TranscriptBorder.Visibility = Visibility.Visible;
            ColTranscript.Width = new GridLength(42, GridUnitType.Star);

            BtnExitFullscreenFloating.Visibility = Visibility.Collapsed;
            BtnFullscreen.Content = "⛶ Plein écran";

            WindowStyle = _prevWindowStyle;
            ResizeMode = _prevResizeMode;
            WindowState = _prevWindowState;

            _isFullscreen = false;
        }
    }
}

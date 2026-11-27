using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using MeetingCapture.Core.Enums;
using MeetingCapture.Core.Interfaces;
using MeetingCapture.Core.Models;
using MeetingCapture.Integrations;

namespace MeetingCapture.UI.ViewModels;

public class MainViewModel : ViewModelBase
{
    private readonly IMeetingOrchestrator _orchestrator;
    private readonly IVideoCaptureService _videoService;
    private readonly IAudioCaptureService _audioService;
    private readonly IMicrophoneService _micService;
    private readonly ITranscriptionService _transcriptionService;
    private readonly IChatCaptureService _chatService;
    private readonly IMeetingStorageService _storageService;
    private readonly TeamsWindowTracker _teamsTracker;

    private bool _isRecording;
    private string _elapsedTimeText = "00:00:00";
    private float _systemAudioLevel;
    private float _micAudioLevel;
    private double _videoFps;
    private string _videoResolution = "1920x1080";
    private int _transcriptCount;
    private int _chatCount;
    private string _statusMessage = "Prêt à enregistrer";
    private bool _isDetailsExpanded;

    // Settings
    private WindowInfo? _selectedWindow;
    private ScreenInfo? _selectedScreen;
    private AudioDeviceInfo? _selectedOutputDevice;
    private AudioDeviceInfo? _selectedInputDevice;
    private int _selectedFps = 30;
    private bool _isMicEnabled = false; // Désactivé par défaut : capture uniquement le son des interlocuteurs
    private bool _isDesktopCapture = false;
    private string _meetingTitle = string.Empty;
    private bool _isAlwaysOnTop = false;

    public ObservableCollection<WindowInfo> Windows { get; } = new();
    public ObservableCollection<ScreenInfo> Screens { get; } = new();
    public ObservableCollection<AudioDeviceInfo> OutputDevices { get; } = new();
    public ObservableCollection<AudioDeviceInfo> InputDevices { get; } = new();
    public ObservableCollection<int> FpsOptions { get; } = new() { 15, 30, 60 };
    public ObservableCollection<TranscriptSegment> RecentTranscripts { get; } = new();

    public bool IsRecording
    {
        get => _isRecording;
        set
        {
            if (SetProperty(ref _isRecording, value))
            {
                OnPropertyChanged(nameof(IsNotRecording));
                ((RelayCommand)ToggleRecordCommand).RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsNotRecording => !IsRecording;

    public string ElapsedTimeText
    {
        get => _elapsedTimeText;
        set => SetProperty(ref _elapsedTimeText, value);
    }

    public float SystemAudioLevel
    {
        get => _systemAudioLevel;
        set => SetProperty(ref _systemAudioLevel, value);
    }

    public float MicAudioLevel
    {
        get => _micAudioLevel;
        set => SetProperty(ref _micAudioLevel, value);
    }

    public double VideoFps
    {
        get => _videoFps;
        set => SetProperty(ref _videoFps, value);
    }

    public string VideoResolution
    {
        get => _videoResolution;
        set => SetProperty(ref _videoResolution, value);
    }

    public int TranscriptCount
    {
        get => _transcriptCount;
        set => SetProperty(ref _transcriptCount, value);
    }

    public int ChatCount
    {
        get => _chatCount;
        set => SetProperty(ref _chatCount, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public bool IsDetailsExpanded
    {
        get => _isDetailsExpanded;
        set => SetProperty(ref _isDetailsExpanded, value);
    }

    public WindowInfo? SelectedWindow
    {
        get => _selectedWindow;
        set
        {
            if (SetProperty(ref _selectedWindow, value))
            {
                if (_selectedWindow != null)
                {
                    StatusMessage = _selectedWindow.IsTeams 
                        ? $"Cible Teams : {_selectedWindow.Title}" 
                        : $"Cible : {_selectedWindow.Title}";
                }
            }
        }
    }

    public ScreenInfo? SelectedScreen
    {
        get => _selectedScreen;
        set => SetProperty(ref _selectedScreen, value);
    }

    public AudioDeviceInfo? SelectedOutputDevice
    {
        get => _selectedOutputDevice;
        set => SetProperty(ref _selectedOutputDevice, value);
    }

    public AudioDeviceInfo? SelectedInputDevice
    {
        get => _selectedInputDevice;
        set => SetProperty(ref _selectedInputDevice, value);
    }

    public int SelectedFps
    {
        get => _selectedFps;
        set => SetProperty(ref _selectedFps, value);
    }

    public bool IsMicEnabled
    {
        get => _isMicEnabled;
        set => SetProperty(ref _isMicEnabled, value);
    }

    public bool IsDesktopCapture
    {
        get => _isDesktopCapture;
        set => SetProperty(ref _isDesktopCapture, value);
    }

    public string MeetingTitle
    {
        get => _meetingTitle;
        set => SetProperty(ref _meetingTitle, value);
    }

    public bool IsAlwaysOnTop
    {
        get => _isAlwaysOnTop;
        set => SetProperty(ref _isAlwaysOnTop, value);
    }

    public ICommand ToggleRecordCommand { get; }
    public ICommand RefreshSourcesCommand { get; }
    public ICommand ToggleDetailsCommand { get; }
    public ICommand ToggleAlwaysOnTopCommand { get; }
    public ICommand OpenHistoryCommand { get; }
    public ICommand OpenSettingsCommand { get; }

    public event Action? RequestOpenHistory;
    public event Action? RequestOpenSettings;

    public MainViewModel(
        IMeetingOrchestrator orchestrator,
        IVideoCaptureService videoService,
        IAudioCaptureService audioService,
        IMicrophoneService micService,
        ITranscriptionService transcriptionService,
        IChatCaptureService chatService,
        IMeetingStorageService storageService,
        TeamsWindowTracker teamsTracker)
    {
        _orchestrator = orchestrator;
        _videoService = videoService;
        _audioService = audioService;
        _micService = micService;
        _transcriptionService = transcriptionService;
        _chatService = chatService;
        _storageService = storageService;
        _teamsTracker = teamsTracker;

        ToggleRecordCommand = new RelayCommand(async () => await ToggleRecordAsync());
        RefreshSourcesCommand = new RelayCommand(async () => await RefreshSourcesAsync());
        ToggleDetailsCommand = new RelayCommand(() => IsDetailsExpanded = !IsDetailsExpanded);
        ToggleAlwaysOnTopCommand = new RelayCommand(() => IsAlwaysOnTop = !IsAlwaysOnTop);
        OpenHistoryCommand = new RelayCommand(() => RequestOpenHistory?.Invoke());
        OpenSettingsCommand = new RelayCommand(() => RequestOpenSettings?.Invoke());

        // Wire events
        _orchestrator.ElapsedTimeUpdated += (s, span) =>
        {
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                ElapsedTimeText = span.ToString(@"hh\:mm\:ss");
                VideoFps = Math.Round(_videoService.CurrentFps, 1);
                TranscriptCount = _transcriptionService.SegmentsCount;
                ChatCount = _chatService.MessagesCount;
            });
        };

        _orchestrator.OverallStatusChanged += (s, status) =>
        {
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                IsRecording = status == CaptureStatus.Capturing;
                StatusMessage = status switch
                {
                    CaptureStatus.Capturing => "Enregistrement en cours...",
                    CaptureStatus.Initializing => "Démarrage des modules...",
                    CaptureStatus.Stopping => "Finalisation des fichiers...",
                    CaptureStatus.Stopped => "Enregistrement sauvegardé",
                    CaptureStatus.Error => "Erreur détectée",
                    _ => "Prêt"
                };
            });
        };

        _audioService.LevelUpdated += (s, level) =>
        {
            Application.Current?.Dispatcher.InvokeAsync(() => SystemAudioLevel = level);
        };

        _micService.LevelUpdated += (s, level) =>
        {
            Application.Current?.Dispatcher.InvokeAsync(() => MicAudioLevel = level);
        };

        _transcriptionService.SegmentReceived += (s, seg) =>
        {
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                RecentTranscripts.Insert(0, seg);
                if (RecentTranscripts.Count > 30) RecentTranscripts.RemoveAt(RecentTranscripts.Count - 1);
            });
        };

        _ = RefreshSourcesAsync();
    }

    public async Task RefreshSourcesAsync()
    {
        var windows = await _videoService.GetAvailableWindowsAsync();
        var screens = await _videoService.GetAvailableScreensAsync();
        var outputs = await _audioService.GetOutputDevicesAsync();
        var inputs = await _micService.GetInputDevicesAsync();

        Windows.Clear();
        foreach (var w in windows) Windows.Add(w);

        Screens.Clear();
        foreach (var sc in screens) Screens.Add(sc);

        OutputDevices.Clear();
        foreach (var o in outputs) OutputDevices.Add(o);

        InputDevices.Clear();
        foreach (var i in inputs) InputDevices.Add(i);

        // Auto-select Teams window if available
        SelectedWindow = Windows.FirstOrDefault(w => w.IsTeams) ?? Windows.FirstOrDefault();
        SelectedScreen = Screens.FirstOrDefault(s => s.IsPrimary) ?? Screens.FirstOrDefault();
        SelectedOutputDevice = OutputDevices.FirstOrDefault(o => o.IsDefault && !o.Name.Contains("EPSON", StringComparison.OrdinalIgnoreCase))
            ?? OutputDevices.FirstOrDefault(o => !o.Name.Contains("EPSON", StringComparison.OrdinalIgnoreCase))
            ?? OutputDevices.FirstOrDefault(o => o.IsDefault)
            ?? OutputDevices.FirstOrDefault();
        SelectedInputDevice = InputDevices.FirstOrDefault(i => i.IsDefault) ?? InputDevices.FirstOrDefault();

        if (SelectedWindow != null && SelectedWindow.IsTeams)
        {
            StatusMessage = $"Fenêtre Teams détectée : {SelectedWindow.Title}";
        }
        else
        {
            StatusMessage = "Prêt à enregistrer";
        }
    }

    private async Task ToggleRecordAsync()
    {
        if (!IsRecording)
        {
            // Start recording
            bool captureEntireScreen = IsDesktopCapture || SelectedWindow == null;

            var videoOpts = new VideoCaptureOptions(
                WindowHandle: SelectedWindow?.Handle ?? 0,
                ScreenIndex: SelectedScreen?.Index ?? 0,
                CaptureEntireScreen: captureEntireScreen,
                TargetFps: SelectedFps,
                TargetWidth: !captureEntireScreen && SelectedWindow != null ? SelectedWindow.Width : (SelectedScreen?.Width ?? 1920),
                TargetHeight: !captureEntireScreen && SelectedWindow != null ? SelectedWindow.Height : (SelectedScreen?.Height ?? 1080)
            );

            int targetPid = SelectedWindow?.ProcessId ?? 0;
            if (targetPid == 0)
            {
                var teamsProc = _teamsTracker.FindTeamsProcess();
                if (teamsProc != null) targetPid = teamsProc.Id;
            }
            else
            {
                // Trouver le processus racine de l'application (ex: msedge ou ms-teams) pour que le process loopback englobe les flux audio
                targetPid = TeamsWindowTracker.GetRootProcessId(targetPid);
            }

            var audioOpts = new AudioCaptureOptions(
                DeviceId: SelectedOutputDevice?.Id,
                DeviceName: SelectedOutputDevice?.Name ?? "Défaut",
                TargetProcessId: targetPid,
                ProcessLoopback: true
            );

            var micOpts = new MicrophoneOptions(
                Enabled: IsMicEnabled,
                DeviceId: SelectedInputDevice?.Id,
                DeviceName: SelectedInputDevice?.Name ?? "Défaut"
            );

            var transOpts = new TranscriptionOptions(Language: "fr", UseLocalEngine: true);
            var chatOpts = new ChatCaptureOptions(Enabled: true);

            string title = string.IsNullOrWhiteSpace(MeetingTitle)
                ? (SelectedWindow?.IsTeams == true ? SelectedWindow.Title : $"Réunion_{DateTime.Now:yyyyMMdd_HHmm}")
                : MeetingTitle;

            RecentTranscripts.Clear();
            await _orchestrator.StartRecordingAsync(videoOpts, audioOpts, micOpts, transOpts, chatOpts, meetingTitle: title);
        }
        else
        {
            // Stop recording
            var finishedSession = await _orchestrator.StopRecordingAsync();
            if (finishedSession != null)
            {
                StatusMessage = $"Archivé dans {Path.GetFileName(finishedSession.RootDirectory)}";
            }
        }
    }
}

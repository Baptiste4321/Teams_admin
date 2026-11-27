using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using MeetingCapture.Core.Interfaces;
using MeetingCapture.Core.Models;

namespace MeetingCapture.UI.ViewModels;

public class HistoryViewModel : ViewModelBase
{
    private readonly IMeetingStorageService _storageService;
    private readonly ITranscriptionService? _transcriptionService;
    private MeetingSession? _selectedMeeting;
    private string _filterText = string.Empty;
    private bool _isTranscribing;

    // Multimedia & Synchronisation
    private string? _selectedMediaUri;
    private bool _hasMedia;
    private string _mediaTypeBadge = "Aucun média";
    private double _currentPlaybackSeconds;
    private double _totalDurationSeconds;
    private string _playbackTimeDisplay = "00:00 / 00:00";
    private bool _isPlaying;
    private double _volume = 0.8;
    private TranscriptSegment? _activeTranscriptSegment;

    public ObservableCollection<MeetingSession> Meetings { get; } = new();
    public ObservableCollection<MeetingSession> FilteredMeetings { get; } = new();

    public MeetingSession? SelectedMeeting
    {
        get => _selectedMeeting;
        set
        {
            if (SetProperty(ref _selectedMeeting, value))
            {
                OnPropertyChanged(nameof(HasSelectedMeeting));
                OnPropertyChanged(nameof(SelectedMeetingSummary));
                OnPropertyChanged(nameof(SelectedMeetingDecisions));
                OnPropertyChanged(nameof(SelectedMeetingActions));

                UpdateMediaSource();
            }
        }
    }

    public bool HasSelectedMeeting => SelectedMeeting != null;
    public string SelectedMeetingSummary => SelectedMeeting?.AiAnalysis?.ExecutiveSummary ?? "Aucun résumé IA disponible pour cette réunion.";
    public IReadOnlyList<string> SelectedMeetingDecisions => SelectedMeeting?.AiAnalysis?.KeyDecisions ?? (IReadOnlyList<string>)Array.Empty<string>();
    public IReadOnlyList<ActionItem> SelectedMeetingActions => SelectedMeeting?.AiAnalysis?.ActionItems ?? (IReadOnlyList<ActionItem>)Array.Empty<ActionItem>();

    public string? SelectedMediaUri
    {
        get => _selectedMediaUri;
        set => SetProperty(ref _selectedMediaUri, value);
    }

    public bool HasMedia
    {
        get => _hasMedia;
        set => SetProperty(ref _hasMedia, value);
    }

    public string MediaTypeBadge
    {
        get => _mediaTypeBadge;
        set => SetProperty(ref _mediaTypeBadge, value);
    }

    public double CurrentPlaybackSeconds
    {
        get => _currentPlaybackSeconds;
        set
        {
            if (SetProperty(ref _currentPlaybackSeconds, value))
            {
                UpdateActiveSegment(value);
                UpdatePlaybackDisplay();
            }
        }
    }

    public double TotalDurationSeconds
    {
        get => _totalDurationSeconds;
        set
        {
            if (SetProperty(ref _totalDurationSeconds, value))
            {
                UpdatePlaybackDisplay();
            }
        }
    }

    public string PlaybackTimeDisplay
    {
        get => _playbackTimeDisplay;
        set => SetProperty(ref _playbackTimeDisplay, value);
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        set => SetProperty(ref _isPlaying, value);
    }

    public double Volume
    {
        get => _volume;
        set => SetProperty(ref _volume, value);
    }

    public TranscriptSegment? ActiveTranscriptSegment
    {
        get => _activeTranscriptSegment;
        set => SetProperty(ref _activeTranscriptSegment, value);
    }

    public bool IsTranscribing
    {
        get => _isTranscribing;
        set
        {
            if (SetProperty(ref _isTranscribing, value))
            {
                OnPropertyChanged(nameof(IsNotTranscribing));
                ((RelayCommand)RetranscribeMeetingCommand).RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsNotTranscribing => !IsTranscribing;

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (SetProperty(ref _filterText, value))
            {
                ApplyFilter();
            }
        }
    }

    public ICommand RefreshCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand ExportZipCommand { get; }
    public ICommand DeleteMeetingCommand { get; }
    public ICommand LoadExternalFolderCommand { get; }
    public ICommand RetranscribeMeetingCommand { get; }
    public ICommand PlayCommand { get; }
    public ICommand PauseCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand SeekToSegmentCommand { get; }

    public event Action<TimeSpan>? RequestSeek;
    public event Action? RequestPlay;
    public event Action? RequestPause;
    public event Action? RequestStop;

    public HistoryViewModel(IMeetingStorageService storageService, ITranscriptionService? transcriptionService = null)
    {
        _storageService = storageService;
        _transcriptionService = transcriptionService;

        RefreshCommand = new RelayCommand(async () => await LoadMeetingsAsync());
        RetranscribeMeetingCommand = new RelayCommand(async () => await RetranscribeCurrentMeetingAsync(), () => HasSelectedMeeting && !IsTranscribing);

        OpenFolderCommand = new RelayCommand(() =>
        {
            if (SelectedMeeting != null)
            {
                if (Directory.Exists(SelectedMeeting.RootDirectory))
                {
                    _storageService.OpenMeetingFolder(SelectedMeeting.RootDirectory);
                }
                else
                {
                    MessageBox.Show($"Le dossier de cette réunion est introuvable sur le disque :\n{SelectedMeeting.RootDirectory}", "Dossier introuvable", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }, () => HasSelectedMeeting);

        ExportZipCommand = new RelayCommand(async () =>
        {
            if (SelectedMeeting == null) return;

            var safeTitle = string.Concat(SelectedMeeting.Title.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            var defaultFileName = $"{safeTitle}_{SelectedMeeting.Metadata.Date}.zip";

            var dialog = new SaveFileDialog
            {
                Title = "Exporter la réunion complète en archive ZIP",
                FileName = defaultFileName,
                Filter = "Archive ZIP (*.zip)|*.zip",
                DefaultExt = ".zip",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            };

            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.FileName))
            {
                // Export direct du dossier de session
                var exportSource = Directory.Exists(SelectedMeeting.RootDirectory) ? SelectedMeeting.RootDirectory : SelectedMeeting.Id;
                var success = await _storageService.ExportMeetingAsync(exportSource, dialog.FileName);

                if (success)
                {
                    var result = MessageBox.Show(
                        $"✅ Exportation terminée avec succès !\n\nFichier créé :\n{dialog.FileName}\n\nSouhaitez-vous ouvrir le dossier contenant l'archive ?",
                        "Export ZIP Réussi",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information);

                    if (result == MessageBoxResult.Yes)
                    {
                        var folder = Path.GetDirectoryName(dialog.FileName);
                        if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                        {
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = folder,
                                UseShellExecute = true
                            });
                        }
                    }
                }
                else
                {
                    MessageBox.Show("Une erreur est survenue lors de la création de l'archive ZIP.", "Erreur d'exportation", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }, () => HasSelectedMeeting);

        DeleteMeetingCommand = new RelayCommand(async () =>
        {
            if (SelectedMeeting == null) return;

            var confirm = MessageBox.Show(
                $"Êtes-vous sûr de vouloir supprimer définitivement cette réunion ?\n\n« {SelectedMeeting.Title} »\n\nTous les fichiers associés (vidéo, audio, transcription) seront supprimés.",
                "Confirmation de suppression",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm == MessageBoxResult.Yes)
            {
                await _storageService.DeleteMeetingAsync(SelectedMeeting.Id);
                await LoadMeetingsAsync();
                MessageBox.Show("La réunion a été supprimée avec succès.", "Suppression effectuée", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }, () => HasSelectedMeeting);

        LoadExternalFolderCommand = new RelayCommand(async () => await LoadExternalFolderAsync());

        PlayCommand = new RelayCommand(() =>
        {
            IsPlaying = true;
            RequestPlay?.Invoke();
        });

        PauseCommand = new RelayCommand(() =>
        {
            IsPlaying = false;
            RequestPause?.Invoke();
        });

        StopCommand = new RelayCommand(() =>
        {
            IsPlaying = false;
            CurrentPlaybackSeconds = 0;
            RequestStop?.Invoke();
        });

        SeekToSegmentCommand = new RelayCommand<TranscriptSegment>(segment =>
        {
            if (segment != null)
            {
                SeekTo(segment.Start);
            }
        });

        _ = LoadMeetingsAsync();
    }

    public void SeekTo(double seconds)
    {
        CurrentPlaybackSeconds = seconds;
        RequestSeek?.Invoke(TimeSpan.FromSeconds(seconds));
    }

    private void UpdateMediaSource()
    {
        RequestStop?.Invoke();
        IsPlaying = false;
        CurrentPlaybackSeconds = 0;

        if (SelectedMeeting == null)
        {
            SelectedMediaUri = null;
            HasMedia = false;
            MediaTypeBadge = "Aucun média";
            return;
        }

        var video = SelectedMeeting.ExistingVideoFilePath;
        var audio = SelectedMeeting.ExistingAudioFilePath;

        if (!string.IsNullOrEmpty(video) && File.Exists(video))
        {
            SelectedMediaUri = video;
            HasMedia = true;
            MediaTypeBadge = Path.GetExtension(video).ToUpperInvariant().TrimStart('.') + (!string.IsNullOrEmpty(audio) && File.Exists(audio) ? " (Vidéo + Son synchro)" : " (Vidéo)");
        }
        else if (!string.IsNullOrEmpty(audio) && File.Exists(audio))
        {
            SelectedMediaUri = audio;
            HasMedia = true;
            MediaTypeBadge = Path.GetExtension(audio).ToUpperInvariant().TrimStart('.') + " (Piste Audio)";
        }
        else
        {
            SelectedMediaUri = null;
            HasMedia = false;
            MediaTypeBadge = "Transcription seule (aucun média brut)";
        }

        if (SelectedMeeting.Metadata.DurationSeconds > 0)
        {
            TotalDurationSeconds = SelectedMeeting.Metadata.DurationSeconds;
        }
        else if (SelectedMeeting.Duration.TotalSeconds > 0)
        {
            TotalDurationSeconds = SelectedMeeting.Duration.TotalSeconds;
        }
        else
        {
            TotalDurationSeconds = 0;
        }
    }

    private void UpdateActiveSegment(double seconds)
    {
        if (SelectedMeeting == null || SelectedMeeting.TranscriptSegments.Count == 0)
        {
            ActiveTranscriptSegment = null;
            return;
        }

        var seg = SelectedMeeting.TranscriptSegments.FirstOrDefault(s =>
            seconds >= s.Start && seconds <= (s.End > s.Start ? s.End : s.Start + 2.5));

        if (seg != null && seg != ActiveTranscriptSegment)
        {
            ActiveTranscriptSegment = seg;
        }
    }

    private void UpdatePlaybackDisplay()
    {
        var cur = TimeSpan.FromSeconds(CurrentPlaybackSeconds);
        var tot = TimeSpan.FromSeconds(TotalDurationSeconds);
        PlaybackTimeDisplay = $"{cur:mm\\:ss} / {tot:mm\\:ss}";
    }

    public async Task LoadExternalFolderAsync()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Sélectionner un dossier d'enregistrement de réunion"
        };

        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
        {
            var session = await _storageService.LoadSessionFromDirectoryAsync(dialog.FolderName);
            if (session != null)
            {
                Meetings.Insert(0, session);
                ApplyFilter();
                SelectedMeeting = session;
            }
        }
    }

    public async Task LoadMeetingsAsync()
    {
        var list = await _storageService.GetMeetingHistoryAsync();
        Meetings.Clear();
        foreach (var m in list)
        {
            Meetings.Add(m);
        }
        ApplyFilter();
        if (SelectedMeeting == null && FilteredMeetings.Count > 0)
        {
            SelectedMeeting = FilteredMeetings[0];
        }
    }

    private void ApplyFilter()
    {
        FilteredMeetings.Clear();
        var filter = FilterText.Trim();
        foreach (var m in Meetings)
        {
            if (string.IsNullOrEmpty(filter) ||
                m.Title.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                m.Metadata.Date.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                FilteredMeetings.Add(m);
            }
        }
    }

    private async Task RetranscribeCurrentMeetingAsync()
    {
        if (SelectedMeeting == null || _transcriptionService == null) return;
        var audio = SelectedMeeting.ExistingAudioFilePath;
        if (string.IsNullOrEmpty(audio) || !File.Exists(audio)) return;

        IsTranscribing = true;
        try
        {
            var segments = await _transcriptionService.TranscribeAudioFileAsync(audio, "fr");
            if (segments.Count > 0)
            {
                var meeting = SelectedMeeting;
                meeting.TranscriptSegments = segments.ToList();
                await _storageService.SaveTranscriptAsync(meeting);
                // Force WPF to rebind the full object (List<T> ne notifie pas les changements internes)
                _selectedMeeting = null;
                SetProperty(ref _selectedMeeting, meeting);
                OnPropertyChanged(nameof(HasSelectedMeeting));
                OnPropertyChanged(nameof(SelectedMeetingSummary));
                OnPropertyChanged(nameof(SelectedMeetingDecisions));
                OnPropertyChanged(nameof(SelectedMeetingActions));
            }
        }
        finally
        {
            IsTranscribing = false;
        }
    }
}

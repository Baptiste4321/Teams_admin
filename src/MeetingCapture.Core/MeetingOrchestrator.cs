using System.Diagnostics;
using MeetingCapture.Core.Enums;
using MeetingCapture.Core.Interfaces;
using MeetingCapture.Core.Models;

namespace MeetingCapture.Core;

public class MeetingOrchestrator : IMeetingOrchestrator, IDisposable
{
    private readonly IVideoCaptureService _videoService;
    private readonly IAudioCaptureService _audioService;
    private readonly IMicrophoneService _micService;
    private readonly ITranscriptionService _transcriptionService;
    private readonly IChatCaptureService _chatService;
    private readonly ISynchronizationService _syncService;
    private readonly IMeetingStorageService _storageService;
    private readonly IMeetingAnalysisService _analysisService;

    private readonly System.Timers.Timer _timer;
    private readonly List<string> _sessionLogs = new();
    private readonly object _lock = new();

    public MeetingSession? CurrentSession { get; private set; }
    public bool IsRecording => CurrentSession != null && _videoService.IsCapturing;
    public TimeSpan ElapsedTime => CurrentSession != null ? CurrentSession.Duration : TimeSpan.Zero;

    public event EventHandler<CaptureStatus>? OverallStatusChanged;
    public event EventHandler<TimeSpan>? ElapsedTimeUpdated;

    public MeetingOrchestrator(
        IVideoCaptureService videoService,
        IAudioCaptureService audioService,
        IMicrophoneService micService,
        ITranscriptionService transcriptionService,
        IChatCaptureService chatService,
        ISynchronizationService syncService,
        IMeetingStorageService storageService,
        IMeetingAnalysisService analysisService)
    {
        _videoService = videoService;
        _audioService = audioService;
        _micService = micService;
        _transcriptionService = transcriptionService;
        _chatService = chatService;
        _syncService = syncService;
        _storageService = storageService;
        _analysisService = analysisService;

        _timer = new System.Timers.Timer(500);
        _timer.Elapsed += (s, e) =>
        {
            if (CurrentSession != null)
            {
                ElapsedTimeUpdated?.Invoke(this, CurrentSession.Duration);

                // Feed audio samples/duration to transcription engine
                if (_transcriptionService.Status == CaptureStatus.Capturing)
                {
                    _transcriptionService.IngestAudioData(ReadOnlySpan<byte>.Empty, _syncService.GetElapsedSeconds());
                }
            }
        };

        // Wire events for logging
        _videoService.ErrorOccurred += (s, ex) => Log($"[Video Error] {ex.Message}");
        _audioService.ErrorOccurred += (s, ex) => Log($"[Audio Error] {ex.Message}");
        _micService.ErrorOccurred += (s, ex) => Log($"[Mic Error] {ex.Message}");
    }

    public async Task<MeetingSession> StartRecordingAsync(
        VideoCaptureOptions videoOpts,
        AudioCaptureOptions audioOpts,
        MicrophoneOptions micOpts,
        TranscriptionOptions transOpts,
        ChatCaptureOptions chatOpts,
        string? customBasePath = null,
        string? meetingTitle = null,
        CancellationToken ct = default)
    {
        lock (_lock)
        {
            _sessionLogs.Clear();
            Log($"[REC Started] Initializing recording session: '{meetingTitle}'");

            // 1. Create meeting session and folder structure
            CurrentSession = _storageService.CreateMeetingSession(customBasePath, meetingTitle);
            Log($"[Storage] Root created at {CurrentSession.RootDirectory}");

            // 2. Start monotonic synchronization clock
            _syncService.StartSession(CurrentSession.Id);

            // 3. Configure file paths
            videoOpts = videoOpts with { OutputFilePath = CurrentSession.VideoFilePath };
            audioOpts = audioOpts with { OutputFilePath = CurrentSession.SystemAudioFilePath };
            micOpts = micOpts with { OutputFilePath = CurrentSession.MicrophoneFilePath };
        }

        OverallStatusChanged?.Invoke(this, CaptureStatus.Initializing);

        // 4. Start Video Capture
        bool videoOk = await _videoService.StartCaptureAsync(videoOpts, ct);
        Log($"[Video] Started: {videoOk} (Resolution: {_videoService.CurrentResolution.Width}x{_videoService.CurrentResolution.Height})");

        // 5. Start Audio Capture (WASAPI Loopback)
        bool audioOk = await _audioService.StartCaptureAsync(audioOpts, ct);
        Log($"[Audio System] Started: {audioOk} (Device: {_audioService.ActiveDeviceName})");

        // 6. Start Microphone (WASAPI Input)
        bool micOk = await _micService.StartCaptureAsync(micOpts, ct);
        Log($"[Microphone] Started: {micOk} (Device: {_micService.ActiveDeviceName})");

        // 7. Start Transcription
        bool transOk = await _transcriptionService.StartAsync(transOpts, ct);
        Log($"[Transcription] Started: {transOk} (Engine: {_transcriptionService.EngineName})");

        // 8. Start Chat
        bool chatOk = await _chatService.StartAsync(chatOpts, ct);
        Log($"[Chat] Started: {chatOk} (Source: {_chatService.SourceType})");

        // 9. Update Metadata
        CurrentSession.Metadata.CapturedSources.AddRange(new[] { "Video", "SystemAudio", "Microphone", "Transcription", "Chat" });
        CurrentSession.Metadata.Statuses["Video"] = _videoService.Status.ToString();
        CurrentSession.Metadata.Statuses["SystemAudio"] = _audioService.Status.ToString();
        CurrentSession.Metadata.Statuses["Microphone"] = _micService.Status.ToString();
        CurrentSession.Metadata.Statuses["Transcription"] = _transcriptionService.Status.ToString();
        CurrentSession.Metadata.Statuses["Chat"] = _chatService.Status.ToString();
        CurrentSession.Metadata.Resolution = $"{_videoService.CurrentResolution.Width}x{_videoService.CurrentResolution.Height}";
        CurrentSession.Metadata.Fps = (int)videoOpts.TargetFps;
        CurrentSession.Metadata.Devices.Add(_audioService.ActiveDeviceName);
        CurrentSession.Metadata.Devices.Add(_micService.ActiveDeviceName);

        _timer.Start();
        OverallStatusChanged?.Invoke(this, CaptureStatus.Capturing);

        return CurrentSession;
    }

    public async Task<MeetingSession?> StopRecordingAsync()
    {
        if (CurrentSession == null) return null;

        _timer.Stop();
        OverallStatusChanged?.Invoke(this, CaptureStatus.Stopping);
        Log("[REC Stopping] Finalizing all capture sources...");

        // 1. Stop video
        await _videoService.StopCaptureAsync();
        Log($"[Video] Stopped. Total frames: {_videoService.CapturedFramesCount}");

        // 2. Stop audio
        await _audioService.StopCaptureAsync();
        Log($"[Audio System] Stopped. Total samples: {_audioService.SamplesCaptured}");

        // 3. Stop microphone
        await _micService.StopCaptureAsync();
        Log($"[Microphone] Stopped. Total samples: {_micService.SamplesCaptured}");

        // 4. Stop transcription & collect
        await _transcriptionService.StopAsync();

        // 4b. Post-recording Whisper transcription on captured audio
        var audioPath = CurrentSession.ExistingAudioFilePath;
        if (!string.IsNullOrEmpty(audioPath) && File.Exists(audioPath))
        {
            Log($"[Transcription] Lancement de la transcription post-enregistrement sur {Path.GetFileName(audioPath)}...");
            try
            {
                var transResults = await _transcriptionService.TranscribeAudioFileAsync(audioPath, "fr");
                CurrentSession.TranscriptSegments = transResults.ToList();
                Log($"[Transcription] Transcription terminée avec succès : {transResults.Count} segments transcrits.");
            }
            catch (Exception ex)
            {
                Log($"[Transcription Error] {ex.Message}");
                CurrentSession.TranscriptSegments = new List<TranscriptSegment>();
            }
        }
        else
        {
            CurrentSession.TranscriptSegments = new List<TranscriptSegment>();
        }
        Log($"[Transcription] Segments finaux : {CurrentSession.TranscriptSegments.Count}");

        // 5. Stop chat & collect
        await _chatService.StopAsync();
        CurrentSession.ChatMessages = _chatService.GetMessages().ToList();
        Log($"[Chat] Stopped. Messages: {CurrentSession.ChatMessages.Count}");

        // 6. Stop sync
        _syncService.StopSession();

        // 7. Session timing
        CurrentSession.EndTime = DateTime.UtcNow;

        // 8. Run AI Analysis
        try
        {
            Log("[AI] Running automated meeting analysis...");
            CurrentSession.AiAnalysis = await _analysisService.AnalyzeMeetingAsync(CurrentSession);
            Log("[AI] Analysis completed (Executive summary, decisions, action items).");
        }
        catch (Exception ex)
        {
            Log($"[AI Error] {ex.Message}");
        }

        // 9. Save all artifacts to disk
        try
        {
            await _storageService.SaveTranscriptAsync(CurrentSession);
            await _storageService.SaveChatAsync(CurrentSession);
            await _storageService.SaveMetadataAsync(CurrentSession);
            await _storageService.SaveLogsAsync(CurrentSession, _sessionLogs);
            Log($"[Storage] All artifacts successfully saved to {CurrentSession.RootDirectory}");
        }
        catch (Exception ex)
        {
            Log($"[Storage Error] {ex.Message}");
        }

        OverallStatusChanged?.Invoke(this, CaptureStatus.Stopped);

        var completed = CurrentSession;
        CurrentSession = null;
        return completed;
    }

    public DiagnosticReport GetCurrentDiagnostic()
    {
        var report = _videoService.GetDiagnostic();

        report.SystemAudioActive = _audioService.IsCapturing;
        report.SystemAudioSamples = _audioService.SamplesCaptured;
        report.SystemAudioLevel = _audioService.CurrentLevel;

        report.MicrophoneActive = _micService.IsCapturing;
        report.MicrophoneSamples = _micService.SamplesCaptured;
        report.MicrophoneLevel = _micService.CurrentLevel;

        report.TranscriptCount = _transcriptionService.SegmentsCount;
        report.ChatMessageCount = _chatService.MessagesCount;

        report.EstimatedAudioVideoSkewMs = _syncService.CalculateAudioVideoSkewMs(
            _videoService.CapturedFramesCount,
            _videoService.CurrentFps > 0 ? _videoService.CurrentFps : 30.0,
            _audioService.SamplesCaptured,
            _audioService.SampleRate > 0 ? _audioService.SampleRate : 48000
        );

        return report;
    }

    private void Log(string message)
    {
        lock (_lock)
        {
            string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
            _sessionLogs.Add(line);
            Debug.WriteLine(line);
        }
    }

    public void Dispose()
    {
        _timer.Dispose();
        _videoService.Dispose();
        _audioService.Dispose();
        _micService.Dispose();
        _transcriptionService.Dispose();
        _chatService.Dispose();
    }
}

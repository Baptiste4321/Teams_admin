using MeetingCapture.Core.Enums;
using MeetingCapture.Core.Models;

namespace MeetingCapture.Core.Interfaces;

public interface IVideoCaptureService : IDisposable
{
    CaptureStatus Status { get; }
    bool IsCapturing { get; }
    double CurrentFps { get; }
    (int Width, int Height) CurrentResolution { get; }
    long CapturedFramesCount { get; }
    nint ActiveWindowHandle { get; }

    event EventHandler<CaptureStatus>? StatusChanged;
    event EventHandler<Exception>? ErrorOccurred;
    event EventHandler<(int Width, int Height, long FrameIndex)>? FrameCaptured;

    Task<IReadOnlyList<WindowInfo>> GetAvailableWindowsAsync();
    Task<IReadOnlyList<ScreenInfo>> GetAvailableScreensAsync();
    Task<bool> StartCaptureAsync(VideoCaptureOptions options, CancellationToken ct = default);
    Task StopCaptureAsync();
    DiagnosticReport GetDiagnostic();
}

public interface IAudioCaptureService : IDisposable
{
    CaptureStatus Status { get; }
    bool IsCapturing { get; }
    float CurrentLevel { get; }
    long SamplesCaptured { get; }
    int SampleRate { get; }
    string ActiveDeviceName { get; }

    event EventHandler<CaptureStatus>? StatusChanged;
    event EventHandler<float>? LevelUpdated;
    event EventHandler<string>? DeviceChanged;
    event EventHandler<Exception>? ErrorOccurred;

    Task<IReadOnlyList<AudioDeviceInfo>> GetOutputDevicesAsync();
    Task<bool> StartCaptureAsync(AudioCaptureOptions options, CancellationToken ct = default);
    Task StopCaptureAsync();
}

public interface IMicrophoneService : IDisposable
{
    CaptureStatus Status { get; }
    bool IsCapturing { get; }
    float CurrentLevel { get; }
    long SamplesCaptured { get; }
    int SampleRate { get; }
    string ActiveDeviceName { get; }

    event EventHandler<CaptureStatus>? StatusChanged;
    event EventHandler<float>? LevelUpdated;
    event EventHandler<Exception>? ErrorOccurred;

    Task<IReadOnlyList<AudioDeviceInfo>> GetInputDevicesAsync();
    Task<bool> StartCaptureAsync(MicrophoneOptions options, CancellationToken ct = default);
    Task StopCaptureAsync();
}

public interface ITranscriptionService : IDisposable
{
    CaptureStatus Status { get; }
    string EngineName { get; }
    int SegmentsCount { get; }

    event EventHandler<TranscriptSegment>? SegmentReceived;
    event EventHandler<CaptureStatus>? StatusChanged;

    Task<bool> StartAsync(TranscriptionOptions options, CancellationToken ct = default);
    Task StopAsync();
    void IngestAudioData(ReadOnlySpan<byte> pcmData, double relativeSeconds);
    IReadOnlyList<TranscriptSegment> GetFullTranscript();
    Task<IReadOnlyList<TranscriptSegment>> TranscribeAudioFileAsync(string audioFilePath, string language = "fr", CancellationToken ct = default);
}

public interface IChatCaptureService : IDisposable
{
    CaptureStatus Status { get; }
    int MessagesCount { get; }
    string SourceType { get; }

    event EventHandler<ChatMessage>? MessageReceived;
    event EventHandler<CaptureStatus>? StatusChanged;

    Task<bool> StartAsync(ChatCaptureOptions options, CancellationToken ct = default);
    Task StopAsync();
    Task ImportChatMessagesAsync(string filePathOrContent);
    void AddMessage(ChatMessage message);
    IReadOnlyList<ChatMessage> GetMessages();
}

public interface ISynchronizationService
{
    bool IsRunning { get; }
    string CurrentSessionId { get; }
    DateTime SessionStartUtc { get; }

    void StartSession(string sessionId);
    void StopSession();
    double GetElapsedSeconds();
    DateTime GetCurrentUtc();
    void RecordEvent(string source, string eventType, string? details = null);
    IReadOnlyList<SyncEvent> GetSyncEvents();
    double CalculateAudioVideoSkewMs(long videoFrames, double targetFps, long audioSamples, int sampleRate);
}

public interface IMeetingStorageService
{
    string DefaultStorageRoot { get; set; }
    MeetingSession CreateMeetingSession(string? customBasePath = null, string? title = null);
    Task SaveMetadataAsync(MeetingSession session);
    Task SaveTranscriptAsync(MeetingSession session);
    Task SaveChatAsync(MeetingSession session);
    Task SaveLogsAsync(MeetingSession session, IEnumerable<string> logLines);
    Task<IReadOnlyList<MeetingSession>> GetMeetingHistoryAsync(string? storageRoot = null);
    Task<MeetingSession?> LoadSessionFromDirectoryAsync(string directoryPath);
    Task<bool> DeleteMeetingAsync(string sessionId, string? storageRoot = null);
    Task<bool> ExportMeetingAsync(string sessionId, string targetZipPath);
    void OpenMeetingFolder(string rootDirectory);
}

public interface IMeetingAnalysisService
{
    Task<AiAnalysisResult> AnalyzeMeetingAsync(MeetingSession session, CancellationToken ct = default);
}

public interface IMeetingOrchestrator
{
    MeetingSession? CurrentSession { get; }
    bool IsRecording { get; }
    TimeSpan ElapsedTime { get; }

    event EventHandler<CaptureStatus>? OverallStatusChanged;
    event EventHandler<TimeSpan>? ElapsedTimeUpdated;

    Task<MeetingSession> StartRecordingAsync(
        VideoCaptureOptions videoOpts,
        AudioCaptureOptions audioOpts,
        MicrophoneOptions micOpts,
        TranscriptionOptions transOpts,
        ChatCaptureOptions chatOpts,
        string? customBasePath = null,
        string? meetingTitle = null,
        CancellationToken ct = default
    );

    Task<MeetingSession?> StopRecordingAsync();
    DiagnosticReport GetCurrentDiagnostic();
}

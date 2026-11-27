namespace MeetingCapture.Core.Models;

public record WindowInfo(
    nint Handle,
    string Title,
    string ProcessName,
    int ProcessId,
    int Width,
    int Height,
    bool IsTeams
);

public record ScreenInfo(
    int Index,
    string DeviceName,
    int Width,
    int Height,
    int Left,
    int Top,
    bool IsPrimary
);

public record AudioDeviceInfo(
    string Id,
    string Name,
    bool IsDefault,
    int Channels,
    int SampleRate
);

public record VideoCaptureOptions(
    nint WindowHandle = 0,
    int ScreenIndex = 0,
    bool CaptureEntireScreen = false,
    int TargetFps = 30,
    int TargetWidth = 1920,
    int TargetHeight = 1080,
    int BitrateKbps = 4500,
    string Codec = "H264",
    string OutputFilePath = ""
);

public record AudioCaptureOptions(
    string? DeviceId = null,
    string DeviceName = "Default Output Device",
    int SampleRate = 48000,
    int Channels = 2,
    int BufferMilliseconds = 100,
    string OutputFilePath = "",
    int TargetProcessId = 0,
    bool ProcessLoopback = true
);

public record MicrophoneOptions(
    bool Enabled = false,
    string? DeviceId = null,
    string DeviceName = "Default Microphone",
    int SampleRate = 48000,
    int Channels = 1,
    int BufferMilliseconds = 100,
    string OutputFilePath = ""
);

public record TranscriptionOptions(
    string Language = "fr",
    bool AutoDetectLanguage = true,
    string ModelName = "whisper-base",
    bool UseLocalEngine = true,
    string OutputDirectory = ""
);

public record ChatCaptureOptions(
    bool Enabled = true,
    string SourceType = "TeamsIntegration",
    string OutputDirectory = ""
);

using MeetingCapture.Core.Enums;

namespace MeetingCapture.Core.Models;

public class MeetingMetadata
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
    public DateTime StartTimeUtc { get; set; }
    public DateTime? EndTimeUtc { get; set; }
    public double DurationSeconds { get; set; }
    public List<string> AssociatedApplications { get; set; } = new();
    public List<string> CapturedSources { get; set; } = new();
    public Dictionary<string, string> Statuses { get; set; } = new();
    public string Resolution { get; set; } = "1920x1080";
    public int Fps { get; set; } = 30;
    public string VideoCodec { get; set; } = "H264";
    public string AudioCodec { get; set; } = "AAC / PCM";
    public List<string> Devices { get; set; } = new();
    public string Language { get; set; } = "fr";
    public string SoftwareVersion { get; set; } = "1.0.0";
    public List<string> Errors { get; set; } = new();
    public string? RecoveryInfo { get; set; }
}

public class MeetingSession
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public TimeSpan Duration => EndTime.HasValue ? EndTime.Value - StartTime : DateTime.UtcNow - StartTime;

    public string RootDirectory { get; set; } = string.Empty;
    public string VideoDirectory => Path.Combine(RootDirectory, "video");
    public string AudioDirectory => Path.Combine(RootDirectory, "audio");
    public string TranscriptDirectory => Path.Combine(RootDirectory, "transcript");
    public string ChatDirectory => Path.Combine(RootDirectory, "chat");
    public string MetadataDirectory => Path.Combine(RootDirectory, "metadata");
    public string LogsDirectory => Path.Combine(RootDirectory, "logs");

    public string VideoFilePath => File.Exists(Path.Combine(VideoDirectory, "recording.avi"))
        ? Path.Combine(VideoDirectory, "recording.avi")
        : Path.Combine(VideoDirectory, "recording.mp4");

    public string? ExistingVideoFilePath =>
        File.Exists(Path.Combine(VideoDirectory, "recording.avi")) ? Path.Combine(VideoDirectory, "recording.avi") :
        File.Exists(Path.Combine(VideoDirectory, "recording.mp4")) ? Path.Combine(VideoDirectory, "recording.mp4") :
        Directory.Exists(VideoDirectory) ? Directory.GetFiles(VideoDirectory, "*.*").FirstOrDefault(f => f.EndsWith(".avi", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)) :
        null;

    public string? ExistingAudioFilePath =>
        File.Exists(MixedAudioFilePath) ? MixedAudioFilePath :
        File.Exists(SystemAudioFilePath) ? SystemAudioFilePath :
        File.Exists(MicrophoneFilePath) ? MicrophoneFilePath :
        Directory.Exists(AudioDirectory) ? Directory.GetFiles(AudioDirectory, "*.*").FirstOrDefault(f => f.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) :
        null;

    public bool HasVideo => !string.IsNullOrEmpty(ExistingVideoFilePath);
    public bool HasAudio => !string.IsNullOrEmpty(ExistingAudioFilePath);
    public string SystemAudioFilePath => Path.Combine(AudioDirectory, "system_audio.wav");
    public string MicrophoneFilePath => Path.Combine(AudioDirectory, "microphone.wav");
    public string MixedAudioFilePath => Path.Combine(AudioDirectory, "mixed_audio.m4a");

    public string TranscriptTxtPath => Path.Combine(TranscriptDirectory, "transcript.txt");
    public string TranscriptJsonPath => Path.Combine(TranscriptDirectory, "transcript.json");
    public string TranscriptSrtPath => Path.Combine(TranscriptDirectory, "transcript.srt");
    public string TranscriptVttPath => Path.Combine(TranscriptDirectory, "transcript.vtt");

    public string ChatTxtPath => Path.Combine(ChatDirectory, "chat.txt");
    public string ChatJsonPath => Path.Combine(ChatDirectory, "chat.json");

    public string MeetingJsonPath => Path.Combine(MetadataDirectory, "meeting.json");
    public string CaptureSourcesJsonPath => Path.Combine(MetadataDirectory, "capture_sources.json");
    public string LogFilePath => Path.Combine(LogsDirectory, "recording.log");

    public MeetingMetadata Metadata { get; set; } = new();
    public List<TranscriptSegment> TranscriptSegments { get; set; } = new();
    public List<ChatMessage> ChatMessages { get; set; } = new();
    public AiAnalysisResult? AiAnalysis { get; set; }
}

public class DiagnosticReport
{
    public bool VideoActive { get; set; }
    public long VideoFramesCaptured { get; set; }
    public double VideoCurrentFps { get; set; }
    public bool VideoBlackFramesDetected { get; set; }
    public bool VideoFrozenFramesDetected { get; set; }

    public bool SystemAudioActive { get; set; }
    public long SystemAudioSamples { get; set; }
    public float SystemAudioLevel { get; set; }
    public bool SystemAudioSilenceDetected { get; set; }

    public bool MicrophoneActive { get; set; }
    public long MicrophoneSamples { get; set; }
    public float MicrophoneLevel { get; set; }

    public int TranscriptCount { get; set; }
    public int ChatMessageCount { get; set; }
    public double EstimatedAudioVideoSkewMs { get; set; }
    public List<string> Warnings { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}

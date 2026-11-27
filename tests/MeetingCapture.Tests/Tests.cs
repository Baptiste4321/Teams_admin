using MeetingCapture.Core.Enums;
using MeetingCapture.Core.Models;
using MeetingCapture.Synchronization;
using MeetingCapture.Storage;
using MeetingCapture.Transcription;
using MeetingCapture.Chat;
using MeetingCapture.AI;
using Xunit;
using System;
using System.Threading.Tasks;
using System.IO;
using System.Linq;

namespace MeetingCapture.Tests;

// ─── Synchronization ────────────────────────────────────────────────
public class SynchronizationServiceTests
{
    [Fact]
    public void StartSession_SetsRunningAndSessionId()
    {
        var svc = new SynchronizationService();
        svc.StartSession("test-001");

        Assert.True(svc.IsRunning);
        Assert.Equal("test-001", svc.CurrentSessionId);
    }

    [Fact]
    public void GetElapsedSeconds_IncreasesOverTime()
    {
        var svc = new SynchronizationService();
        svc.StartSession("timer-test");

        double t0 = svc.GetElapsedSeconds();
        System.Threading.Thread.Sleep(150);
        double t1 = svc.GetElapsedSeconds();

        Assert.True(t1 > t0);
    }

    [Fact]
    public void StopSession_StopsTimer()
    {
        var svc = new SynchronizationService();
        svc.StartSession("stop-test");
        svc.StopSession();

        Assert.False(svc.IsRunning);
    }

    [Fact]
    public void RecordEvent_AddsToList()
    {
        var svc = new SynchronizationService();
        svc.StartSession("events-test");
        svc.RecordEvent("VideoCapture", "FrameCaptured", "frame#1");
        svc.RecordEvent("AudioCapture", "SampleReceived");

        var events = svc.GetSyncEvents();
        // StartSession itself records one event, so at least 3
        Assert.True(events.Count >= 3);
        Assert.Contains(events, e => e.Source == "VideoCapture");
        Assert.Contains(events, e => e.Source == "AudioCapture");
    }

    [Fact]
    public void CalculateAudioVideoSkewMs_Zero_WhenAligned()
    {
        var svc = new SynchronizationService();
        // 30 fps * 1 second = 30 frames | 48000 samples * 1 second = 48000 samples
        double skew = svc.CalculateAudioVideoSkewMs(30, 30.0, 48000, 48000);
        Assert.Equal(0.0, skew, 2);
    }

    [Fact]
    public void CalculateAudioVideoSkewMs_DetectsDrift()
    {
        var svc = new SynchronizationService();
        // Video: 30 frames at 30 fps = 1000ms | Audio: 44000 samples at 48000 Hz = 916ms → ~84ms drift
        double skew = svc.CalculateAudioVideoSkewMs(30, 30.0, 44000, 48000);
        Assert.True(skew > 50); // Should detect non-trivial drift
    }

    [Fact]
    public void InvalidInputs_ReturnZeroSkew()
    {
        var svc = new SynchronizationService();
        double skew = svc.CalculateAudioVideoSkewMs(100, 0, 48000, 0);
        Assert.Equal(0.0, skew);
    }
}

// ─── Transcription ───────────────────────────────────────────────────
public class TranscriptionServiceTests
{
    [Fact]
    public async Task Start_SetsCapturingStatus()
    {
        var svc = new TranscriptionService();
        var opts = new TranscriptionOptions(Language: "fr", UseLocalEngine: true);
        bool result = await svc.StartAsync(opts);

        Assert.True(result);
        Assert.Equal(CaptureStatus.Capturing, svc.Status);
    }

    [Fact]
    public async Task IngestAudio_UpdatesOffsetWithoutGeneratingSegments()
    {
        var svc = new TranscriptionService();
        var opts = new TranscriptionOptions(Language: "fr");
        await svc.StartAsync(opts);

        // IngestAudioData doit juste mémoriser l'offset, pas générer de segments
        // (la transcription se fait en post-traitement via Whisper)
        svc.IngestAudioData(ReadOnlySpan<byte>.Empty, 4.1);
        svc.IngestAudioData(ReadOnlySpan<byte>.Empty, 8.3);
        svc.IngestAudioData(ReadOnlySpan<byte>.Empty, 12.5);

        var transcript = svc.GetFullTranscript();
        Assert.Empty(transcript); // Aucun segment en direct — Whisper tourne après l'enregistrement
    }

    [Fact]
    public async Task Stop_ChangesStatusToStopped()
    {
        var svc = new TranscriptionService();
        await svc.StartAsync(new TranscriptionOptions());
        await svc.StopAsync();

        Assert.Equal(CaptureStatus.Stopped, svc.Status);
    }

    [Fact]
    public async Task SegmentReceived_EventNotFiredDuringIngest()
    {
        // La transcription en direct a été supprimée : IngestAudioData n'émet plus l'event SegmentReceived.
        // Les segments sont produits uniquement par Whisper en post-traitement.
        var svc = new TranscriptionService();
        await svc.StartAsync(new TranscriptionOptions());

        TranscriptSegment? received = null;
        svc.SegmentReceived += (_, seg) => received = seg;

        svc.IngestAudioData(ReadOnlySpan<byte>.Empty, 5.0);
        await Task.Delay(50);

        Assert.Null(received); // Aucun segment en temps réel attendu
    }

    [Fact]
    public void TranscriptSegments_HaveValidTimestamps()
    {
        var svc = new TranscriptionService();
        _ = svc.StartAsync(new TranscriptionOptions());
        svc.IngestAudioData(ReadOnlySpan<byte>.Empty, 4.5);
        svc.IngestAudioData(ReadOnlySpan<byte>.Empty, 9.0);

        var segments = svc.GetFullTranscript();
        foreach (var seg in segments)
        {
            Assert.True(seg.Start >= 0);
            Assert.True(seg.End >= seg.Start || seg.End == 0);
            Assert.False(string.IsNullOrWhiteSpace(seg.Speaker));
        }
    }
}

// ─── Chat ────────────────────────────────────────────────────────────
public class ChatCaptureServiceTests
{
    [Fact]
    public async Task Start_SetsCapturingStatus()
    {
        var svc = new ChatCaptureService();
        bool result = await svc.StartAsync(new ChatCaptureOptions());

        Assert.True(result);
        Assert.Equal(CaptureStatus.Capturing, svc.Status);
    }

    [Fact]
    public async Task AddMessage_AppearsInGetMessages()
    {
        var svc = new ChatCaptureService();
        await svc.StartAsync(new ChatCaptureOptions());

        var msg = new ChatMessage
        {
            Author = "Alice",
            Text = "Bonjour à tous !",
            TimestampUtc = DateTime.UtcNow
        };
        svc.AddMessage(msg);

        var messages = svc.GetMessages();
        Assert.Single(messages);
        Assert.Equal("Alice", messages[0].Author);
        Assert.Equal("Bonjour à tous !", messages[0].Text);
    }

    [Fact]
    public async Task AddMessage_FiresEvent()
    {
        var svc = new ChatCaptureService();
        await svc.StartAsync(new ChatCaptureOptions());

        ChatMessage? received = null;
        svc.MessageReceived += (_, m) => received = m;

        svc.AddMessage(new ChatMessage { Author = "Bob", Text = "Hello" });
        await Task.Delay(20);

        Assert.NotNull(received);
        Assert.Equal("Bob", received!.Author);
    }

    [Fact]
    public async Task ImportChatMessages_ParsesJson()
    {
        var svc = new ChatCaptureService();
        await svc.StartAsync(new ChatCaptureOptions());

        string json = """[{"Author":"Eve","Text":"JSON test","TimestampUtc":"2026-11-23T10:00:00Z"}]""";
        await svc.ImportChatMessagesAsync(json);

        var messages = svc.GetMessages();
        Assert.True(messages.Count >= 1);
        Assert.Equal("Eve", messages[0].Author);
    }

    [Fact]
    public async Task ImportChatMessages_ParsesPlainText()
    {
        var svc = new ChatCaptureService();
        await svc.StartAsync(new ChatCaptureOptions());

        string plainText = "Première ligne\nDeuxième ligne";
        await svc.ImportChatMessagesAsync(plainText);

        var messages = svc.GetMessages();
        Assert.Equal(2, messages.Count);
    }

    [Fact]
    public async Task Stop_ChangesStatusToStopped()
    {
        var svc = new ChatCaptureService();
        await svc.StartAsync(new ChatCaptureOptions());
        await svc.StopAsync();

        Assert.Equal(CaptureStatus.Stopped, svc.Status);
    }

    [Fact]
    public async Task MultipleSources_AllMessagesRetained()
    {
        var svc = new ChatCaptureService();
        await svc.StartAsync(new ChatCaptureOptions());

        for (int i = 0; i < 20; i++)
        {
            svc.AddMessage(new ChatMessage { Author = $"User{i}", Text = $"Message {i}" });
        }

        Assert.Equal(20, svc.MessagesCount);
    }
}

// ─── Storage ─────────────────────────────────────────────────────────
public class MeetingStorageServiceTests : IDisposable
{
    private readonly string _testRoot;

    public MeetingStorageServiceTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"MCTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testRoot);
    }

    [Fact]
    public void CreateMeetingSession_CreatesFolderStructure()
    {
        var svc = new MeetingStorageService(_testRoot);
        var session = svc.CreateMeetingSession(title: "Test Réunion");

        Assert.True(Directory.Exists(session.VideoDirectory));
        Assert.True(Directory.Exists(session.AudioDirectory));
        Assert.True(Directory.Exists(session.TranscriptDirectory));
        Assert.True(Directory.Exists(session.ChatDirectory));
        Assert.True(Directory.Exists(session.MetadataDirectory));
        Assert.True(Directory.Exists(session.LogsDirectory));
    }

    [Fact]
    public void CreateMeetingSession_DuplicateTitles_GeneratesVersionedDirectories()
    {
        var svc = new MeetingStorageService(_testRoot);
        var session1 = svc.CreateMeetingSession(title: "MêmeTitre");
        var session2 = svc.CreateMeetingSession(title: "MêmeTitre");
        var session3 = svc.CreateMeetingSession(title: "MêmeTitre");

        Assert.NotEqual(session1.RootDirectory, session2.RootDirectory);
        Assert.NotEqual(session2.RootDirectory, session3.RootDirectory);
        Assert.EndsWith("MêmeTitre", session1.RootDirectory);
        Assert.EndsWith("MêmeTitre_v1", session2.RootDirectory);
        Assert.EndsWith("MêmeTitre_v2", session3.RootDirectory);
    }

    [Fact]
    public async Task SaveMetadata_CreatesJsonFile()
    {
        var svc = new MeetingStorageService(_testRoot);
        var session = svc.CreateMeetingSession(title: "Meta Test");
        session.EndTime = DateTime.UtcNow;

        await svc.SaveMetadataAsync(session);

        Assert.True(File.Exists(session.MeetingJsonPath));
        string content = await File.ReadAllTextAsync(session.MeetingJsonPath);
        Assert.Contains("Meta Test", content);
    }

    [Fact]
    public async Task SaveTranscript_CreatesAllFormats()
    {
        var svc = new MeetingStorageService(_testRoot);
        var session = svc.CreateMeetingSession(title: "Transcript Test");
        session.TranscriptSegments.Add(new TranscriptSegment
        {
            Start = 0.0,
            End = 3.5,
            Speaker = "Alice",
            Text = "Bonjour tout le monde."
        });

        await svc.SaveTranscriptAsync(session);

        Assert.True(File.Exists(session.TranscriptTxtPath));
        Assert.True(File.Exists(session.TranscriptJsonPath));
        Assert.True(File.Exists(session.TranscriptSrtPath));
        Assert.True(File.Exists(session.TranscriptVttPath));

        string srt = await File.ReadAllTextAsync(session.TranscriptSrtPath);
        Assert.Contains("Alice", srt);
        Assert.Contains("Bonjour tout le monde.", srt);

        string vtt = await File.ReadAllTextAsync(session.TranscriptVttPath);
        Assert.Contains("WEBVTT", vtt);
    }

    [Fact]
    public async Task SaveChat_CreatesTxtAndJson()
    {
        var svc = new MeetingStorageService(_testRoot);
        var session = svc.CreateMeetingSession(title: "Chat Test");
        session.ChatMessages.Add(new ChatMessage
        {
            Author = "Bob",
            Text = "Voici un lien important.",
            TimestampUtc = DateTime.UtcNow
        });

        await svc.SaveChatAsync(session);

        Assert.True(File.Exists(session.ChatTxtPath));
        Assert.True(File.Exists(session.ChatJsonPath));

        string txt = await File.ReadAllTextAsync(session.ChatTxtPath);
        Assert.Contains("Bob", txt);
    }

    [Fact]
    public async Task GetMeetingHistory_ReturnsCreatedSessions()
    {
        var svc = new MeetingStorageService(_testRoot);
        var session = svc.CreateMeetingSession(title: "History Test");
        session.EndTime = DateTime.UtcNow;
        await svc.SaveMetadataAsync(session);

        var history = await svc.GetMeetingHistoryAsync(_testRoot);
        Assert.True(history.Count >= 1);
        Assert.Contains(history, s => s.Id == session.Id);
    }

    [Fact]
    public async Task DeleteMeeting_RemovesDirectory()
    {
        var svc = new MeetingStorageService(_testRoot);
        var session = svc.CreateMeetingSession(title: "Delete Test");
        session.EndTime = DateTime.UtcNow;
        await svc.SaveMetadataAsync(session);

        bool deleted = await svc.DeleteMeetingAsync(session.Id, _testRoot);
        Assert.True(deleted);
        Assert.False(Directory.Exists(session.RootDirectory));
    }

    [Fact]
    public async Task SaveLogs_WritesLogFile()
    {
        var svc = new MeetingStorageService(_testRoot);
        var session = svc.CreateMeetingSession(title: "Log Test");

        await svc.SaveLogsAsync(session, new[]
        {
            "[2026-11-23 10:00:00] INFO: Session started",
            "[2026-11-23 10:05:00] INFO: Video capturing at 30fps"
        });

        Assert.True(File.Exists(session.LogFilePath));
        string content = await File.ReadAllTextAsync(session.LogFilePath);
        Assert.Contains("Session started", content);
    }

    public void Dispose()
    {
        try { Directory.Delete(_testRoot, recursive: true); } catch { }
    }
}

// ─── AI Analysis ─────────────────────────────────────────────────────
public class MeetingAnalysisServiceTests
{
    [Fact]
    public async Task AnalyzeMeeting_ReturnsNonEmptySummary()
    {
        var svc = new MeetingAnalysisService();
        var session = new MeetingSession
        {
            Id = "ai-test-001",
            Title = "Revue Architecture",
            StartTime = DateTime.UtcNow.AddMinutes(-45),
            EndTime = DateTime.UtcNow,
            RootDirectory = Path.GetTempPath()
        };
        session.TranscriptSegments.Add(new TranscriptSegment
        {
            Start = 0,
            End = 5,
            Speaker = "Lead Dev",
            Text = "Nous allons déployer cette solution la semaine prochaine."
        });

        var result = await svc.AnalyzeMeetingAsync(session);

        Assert.NotNull(result);
        Assert.False(string.IsNullOrWhiteSpace(result.ExecutiveSummary));
        Assert.NotEmpty(result.KeyDecisions);
        Assert.NotEmpty(result.ActionItems);
    }

    [Fact]
    public async Task AnalyzeMeeting_EmptySession_ReturnsGracefulResult()
    {
        var svc = new MeetingAnalysisService();
        var session = new MeetingSession
        {
            Id = "empty-001",
            Title = "Session vide",
            StartTime = DateTime.UtcNow.AddMinutes(-5),
            RootDirectory = Path.GetTempPath()
        };

        var result = await svc.AnalyzeMeetingAsync(session);

        Assert.NotNull(result);
        Assert.False(string.IsNullOrWhiteSpace(result.ExecutiveSummary));
    }
}

// ─── MeetingSession Model ────────────────────────────────────────────
public class MeetingSessionModelTests
{
    [Fact]
    public void Duration_CalculatesFromStartAndEnd()
    {
        var session = new MeetingSession
        {
            StartTime = DateTime.UtcNow.AddMinutes(-30),
            EndTime = DateTime.UtcNow
        };
        Assert.True(session.Duration.TotalMinutes >= 29);
    }

    [Fact]
    public void Paths_DerivedFromRootDirectory()
    {
        var session = new MeetingSession { RootDirectory = @"C:\MeetingCapture\Test" };
        Assert.EndsWith("video", session.VideoDirectory);
        Assert.EndsWith("audio", session.AudioDirectory);
        Assert.EndsWith("transcript", session.TranscriptDirectory);
        Assert.EndsWith("chat", session.ChatDirectory);
        Assert.EndsWith("metadata", session.MetadataDirectory);
        Assert.EndsWith("logs", session.LogsDirectory);
    }

    [Fact]
    public void FilePaths_CorrectNames()
    {
        var session = new MeetingSession { RootDirectory = @"C:\MeetingCapture\Test" };
        Assert.EndsWith("recording.mp4", session.VideoFilePath);
        Assert.EndsWith("system_audio.wav", session.SystemAudioFilePath);
        Assert.EndsWith("microphone.wav", session.MicrophoneFilePath);
        Assert.EndsWith("mixed_audio.m4a", session.MixedAudioFilePath);
        Assert.EndsWith("meeting.json", session.MeetingJsonPath);
        Assert.EndsWith("recording.log", session.LogFilePath);
    }

    [Fact]
    public async Task LoadSessionFromDirectoryAsync_LoadsExternalFolderCorrectly()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"ext_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var metaDir = Path.Combine(tempDir, "metadata");
        Directory.CreateDirectory(metaDir);
        var transDir = Path.Combine(tempDir, "transcript");
        Directory.CreateDirectory(transDir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(metaDir, "meeting.json"), "{\"Id\":\"ext-123\",\"Name\":\"External Meeting\"}");
            await File.WriteAllTextAsync(Path.Combine(transDir, "transcript.json"), "[{\"Start\":0.0,\"End\":2.5,\"Speaker\":\"Alice\",\"Text\":\"Bonjour\"}]");

            var svc = new MeetingStorageService(tempDir);
            var session = await svc.LoadSessionFromDirectoryAsync(tempDir);

            Assert.NotNull(session);
            Assert.Equal("ext-123", session.Id);
            Assert.Equal("External Meeting", session.Title);
            Assert.Single(session.TranscriptSegments);
            Assert.Equal("Alice", session.TranscriptSegments[0].Speaker);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void AudioOptions_ProcessLoopbackAndMicDefaults()
    {
        var audioOpts = new AudioCaptureOptions(TargetProcessId: 4321, ProcessLoopback: true);
        Assert.Equal(4321, audioOpts.TargetProcessId);
        Assert.True(audioOpts.ProcessLoopback);

        var micOpts = new MicrophoneOptions();
        Assert.False(micOpts.Enabled); // Local microphone disabled by default as requested
    }

    [Fact]
    public void AudioSessionManager_EnumerateActiveAudioProcesses_Success()
    {
        using var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
        var devices = enumerator.EnumerateAudioEndPoints(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.DeviceState.Active);
        Assert.NotEmpty(devices);
    }

    [Fact]
    public void Test_WgcCaptureEngine_Create()
    {
        var engine = new MeetingCapture.Video.WgcCaptureEngine();
        Assert.NotNull(engine);
    }
}

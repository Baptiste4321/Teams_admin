using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using MeetingCapture.Core.Interfaces;
using MeetingCapture.Core.Models;

namespace MeetingCapture.Storage;

public class MeetingStorageService : IMeetingStorageService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public string DefaultStorageRoot { get; set; }

    public MeetingStorageService(string? customRoot = null)
    {
        DefaultStorageRoot = customRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "MeetingCapture"
        );
    }

    public MeetingSession CreateMeetingSession(string? customBasePath = null, string? title = null)
    {
        var now = DateTime.Now;
        var sessionId = $"meeting_{now:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..6]}";
        var safeTitle = string.IsNullOrWhiteSpace(title) ? $"Meeting_{now:HH-mm}" : SanitizeFileName(title);

        var root = customBasePath ?? DefaultStorageRoot;
        var dayDir = Path.Combine(root, now.Year.ToString(), now.Month.ToString("D2"), now.Day.ToString("D2"));
        var sessionDir = Path.Combine(dayDir, safeTitle);
        var finalTitle = string.IsNullOrWhiteSpace(title) ? $"Réunion du {now:dd/MM/yyyy HH:mm}" : title;

        // Évite l'écrasement si 2 sessions ont le même nom : génère des versions _v1, _v2, etc.
        if (Directory.Exists(sessionDir))
        {
            int version = 1;
            string versionedDir;
            do
            {
                versionedDir = Path.Combine(dayDir, $"{safeTitle}_v{version}");
                version++;
            } while (Directory.Exists(versionedDir));

            sessionDir = versionedDir;
            finalTitle = $"{finalTitle} (v{version - 1})";
        }

        var session = new MeetingSession
        {
            Id = sessionId,
            Title = finalTitle,
            StartTime = DateTime.UtcNow,
            RootDirectory = sessionDir,
            Metadata = new MeetingMetadata
            {
                Id = sessionId,
                Name = Path.GetFileName(sessionDir),
                Date = now.ToString("yyyy-MM-dd"),
                StartTimeUtc = DateTime.UtcNow,
                SoftwareVersion = "1.0.0"
            }
        };

        // Create directory structure
        Directory.CreateDirectory(session.VideoDirectory);
        Directory.CreateDirectory(session.AudioDirectory);
        Directory.CreateDirectory(session.TranscriptDirectory);
        Directory.CreateDirectory(session.ChatDirectory);
        Directory.CreateDirectory(session.MetadataDirectory);
        Directory.CreateDirectory(session.LogsDirectory);

        return session;
    }

    public async Task SaveMetadataAsync(MeetingSession session)
    {
        session.Metadata.EndTimeUtc = session.EndTime ?? DateTime.UtcNow;
        session.Metadata.DurationSeconds = session.Duration.TotalSeconds;

        var meetingJson = JsonSerializer.Serialize(session.Metadata, JsonOpts);
        await File.WriteAllTextAsync(session.MeetingJsonPath, meetingJson, Encoding.UTF8);

        var sourcesInfo = new
        {
            SessionId = session.Id,
            SavedAtUtc = DateTime.UtcNow,
            Sources = session.Metadata.CapturedSources,
            Statuses = session.Metadata.Statuses,
            VideoFilePath = session.VideoFilePath,
            AudioFiles = new[]
            {
                session.SystemAudioFilePath,
                session.MicrophoneFilePath,
                session.MixedAudioFilePath
            },
            TranscriptFiles = new[]
            {
                session.TranscriptTxtPath,
                session.TranscriptJsonPath,
                session.TranscriptSrtPath,
                session.TranscriptVttPath
            },
            ChatFiles = new[]
            {
                session.ChatTxtPath,
                session.ChatJsonPath
            }
        };

        var sourcesJson = JsonSerializer.Serialize(sourcesInfo, JsonOpts);
        await File.WriteAllTextAsync(session.CaptureSourcesJsonPath, sourcesJson, Encoding.UTF8);
    }

    public async Task SaveTranscriptAsync(MeetingSession session)
    {
        var segments = session.TranscriptSegments;

        // 1. JSON
        var json = JsonSerializer.Serialize(segments, JsonOpts);
        await File.WriteAllTextAsync(session.TranscriptJsonPath, json, Encoding.UTF8);

        // 2. TXT
        var txtSb = new StringBuilder();
        foreach (var seg in segments)
        {
            var ts = TimeSpan.FromSeconds(seg.Start);
            txtSb.AppendLine($"[{ts:hh\\:mm\\:ss}] {seg.Speaker}: {seg.Text}");
        }
        await File.WriteAllTextAsync(session.TranscriptTxtPath, txtSb.ToString(), Encoding.UTF8);

        // 3. SRT
        var srtSb = new StringBuilder();
        for (int i = 0; i < segments.Count; i++)
        {
            var s = segments[i];
            var startTs = TimeSpan.FromSeconds(s.Start);
            var endTs = TimeSpan.FromSeconds(s.End > s.Start ? s.End : s.Start + 2.0);
            srtSb.AppendLine((i + 1).ToString());
            srtSb.AppendLine($"{startTs:hh\\:mm\\:ss\\,fff} --> {endTs:hh\\:mm\\:ss\\,fff}");
            srtSb.AppendLine($"{s.Speaker}: {s.Text}");
            srtSb.AppendLine();
        }
        await File.WriteAllTextAsync(session.TranscriptSrtPath, srtSb.ToString(), Encoding.UTF8);

        // 4. VTT
        var vttSb = new StringBuilder();
        vttSb.AppendLine("WEBVTT");
        vttSb.AppendLine();
        for (int i = 0; i < segments.Count; i++)
        {
            var s = segments[i];
            var startTs = TimeSpan.FromSeconds(s.Start);
            var endTs = TimeSpan.FromSeconds(s.End > s.Start ? s.End : s.Start + 2.0);
            vttSb.AppendLine($"{startTs:hh\\:mm\\:ss\\.fff} --> {endTs:hh\\:mm\\:ss\\.fff}");
            vttSb.AppendLine($"<v {s.Speaker}>{s.Text}</v>");
            vttSb.AppendLine();
        }
        await File.WriteAllTextAsync(session.TranscriptVttPath, vttSb.ToString(), Encoding.UTF8);
    }

    public async Task SaveChatAsync(MeetingSession session)
    {
        var messages = session.ChatMessages;

        // 1. JSON
        var json = JsonSerializer.Serialize(messages, JsonOpts);
        await File.WriteAllTextAsync(session.ChatJsonPath, json, Encoding.UTF8);

        // 2. TXT
        var txtSb = new StringBuilder();
        foreach (var msg in messages)
        {
            txtSb.AppendLine($"[{msg.TimestampUtc.ToLocalTime():HH:mm:ss}] {msg.Author}: {msg.Text}");
            if (msg.Links.Count > 0)
            {
                txtSb.AppendLine($"   Liens: {string.Join(", ", msg.Links)}");
            }
        }
        await File.WriteAllTextAsync(session.ChatTxtPath, txtSb.ToString(), Encoding.UTF8);
    }

    public async Task SaveLogsAsync(MeetingSession session, IEnumerable<string> logLines)
    {
        var content = string.Join(Environment.NewLine, logLines);
        await File.WriteAllTextAsync(session.LogFilePath, content, Encoding.UTF8);
    }

    public async Task<IReadOnlyList<MeetingSession>> GetMeetingHistoryAsync(string? storageRoot = null)
    {
        var root = storageRoot ?? DefaultStorageRoot;
        var list = new List<MeetingSession>();
        if (!Directory.Exists(root)) return list;

        var metadataFiles = Directory.GetFiles(root, "meeting.json", SearchOption.AllDirectories);
        foreach (var metaFile in metadataFiles)
        {
            try
            {
                var json = await File.ReadAllTextAsync(metaFile, Encoding.UTF8);
                var meta = JsonSerializer.Deserialize<MeetingMetadata>(json, JsonOpts);
                if (meta == null) continue;

                var metaDir = Path.GetDirectoryName(metaFile);
                var sessionDir = Path.GetDirectoryName(metaDir) ?? metaDir!;

                var session = new MeetingSession
                {
                    Id = meta.Id,
                    Title = meta.Name,
                    StartTime = meta.StartTimeUtc,
                    EndTime = meta.EndTimeUtc,
                    RootDirectory = sessionDir,
                    Metadata = meta
                };

                // Check for transcript
                if (File.Exists(session.TranscriptJsonPath))
                {
                    var transJson = await File.ReadAllTextAsync(session.TranscriptJsonPath, Encoding.UTF8);
                    session.TranscriptSegments = JsonSerializer.Deserialize<List<TranscriptSegment>>(transJson, JsonOpts) ?? new();
                }

                // Check for chat
                if (File.Exists(session.ChatJsonPath))
                {
                    var chatJson = await File.ReadAllTextAsync(session.ChatJsonPath, Encoding.UTF8);
                    session.ChatMessages = JsonSerializer.Deserialize<List<ChatMessage>>(chatJson, JsonOpts) ?? new();
                }

                list.Add(session);
            }
            catch
            {
                // Ignore corrupted individual files
            }
        }

        return list.OrderByDescending(s => s.StartTime).ToList();
    }

    public async Task<MeetingSession?> LoadSessionFromDirectoryAsync(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
            return null;

        try
        {
            // Chercher le meeting.json (dans metadata/ ou directement à la racine)
            string? metaFile = Path.Combine(directoryPath, "metadata", "meeting.json");
            if (!File.Exists(metaFile))
            {
                metaFile = Path.Combine(directoryPath, "meeting.json");
            }
            if (!File.Exists(metaFile))
            {
                var found = Directory.GetFiles(directoryPath, "meeting.json", SearchOption.AllDirectories);
                metaFile = found.Length > 0 ? found[0] : null;
            }

            MeetingMetadata meta;
            if (metaFile != null && File.Exists(metaFile))
            {
                var json = await File.ReadAllTextAsync(metaFile, Encoding.UTF8);
                meta = JsonSerializer.Deserialize<MeetingMetadata>(json, JsonOpts) ?? new MeetingMetadata();
            }
            else
            {
                var dirInfo = new DirectoryInfo(directoryPath);
                meta = new MeetingMetadata
                {
                    Id = $"imported_{Guid.NewGuid():N}",
                    Name = dirInfo.Name,
                    Date = dirInfo.CreationTime.ToString("yyyy-MM-dd"),
                    StartTimeUtc = dirInfo.CreationTimeUtc,
                    SoftwareVersion = "1.0.0"
                };
            }

            var session = new MeetingSession
            {
                Id = meta.Id,
                Title = meta.Name,
                StartTime = meta.StartTimeUtc,
                EndTime = meta.EndTimeUtc,
                RootDirectory = directoryPath,
                Metadata = meta
            };

            // Charger transcription si existante
            string? transJson = File.Exists(session.TranscriptJsonPath) ? session.TranscriptJsonPath :
                File.Exists(Path.Combine(directoryPath, "transcript.json")) ? Path.Combine(directoryPath, "transcript.json") : null;
            if (transJson != null)
            {
                var tj = await File.ReadAllTextAsync(transJson, Encoding.UTF8);
                session.TranscriptSegments = JsonSerializer.Deserialize<List<TranscriptSegment>>(tj, JsonOpts) ?? new();
            }

            // Charger chat si existant
            string? chatJson = File.Exists(session.ChatJsonPath) ? session.ChatJsonPath :
                File.Exists(Path.Combine(directoryPath, "chat.json")) ? Path.Combine(directoryPath, "chat.json") : null;
            if (chatJson != null)
            {
                var cj = await File.ReadAllTextAsync(chatJson, Encoding.UTF8);
                session.ChatMessages = JsonSerializer.Deserialize<List<ChatMessage>>(cj, JsonOpts) ?? new();
            }

            return session;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Erreur chargement session externe : {ex.Message}");
            return null;
        }
    }

    public Task<bool> DeleteMeetingAsync(string sessionId, string? storageRoot = null)
    {
        var root = storageRoot ?? DefaultStorageRoot;
        if (!Directory.Exists(root)) return Task.FromResult(false);

        var metaFiles = Directory.GetFiles(root, "meeting.json", SearchOption.AllDirectories);
        foreach (var metaFile in metaFiles)
        {
            try
            {
                var content = File.ReadAllText(metaFile);
                if (content.Contains(sessionId, StringComparison.OrdinalIgnoreCase))
                {
                    var metaDir = Path.GetDirectoryName(metaFile);
                    var sessionDir = Path.GetDirectoryName(metaDir);
                    if (sessionDir != null && Directory.Exists(sessionDir))
                    {
                        Directory.Delete(sessionDir, true);
                        return Task.FromResult(true);
                    }
                }
            }
            catch
            {
                return Task.FromResult(false);
            }
        }

        return Task.FromResult(false);
    }

    public Task<bool> ExportMeetingAsync(string sessionIdOrPath, string targetZipPath)
    {
        try
        {
            // S'il s'agit directement du chemin de dossier de la session
            if (Directory.Exists(sessionIdOrPath))
            {
                var zipDir = Path.GetDirectoryName(targetZipPath);
                if (!string.IsNullOrEmpty(zipDir) && !Directory.Exists(zipDir))
                {
                    Directory.CreateDirectory(zipDir);
                }

                if (File.Exists(targetZipPath)) File.Delete(targetZipPath);
                ZipFile.CreateFromDirectory(sessionIdOrPath, targetZipPath, CompressionLevel.Optimal, false);
                return Task.FromResult(true);
            }

            // Sinon recherche par SessionId dans DefaultStorageRoot
            var metaFiles = Directory.GetFiles(DefaultStorageRoot, "meeting.json", SearchOption.AllDirectories);
            foreach (var metaFile in metaFiles)
            {
                var content = File.ReadAllText(metaFile);
                if (content.Contains(sessionIdOrPath, StringComparison.OrdinalIgnoreCase))
                {
                    var metaDir = Path.GetDirectoryName(metaFile);
                    var sessionDir = Path.GetDirectoryName(metaDir);
                    if (sessionDir != null && Directory.Exists(sessionDir))
                    {
                        var zipDir = Path.GetDirectoryName(targetZipPath);
                        if (!string.IsNullOrEmpty(zipDir) && !Directory.Exists(zipDir))
                        {
                            Directory.CreateDirectory(zipDir);
                        }

                        if (File.Exists(targetZipPath)) File.Delete(targetZipPath);
                        ZipFile.CreateFromDirectory(sessionDir, targetZipPath, CompressionLevel.Optimal, false);
                        return Task.FromResult(true);
                    }
                }
            }
        }
        catch
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(false);
    }

    public void OpenMeetingFolder(string rootDirectory)
    {
        if (Directory.Exists(rootDirectory))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = rootDirectory,
                UseShellExecute = true,
                Verb = "open"
            });
        }
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c));
    }
}

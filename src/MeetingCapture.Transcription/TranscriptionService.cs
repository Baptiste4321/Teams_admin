using System.Diagnostics;
using System.Text.Json;
using MeetingCapture.Core.Enums;
using MeetingCapture.Core.Interfaces;
using MeetingCapture.Core.Models;

namespace MeetingCapture.Transcription;

public class TranscriptionService : ITranscriptionService
{
    private readonly List<TranscriptSegment> _segments = new();
    private readonly object _lock = new();
    private CaptureStatus _status = CaptureStatus.Idle;
    private CancellationTokenSource? _cts;
    private double _lastProcessedOffset;
    private int _speakerCycle = 1;

    public CaptureStatus Status
    {
        get => _status;
        private set
        {
            if (_status != value)
            {
                _status = value;
                StatusChanged?.Invoke(this, _status);
            }
        }
    }

    public string EngineName { get; private set; } = "Whisper-Local Engine (Français / Multilingue)";
    public int SegmentsCount
    {
        get
        {
            lock (_lock) return _segments.Count;
        }
    }

    public event EventHandler<TranscriptSegment>? SegmentReceived;
    public event EventHandler<CaptureStatus>? StatusChanged;

    public Task<bool> StartAsync(TranscriptionOptions options, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _segments.Clear();
            _lastProcessedOffset = 0;
            _cts = new CancellationTokenSource();
            EngineName = options.UseLocalEngine
                ? $"Whisper-Engine [{options.Language.ToUpper()}] ({options.ModelName})"
                : $"Cloud Transcription Service ({options.Language})";
            Status = CaptureStatus.Capturing;
        }
        return Task.FromResult(true);
    }

    public void IngestAudioData(ReadOnlySpan<byte> pcmData, double relativeSeconds)
    {
        // Enregistrement des métriques temporelles d'audio reçues.
        // Aucun faux segment simulé n'est injecté : la transcription est strictement issue du moteur Whisper réel.
        _lastProcessedOffset = relativeSeconds;
    }

    public async Task<IReadOnlyList<TranscriptSegment>> TranscribeAudioFileAsync(string audioFilePath, string language = "fr", CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(audioFilePath) || !File.Exists(audioFilePath))
        {
            return Array.Empty<TranscriptSegment>();
        }

        Status = CaptureStatus.Initializing;

        try
        {
            // 1. Tenter la transcription avec le script Whisper local Python
            var scriptPath = FindTranscribeScript();
            if (!string.IsNullOrEmpty(scriptPath) && File.Exists(scriptPath))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "python",
                    Arguments = $"\"{scriptPath}\" \"{audioFilePath}\" tiny {language}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var proc = new Process { StartInfo = psi };
                proc.Start();

                var outputTask = proc.StandardOutput.ReadToEndAsync(ct);
                var errorTask = proc.StandardError.ReadToEndAsync(ct);

                await proc.WaitForExitAsync(ct);
                string stdout = await outputTask;
                string stderr = await errorTask;

                if (proc.ExitCode == 0 && !string.IsNullOrWhiteSpace(stdout))
                {
                    try
                    {
                        var segments = JsonSerializer.Deserialize<List<TranscriptSegment>>(stdout, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });

                        if (segments != null && segments.Count > 0)
                        {
                            lock (_lock)
                            {
                                _segments.Clear();
                                foreach (var s in segments)
                                {
                                    _segments.Add(s);
                                    SegmentReceived?.Invoke(this, s);
                                }
                            }
                            Status = CaptureStatus.Stopped;
                            return segments;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[TranscriptionService] Erreur désérialisation JSON Whisper : {ex.Message}");
                    }
                }
                else
                {
                    Debug.WriteLine($"[TranscriptionService] Whisper script stderr: {stderr}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TranscriptionService] Échec exécution Whisper local : {ex.Message}");
        }

        // 2. Si Whisper n'a pas retourné de segments ou si le fichier était court, renvoyer les segments actuels
        lock (_lock)
        {
            Status = CaptureStatus.Stopped;
            return _segments.ToList();
        }
    }

    private static string? FindTranscribeScript()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "scripts", "transcribe_wav.py"),
            Path.Combine(Directory.GetCurrentDirectory(), "scripts", "transcribe_wav.py"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "scripts", "transcribe_wav.py")
        };

        foreach (var c in candidates)
        {
            try
            {
                var full = Path.GetFullPath(c);
                if (File.Exists(full)) return full;
            }
            catch { }
        }
        return null;
    }

    public void AddManualSegment(TranscriptSegment segment)
    {
        lock (_lock)
        {
            _segments.Add(segment);
        }
        SegmentReceived?.Invoke(this, segment);
    }

    public IReadOnlyList<TranscriptSegment> GetFullTranscript()
    {
        lock (_lock)
        {
            return _segments.ToList();
        }
    }

    public Task StopAsync()
    {
        lock (_lock)
        {
            Status = CaptureStatus.Stopping;
            _cts?.Cancel();
            Status = CaptureStatus.Stopped;
        }
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
        _cts?.Dispose();
    }
}

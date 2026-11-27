using System.Diagnostics;
using MeetingCapture.Core.Interfaces;
using MeetingCapture.Core.Models;

namespace MeetingCapture.Synchronization;

public class SynchronizationService : ISynchronizationService
{
    private readonly Stopwatch _stopwatch = new();
    private readonly List<SyncEvent> _events = new();
    private readonly object _lock = new();

    public bool IsRunning => _stopwatch.IsRunning;
    public string CurrentSessionId { get; private set; } = string.Empty;
    public DateTime SessionStartUtc { get; private set; }

    public void StartSession(string sessionId)
    {
        lock (_lock)
        {
            CurrentSessionId = sessionId;
            SessionStartUtc = DateTime.UtcNow;
            _events.Clear();
            _stopwatch.Restart();

            RecordEvent("SynchronizationService", "SessionStarted", $"Session {sessionId} started at {SessionStartUtc:O}");
        }
    }

    public void StopSession()
    {
        lock (_lock)
        {
            if (_stopwatch.IsRunning)
            {
                RecordEvent("SynchronizationService", "SessionStopped", $"Elapsed: {_stopwatch.Elapsed.TotalSeconds:F2}s");
                _stopwatch.Stop();
            }
        }
    }

    public double GetElapsedSeconds()
    {
        return _stopwatch.Elapsed.TotalSeconds;
    }

    public DateTime GetCurrentUtc()
    {
        return SessionStartUtc.Add(_stopwatch.Elapsed);
    }

    public void RecordEvent(string source, string eventType, string? details = null)
    {
        lock (_lock)
        {
            var evt = new SyncEvent
            {
                SessionId = CurrentSessionId,
                Source = source,
                EventType = eventType,
                Details = details,
                RelativeSeconds = _stopwatch.Elapsed.TotalSeconds,
                UtcTime = DateTime.UtcNow,
                LocalTime = DateTime.Now
            };
            _events.Add(evt);
        }
    }

    public IReadOnlyList<SyncEvent> GetSyncEvents()
    {
        lock (_lock)
        {
            return _events.ToList();
        }
    }

    public double CalculateAudioVideoSkewMs(long videoFrames, double targetFps, long audioSamples, int sampleRate)
    {
        if (targetFps <= 0 || sampleRate <= 0) return 0;
        double videoDurationMs = (videoFrames / targetFps) * 1000.0;
        double audioDurationMs = ((double)audioSamples / sampleRate) * 1000.0;
        return Math.Abs(videoDurationMs - audioDurationMs);
    }
}

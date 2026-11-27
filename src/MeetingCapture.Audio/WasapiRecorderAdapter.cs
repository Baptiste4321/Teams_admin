using System;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MeetingCapture.Audio;

/// <summary>
/// Adaptateur permettant d'exposer WasapiRecorder (qui n'implémente pas nativement IWaveIn)
/// sous l'interface unifiée IWaveIn utilisée par l'enregistreur et le WaveFileWriter.
/// </summary>
public sealed class WasapiRecorderAdapter : IWaveIn
{
    private readonly WasapiRecorder _recorder;
    private byte[] _tempBuffer = new byte[8192];
    private bool _disposed;

    public WasapiRecorderAdapter(WasapiRecorder recorder)
    {
        _recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
        _recorder.DataAvailable += OnWasapiDataAvailable;
        _recorder.RecordingStopped += OnWasapiRecordingStopped;
    }

    private void OnWasapiDataAvailable(ReadOnlySpan<byte> buffer, AudioClientBufferFlags flags, long devicePosition, long qpcPosition)
    {
        if (buffer.Length == 0) return;

        lock (_tempBuffer)
        {
            if (_tempBuffer.Length < buffer.Length)
            {
                _tempBuffer = new byte[buffer.Length];
            }
            buffer.CopyTo(_tempBuffer);
            DataAvailable?.Invoke(this, new WaveInEventArgs(_tempBuffer, buffer.Length));
        }
    }

    private void OnWasapiRecordingStopped(object? sender, StoppedEventArgs e)
    {
        RecordingStopped?.Invoke(this, e);
    }

    public WaveFormat WaveFormat
    {
        get => _recorder.WaveFormat;
        set { /* Format déterminé par le sous-système audio WASAPI */ }
    }

    public event EventHandler<WaveInEventArgs>? DataAvailable;
    public event EventHandler<StoppedEventArgs>? RecordingStopped;

    public void StartRecording() => _recorder.StartRecording();
    public void StopRecording() => _recorder.StopRecording();

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _recorder.DataAvailable -= OnWasapiDataAvailable;
            _recorder.RecordingStopped -= OnWasapiRecordingStopped;
            _recorder.Dispose();
        }
    }
}

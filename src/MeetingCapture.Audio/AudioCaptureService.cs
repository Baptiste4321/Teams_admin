using System.Diagnostics;
using MeetingCapture.Core.Enums;
using MeetingCapture.Core.Interfaces;
using MeetingCapture.Core.Models;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;

namespace MeetingCapture.Audio;

public class AudioCaptureService : IAudioCaptureService
{
    private IWaveIn? _capture;
    private WaveFileWriter? _writer;
    private readonly object _lock = new();
    private readonly SemaphoreSlim _asyncLock = new(1, 1);
    private long _samplesCaptured;
    private float _currentLevel;
    private CaptureStatus _status = CaptureStatus.Idle;
    private string _activeDeviceName = "Haut-parleurs / Périphérique de sortie";
    private int _sampleRate = 48000;
    private DateTime _lastSoundTime = DateTime.UtcNow;
    private byte[] _pcmConversionBuffer = new byte[65536];

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

    public bool IsCapturing => Status == CaptureStatus.Capturing;
    public float CurrentLevel => _currentLevel;
    public long SamplesCaptured => _samplesCaptured;
    public int SampleRate => _sampleRate;
    public string ActiveDeviceName => _activeDeviceName;

    public event EventHandler<CaptureStatus>? StatusChanged;
    public event EventHandler<float>? LevelUpdated;
    public event EventHandler<string>? DeviceChanged;
    public event EventHandler<Exception>? ErrorOccurred;

    public Task<IReadOnlyList<AudioDeviceInfo>> GetOutputDevicesAsync()
    {
        var list = new List<AudioDeviceInfo>();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            MMDevice? defaultDevice = null;
            try { defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia); } catch { }

            // Find best default device: prioritize real hardware (not EPSON/virtual) or device with active audio sessions
            MMDevice? preferredDefault = null;
            foreach (var dev in devices)
            {
                if (dev.FriendlyName.Contains("EPSON", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    var sessions = dev.AudioSessionManager?.Sessions;
                    if (sessions != null)
                    {
                        for (int s = 0; s < sessions.Count; s++)
                        {
                            if (sessions[s].State == AudioSessionState.AudioSessionStateActive)
                            {
                                preferredDefault = dev;
                                break;
                            }
                        }
                    }
                }
                catch { }
                if (preferredDefault != null) break;
            }

            if (preferredDefault == null)
            {
                preferredDefault = devices.FirstOrDefault(d => !d.FriendlyName.Contains("EPSON", StringComparison.OrdinalIgnoreCase))
                                   ?? defaultDevice;
            }

            foreach (var dev in devices)
            {
                bool isDef = preferredDefault != null ? dev.ID == preferredDefault.ID : (defaultDevice != null && dev.ID == defaultDevice.ID);
                int sr = 48000;
                int ch = 2;
                try
                {
                    sr = dev.AudioClient.MixFormat.SampleRate;
                    ch = dev.AudioClient.MixFormat.Channels;
                }
                catch { }

                list.Add(new AudioDeviceInfo(dev.ID, dev.FriendlyName, isDef, ch, sr));
            }

            // Order so non-virtual / real devices come first
            list = list.OrderByDescending(d => d.IsDefault)
                       .ThenBy(d => d.Name.Contains("EPSON", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                       .ToList();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Audio output enumeration: {ex.Message}");
        }

        if (list.Count == 0)
        {
            list.Add(new AudioDeviceInfo("default-loopback", "Périphérique audio système (défaut)", true, 2, 48000));
        }

        return Task.FromResult<IReadOnlyList<AudioDeviceInfo>>(list);
    }

    private MMDevice? ResolveBestOutputDevice(string? requestedDeviceId)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var activeDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

            // 1. If explicit ID requested and valid (and not default placeholder)
            if (!string.IsNullOrEmpty(requestedDeviceId) && requestedDeviceId != "default-loopback")
            {
                var match = activeDevices.FirstOrDefault(d => d.ID == requestedDeviceId);
                if (match != null) return match;
            }

            // 2. Look for active endpoint with active audio session (e.g. Teams, browser)
            foreach (var dev in activeDevices)
            {
                if (dev.FriendlyName.Contains("EPSON", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    var sessions = dev.AudioSessionManager?.Sessions;
                    if (sessions != null)
                    {
                        for (int i = 0; i < sessions.Count; i++)
                        {
                            if (sessions[i].State == AudioSessionState.AudioSessionStateActive)
                            {
                                Debug.WriteLine($"[AudioCaptureService] Périphérique avec session active détecté : {dev.FriendlyName}");
                                return dev;
                            }
                        }
                    }
                }
                catch { }
            }

            // 3. Look for real hardware sound card (non-EPSON, non-virtual)
            var realDevice = activeDevices.FirstOrDefault(d =>
                !d.FriendlyName.Contains("EPSON", StringComparison.OrdinalIgnoreCase) &&
                !d.FriendlyName.Contains("Virtual", StringComparison.OrdinalIgnoreCase));
            if (realDevice != null) return realDevice;

            // 4. Default endpoint fallback
            try { return enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia); } catch { }
            return activeDevices.FirstOrDefault();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AudioCaptureService] Erreur lors de la résolution du périphérique : {ex.Message}");
            return null;
        }
    }

    public async Task<bool> StartCaptureAsync(AudioCaptureOptions options, CancellationToken ct = default)
    {
        await _asyncLock.WaitAsync(ct);
        try
        {
            Status = CaptureStatus.Initializing;
            _samplesCaptured = 0;
            _currentLevel = 0f;

            // Résolution du meilleur périphérique de sortie réel (évite les pilotes virtuels muets EPSON)
            var targetDevice = ResolveBestOutputDevice(options.DeviceId);

            if (targetDevice != null)
            {
                _activeDeviceName = targetDevice.FriendlyName;
                _capture = new WasapiLoopbackCapture(targetDevice);
                Debug.WriteLine($"[AudioCaptureService] WASAPI Loopback initialisé sur : {_activeDeviceName} ({targetDevice.ID})");
            }
            else
            {
                _capture = new WasapiLoopbackCapture();
                _activeDeviceName = "Haut-parleurs / Périphérique système";
            }

            _sampleRate = _capture.WaveFormat.SampleRate;

            if (!string.IsNullOrEmpty(options.OutputFilePath))
            {
                var dir = Path.GetDirectoryName(options.OutputFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                // Conversion systématique en format PCM standard 16-bit (compatible tous lecteurs et MediaElement)
                var pcmFormat = new WaveFormat(_capture.WaveFormat.SampleRate, 16, _capture.WaveFormat.Channels);
                _writer = new WaveFileWriter(options.OutputFilePath, pcmFormat);
            }

            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += OnRecordingStopped;

            _capture.StartRecording();
            Status = CaptureStatus.Capturing;
            return true;
        }
        catch (Exception ex)
        {
            Status = CaptureStatus.Error;
            ErrorOccurred?.Invoke(this, ex);
            return false;
        }
        finally
        {
            _asyncLock.Release();
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded <= 0) return;

        lock (_lock)
        {
            try
            {
                float sum = 0f;
                int sampleCount = 0;

                if (_capture?.WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat)
                {
                    int floatSampleCount = e.BytesRecorded / 4;
                    int pcmByteCount = floatSampleCount * 2;

                    if (_pcmConversionBuffer.Length < pcmByteCount)
                    {
                        _pcmConversionBuffer = new byte[pcmByteCount * 2];
                    }

                    int outIndex = 0;
                    for (int i = 0; i < e.BytesRecorded; i += 4)
                    {
                        float sample = BitConverter.ToSingle(e.Buffer, i);
                        sum += sample * sample;
                        sampleCount++;

                        // Float (-1.0f .. 1.0f) to 16-bit PCM short (-32768 .. 32767)
                        short pcmSample = (short)Math.Clamp((int)(sample * 32767f), -32768, 32767);
                        _pcmConversionBuffer[outIndex++] = (byte)(pcmSample & 0xFF);
                        _pcmConversionBuffer[outIndex++] = (byte)((pcmSample >> 8) & 0xFF);
                    }

                    _writer?.Write(_pcmConversionBuffer, 0, pcmByteCount);
                    int channels = _capture?.WaveFormat.Channels ?? 2;
                    _samplesCaptured += floatSampleCount / channels;
                }
                else
                {
                    _writer?.Write(e.Buffer, 0, e.BytesRecorded);

                    int bytesPerSample = (_capture?.WaveFormat.BitsPerSample ?? 16) / 8;
                    int channels = _capture?.WaveFormat.Channels ?? 2;
                    int totalSamples = e.BytesRecorded / (bytesPerSample * channels);
                    _samplesCaptured += totalSamples;

                    for (int i = 0; i < e.BytesRecorded; i += 2)
                    {
                        short sample = BitConverter.ToInt16(e.Buffer, i);
                        float normalized = sample / 32768f;
                        sum += normalized * normalized;
                        sampleCount++;
                    }
                }

                if (sampleCount > 0)
                {
                    float rms = MathF.Sqrt(sum / sampleCount);
                    _currentLevel = Math.Clamp(rms * 2.5f, 0f, 1f); // Normalize for VU display
                    LevelUpdated?.Invoke(this, _currentLevel);

                    if (_currentLevel > 0.01f)
                    {
                        _lastSoundTime = DateTime.UtcNow;
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, ex);
            }
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null)
        {
            ErrorOccurred?.Invoke(this, e.Exception);
            Status = CaptureStatus.Error;
        }
        else
        {
            Status = CaptureStatus.Stopped;
        }
    }

    public async Task StopCaptureAsync()
    {
        await _asyncLock.WaitAsync();
        try
        {
            Status = CaptureStatus.Stopping;
            lock (_lock)
            {
                if (_capture != null)
                {
                    _capture.StopRecording();
                    _capture.DataAvailable -= OnDataAvailable;
                    _capture.RecordingStopped -= OnRecordingStopped;
                    _capture.Dispose();
                    _capture = null;
                }

                if (_writer != null)
                {
                    _writer.Flush();
                    _writer.Dispose();
                    _writer = null;
                }

                _currentLevel = 0f;
                LevelUpdated?.Invoke(this, 0f);
                Status = CaptureStatus.Stopped;
            }
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex);
            Status = CaptureStatus.Error;
        }
        finally
        {
            _asyncLock.Release();
        }
    }

    public void Dispose()
    {
        StopCaptureAsync().GetAwaiter().GetResult();
    }
}

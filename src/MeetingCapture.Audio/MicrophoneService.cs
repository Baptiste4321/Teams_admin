using System.Diagnostics;
using MeetingCapture.Core.Enums;
using MeetingCapture.Core.Interfaces;
using MeetingCapture.Core.Models;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MeetingCapture.Audio;

public class MicrophoneService : IMicrophoneService
{
    private WasapiCapture? _capture;
    private WaveFileWriter? _writer;
    private readonly object _lock = new();
    private long _samplesCaptured;
    private float _currentLevel;
    private CaptureStatus _status = CaptureStatus.Idle;
    private string _activeDeviceName = "Microphone par défaut";
    private int _sampleRate = 48000;

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
    public event EventHandler<Exception>? ErrorOccurred;

    public Task<IReadOnlyList<AudioDeviceInfo>> GetInputDevicesAsync()
    {
        var list = new List<AudioDeviceInfo>();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            MMDevice? defaultDevice = null;
            try { defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications); } catch { }

            foreach (var dev in devices)
            {
                bool isDef = defaultDevice != null && dev.ID == defaultDevice.ID;
                int sr = 48000;
                int ch = 1;
                try
                {
                    sr = dev.AudioClient.MixFormat.SampleRate;
                    ch = dev.AudioClient.MixFormat.Channels;
                }
                catch { }

                list.Add(new AudioDeviceInfo(dev.ID, dev.FriendlyName, isDef, ch, sr));
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Microphone enumeration: {ex.Message}");
        }

        if (list.Count == 0)
        {
            list.Add(new AudioDeviceInfo("default-mic", "Microphone par défaut", true, 1, 48000));
        }

        return Task.FromResult<IReadOnlyList<AudioDeviceInfo>>(list);
    }

    public Task<bool> StartCaptureAsync(MicrophoneOptions options, CancellationToken ct = default)
    {
        if (!options.Enabled)
        {
            Status = CaptureStatus.Stopped;
            return Task.FromResult(true);
        }

        lock (_lock)
        {
            try
            {
                Status = CaptureStatus.Initializing;
                _samplesCaptured = 0;
                _currentLevel = 0f;

                MMDevice? targetDevice = null;
                try
                {
                    using var enumerator = new MMDeviceEnumerator();
                    if (!string.IsNullOrEmpty(options.DeviceId))
                    {
                        targetDevice = enumerator.GetDevice(options.DeviceId);
                    }
                    else
                    {
                        targetDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                    }
                }
                catch
                {
                    // Fallback to default
                }

                if (targetDevice != null)
                {
                    _activeDeviceName = targetDevice.FriendlyName;
                    _capture = new WasapiCapture(targetDevice);
                }
                else
                {
                    _capture = new WasapiCapture();
                }

                _sampleRate = _capture.WaveFormat.SampleRate;

                if (!string.IsNullOrEmpty(options.OutputFilePath))
                {
                    var dir = Path.GetDirectoryName(options.OutputFilePath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                    _writer = new WaveFileWriter(options.OutputFilePath, _capture.WaveFormat);
                }

                _capture.DataAvailable += OnDataAvailable;
                _capture.RecordingStopped += OnRecordingStopped;

                _capture.StartRecording();
                Status = CaptureStatus.Capturing;
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                Status = CaptureStatus.Error;
                ErrorOccurred?.Invoke(this, ex);
                return Task.FromResult(false);
            }
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded <= 0) return;

        lock (_lock)
        {
            try
            {
                _writer?.Write(e.Buffer, 0, e.BytesRecorded);

                int bytesPerSample = (_capture?.WaveFormat.BitsPerSample ?? 16) / 8;
                int channels = _capture?.WaveFormat.Channels ?? 1;
                int totalSamples = e.BytesRecorded / (bytesPerSample * channels);
                _samplesCaptured += totalSamples;

                // Calculate RMS
                float sum = 0f;
                int sampleCount = 0;

                if (_capture?.WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat)
                {
                    for (int i = 0; i < e.BytesRecorded; i += 4)
                    {
                        float sample = BitConverter.ToSingle(e.Buffer, i);
                        sum += sample * sample;
                        sampleCount++;
                    }
                }
                else if (bytesPerSample == 2)
                {
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
                    _currentLevel = Math.Clamp(rms * 3.0f, 0f, 1f);
                    LevelUpdated?.Invoke(this, _currentLevel);
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

    public Task StopCaptureAsync()
    {
        lock (_lock)
        {
            try
            {
                Status = CaptureStatus.Stopping;
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
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, ex);
                Status = CaptureStatus.Error;
            }
        }
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        StopCaptureAsync().GetAwaiter().GetResult();
    }
}

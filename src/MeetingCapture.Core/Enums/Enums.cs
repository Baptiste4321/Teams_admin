namespace MeetingCapture.Core.Enums;

public enum CaptureStatus
{
    Idle,
    Initializing,
    Capturing,
    Paused,
    Stopping,
    Stopped,
    Error
}

public enum CaptureSourceType
{
    TeamsWindow,
    CustomWindow,
    DesktopScreen,
    SystemAudioLoopback,
    Microphone,
    MeetingChat,
    Transcription
}

public enum VideoCodecType
{
    H264,
    HEVC,
    MPEG4,
    MJPEG,
    RawBmp
}

public enum AudioFormatType
{
    WavPcm16,
    WavFloat32,
    AacM4a
}

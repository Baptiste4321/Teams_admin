using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace MeetingCapture.Video;

/// <summary>
/// Progressive video container writer (AVI with MJPEG/Raw stream)
/// ensuring valid video files playable by standard players (VLC, Windows Media Player)
/// without relying on external ffmpeg binaries.
/// </summary>
public class VideoStreamWriter : IDisposable
{
    private readonly FileStream _fileStream;
    private readonly BinaryWriter _writer;
    private readonly int _width;
    private readonly int _height;
    private readonly int _fps;
    private readonly List<int> _indexOffsets = new();
    private readonly List<int> _indexLengths = new();
    private long _moviListPosition;
    private long _riffSizePosition;
    private long _totalFramesPosition;
    private int _frameCount;
    private readonly object _lock = new();

    public int FrameCount => _frameCount;

    public VideoStreamWriter(string filePath, int width, int height, int fps)
    {
        _width = width;
        _height = height;
        _fps = fps <= 0 ? 30 : fps;

        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        _fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read);
        _writer = new BinaryWriter(_fileStream);

        WriteHeader();
    }

    private void WriteHeader()
    {
        // RIFF header
        _writer.Write(new[] { 'R', 'I', 'F', 'F' });
        _riffSizePosition = _fileStream.Position;
        _writer.Write(0); // placeholder for RIFF size
        _writer.Write(new[] { 'A', 'V', 'I', ' ' });

        // LIST hdrl
        _writer.Write(new[] { 'L', 'I', 'S', 'T' });
        _writer.Write(192); // hdrl size
        _writer.Write(new[] { 'h', 'd', 'r', 'l' });

        // avih chunk
        _writer.Write(new[] { 'a', 'v', 'i', 'h' });
        _writer.Write(56); // avih size
        int microsecPerFrame = (int)(1_000_000.0 / _fps);
        _writer.Write(microsecPerFrame);
        _writer.Write(0); // max bytes per sec
        _writer.Write(0); // padding
        _writer.Write(0x10); // flags (AVIF_HASINDEX)
        _totalFramesPosition = _fileStream.Position;
        _writer.Write(0); // total frames placeholder
        _writer.Write(0); // initial frames
        _writer.Write(1); // streams
        _writer.Write(1024 * 1024); // buffer size
        _writer.Write(_width);
        _writer.Write(_height);
        _writer.Write(0); // reserved
        _writer.Write(0);
        _writer.Write(0);
        _writer.Write(0);

        // LIST strl
        _writer.Write(new[] { 'L', 'I', 'S', 'T' });
        _writer.Write(116); // strl size
        _writer.Write(new[] { 's', 't', 'r', 'l' });

        // strh chunk
        _writer.Write(new[] { 's', 't', 'r', 'h' });
        _writer.Write(56);
        _writer.Write(new[] { 'v', 'i', 'd', 's' });
        _writer.Write(new[] { 'M', 'J', 'P', 'G' }); // Motion JPEG
        _writer.Write(0); // flags
        _writer.Write((short)0); // priority
        _writer.Write((short)0); // language
        _writer.Write(0); // initial frames
        _writer.Write(1); // rate / scale
        _writer.Write(_fps);
        _writer.Write(0); // start
        _writer.Write(0); // length placeholder
        _writer.Write(1024 * 1024); // suggested buffer size
        _writer.Write(10000); // quality
        _writer.Write(0); // sample size
        _writer.Write((short)0); // frame rect left
        _writer.Write((short)0); // top
        _writer.Write((short)_width);
        _writer.Write((short)_height);

        // strf chunk (BITMAPINFOHEADER)
        _writer.Write(new[] { 's', 't', 'r', 'f' });
        _writer.Write(40);
        _writer.Write(40); // biSize
        _writer.Write(_width);
        _writer.Write(_height);
        _writer.Write((short)1); // planes
        _writer.Write((short)24); // bitcount
        _writer.Write(new[] { 'M', 'J', 'P', 'G' }); // compression
        _writer.Write(_width * _height * 3); // image size
        _writer.Write(0); // biXPelsPerMeter
        _writer.Write(0); // biYPelsPerMeter
        _writer.Write(0); // biClrUsed
        _writer.Write(0); // biClrImportant

        // LIST movi
        _writer.Write(new[] { 'L', 'I', 'S', 'T' });
        _moviListPosition = _fileStream.Position;
        _writer.Write(0); // movi size placeholder
        _writer.Write(new[] { 'm', 'o', 'v', 'i' });
    }

    public void AddFrame(Bitmap bitmap)
    {
        lock (_lock)
        {
            using var ms = new MemoryStream();
            var encoderParameters = new EncoderParameters(1);
            encoderParameters.Param[0] = new EncoderParameter(Encoder.Quality, 85L);
            var codec = GetEncoder(ImageFormat.Jpeg);

            if (codec != null)
            {
                bitmap.Save(ms, codec, encoderParameters);
            }
            else
            {
                bitmap.Save(ms, ImageFormat.Jpeg);
            }

            byte[] jpegBytes = ms.ToArray();
            int length = jpegBytes.Length;

            // Chunk header
            long chunkStartOffset = _fileStream.Position - (_moviListPosition + 4);
            _writer.Write(new[] { '0', '0', 'd', 'b' });
            _writer.Write(length);
            _writer.Write(jpegBytes);

            // Pad to even boundary if needed
            if (length % 2 != 0)
            {
                _writer.Write((byte)0);
            }

            _indexOffsets.Add((int)chunkStartOffset);
            _indexLengths.Add(length);
            _frameCount++;
        }
    }

    private static ImageCodecInfo? GetEncoder(ImageFormat format)
    {
        var codecs = ImageCodecInfo.GetImageDecoders();
        return codecs.FirstOrDefault(c => c.FormatID == format.Guid);
    }

    public void FinalizeFile()
    {
        lock (_lock)
        {
            if (_fileStream == null || !_fileStream.CanWrite) return;

            long moviEnd = _fileStream.Position;
            int moviDataSize = (int)(moviEnd - (_moviListPosition + 4));

            // Write idx1 index
            _writer.Write(new[] { 'i', 'd', 'x', '1' });
            _writer.Write(_indexOffsets.Count * 16);
            for (int i = 0; i < _indexOffsets.Count; i++)
            {
                _writer.Write(new[] { '0', '0', 'd', 'b' });
                _writer.Write(0x10); // keyframe AVIIF_KEYFRAME
                _writer.Write(_indexOffsets[i]);
                _writer.Write(_indexLengths[i]);
            }

            long totalFileSize = _fileStream.Position;

            // Update movi size
            _fileStream.Seek(_moviListPosition, SeekOrigin.Begin);
            _writer.Write(moviDataSize);

            // Update total frames in avih
            _fileStream.Seek(_totalFramesPosition, SeekOrigin.Begin);
            _writer.Write(_frameCount);

            // Update RIFF size
            _fileStream.Seek(_riffSizePosition, SeekOrigin.Begin);
            _writer.Write((int)(totalFileSize - 8));

            _fileStream.Flush();
        }
    }

    public void Dispose()
    {
        FinalizeFile();
        _writer.Dispose();
        _fileStream.Dispose();
    }
}

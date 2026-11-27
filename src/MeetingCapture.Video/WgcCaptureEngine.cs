using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using WinRT.Interop;

namespace MeetingCapture.Video;

/// <summary>
/// Windows Graphics Capture (WGC) engine — captures a HWND or monitor
/// using the modern DWM-level API introduced in Windows 10 1803.
/// 
/// Advantages over GDI BitBlt:
/// - Captures hardware-accelerated surfaces (Chromium/Teams/WebView2) correctly
/// - Captures even when the window is partially offscreen or behind other windows
/// - No yellow border in Windows 11 22H2+ when IsBorderRequired=false
/// - Uses GPU compositing — minimal CPU overhead
/// </summary>
public class WgcCaptureEngine : IDisposable
{
    // COM interop to create a GraphicsCaptureItem from a HWND
    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        GraphicsCaptureItem CreateForWindow(nint hwnd, in Guid iid);
        GraphicsCaptureItem CreateForMonitor(nint hmonitor, in Guid iid);
    }

    private static readonly Guid IID_GraphicsCaptureItem = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    private GraphicsCaptureItem? _item;
    private Direct3D11CaptureFramePool? _framePool;
    private GraphicsCaptureSession? _session;
    private readonly object _frameLock = new();
    private volatile Bitmap? _latestFrame;
    private bool _disposed;

    public bool IsSupported => ApiInformation.IsTypePresent("Windows.Graphics.Capture.GraphicsCaptureSession")
                               && GraphicsCaptureSession.IsSupported();

    public event EventHandler<Bitmap>? FrameArrived;
    public event EventHandler<Exception>? Error;

    public static bool IsAvailable()
    {
        try
        {
            return ApiInformation.IsTypePresent("Windows.Graphics.Capture.GraphicsCaptureSession")
                   && GraphicsCaptureSession.IsSupported();
        }
        catch
        {
            return false;
        }
    }

    public bool TryStartCapture(nint hwnd)
    {
        try
        {
            var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
            _item = interop.CreateForWindow(hwnd, IID_GraphicsCaptureItem);
            return StartInternal();
        }
        catch (Exception ex)
        {
            Error?.Invoke(this, ex);
            return false;
        }
    }

    public bool TryStartCaptureMonitor(nint hmonitor)
    {
        try
        {
            var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
            _item = interop.CreateForMonitor(hmonitor, IID_GraphicsCaptureItem);
            return StartInternal();
        }
        catch (Exception ex)
        {
            Error?.Invoke(this, ex);
            return false;
        }
    }

    private bool StartInternal()
    {
        if (_item == null) return false;

        var size = _item.Size;

        // Create frame pool using software rendering (no D3D11 device needed)
        _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            CreateDirect3DDevice(),
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            numberOfBuffers: 2,
            size);

        _framePool.FrameArrived += OnFrameArrived;

        _session = _framePool.CreateCaptureSession(_item);

        // Windows 11 22H2+: remove the yellow capture indicator border
        if (_session is { } s)
        {
            try { s.IsBorderRequired = false; } catch { /* Older Windows — not supported, ignore */ }
            try { s.IsCursorCaptureEnabled = false; } catch { }
        }

        _session.StartCapture();
        return true;
    }

    private static IDirect3DDevice CreateDirect3DDevice()
    {
        // Use the WinRT software D3D device for maximum compatibility
        // (no NVIDIA/AMD GPU required, works on any Windows 10 machine)
        return Direct3D11Helper.CreateDevice();
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        try
        {
            using var frame = sender.TryGetNextFrame();
            if (frame == null) return;

            var bitmap = ConvertFrameToBitmap(frame);
            if (bitmap == null) return;

            lock (_frameLock)
            {
                _latestFrame?.Dispose();
                _latestFrame = bitmap;
            }

            FrameArrived?.Invoke(this, bitmap);
        }
        catch (Exception ex)
        {
            Error?.Invoke(this, ex);
        }
    }

    private static Bitmap? ConvertFrameToBitmap(Direct3D11CaptureFrame frame)
    {
        try
        {
            var surface = frame.Surface;
            var size = frame.ContentSize;

            // Convert IDirect3DSurface -> SoftwareBitmap via GetAsBitmap
            using var sbmp = SoftwareBitmap.CreateCopyFromSurfaceAsync(surface).GetAwaiter().GetResult();
            if (sbmp == null) return null;

            // Convert format to BGRA8
            SoftwareBitmap converted;
            if (sbmp.BitmapPixelFormat != BitmapPixelFormat.Bgra8 || sbmp.BitmapAlphaMode != BitmapAlphaMode.Premultiplied)
            {
                converted = SoftwareBitmap.Convert(sbmp, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            }
            else
            {
                converted = sbmp;
            }

            using (converted)
            {
                int width = converted.PixelWidth;
                int height = converted.PixelHeight;

                var bmp = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                var bmpData = bmp.LockBits(new Rectangle(0, 0, width, height),
                    ImageLockMode.WriteOnly,
                    System.Drawing.Imaging.PixelFormat.Format32bppArgb);

                try
                {
                    var buf = new byte[height * bmpData.Stride];
                    converted.CopyToBuffer(buf.AsBuffer());
                    Marshal.Copy(buf, 0, bmpData.Scan0, buf.Length);
                }
                finally
                {
                    bmp.UnlockBits(bmpData);
                }

                return bmp;
            }
        }
        catch
        {
            return null;
        }
    }

    public Bitmap? GetLatestFrame()
    {
        lock (_frameLock)
        {
            return _latestFrame != null ? new Bitmap(_latestFrame) : null;
        }
    }

    public void Stop()
    {
        _session?.Dispose();
        _session = null;

        if (_framePool != null)
        {
            _framePool.FrameArrived -= OnFrameArrived;
            _framePool.Dispose();
            _framePool = null;
        }

        lock (_frameLock)
        {
            _latestFrame?.Dispose();
            _latestFrame = null;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Stop();
            _disposed = true;
        }
    }
}

/// <summary>Helper to create a WinRT Direct3D11 device without SharpDX dependency.</summary>
internal static class Direct3D11Helper
{
    [DllImport("d3d11.dll", EntryPoint = "D3D11CreateDevice", SetLastError = false,
        CharSet = CharSet.Unicode, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    private static extern int D3D11CreateDevice(
        nint adapter, uint driverType, nint software, uint flags,
        nint featureLevels, uint numFeatureLevels, uint sdkVersion,
        out nint device, nint featureLevel, out nint context);

    [DllImport("d3d11.dll", EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice",
        SetLastError = false, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(nint dxgiDevice, out nint graphicsDevice);

    private const uint D3D_DRIVER_TYPE_HARDWARE = 1;
    private const uint D3D_DRIVER_TYPE_WARP = 5;
    private const uint D3D11_SDK_VERSION = 7;

    public static IDirect3DDevice CreateDevice()
    {
        // Try hardware first, fallback to WARP software renderer
        int hr = D3D11CreateDevice(
            0, D3D_DRIVER_TYPE_HARDWARE, 0, 0, 0, 0,
            D3D11_SDK_VERSION, out nint d3dDevice, 0, out _);

        if (hr != 0)
        {
            hr = D3D11CreateDevice(
                0, D3D_DRIVER_TYPE_WARP, 0, 0, 0, 0,
                D3D11_SDK_VERSION, out d3dDevice, 0, out _);
        }

        if (hr != 0 || d3dDevice == 0)
        {
            throw new InvalidOperationException($"D3D11CreateDevice failed: 0x{hr:X8}");
        }

        // Get IDXGIDevice interface
        var dxgiDevice = Marshal.GetObjectForIUnknown(d3dDevice);
        Marshal.Release(d3dDevice);

        int hr2 = CreateDirect3D11DeviceFromDXGIDevice(
            Marshal.GetIUnknownForObject(dxgiDevice), out nint rtDevice);

        if (hr2 != 0 || rtDevice == 0)
        {
            throw new InvalidOperationException($"CreateDirect3D11DeviceFromDXGIDevice failed: 0x{hr2:X8}");
        }

        var winrtDevice = Marshal.GetObjectForIUnknown(rtDevice) as IDirect3DDevice;
        Marshal.Release(rtDevice);

        return winrtDevice ?? throw new InvalidOperationException("IDirect3DDevice cast failed");
    }
}

/// <summary>Minimal WinRT API availability checker.</summary>
internal static class ApiInformation
{
    public static bool IsTypePresent(string typeName)
    {
        try
        {
            return Windows.Foundation.Metadata.ApiInformation.IsTypePresent(typeName);
        }
        catch
        {
            return false;
        }
    }
}

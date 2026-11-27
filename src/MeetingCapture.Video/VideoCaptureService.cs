using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using MeetingCapture.Core.Enums;
using MeetingCapture.Core.Interfaces;
using MeetingCapture.Core.Models;

namespace MeetingCapture.Video;

public class VideoCaptureService : IVideoCaptureService
{
    // ─────────────────────────── WGC engine ────────────────────────────
    private WgcCaptureEngine? _wgcEngine;
    private bool _usingWgc;
    // ───────────────────────────────────────────────────────────────────

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint hWnd, nint hDC);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleDC(nint hdc);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleBitmap(nint hdc, int nWidth, int nHeight);

    [DllImport("gdi32.dll")]
    private static extern nint SelectObject(nint hdc, nint hgdiobj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(nint hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint hObject);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(nint hdcDest, int nXDest, int nYDest, int nWidth, int nHeight, nint hdcSrc, int nXSrc, int nYSrc, int dwRop);

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(nint hwnd, nint hdcBlt, uint nFlags);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint hWnd);

    private const int SRCCOPY = 0x00CC0020;
    private const uint PW_RENDERFULLCONTENT = 0x00000002;

    private readonly object _lock = new();
    private CancellationTokenSource? _cts;
    private Task? _captureTask;
    private VideoStreamWriter? _streamWriter;

    private CaptureStatus _status = CaptureStatus.Idle;
    private double _currentFps;
    private (int Width, int Height) _currentResolution = (1920, 1080);
    private long _capturedFramesCount;
    private nint _activeWindowHandle;
    private bool _blackFramesDetected;
    private bool _frozenFramesDetected;
    private int _consecutiveIdenticalFrames;
    private int _consecutiveBlackFrames;
    private long _lastFrameHash;

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
    public double CurrentFps => _currentFps;
    public (int Width, int Height) CurrentResolution => _currentResolution;
    public long CapturedFramesCount => _capturedFramesCount;
    public nint ActiveWindowHandle => _activeWindowHandle;

    public event EventHandler<CaptureStatus>? StatusChanged;
    public event EventHandler<Exception>? ErrorOccurred;
    public event EventHandler<(int Width, int Height, long FrameIndex)>? FrameCaptured;

    public Task<IReadOnlyList<WindowInfo>> GetAvailableWindowsAsync()
    {
        return Task.FromResult(WindowEnumerator.GetOpenWindows());
    }

    public Task<IReadOnlyList<ScreenInfo>> GetAvailableScreensAsync()
    {
        return Task.FromResult(ScreenEnumerator.GetScreens());
    }

    public Task<bool> StartCaptureAsync(VideoCaptureOptions options, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (IsCapturing) return Task.FromResult(true);

            try
            {
                Status = CaptureStatus.Initializing;
                _capturedFramesCount = 0;
                _blackFramesDetected = false;
                _frozenFramesDetected = false;
                _consecutiveIdenticalFrames = 0;
                _consecutiveBlackFrames = 0;
                _activeWindowHandle = options.WindowHandle;
                _usingWgc = false;

                int width = options.TargetWidth;
                int height = options.TargetHeight;

                if (!options.CaptureEntireScreen && options.WindowHandle != 0 && IsWindow(options.WindowHandle))
                {
                    try
                    {
                        if (IsIconic(options.WindowHandle))
                        {
                            ShowWindow(options.WindowHandle, SW_RESTORE);
                        }
                        SetForegroundWindow(options.WindowHandle);
                        BringWindowToTop(options.WindowHandle);
                    }
                    catch { }

                    if (WindowEnumerator.GetWindowBounds(options.WindowHandle, out var rect) && rect.Width > 0 && rect.Height > 0)
                    {
                        width = (rect.Width / 2) * 2; // dimensions paires obligatoires
                        height = (rect.Height / 2) * 2;
                    }
                }
                else
                {
                    var screens = ScreenEnumerator.GetScreens();
                    var screen = options.ScreenIndex >= 0 && options.ScreenIndex < screens.Count
                        ? screens[options.ScreenIndex]
                        : screens.FirstOrDefault(s => s.IsPrimary) ?? screens[0];

                    width = (screen.Width / 2) * 2;
                    height = (screen.Height / 2) * 2;
                }

                _currentResolution = (width, height);

                // Chemin du fichier : .avi (AVI/MJPEG, sans FFmpeg) — nommé recording.avi
                var outPath = string.IsNullOrEmpty(options.OutputFilePath)
                    ? Path.Combine(Path.GetTempPath(), $"capture_{Guid.NewGuid():N}.avi")
                    : options.OutputFilePath;

                _streamWriter = new VideoStreamWriter(outPath, width, height, options.TargetFps);

                // ── Tentative WGC (Windows Graphics Capture) ──────────────────────
                if (WgcCaptureEngine.IsAvailable())
                {
                    _wgcEngine?.Dispose();
                    _wgcEngine = new WgcCaptureEngine();
                    _wgcEngine.Error += (_, ex) => ErrorOccurred?.Invoke(this, ex);
                    _wgcEngine.FrameArrived += OnWgcFrameArrived;

                    bool wgcStarted = !options.CaptureEntireScreen && options.WindowHandle != 0
                        ? _wgcEngine.TryStartCapture(options.WindowHandle)
                        : TryStartWgcMonitor(options.ScreenIndex);

                    if (wgcStarted)
                    {
                        _usingWgc = true;
                        Status = CaptureStatus.Capturing;
                        return Task.FromResult(true);
                    }

                    // WGC échec — nettoyage, bascule sur GDI
                    _wgcEngine.FrameArrived -= OnWgcFrameArrived;
                    _wgcEngine.Dispose();
                    _wgcEngine = null;
                }
                // ── Fallback : boucle GDI ─────────────────────────────────────────
                _cts = new CancellationTokenSource();
                _captureTask = Task.Run(() => CaptureLoop(options, width, height, _cts.Token));

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

    private bool TryStartWgcMonitor(int screenIndex)
    {
        if (_wgcEngine == null) return false;
        var screens = ScreenEnumerator.GetScreens();
        var screen = screenIndex >= 0 && screenIndex < screens.Count ? screens[screenIndex] : screens.FirstOrDefault(s => s.IsPrimary);
        if (screen == null) return false;

        // Obtenir le HMONITOR via EnumDisplayMonitors n'est pas trivial —
        // on utilise MonitorFromPoint pour l'écran actif.
        nint hmon = MonitorFromPoint(new POINT { X = screen.Left + screen.Width / 2, Y = screen.Top + screen.Height / 2 }, MONITOR_DEFAULTTONEAREST);
        return hmon != 0 && _wgcEngine.TryStartCaptureMonitor(hmon);
    }

    private void OnWgcFrameArrived(object? sender, Bitmap bmp)
    {
        try
        {
            AnalyzeFrameHealth(bmp);
            _streamWriter?.AddFrame(bmp);
            long fc = Interlocked.Increment(ref _capturedFramesCount);
            FrameCaptured?.Invoke(this, (_currentResolution.Width, _currentResolution.Height, fc));
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex);
        }
    }

    // P/Invoke for monitor enumeration
    [DllImport("user32.dll")]
    private static extern nint MonitorFromPoint(POINT pt, uint dwFlags);
    private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    private void CaptureLoop(VideoCaptureOptions options, int width, int height, CancellationToken token)
    {
        int targetFps = options.TargetFps > 0 ? options.TargetFps : 30;
        double frameIntervalMs = 1000.0 / targetFps;
        var sw = Stopwatch.StartNew();
        var fpsSw = Stopwatch.StartNew();
        int frameCountInSecond = 0;

        while (!token.IsCancellationRequested)
        {
            long frameStartMs = sw.ElapsedMilliseconds;

            try
            {
                using var bmp = CaptureSingleFrame(options, width, height);
                if (bmp != null)
                {
                    AnalyzeFrameHealth(bmp);
                    _streamWriter?.AddFrame(bmp);
                    _capturedFramesCount++;
                    frameCountInSecond++;

                    FrameCaptured?.Invoke(this, (width, height, _capturedFramesCount));
                }

                if (fpsSw.ElapsedMilliseconds >= 1000)
                {
                    _currentFps = frameCountInSecond / (fpsSw.ElapsedMilliseconds / 1000.0);
                    frameCountInSecond = 0;
                    fpsSw.Restart();
                }

                long elapsed = sw.ElapsedMilliseconds - frameStartMs;
                int delay = (int)(frameIntervalMs - elapsed);
                if (delay > 1)
                {
                    Thread.Sleep(delay);
                }
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, ex);
            }
        }
    }

    private Bitmap? CaptureSingleFrame(VideoCaptureOptions options, int width, int height)
    {
        if (!options.CaptureEntireScreen && options.WindowHandle != 0 && IsWindow(options.WindowHandle))
        {
            return CaptureWindowBitmap(options.WindowHandle, width, height);
        }
        else
        {
            return CaptureScreenBitmap(options.ScreenIndex, width, height);
        }
    }

    [DllImport("gdi32.dll")]
    private static extern int SetStretchBltMode(nint hdc, int mode);

    [DllImport("gdi32.dll")]
    private static extern bool StretchBlt(
        nint hdcDest, int nXOriginDest, int nYOriginDest, int nWidthDest, int nHeightDest,
        nint hdcSrc, int nXOriginSrc, int nYOriginSrc, int nWidthSrc, int nHeightSrc, int dwRop);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    private const int SW_RESTORE = 9;
    private const int HALFTONE = 4;

    private Bitmap? CaptureWindowBitmap(nint hWnd, int width, int height)
    {
        if (!IsWindow(hWnd)) return null;

        if (!WindowEnumerator.GetWindowBounds(hWnd, out var rect) || rect.Width <= 0 || rect.Height <= 0)
        {
            return null;
        }

        // 1. Tenter PrintWindow avec PW_RENDERFULLCONTENT (0x02)
        // PW_RENDERFULLCONTENT demande à DWM de restituer EXCLUSIVEMENT la fenêtre cible
        // dans le contexte mémoire. Ainsi, aucune autre fenêtre superposée n'apparaît !
        var printBmp = CaptureViaPrintWindow(hWnd, rect, width, height);
        if (printBmp != null)
        {
            return printBmp;
        }

        // 2. Fallback si PrintWindow échoue ou renvoie une image vide :
        // Découpage direct aux coordonnées exactes de la fenêtre sur l'écran.
        return CaptureViaScreenBlt(rect, width, height);
    }

    private Bitmap? CaptureViaPrintWindow(nint hWnd, WindowEnumerator.RECT rect, int width, int height)
    {
        nint hdcSrc = GetDC(0);
        if (hdcSrc == 0) return null;

        nint hdcMem = CreateCompatibleDC(hdcSrc);
        nint hBitmap = CreateCompatibleBitmap(hdcSrc, rect.Width, rect.Height);
        nint hOld = SelectObject(hdcMem, hBitmap);

        try
        {
            bool ok = PrintWindow(hWnd, hdcMem, PW_RENDERFULLCONTENT);
            if (!ok)
            {
                ok = PrintWindow(hWnd, hdcMem, 0);
            }

            if (ok)
            {
                using var rawBmp = Image.FromHbitmap(hBitmap);
                if (IsBitmapNonEmpty(rawBmp))
                {
                    if (rawBmp.Width == width && rawBmp.Height == height)
                    {
                        return (Bitmap)rawBmp.Clone();
                    }

                    var scaled = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    using var g = Graphics.FromImage(scaled);
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    g.DrawImage(rawBmp, 0, 0, width, height);
                    return scaled;
                }
            }
        }
        catch
        {
            // Erreur ignorée, bascule vers le fallback
        }
        finally
        {
            SelectObject(hdcMem, hOld);
            DeleteObject(hBitmap);
            DeleteDC(hdcMem);
            ReleaseDC(0, hdcSrc);
        }

        return null;
    }

    private static bool IsBitmapNonEmpty(Bitmap bmp)
    {
        int w = bmp.Width;
        int h = bmp.Height;
        if (w <= 0 || h <= 0) return false;

        int[] xs = { w / 4, w / 2, 3 * w / 4 };
        int[] ys = { h / 4, h / 2, 3 * h / 4 };

        foreach (int x in xs)
        {
            foreach (int y in ys)
            {
                var color = bmp.GetPixel(x, y);
                if (color.A > 0 && (color.R > 5 || color.G > 5 || color.B > 5))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private Bitmap? CaptureViaScreenBlt(WindowEnumerator.RECT rect, int width, int height)
    {
        nint hdcScreen = GetDC(0);
        if (hdcScreen == 0) return null;

        nint hdcMem = CreateCompatibleDC(hdcScreen);
        nint hBmp = CreateCompatibleBitmap(hdcScreen, width, height);
        nint hOldBmp = SelectObject(hdcMem, hBmp);

        try
        {
            if (rect.Width == width && rect.Height == height)
            {
                BitBlt(hdcMem, 0, 0, width, height, hdcScreen, rect.Left, rect.Top, SRCCOPY);
            }
            else
            {
                SetStretchBltMode(hdcMem, HALFTONE);
                StretchBlt(hdcMem, 0, 0, width, height, hdcScreen, rect.Left, rect.Top, rect.Width, rect.Height, SRCCOPY);
            }

            return Image.FromHbitmap(hBmp);
        }
        finally
        {
            SelectObject(hdcMem, hOldBmp);
            DeleteObject(hBmp);
            DeleteDC(hdcMem);
            ReleaseDC(0, hdcScreen);
        }
    }

    private Bitmap? CaptureScreenBitmap(int screenIndex, int width, int height)
    {
        var screens = ScreenEnumerator.GetScreens();
        var screen = screenIndex >= 0 && screenIndex < screens.Count ? screens[screenIndex] : screens[0];

        nint hdcSrc = GetDC(0);
        if (hdcSrc == 0) return null;

        nint hdcDest = CreateCompatibleDC(hdcSrc);
        nint hBitmap = CreateCompatibleBitmap(hdcSrc, width, height);
        nint hOld = SelectObject(hdcDest, hBitmap);

        try
        {
            BitBlt(hdcDest, 0, 0, width, height, hdcSrc, screen.Left, screen.Top, SRCCOPY);
            return Image.FromHbitmap(hBitmap);
        }
        finally
        {
            SelectObject(hdcDest, hOld);
            DeleteObject(hBitmap);
            DeleteDC(hdcDest);
            ReleaseDC(0, hdcSrc);
        }
    }

    private void AnalyzeFrameHealth(Bitmap bmp)
    {
        try
        {
            // Sample a 8x8 grid of pixels for health check
            long luminanceSum = 0;
            long hash = 17;
            int stepX = Math.Max(1, bmp.Width / 8);
            int stepY = Math.Max(1, bmp.Height / 8);

            for (int y = 0; y < bmp.Height; y += stepY)
            {
                for (int x = 0; x < bmp.Width; x += stepX)
                {
                    var color = bmp.GetPixel(x, y);
                    int lum = (int)(0.299 * color.R + 0.587 * color.G + 0.114 * color.B);
                    luminanceSum += lum;
                    hash = hash * 31 + color.ToArgb();
                }
            }

            int sampleCount = (bmp.Width / stepX) * (bmp.Height / stepY);
            if (sampleCount > 0)
            {
                double avgLum = (double)luminanceSum / sampleCount;
                if (avgLum < 1.0)
                {
                    _consecutiveBlackFrames++;
                    if (_consecutiveBlackFrames > 45) // ~1.5s at 30 fps
                    {
                        _blackFramesDetected = true;
                    }
                }
                else
                {
                    _consecutiveBlackFrames = 0;
                    _blackFramesDetected = false;
                }
            }

            if (hash == _lastFrameHash)
            {
                _consecutiveIdenticalFrames++;
                if (_consecutiveIdenticalFrames > 60) // ~2s at 30 fps
                {
                    _frozenFramesDetected = true;
                }
            }
            else
            {
                _consecutiveIdenticalFrames = 0;
                _frozenFramesDetected = false;
                _lastFrameHash = hash;
            }
        }
        catch
        {
            // Ignore analysis exceptions on rapid frame transitions
        }
    }

    public async Task StopCaptureAsync()
    {
        lock (_lock)
        {
            if (!IsCapturing && Status != CaptureStatus.Initializing) return;
            Status = CaptureStatus.Stopping;
            _cts?.Cancel();

            if (_wgcEngine != null)
            {
                _wgcEngine.FrameArrived -= OnWgcFrameArrived;
                _wgcEngine.Dispose();
                _wgcEngine = null;
            }
        }

        if (_captureTask != null)
        {
            try
            {
                await _captureTask;
            }
            catch (OperationCanceledException) { }
            catch { }
            _captureTask = null;
        }

        lock (_lock)
        {
            _streamWriter?.Dispose();
            _streamWriter = null;
            _currentFps = 0;
            Status = CaptureStatus.Stopped;
        }
    }

    public DiagnosticReport GetDiagnostic()
    {
        var report = new DiagnosticReport
        {
            VideoActive = IsCapturing,
            VideoFramesCaptured = _capturedFramesCount,
            VideoCurrentFps = _currentFps,
            VideoBlackFramesDetected = _blackFramesDetected,
            VideoFrozenFramesDetected = _frozenFramesDetected
        };

        if (_blackFramesDetected)
        {
            report.Warnings.Add("Flux vidéo noir détecté (protection d'écran ou fenêtre masquée).");
        }
        if (_frozenFramesDetected)
        {
            report.Warnings.Add("Image figée détectée sur la source vidéo.");
        }

        return report;
    }

    public void Dispose()
    {
        StopCaptureAsync().GetAwaiter().GetResult();
        _cts?.Dispose();
    }
}

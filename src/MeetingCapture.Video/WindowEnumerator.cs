using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using MeetingCapture.Core.Models;

namespace MeetingCapture.Video;

public static class WindowEnumerator
{
    private delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    private static extern int DwmGetWindowAttributeRect(nint hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        public int Width => Math.Max(0, Right - Left);
        public int Height => Math.Max(0, Bottom - Top);
    }

    private const int DWMWA_CLOAKED = 14;
    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    /// <summary>
    /// Gets the exact on-screen visible bounds of a window, excluding DWM drop shadows.
    /// Perfectly frames windows on Windows 10 & 11 without margin offsets.
    /// </summary>
    public static bool GetWindowBounds(nint hWnd, out RECT rect)
    {
        try
        {
            if (DwmGetWindowAttributeRect(hWnd, DWMWA_EXTENDED_FRAME_BOUNDS, out rect, Marshal.SizeOf<RECT>()) == 0)
            {
                if (rect.Width > 0 && rect.Height > 0)
                {
                    return true;
                }
            }
        }
        catch { }

        return GetWindowRect(hWnd, out rect);
    }

    public static IReadOnlyList<WindowInfo> GetOpenWindows()
    {
        var windows = new List<(WindowInfo info, int score)>();
        uint currentPid = (uint)Environment.ProcessId;

        EnumWindows((hWnd, lParam) =>
        {
            if (!IsWindowVisible(hWnd)) return true;

            int length = GetWindowTextLength(hWnd);
            if (length == 0) return true;

            // Check if window is cloaked (hidden virtual desktop or UWP suspended)
            if (DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out int isCloaked, sizeof(int)) == 0 && isCloaked != 0)
            {
                return true;
            }

            if (!GetWindowBounds(hWnd, out var rect) || rect.Width <= 200 || rect.Height <= 200)
            {
                return true;
            }

            var builder = new StringBuilder(length + 1);
            GetWindowText(hWnd, builder, builder.Capacity);
            var title = builder.ToString().Trim();
            if (string.IsNullOrEmpty(title)) return true;

            GetWindowThreadProcessId(hWnd, out uint pid);

            // Exclude our own application window!
            if (pid == currentPid) return true;

            string procName = string.Empty;
            try
            {
                using var proc = Process.GetProcessById((int)pid);
                procName = proc.ProcessName;
            }
            catch
            {
                procName = "Unknown";
            }

            // Exclude common shells/system background windows
            if (procName.Equals("Progman", StringComparison.OrdinalIgnoreCase) ||
                procName.Equals("ShellExperienceHost", StringComparison.OrdinalIgnoreCase) ||
                procName.Equals("SearchHost", StringComparison.OrdinalIgnoreCase) ||
                procName.Equals("Taskbar", StringComparison.OrdinalIgnoreCase) ||
                procName.Equals("MeetingCapture.UI", StringComparison.OrdinalIgnoreCase) ||
                (procName.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase) && title.Length < 3))
            {
                return true;
            }

            int score = 0;
            bool isTeams = false;

            bool hasLiveMeeting = title.Contains("teams.live.com", StringComparison.OrdinalIgnoreCase) ||
                                  title.Contains("live.com/meet", StringComparison.OrdinalIgnoreCase) ||
                                  title.Contains("teams.microsoft.com", StringComparison.OrdinalIgnoreCase);

            bool isTeamsProc = procName.Contains("teams", StringComparison.OrdinalIgnoreCase) ||
                               procName.Contains("msteams", StringComparison.OrdinalIgnoreCase);

            bool hasMeetingKeyword = title.Contains("Réunion", StringComparison.OrdinalIgnoreCase) ||
                                     title.Contains("Meeting", StringComparison.OrdinalIgnoreCase) ||
                                     title.Contains("Appel", StringComparison.OrdinalIgnoreCase) ||
                                     title.Contains("Call", StringComparison.OrdinalIgnoreCase) ||
                                     title.Contains("Rejoindre", StringComparison.OrdinalIgnoreCase);

            if (hasLiveMeeting || (isTeamsProc && hasMeetingKeyword) || (title.Contains("Teams", StringComparison.OrdinalIgnoreCase) && hasMeetingKeyword))
            {
                score = 100; // Priorité 1 : Vraie réunion active Teams
                isTeams = true;
            }
            else if (isTeamsProc || title.Contains("Teams", StringComparison.OrdinalIgnoreCase))
            {
                score = 50; // Application Teams générale
                isTeams = true;
            }
            else if (hasMeetingKeyword && (procName.Contains("edge", StringComparison.OrdinalIgnoreCase) || 
                                           procName.Contains("chrome", StringComparison.OrdinalIgnoreCase) || 
                                           procName.Contains("brave", StringComparison.OrdinalIgnoreCase)))
            {
                score = 30; // Réunion dans un navigateur
                isTeams = true;
            }

            var winInfo = new WindowInfo(hWnd, title, procName, (int)pid, rect.Width, rect.Height, isTeams);
            windows.Add((winInfo, score));
            return true;
        }, 0);

        return windows
            .OrderByDescending(w => w.score)
            .ThenByDescending(w => w.info.Width * w.info.Height)
            .Select(w => w.info)
            .ToList();
    }
}

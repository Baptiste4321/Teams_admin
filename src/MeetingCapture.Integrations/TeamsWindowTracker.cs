using System.Diagnostics;
using MeetingCapture.Core.Models;
using MeetingCapture.Video;

namespace MeetingCapture.Integrations;

public class TeamsWindowTracker
{
    public WindowInfo? FindActiveTeamsMeetingWindow()
    {
        var windows = WindowEnumerator.GetOpenWindows();
        return windows.FirstOrDefault(w => w.IsTeams);
    }

    public bool IsTeamsRunning()
    {
        var procs = Process.GetProcesses();
        return procs.Any(p => p.ProcessName.Contains("teams", StringComparison.OrdinalIgnoreCase));
    }

    public Process? FindTeamsProcess()
    {
        var activeWindow = FindActiveTeamsMeetingWindow();
        if (activeWindow != null && activeWindow.ProcessId > 0)
        {
            try 
            {
                int rootPid = GetRootProcessId(activeWindow.ProcessId);
                return Process.GetProcessById(rootPid); 
            } 
            catch { }
        }

        var procs = Process.GetProcesses();
        var teamsProcs = procs.Where(p =>
            p.ProcessName.Contains("teams", StringComparison.OrdinalIgnoreCase) ||
            p.ProcessName.Contains("msteams", StringComparison.OrdinalIgnoreCase)).ToList();

        if (teamsProcs.Count > 0)
        {
            // Préférer le processus parent racine (démarré en premier)
            return teamsProcs.OrderBy(p =>
            {
                try { return p.StartTime; } catch { return DateTime.MaxValue; }
            }).FirstOrDefault();
        }

        return null;
    }

    /// <summary>
    /// Pour les navigateurs (Edge, Chrome) ou Teams (Electron/WebView2),
    /// retrouve le processus racine (lancé en premier) afin que le Process Loopback
    /// (qui opère sur l'arborescence descendante) englobe le sous-processus audio.
    /// </summary>
    public static int GetRootProcessId(int pid)
    {
        if (pid <= 0) return 0;
        try
        {
            var proc = Process.GetProcessById(pid);
            string baseName = proc.ProcessName;

            // Si c'est msedgewebview2 utilisé par Teams, chercher ms-teams
            if (baseName.Contains("webview", StringComparison.OrdinalIgnoreCase))
            {
                var teamsProcs = Process.GetProcesses().Where(p =>
                    p.ProcessName.Contains("teams", StringComparison.OrdinalIgnoreCase)).ToList();
                if (teamsProcs.Count > 0)
                {
                    return teamsProcs.OrderBy(p => { try { return p.StartTime; } catch { return DateTime.MaxValue; } }).First().Id;
                }
            }

            var siblings = Process.GetProcessesByName(baseName);
            if (siblings.Length > 1)
            {
                var root = siblings.OrderBy(p =>
                {
                    try { return p.StartTime; } catch { return DateTime.MaxValue; }
                }).FirstOrDefault();

                if (root != null) return root.Id;
            }
        }
        catch { }
        return pid;
    }
}

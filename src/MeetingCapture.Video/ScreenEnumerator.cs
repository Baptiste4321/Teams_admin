using MeetingCapture.Core.Models;

namespace MeetingCapture.Video;

public static class ScreenEnumerator
{
    public static IReadOnlyList<ScreenInfo> GetScreens()
    {
        var list = new List<ScreenInfo>();
        try
        {
            var screens = System.Windows.Forms.Screen.AllScreens;
            for (int i = 0; i < screens.Length; i++)
            {
                var s = screens[i];
                list.Add(new ScreenInfo(
                    Index: i,
                    DeviceName: s.DeviceName,
                    Width: s.Bounds.Width,
                    Height: s.Bounds.Height,
                    Left: s.Bounds.Left,
                    Top: s.Bounds.Top,
                    IsPrimary: s.Primary
                ));
            }
        }
        catch
        {
            list.Add(new ScreenInfo(0, "Primary Display", 1920, 1080, 0, 0, true));
        }

        return list;
    }
}

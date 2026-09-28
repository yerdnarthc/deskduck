using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace DeskDuck.Views;

/// <summary>
/// Native Win11 titlebar tinting via DWM attributes: caption background and
/// window border follow the app palette, title text stays readable, and the
/// minimize/maximize/close glyphs adapt automatically. This styles the caption
/// *area* — fully custom-drawn caption buttons would mean reimplementing
/// drag/snap/aero via WindowChrome, deliberately out of scope for a utility.
/// No-op below Win11, and never throws (purely cosmetic).
/// </summary>
public static class WindowChrome
{
    private const int DWMWA_TEXT_COLOR = 19;
    private const int DWMWA_CAPTION_COLOR = 20;
    private const int DWMWA_BORDER_COLOR = 35;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public static void Tint(Window window)
    {
        if (Environment.OSVersion.Version.Build < 22000)
            return;
        try
        {
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
                return;
            int bg = 0x00131414;   // app background #141413 as COLORREF 0x00BBGGRR
            int text = 0x00DCDCDF; // app text #dfdcdc as COLORREF
            DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref bg, sizeof(int));
            DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref bg, sizeof(int));
            DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref text, sizeof(int));
        }
        catch
        {
            // Cosmetic only — never break startup.
        }
    }
}

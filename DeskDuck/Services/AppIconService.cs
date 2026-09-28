using System.Drawing;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DeskDuck.Services;

/// <summary>
/// Extracts an app's exe icon once and caches it by path. Every failure
/// (exited process, access denied, no icon) resolves to null — callers show
/// text only rather than a wrong or generic glyph.
/// </summary>
public static class AppIconService
{
    private static readonly Dictionary<string, ImageSource> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Gate = new();

    public static ImageSource? GetIcon(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath))
            return null;
        lock (Gate)
        {
            if (Cache.TryGetValue(exePath, out var cached))
                return cached;
            var icon = Extract(exePath);
            if (icon is not null)
                Cache[exePath] = icon;
            return icon;
        }
    }

    private static ImageSource? Extract(string exePath)
    {
        try
        {
            // Icon owns its handle; disposing releases it (no separate DestroyIcon).
            using var icon = Icon.ExtractAssociatedIcon(exePath);
            if (icon is null)
                return null;
            var source = Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze(); // immutable: safe to share across rows/threads
            return source;
        }
        catch
        {
            return null;
        }
    }
}

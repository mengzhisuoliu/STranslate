using ScreenGrab;
using ScreenGrab.Extensions;
using STranslate.Helpers;
using System.Drawing;
using System.Windows;
using Windows.Win32;
using DrawingPoint = System.Drawing.Point;
using DrawingRectangle = System.Drawing.Rectangle;

namespace STranslate.Core;

public class Screenshot(Settings settings) : IScreenshot
{
    private const int DefaultCaptureDelayMs = 150;

    public Bitmap? GetScreenshot()
    {
        if (ScreenGrabber.IsCapturing)
            return default;
        var bitmap = ScreenGrabber.CaptureDialog(settings.ShowScreenshotAuxiliaryLines);
        if (bitmap == null)
            return default;
        return bitmap;
    }

    public async Task<Bitmap?> GetScreenshotAsync()
    {
        return await CaptureBitmapAsync();
    }

    public async Task<ScreenshotCaptureResult?> GetScreenshotCaptureAsync()
    {
        var bitmap = await CaptureBitmapAsync();
        if (bitmap == null)
            return default;

        var physicalBounds = ResolvePhysicalBounds(bitmap);
        return new ScreenshotCaptureResult(bitmap, physicalBounds);
    }

    private async Task<Bitmap?> CaptureBitmapAsync()
    {
        if (ScreenGrabber.IsCapturing)
            return default;

        if (App.Current.MainWindow.Visibility == Visibility.Visible &&
            !App.Current.MainWindow.Topmost)
            App.Current.MainWindow.Visibility = Visibility.Collapsed;

        // Allow UI to update before capturing
        await Task.Delay(DefaultCaptureDelayMs);

        var bitmap = await ScreenGrabber.CaptureAsync(settings.ShowScreenshotAuxiliaryLines);
        if (bitmap == null)
            return default;

        return bitmap;
    }

    private static DrawingRectangle? ResolvePhysicalBounds(Bitmap bitmap)
    {
        if (!PInvoke.GetCursorPos(out var cursorPoint))
            return null;

        var cursorPosition = new DrawingPoint(cursorPoint.X, cursorPoint.Y);
        var screenBounds = MonitorInfo.GetDisplayMonitors()
            .Select(monitor => new DrawingRectangle(
                (int)Math.Round(monitor.Bounds.X),
                (int)Math.Round(monitor.Bounds.Y),
                (int)Math.Round(monitor.Bounds.Width),
                (int)Math.Round(monitor.Bounds.Height)))
            .ToArray();

        return ScreenshotSelectionResolver.Resolve(bitmap, cursorPosition, screenBounds, CaptureRegion);
    }

    private static Bitmap? CaptureRegion(DrawingRectangle bounds)
    {
        try
        {
            return ImageExtensions.GetRegionOfScreenAsBitmap(bounds);
        }
        catch
        {
            return null;
        }
    }
}

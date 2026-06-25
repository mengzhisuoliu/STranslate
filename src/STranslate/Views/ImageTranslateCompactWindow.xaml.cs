using CommunityToolkit.Mvvm.DependencyInjection;
using STranslate.Core;
using STranslate.Helpers;
using STranslate.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using Windows.Win32;
using DrawingPoint = System.Drawing.Point;
using DrawingRectangle = System.Drawing.Rectangle;
using DrawingSize = System.Drawing.Size;

namespace STranslate.Views;

public partial class ImageTranslateCompactWindow
{
    private const double FallbackScreenWidthRatio = 0.85;
    private const double FallbackScreenHeightRatio = 0.85;
    private const double FallbackMinWidth = 320;
    private const double FallbackMinHeight = 180;
    private const double ToolbarReservedHeight = 64;

    private readonly ImageTranslateWindowViewModel _viewModel;
    private bool _isContextMenuOpen;
    private bool _isClosing;

    public ImageTranslateCompactWindow()
    {
        _viewModel = Ioc.Default.GetRequiredService<ImageTranslateWindowViewModel>();
        DataContext = _viewModel;

        InitializeComponent();
    }

    public void PlaceForCapture(DrawingRectangle? physicalBounds, DrawingSize bitmapSize)
    {
        if (physicalBounds is { Width: > 0, Height: > 0 } bounds)
        {
            PlaceOnPhysicalBounds(bounds);
            return;
        }

        PlaceNearCursorScreen(bitmapSize);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Win32Helper.HideFromAltTab(this);
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);

        if (_isContextMenuOpen || _isClosing)
            return;

        Dispatcher.BeginInvoke(() =>
        {
            if (!_isContextMenuOpen && !_isClosing && IsVisible && !IsActive)
                Close();
        }, DispatcherPriority.Background);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _isClosing = true;
        _viewModel.CancelOperations();
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.Dispose();
        base.OnClosed(e);
    }

    private void PlaceOnPhysicalBounds(DrawingRectangle bounds)
    {
        var topLeft = Win32Helper.TransformPixelsToDIP(this, bounds.Left, bounds.Top);
        var bottomRight = Win32Helper.TransformPixelsToDIP(this, bounds.Right, bounds.Bottom);

        Left = topLeft.X;
        Top = topLeft.Y;
        Width = Math.Max(MinWidth, bottomRight.X - topLeft.X);
        Height = Math.Max(MinHeight, bottomRight.Y - topLeft.Y) + ToolbarReservedHeight;
    }

    private void PlaceNearCursorScreen(DrawingSize bitmapSize)
    {
        if (!PInvoke.GetCursorPos(out var cursorPoint))
        {
            PlaceCenteredOnPrimaryScreen(bitmapSize);
            return;
        }

        var cursorPosition = new DrawingPoint(cursorPoint.X, cursorPoint.Y);
        var screenBounds = MonitorInfo.GetDisplayMonitors()
            .Select(monitor => new DrawingRectangle(
                (int)Math.Round(monitor.Bounds.X),
                (int)Math.Round(monitor.Bounds.Y),
                (int)Math.Round(monitor.Bounds.Width),
                (int)Math.Round(monitor.Bounds.Height)))
            .ToArray();

        var candidateBounds = ScreenshotSelectionResolver.CreateCandidateBounds(bitmapSize, cursorPosition)
            .FirstOrDefault(candidate => screenBounds.Length == 0 || screenBounds.Any(screen => screen.Contains(candidate)));

        if (candidateBounds is { Width: > 0, Height: > 0 })
        {
            PlaceOnPhysicalBounds(candidateBounds);
            return;
        }

        PlaceOnPhysicalBounds(new DrawingRectangle(
            cursorPosition.X - bitmapSize.Width,
            cursorPosition.Y - bitmapSize.Height,
            bitmapSize.Width,
            bitmapSize.Height));
    }

    private void PlaceCenteredOnPrimaryScreen(DrawingSize bitmapSize)
    {
        var screen = MonitorInfo.GetPrimaryDisplayMonitor();
        var workAreaTopLeft = Win32Helper.TransformPixelsToDIP(this, screen.WorkingArea.Left, screen.WorkingArea.Top);
        var workAreaBottomRight = Win32Helper.TransformPixelsToDIP(this, screen.WorkingArea.Right, screen.WorkingArea.Bottom);
        var workAreaWidth = Math.Max(1, workAreaBottomRight.X - workAreaTopLeft.X);
        var workAreaHeight = Math.Max(1, workAreaBottomRight.Y - workAreaTopLeft.Y);
        var bitmapSizeDip = Win32Helper.TransformPixelsToDIP(this, bitmapSize.Width, bitmapSize.Height);
        var maxImageHeight = Math.Max(1, workAreaHeight * FallbackScreenHeightRatio - ToolbarReservedHeight);

        Width = Math.Clamp(bitmapSizeDip.X, FallbackMinWidth, workAreaWidth * FallbackScreenWidthRatio);
        Height = Math.Clamp(bitmapSizeDip.Y, FallbackMinHeight, maxImageHeight) + ToolbarReservedHeight;
        Left = workAreaTopLeft.X + (workAreaWidth - Width) / 2;
        Top = workAreaTopLeft.Y + (workAreaHeight - Height) / 2;
    }

    private void OnImageContextMenuOpened(object sender, RoutedEventArgs e) => _isContextMenuOpen = true;

    private void OnImageContextMenuClosed(object sender, RoutedEventArgs e)
    {
        _isContextMenuOpen = false;
        Activate();
    }
}

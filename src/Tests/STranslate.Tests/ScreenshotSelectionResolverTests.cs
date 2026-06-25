using STranslate.Core;
using System.Drawing;
using System.Drawing.Imaging;

namespace STranslate.Tests;

public class ScreenshotSelectionResolverTests
{
    [Fact]
    public void ResolveMatchesBottomRightDragCandidate()
    {
        using var bitmap = CreatePatternBitmap(20, 10);
        var expected = new Rectangle(100, 60, 20, 10);

        var actual = ScreenshotSelectionResolver.Resolve(
            bitmap,
            new Point(120, 70),
            [new Rectangle(0, 0, 300, 200)],
            CaptureMatchingRegion(bitmap, expected));

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ResolveMatchesReverseDragCandidate()
    {
        using var bitmap = CreatePatternBitmap(20, 10);
        var expected = new Rectangle(100, 60, 20, 10);

        var actual = ScreenshotSelectionResolver.Resolve(
            bitmap,
            new Point(100, 60),
            [new Rectangle(0, 0, 300, 200)],
            CaptureMatchingRegion(bitmap, expected));

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ResolveSupportsNegativeScreenCoordinates()
    {
        using var bitmap = CreatePatternBitmap(30, 20);
        var expected = new Rectangle(-80, 40, 30, 20);

        var actual = ScreenshotSelectionResolver.Resolve(
            bitmap,
            new Point(-50, 60),
            [new Rectangle(-200, 0, 200, 200)],
            CaptureMatchingRegion(bitmap, expected));

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ResolveReturnsNullWhenNoCandidateMatches()
    {
        using var bitmap = CreatePatternBitmap(20, 10);

        var actual = ScreenshotSelectionResolver.Resolve(
            bitmap,
            new Point(120, 70),
            [new Rectangle(0, 0, 300, 200)],
            _ => CreateMismatchBitmap(20, 10));

        Assert.Null(actual);
    }

    private static Func<Rectangle, Bitmap?> CaptureMatchingRegion(Bitmap bitmap, Rectangle expected)
    {
        return candidate => candidate == expected
            ? CloneBitmap(bitmap)
            : CreateMismatchBitmap(bitmap.Width, bitmap.Height);
    }

    private static Bitmap CreatePatternBitmap(int width, int height)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                bitmap.SetPixel(x, y, Color.FromArgb(255, (x * 17) % 256, (y * 23) % 256, (x + y * 3) % 256));
            }
        }

        return bitmap;
    }

    private static Bitmap CreateMismatchBitmap(int width, int height)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Magenta);
        return bitmap;
    }

    private static Bitmap CloneBitmap(Bitmap bitmap) =>
        bitmap.Clone(new Rectangle(0, 0, bitmap.Width, bitmap.Height), PixelFormat.Format32bppArgb);
}

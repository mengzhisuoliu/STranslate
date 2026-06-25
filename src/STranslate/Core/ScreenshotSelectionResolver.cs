using System.Drawing;

namespace STranslate.Core;

internal static class ScreenshotSelectionResolver
{
    private const int MaxSampleAxisCount = 32;
    private const int EdgeTolerance = 1;
    private const double MatchThreshold = 0.995;

    internal static Rectangle? Resolve(
        Bitmap capturedBitmap,
        Point cursorPosition,
        IReadOnlyCollection<Rectangle> screenBounds,
        Func<Rectangle, Bitmap?> captureRegion)
    {
        ArgumentNullException.ThrowIfNull(capturedBitmap);
        ArgumentNullException.ThrowIfNull(captureRegion);

        if (capturedBitmap.Width <= 0 || capturedBitmap.Height <= 0)
            return null;

        foreach (var candidate in CreateCandidateBounds(capturedBitmap.Size, cursorPosition))
        {
            if (!IsInsideAnyScreen(candidate, screenBounds))
                continue;

            using var candidateBitmap = captureRegion(candidate);
            if (candidateBitmap == null)
                continue;

            if (IsMatch(capturedBitmap, candidateBitmap))
                return candidate;
        }

        return null;
    }

    internal static IReadOnlyList<Rectangle> CreateCandidateBounds(Size bitmapSize, Point cursorPosition)
    {
        if (bitmapSize.Width <= 0 || bitmapSize.Height <= 0)
            return [];

        var candidates = new List<Rectangle>();
        var seen = new HashSet<Rectangle>();

        foreach (var offsetX in CreateEdgeOffsets())
        {
            foreach (var offsetY in CreateEdgeOffsets())
            {
                AddCandidate(cursorPosition.X - bitmapSize.Width + offsetX, cursorPosition.Y - bitmapSize.Height + offsetY);
                AddCandidate(cursorPosition.X + offsetX, cursorPosition.Y - bitmapSize.Height + offsetY);
                AddCandidate(cursorPosition.X - bitmapSize.Width + offsetX, cursorPosition.Y + offsetY);
                AddCandidate(cursorPosition.X + offsetX, cursorPosition.Y + offsetY);
            }
        }

        return candidates;

        void AddCandidate(int left, int top)
        {
            var candidate = new Rectangle(left, top, bitmapSize.Width, bitmapSize.Height);
            if (seen.Add(candidate))
                candidates.Add(candidate);
        }
    }

    private static IEnumerable<int> CreateEdgeOffsets()
    {
        yield return 0;
        for (var offset = 1; offset <= EdgeTolerance; offset++)
        {
            yield return offset;
            yield return -offset;
        }
    }

    private static bool IsInsideAnyScreen(Rectangle candidate, IReadOnlyCollection<Rectangle> screenBounds)
    {
        if (screenBounds.Count == 0)
            return true;

        return screenBounds.Any(screen => screen.Contains(candidate));
    }

    private static bool IsMatch(Bitmap capturedBitmap, Bitmap candidateBitmap)
    {
        if (capturedBitmap.Width != candidateBitmap.Width ||
            capturedBitmap.Height != candidateBitmap.Height)
            return false;

        var xSamples = CreateSamplePositions(capturedBitmap.Width);
        var ySamples = CreateSamplePositions(capturedBitmap.Height);
        var total = 0;
        var matched = 0;

        foreach (var y in ySamples)
        {
            foreach (var x in xSamples)
            {
                total++;
                if (IsColorClose(capturedBitmap.GetPixel(x, y), candidateBitmap.GetPixel(x, y)))
                    matched++;
            }
        }

        return total > 0 && matched / (double)total >= MatchThreshold;
    }

    private static int[] CreateSamplePositions(int length)
    {
        if (length <= 1)
            return [0];

        var count = Math.Min(length, MaxSampleAxisCount);
        var positions = new int[count];
        for (var i = 0; i < count; i++)
        {
            positions[i] = (int)Math.Round(i * (length - 1) / (double)(count - 1));
        }

        return positions;
    }

    private static bool IsColorClose(Color first, Color second)
    {
        const int tolerance = 2;
        return Math.Abs(first.A - second.A) <= tolerance &&
               Math.Abs(first.R - second.R) <= tolerance &&
               Math.Abs(first.G - second.G) <= tolerance &&
               Math.Abs(first.B - second.B) <= tolerance;
    }
}

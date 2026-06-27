using STranslate.Core;
using STranslate.Helpers;
using STranslate.Plugin;
using STranslate.ViewModels;
using System.Windows;
using System.Windows.Media;

namespace STranslate.Tests;

public class ImageTranslateTextOverlayLayoutTests
{
    [Fact]
    public void MultilineShortTranslationUsesRegionFillFontSize()
    {
        var block = Block(
            Box(0, 0, 1000, 150),
            Box(0, 0, 900, 30),
            Box(0, 36, 900, 30),
            Box(0, 72, 900, 30));

        var plan = CreatePlan(
            block,
            new Rect(0, 0, 1000, 150),
            (fontSize, _) => new Size(
                fontSize * 4,
                fontSize * ImageTranslateTextOverlayPlan.MultilineLineHeightScale));

        Assert.True(plan.IsMultiLine);
        Assert.False(plan.ShouldTrim);
        Assert.True(plan.FontSize > 30 * 0.90);
        Assert.Equal(ImageTranslateTextOverlayPlan.MaxFontSize, plan.FontSize);
    }

    [Fact]
    public void MultilineLongTranslationShrinksToFitRegion()
    {
        const double textUnits = 90;
        var block = Block(
            Box(0, 0, 1000, 150),
            Box(0, 0, 900, 30),
            Box(0, 36, 900, 30),
            Box(0, 72, 900, 30));

        var plan = CreatePlan(
            block,
            new Rect(0, 0, 1000, 150),
            (fontSize, textRect) => MeasureWrappedText(fontSize, textRect, textUnits));

        var measured = MeasureWrappedText(plan.FontSize, plan.TextRect, textUnits);

        Assert.True(plan.IsMultiLine);
        Assert.False(plan.ShouldTrim);
        Assert.True(plan.FontSize > 30 * 0.90);
        Assert.True(plan.FontSize < ImageTranslateTextOverlayPlan.MaxFontSize);
        Assert.True(measured.Height <= plan.TextRect.Height + 0.1);
    }

    [Fact]
    public void MultilineUsesParagraphAndLineBoundsUnionForRegionFill()
    {
        const double textUnits = 600;
        var paragraphRect = new Rect(0, 0, 1000, 80);
        var block = Block(
            Box(0, 0, 1000, 80),
            Box(0, 0, 900, 30),
            Box(0, 50, 900, 30),
            Box(0, 100, 900, 30),
            Box(0, 150, 900, 30),
            Box(0, 200, 900, 30));

        var plan = CreatePlan(
            block,
            paragraphRect,
            (fontSize, textRect) => MeasureWrappedText(fontSize, textRect, textUnits));
        var measured = MeasureWrappedText(plan.FontSize, plan.TextRect, textUnits);

        Assert.Equal(230, plan.BoundingRect.Height, precision: 3);
        Assert.True(plan.TextRect.Height > paragraphRect.Height * 2);
        Assert.True(plan.FontSize > 15);
        Assert.False(plan.ShouldTrim);
        Assert.True(measured.Height <= plan.TextRect.Height + 0.1);
    }

    [Fact]
    public void MultilineUsesRemainingRegionHeightForLineSpacing()
    {
        var block = Block(
            Box(0, 0, 1000, 240),
            Box(0, 0, 900, 30),
            Box(0, 50, 900, 30),
            Box(0, 100, 900, 30),
            Box(0, 150, 900, 30),
            Box(0, 200, 900, 30));

        var plan = CreatePlan(
            block,
            new Rect(0, 0, 1000, 240),
            (fontSize, textRect) =>
            {
                var lineCount = fontSize <= 22 ? 7 : 12;
                return new Size(textRect.Width, lineCount * fontSize * ImageTranslateTextOverlayPlan.MultilineLineHeightScale);
            });

        Assert.True(plan.LineHeight > plan.FontSize * ImageTranslateTextOverlayPlan.MultilineLineHeightScale);
        Assert.True(plan.LineHeight * 7 >= plan.TextRect.Height * 0.90);
        Assert.True(plan.LineHeight <= plan.FontSize * 2);
    }

    [Fact]
    public void MultilinePrioritizesLargerFontWithCompactFitLineHeight()
    {
        const double textUnits = 600;
        var block = Block(
            Box(0, 0, 1000, 240),
            Box(0, 0, 900, 30),
            Box(0, 50, 900, 30),
            Box(0, 100, 900, 30),
            Box(0, 150, 900, 30),
            Box(0, 200, 900, 30));

        var plan = CreatePlan(
            block,
            new Rect(0, 0, 1000, 240),
            (fontSize, textRect) => MeasureWrappedText(fontSize, textRect, textUnits));
        var measured = MeasureWrappedText(plan.FontSize, plan.TextRect, textUnits);

        Assert.True(plan.FontSize > 18);
        Assert.True(measured.Height >= plan.TextRect.Height * 0.90);
        Assert.True(measured.Height <= plan.TextRect.Height + 0.1);
    }

    [Fact]
    public void SingleLineTitleFontSizeUsesExpandedRegionHeight()
    {
        var block = Block(
            Box(0, 0, 400, 80),
            Box(0, 0, 380, 50));

        var plan = CreatePlan(block, new Rect(0, 0, 400, 80), (_, _) => new Size(20, 20));

        Assert.False(plan.IsMultiLine);
        Assert.Equal(plan.TextClipRect.Height * 1.2, plan.FontSize, precision: 3);
    }

    [Fact]
    public void EraseRectsExpandLineBoxesAndUseAdaptiveOverlayBackground()
    {
        var block = Block(
            Box(0, 0, 200, 60),
            Box(10, 20, 100, 20));

        var plan = CreatePlan(block, new Rect(0, 0, 200, 60), (_, _) => new Size(10, 10));

        Assert.Equal(Color.FromArgb(235, 255, 255, 255), plan.OverlayBackgroundColor);
        Assert.Equal(Colors.Black, plan.ForegroundColor);
        Assert.Single(plan.EraseRects);
        Assert.Equal(8, plan.EraseRects[0].Left, precision: 3);
        Assert.Equal(16.4, plan.EraseRects[0].Top, precision: 3);
        Assert.Equal(104, plan.EraseRects[0].Width, precision: 3);
        Assert.Equal(27.2, plan.EraseRects[0].Height, precision: 3);
    }

    [Fact]
    public void SingleLineTextClipRectCoversExpandedEraseRect()
    {
        var block = Block(
            Box(10, 20, 100, 20),
            Box(10, 20, 100, 20));

        var plan = CreatePlan(block, new Rect(10, 20, 100, 20), (_, _) => new Size(10, 10));

        Assert.Equal(8, plan.TextClipRect.Left, precision: 3);
        Assert.Equal(16.4, plan.TextClipRect.Top, precision: 3);
        Assert.Equal(104, plan.TextClipRect.Width, precision: 3);
        Assert.Equal(27.2, plan.TextClipRect.Height, precision: 3);
        AssertCovers(plan.TextClipRect, plan.BoundingRect);
        AssertCovers(plan.TextClipRect, plan.EraseRects[0]);
        AssertCovers(plan.OverlayRect, plan.TextClipRect);
    }

    [Fact]
    public void SingleLineUsesNaturalTextHeightAndExpandedFitHeight()
    {
        var measuredRects = new List<Rect>();
        var block = Block(
            Box(10, 20, 100, 20),
            Box(10, 20, 100, 20));

        var plan = ImageTranslateTextOverlayLayout.Create(
            block,
            new Rect(10, 20, 100, 20),
            (_, textRect, _) =>
            {
                measuredRects.Add(textRect);
                return new Size(10, 10);
            });

        Assert.Equal(0, plan.LineHeight);
        Assert.Equal(1, plan.MaxLineCount);
        Assert.True(double.IsPositiveInfinity(plan.MaxTextHeight));
        Assert.All(measuredRects, rect => Assert.Equal(plan.TextClipRect.Height, rect.Height, precision: 3));
    }

    [Fact]
    public void SingleLineLongTranslationExpandsToWrappedTextBeforeTrimming()
    {
        var measuredModes = new List<bool>();
        var block = Block(
            Box(10, 100, 200, 24),
            Box(10, 100, 200, 24));

        var plan = ImageTranslateTextOverlayLayout.Create(
            block,
            new Rect(10, 100, 200, 24),
            (fontSize, textRect, isMultiLine) =>
            {
                measuredModes.Add(isMultiLine);
                return isMultiLine
                    ? MeasureWrappedText(fontSize, textRect, textUnits: 35)
                    : new Size(textRect.Width + 1, textRect.Height + 1);
            });

        Assert.Contains(false, measuredModes);
        Assert.Contains(true, measuredModes);
        Assert.True(plan.IsMultiLine);
        Assert.False(plan.ShouldTrim);
        Assert.Equal(0, plan.MaxLineCount);
        Assert.Equal(plan.TextRect.Height, plan.MaxTextHeight);
        Assert.True(plan.TextClipRect.Height > plan.BoundingRect.Height * 2);
        AssertCovers(plan.OverlayRect, plan.EraseRects[0]);
    }

    [Fact]
    public void ViewModelSingleLineMeasurementUsesNaturalWidth()
    {
        const string text = "My self media video lighting shooting tips are now publicly available";
        const double maxWidth = 260;

        var measured = ImageTranslateRenderer.MeasureFormattedText(
            text,
            fontSize: 36,
            maxWidth,
            Brushes.Black,
            pixelsPerDip: 1,
            lineHeight: 0,
            maxLineCount: 1);

        Assert.True(measured.Width > maxWidth);
    }

    [Fact]
    public void RealSingleLineLongTranslationShrinksInsteadOfTrimming()
    {
        var block = new OcrLayoutBlock
        {
            Text = "My self media video lighting shooting tips are now publicly available in less than 4 square meters of shooting space",
            BoxPoints = Box(10, 100, 960, 42),
            LineBoxPoints = [Box(10, 100, 960, 42)]
        };

        var plan = ImageTranslateTextOverlayLayout.Create(
            block,
            new Rect(10, 100, 960, 42),
            (fontSize, textRect, isMultiLine) => ImageTranslateRenderer.MeasureFormattedText(
                block.Text,
                fontSize,
                textRect.Width,
                Brushes.Black,
                pixelsPerDip: 1,
                lineHeight: isMultiLine
                    ? fontSize * ImageTranslateTextOverlayPlan.MultilineLineHeightScale
                    : 0,
                maxLineCount: isMultiLine ? 0 : 1));

        Assert.False(plan.ShouldTrim);
        Assert.True(plan.FontSize < plan.TextClipRect.Height * 1.2);
    }

    [Fact]
    public void MultilineTextClipRectCoversAllExpandedEraseRects()
    {
        var block = Block(
            Box(10, 20, 180, 46),
            Box(10, 20, 180, 20),
            Box(10, 46, 180, 20));

        var plan = CreatePlan(block, new Rect(10, 20, 180, 46), (_, _) => new Size(10, 10));

        AssertCovers(plan.TextClipRect, plan.BoundingRect);
        Assert.All(plan.EraseRects, eraseRect => AssertCovers(plan.TextClipRect, eraseRect));
        Assert.All(plan.EraseRects, eraseRect => AssertCovers(plan.OverlayRect, eraseRect));
        Assert.True(plan.TextClipRect.Top < plan.BoundingRect.Top);
        Assert.True(plan.TextClipRect.Bottom > plan.BoundingRect.Bottom);
    }

    [Fact]
    public void MultilineKeepsExplicitLineHeightAndTextHeightLimit()
    {
        var block = Block(
            Box(10, 20, 180, 46),
            Box(10, 20, 180, 20),
            Box(10, 46, 180, 20));

        var plan = CreatePlan(block, new Rect(10, 20, 180, 46), (_, _) => new Size(10, 10));

        Assert.True(plan.LineHeight > 0);
        Assert.Equal(0, plan.MaxLineCount);
        Assert.Equal(plan.TextRect.Height, plan.MaxTextHeight);
    }

    [Fact]
    public void CreatePassesLineModeToMeasureText()
    {
        var singleLineModes = new List<bool>();
        var multilineModes = new List<bool>();
        var singleLineBlock = Block(
            Box(0, 0, 100, 20),
            Box(0, 0, 100, 20));
        var multilineBlock = Block(
            Box(0, 0, 100, 46),
            Box(0, 0, 100, 20),
            Box(0, 26, 100, 20));

        ImageTranslateTextOverlayLayout.Create(
            singleLineBlock,
            new Rect(0, 0, 100, 20),
            (_, _, isMultiLine) =>
            {
                singleLineModes.Add(isMultiLine);
                return new Size(10, 10);
            });
        ImageTranslateTextOverlayLayout.Create(
            multilineBlock,
            new Rect(0, 0, 100, 46),
            (_, _, isMultiLine) =>
            {
                multilineModes.Add(isMultiLine);
                return new Size(10, 10);
            });

        Assert.All(singleLineModes, mode => Assert.False(mode));
        Assert.All(multilineModes, mode => Assert.True(mode));
    }

    [Fact]
    public void TextRectStaysInsideParagraphBoundsWithPadding()
    {
        var block = Block(
            Box(10, 20, 200, 100),
            Box(10, 20, 180, 20),
            Box(10, 46, 180, 20));

        var plan = CreatePlan(block, new Rect(10, 20, 200, 100), (_, _) => new Size(10, 10));

        Assert.Equal(11, plan.TextRect.Left);
        Assert.Equal(21, plan.TextRect.Top);
        Assert.Equal(209, plan.TextRect.Right);
        Assert.Equal(119, plan.TextRect.Bottom);
    }

    [Fact]
    public void OversizedTextAtMinimumFontSizeIsMarkedForTrimming()
    {
        var block = Block(
            Box(0, 0, 80, 20),
            Box(0, 0, 80, 20));

        var plan = CreatePlan(block, new Rect(0, 0, 80, 20), (_, textRect) => new Size(textRect.Width + 1, textRect.Height + 1));

        Assert.True(plan.ShouldTrim);
        Assert.Equal(ImageTranslateTextOverlayPlan.MinFontSize, plan.FontSize);
    }

    [Fact]
    public void MissingLineBoxesFallsBackToParagraphBoxForErase()
    {
        var block = Block(Box(5, 6, 70, 30));
        var boundingRect = new Rect(5, 6, 70, 30);

        var plan = CreatePlan(block, boundingRect, (_, _) => new Size(10, 10));

        Assert.False(plan.IsMultiLine);
        Assert.Equal(boundingRect, plan.EraseRects[0]);
        Assert.Equal(boundingRect, plan.TextClipRect);
        Assert.Equal(boundingRect, plan.OverlayRect);
        Assert.Equal(boundingRect.Height * 1.2, plan.FontSize, precision: 3);
    }

    [Fact]
    public void LightThemeUsesLightOverlayAndBlackText()
    {
        var block = Block(
            Box(0, 0, 200, 40),
            Box(0, 0, 180, 20));

        var plan = ImageTranslateTextOverlayLayout.Create(
            block,
            new Rect(0, 0, 200, 40),
            (_, _, _) => new Size(10, 10),
            ImageTranslateOverlayTheme.Light);

        Assert.Equal(Color.FromArgb(235, 255, 255, 255), plan.OverlayBackgroundColor);
        Assert.Equal(Colors.Black, plan.ForegroundColor);
    }

    [Fact]
    public void DarkThemeUsesDarkOverlayAndWhiteText()
    {
        var block = Block(
            Box(0, 0, 200, 40),
            Box(0, 0, 180, 20));

        var plan = ImageTranslateTextOverlayLayout.Create(
            block,
            new Rect(0, 0, 200, 40),
            (_, _, _) => new Size(10, 10),
            ImageTranslateOverlayTheme.Dark);

        Assert.Equal(Color.FromArgb(230, 0, 0, 0), plan.OverlayBackgroundColor);
        Assert.Equal(Colors.White, plan.ForegroundColor);
    }

    [Fact]
    public void OverlayRectCoversEraseRectsAndTextClip()
    {
        var block = Block(
            Box(10, 20, 180, 46),
            Box(10, 20, 180, 20),
            Box(10, 46, 180, 20));

        var plan = CreatePlan(block, new Rect(10, 20, 180, 46), (_, _) => new Size(10, 10));

        AssertCovers(plan.OverlayRect, plan.TextClipRect);
        Assert.All(plan.EraseRects, eraseRect => AssertCovers(plan.OverlayRect, eraseRect));
    }

    [Theory]
    [InlineData("\r\n为快速开始，请选择以下任一安装方法：\r\n", "为快速开始，请选择以下任一安装方法：")]
    [InlineData("前往PowerToys的GitHub版本页面，\n  向下滚动并选择 Assets", "前往PowerToys的GitHub版本页面，向下滚动并选择Assets")]
    [InlineData("hello\nworld", "hello world")]
    [InlineData("  \t\n  ", "")]
    public void NormalizeOverlayTextRemovesLeadingLineBreaksAndCollapsesWhitespace(string input, string expected)
    {
        var result = ImageTranslateTextOverlayLayout.NormalizeOverlayText(input);

        Assert.Equal(expected, result);
    }

    private static ImageTranslateTextOverlayPlan CreatePlan(
        OcrLayoutBlock block,
        Rect boundingRect,
        Func<double, Rect, Size> measureText) =>
        ImageTranslateTextOverlayLayout.Create(
            block,
            boundingRect,
            (fontSize, textRect, _) => measureText(fontSize, textRect),
            ImageTranslateOverlayTheme.Light);

    private static void AssertCovers(Rect outer, Rect inner)
    {
        Assert.True(outer.Left <= inner.Left);
        Assert.True(outer.Top <= inner.Top);
        Assert.True(outer.Right >= inner.Right);
        Assert.True(outer.Bottom >= inner.Bottom);
    }

    private static Size MeasureWrappedText(double fontSize, Rect textRect, double textUnits)
    {
        var lineCount = Math.Max(1, (int)Math.Ceiling(textUnits * fontSize / textRect.Width));
        return new Size(
            textRect.Width,
            lineCount * fontSize * ImageTranslateTextOverlayPlan.MultilineLineHeightScale);
    }

    private static OcrLayoutBlock Block(List<BoxPoint> boxPoints, params List<BoxPoint>[] lineBoxPoints) =>
        new()
        {
            Text = "translated text",
            BoxPoints = boxPoints,
            LineBoxPoints = [.. lineBoxPoints]
        };

    private static List<BoxPoint> Box(double left, double top, double width, double height) =>
    [
        new((float)left, (float)top),
        new((float)(left + width), (float)top),
        new((float)(left + width), (float)(top + height)),
        new((float)left, (float)(top + height))
    ];
}

/// <summary>
/// 超采样缩放因子与像素预算计算的单测。
/// </summary>
public class ImageTranslateRendererSupersampleTests
{
    /// <summary>
    /// 小图（最小边 &lt; 1000）应放大到至少 MinScale(2.0)，
    /// 且不超过 MaxScale(4.0)。
    /// </summary>
    [Fact]
    public void SmallImage_AmplifiesToAtLeastMinScale()
    {
        // 200x150，最小边 150，理论放大 1000/150≈6.67，应被 MaxScale 钳到 4.0
        var (scale, w, h) = ImageTranslateRenderer.ComputeSupersampleScale(200, 150);
        Assert.Equal(4.0, scale, precision: 2);
        Assert.Equal(800, w);
        Assert.Equal(600, h);
    }

    /// <summary>
    /// 大图（最小边 ≥ 1000）不放大，scaleFactor 保持 1.0。
    /// </summary>
    [Fact]
    public void LargeImage_DoesNotAmplify()
    {
        var (scale, w, h) = ImageTranslateRenderer.ComputeSupersampleScale(1920, 1080);
        Assert.Equal(1.0, scale, precision: 2);
        Assert.Equal(1920, w);
        Assert.Equal(1080, h);
    }

    /// <summary>
    /// 超采样后总像素超过预算(8MP)时，应按面积比例降低 scaleFactor，
    /// 使结果图总像素不超过预算。
    /// </summary>
    [Fact]
    public void OversizedAmplification_CappedByPixelBudget()
    {
        // 构造一个会触发预算保护的场景：
        // 500x500 最小边 500 → 放大 1000/500=2.0（恰为 MinScale）→ 1000x1000=1MP，未超预算。
        // 要触发预算，需更大的小图放大后超 8MP：
        // 1500x1500 最小边 1500 ≥1000 不放大 → 不触发。
        // 改用 700x700：最小边 700 <1000 → 放大 1000/700≈1.43，但会被 MinScale 抬到 2.0
        //   → 1400x1400=1.96MP，仍未超。
        // 真正触发需要中等尺寸 + 高 MaxScale：900x900 最小边 900<1000 → 1000/900≈1.11，
        //   MinScale 抬到 2.0 → 1800x1800=3.24MP，未超。
        // 直接验证边界：预算为 8MP，构造放大后恰超预算的输入。
        // 用 2048x2048 不放大不触发；要让放大后超 8MP，需原图较小且放大后大：
        //   1024x1024 最小边 1024≥1000 不放大 → 不触发。
        // 结论：现有 [MinScale=2, MaxScale=4] + MinDimension=1000 的组合下，
        //   放大后最大像素 = 1000*4 × 1000*4 = 4000x4000 = 16MP（当原图最小边接近 1000 时
        //   不会被放大；最小边很小时放大后仍小）。真正会超 8MP 的是原图最小边在
        //   (200, 1000) 且放大后超 8MP 的区间。例如 500x500 放大 2x → 1000x1000=1MP 不超。
        //   要稳定触发预算，需原图约 700x3500 这种极端纵横比放大后超预算。
        // 这里用一个明确会超预算的构造：原图 600x6000，最小边 600<1000 → 放大 1000/600≈1.67
        //   被 MinScale 抬到 2.0 → 1200x12000=14.4MP > 8MP → 触发预算降采样。
        var (scale, w, h) = ImageTranslateRenderer.ComputeSupersampleScale(600, 6000);
        long totalPixels = (long)w * h;
        const long budget = 8L * 1024 * 1024;
        Assert.True(totalPixels <= budget,
            $"放大后总像素 {totalPixels} 应不超过预算 {budget}，实际 scale={scale} {w}x{h}");
        // 降采样后 scaleFactor 应低于原始放大因子 2.0
        Assert.True(scale < 2.0, $"应已降采样，scale={scale} 应低于 2.0");
    }

    /// <summary>
    /// 常规截图（最小边略低于阈值）会放大但不触发预算降采样，
    /// 因为放大后总像素仍在 8MP 预算内。
    /// </summary>
    [Fact]
    public void NormalScreenshot_AmplifiesButStaysWithinBudget()
    {
        // 1920x900 最小边 900<1000 → 放大 1000/900≈1.11 被 MinScale 抬到 2.0
        //   → 3840x1800=6.9MP < 8MP，不触发预算降采样
        var (scale, w, h) = ImageTranslateRenderer.ComputeSupersampleScale(1920, 900);
        Assert.Equal(2.0, scale, precision: 2);
        Assert.Equal(3840, w);
        Assert.Equal(1800, h);
        long totalPixels = (long)w * h;
        Assert.True(totalPixels <= 8L * 1024 * 1024);
    }
}

using STranslate.Core;
using STranslate.Plugin;
using System.Windows;

namespace STranslate.Tests;

public class ImageTranslateTextOverlayLayoutTests
{
    [Fact]
    public void MultilineParagraphFontSizeIsLimitedByMedianLineHeight()
    {
        var block = Block(
            Box(0, 0, 1000, 150),
            Box(0, 0, 900, 30),
            Box(0, 36, 900, 30),
            Box(0, 72, 900, 30));

        var plan = CreatePlan(block, new Rect(0, 0, 1000, 150), (_, _) => new Size(20, 20));

        Assert.True(plan.IsMultiLine);
        Assert.Equal(30 * 0.90, plan.FontSize, precision: 3);
    }

    [Fact]
    public void SingleLineTitleFontSizeUsesSingleLineLimitAndGlobalMaximum()
    {
        var block = Block(
            Box(0, 0, 400, 80),
            Box(0, 0, 380, 50));

        var plan = CreatePlan(block, new Rect(0, 0, 400, 80), (_, _) => new Size(20, 20));

        Assert.False(plan.IsMultiLine);
        Assert.Equal(ImageTranslateTextOverlayPlan.MaxFontSize, plan.FontSize);
    }

    [Fact]
    public void EraseRectsExpandLineBoxesAndUseOpaqueBackground()
    {
        var block = Block(
            Box(0, 0, 200, 60),
            Box(10, 20, 100, 20));

        var plan = CreatePlan(block, new Rect(0, 0, 200, 60), (_, _) => new Size(10, 10));

        Assert.Equal(255, plan.BackgroundColor.A);
        Assert.Single(plan.EraseRects);
        Assert.Equal(8, plan.EraseRects[0].Left, precision: 3);
        Assert.Equal(16.4, plan.EraseRects[0].Top, precision: 3);
        Assert.Equal(104, plan.EraseRects[0].Width, precision: 3);
        Assert.Equal(27.2, plan.EraseRects[0].Height, precision: 3);
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
        Assert.Equal(30 * 0.96, plan.FontSize, precision: 3);
    }

    private static ImageTranslateTextOverlayPlan CreatePlan(
        OcrLayoutBlock block,
        Rect boundingRect,
        Func<double, Rect, Size> measureText) =>
        ImageTranslateTextOverlayLayout.Create(block, boundingRect, measureText);

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

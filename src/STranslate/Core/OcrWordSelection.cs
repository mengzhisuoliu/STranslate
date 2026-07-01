namespace STranslate.Core;

internal static class OcrWordSelection
{
    internal static bool TryGetVisualLineRange(
        IReadOnlyList<OcrWord>? words,
        OcrWord? anchorWord,
        out int startIndex,
        out int endIndex)
    {
        startIndex = 0;
        endIndex = -1;
        if (words == null || anchorWord == null || anchorWord.VisualLineIndex < 0)
            return false;

        var hasLineWord = false;
        foreach (var word in words)
        {
            if (word.VisualLineIndex != anchorWord.VisualLineIndex || string.IsNullOrEmpty(word.Text))
                continue;

            var wordEndIndex = word.EndIndexInFullText - 1;
            if (!hasLineWord)
            {
                startIndex = word.StartIndexInFullText;
                endIndex = wordEndIndex;
                hasLineWord = true;
                continue;
            }

            startIndex = Math.Min(startIndex, word.StartIndexInFullText);
            endIndex = Math.Max(endIndex, wordEndIndex);
        }

        return hasLineWord;
    }
}

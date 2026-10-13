using Windows.System;

namespace TextTemplateManager.Helpers;

/// <summary>Keyboard rules shared by the main window and Quick Paste: a search box above a tree.</summary>
public static class SearchNavigation
{
    public enum SearchDown { MoveCaretToEnd, EnterTree }

    /// <summary>What Down does from the search box: step into the tree, or just move the caret to the
    /// end. It enters the tree only from a collapsed caret already at the end (an empty box counts as
    /// at-the-end) and only when the tree has rows; a selection or a mid-string caret collapses to the
    /// end first, and an empty tree keeps the caret in the box.</summary>
    public static SearchDown DecideSearchDown(int caret, int selectionLength, int textLength, int treeNodeCount)
    {
        bool atEnd = selectionLength == 0 && caret >= textLength;
        return atEnd && treeNodeCount > 0 ? SearchDown.EnterTree : SearchDown.MoveCaretToEnd;
    }

    /// <summary>The next stop of a fixed Tab cycle of <paramref name="count"/> stops (Shift+Tab: the previous one),
    /// wrapping at both ends.</summary>
    public static int NextInCycle(int current, int count, bool back) => (current + (back ? count - 1 : 1)) % count;

    /// <summary>The character a letter or digit key types (lower case), for single-key shortcuts and for
    /// typing from the tree into the search box.</summary>
    public static bool TryGetCharKey(VirtualKey key, out char c)
    {
        c = key switch
        {
            >= VirtualKey.A and <= VirtualKey.Z => (char)('a' + (key - VirtualKey.A)),
            >= VirtualKey.Number0 and <= VirtualKey.Number9 => (char)('0' + (key - VirtualKey.Number0)),
            >= VirtualKey.NumberPad0 and <= VirtualKey.NumberPad9 => (char)('0' + (key - VirtualKey.NumberPad0)),
            _ => '\0',
        };
        return c != '\0';
    }
}

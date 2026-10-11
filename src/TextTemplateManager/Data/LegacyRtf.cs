using System;
using TextTemplateManager.Models;

namespace TextTemplateManager.Data;

/// <summary>Templates from early versions stored RTF. They are converted to HTML once, as a file is read,
/// so everything after the load (editor, preview, paste, connector) only ever sees HTML.</summary>
public static class LegacyRtf
{
    /// <summary>Converts every RTF template in the tree. LastChange stays, so the conversion alone is no
    /// edit that sync would pass on. Content that can't be converted is kept as it is.</summary>
    public static T? ConvertTree<T>(T? root) where T : class
    {
        if (root is not BaseItem item) return root;
        foreach (var each in item.SelfAndDescendants())
        {
            if (each is Template { Content: { } content } t && IsRtf(content))
            {
                try { t.Content = RtfPipe.Rtf.ToHtml(content.Replace("\0", "")); }
                catch { }
            }
        }
        return root;
    }

    public static bool IsRtf(string content) =>
        content.TrimStart().StartsWith(@"{\rtf1", StringComparison.OrdinalIgnoreCase);
}

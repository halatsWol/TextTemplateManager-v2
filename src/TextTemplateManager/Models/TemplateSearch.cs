using System;
using System.Collections.Generic;
using System.Linq;
using TextTemplateManager.Helpers;

namespace TextTemplateManager.Models;

/// <summary>The search both windows use: every word of the query must appear in the item's title or tags
/// (optionally its text), its letters in order but not necessarily next to each other.</summary>
public static class TemplateSearch
{
    public static bool Matches(string query, BaseItem item, bool includeContent = false)
    {
        string text = item.Title;
        if (item is Template t)
        {
            text += $" {t.TagsCsv}";
            if (includeContent) text += $" {HtmlUtils.ToPlainText(t.Content)}";
        }
        return Matches(query, text);
    }

    public static bool Matches(string query, string text)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var lower = text.ToLowerInvariant();
        return query.ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .All(word => ContainsInOrder(lower, word));
    }

    /// <summary>The tree cut down to the matches: an item stays when it matches or something below it does,
    /// and keeps only those children. <paramref name="node"/> builds a window's node from an item and its
    /// remaining children's nodes.</summary>
    public static List<TNode> Prune<TNode>(IEnumerable<BaseItem> items, string query, bool includeContent,
        Func<BaseItem, List<TNode>, TNode> node)
    {
        var kept = new List<TNode>();
        foreach (var item in items)
        {
            var children = Prune(item.Children, query, includeContent, node);
            if (children.Count > 0 || Matches(query, item, includeContent))
                kept.Add(node(item, children));
        }
        return kept;
    }

    private static bool ContainsInOrder(string text, string word)
    {
        if (text.Contains(word)) return true;
        int next = 0;
        foreach (char c in text)
            if (next < word.Length && c == word[next]) next++;
        return next == word.Length;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TextTemplateManager.Helpers;

namespace TextTemplateManager.Models;

/// <summary>The search both windows use: every word of the query must appear in the item's title or tags
/// (optionally its text), its letters in order but not necessarily next to each other. Strict: the query as one
/// phrase, as typed, anywhere in one of those fields, case ignored, * for any characters and . for exactly one
/// (\* and \. for the characters themselves).</summary>
public static class TemplateSearch
{
    public static bool Matches(string query, BaseItem item, bool includeContent = false, bool strict = false)
    {
        if (strict)
        {
            if (StrictPattern(query) is not { } pattern) return true;
            var fields = new List<string> { item.Title };
            if (item is Template template)
            {
                fields.Add(template.TagsCsv);
                if (includeContent) fields.Add(HtmlUtils.ToPlainText(template.Content));
            }
            return fields.Any(f => !string.IsNullOrEmpty(f) && pattern.IsMatch(Spaces.Replace(f, " ")));
        }

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
        Func<BaseItem, List<TNode>, TNode> node, bool strict = false)
    {
        var kept = new List<TNode>();
        foreach (var item in items)
        {
            var children = Prune(item.Children, query, includeContent, node, strict);
            if (children.Count > 0 || Matches(query, item, includeContent, strict))
                kept.Add(node(item, children));
        }
        return kept;
    }

    private static readonly Regex Spaces = new(@"\s+", RegexOptions.CultureInvariant);

    // One query is matched against every item of a search; built once per query.
    private static (string Query, Regex? Pattern) _lastStrict;

    /// <summary>The strict query as a pattern (null: an empty query, which matches everything). Runs of spaces
    /// match one space (the fields are compared with theirs collapsed too).</summary>
    internal static Regex? StrictPattern(string query)
    {
        var cached = _lastStrict;
        if (cached.Query == query) return cached.Pattern;

        string q = query.Trim();
        Regex? pattern = null;
        if (q.Length > 0)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < q.Length; i++)
            {
                char c = q[i];
                if (c == '\\' && i + 1 < q.Length) sb.Append(Regex.Escape(q[++i].ToString()));
                else if (c == '*') sb.Append(".*");
                else if (c == '.') sb.Append('.');
                else if (char.IsWhiteSpace(c))
                {
                    while (i + 1 < q.Length && char.IsWhiteSpace(q[i + 1])) i++;
                    sb.Append(' ');
                }
                else sb.Append(Regex.Escape(c.ToString()));
            }
            pattern = new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);
        }
        _lastStrict = (query, pattern);
        return pattern;
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

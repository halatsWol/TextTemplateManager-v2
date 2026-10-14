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
/// (\* and \. for the characters themselves); in the tags the words may come in any order, each in one of the tags.</summary>
public static class TemplateSearch
{
    public static bool Matches(string query, BaseItem item, bool includeContent = false, bool strict = false)
    {
        if (strict)
        {
            if (StrictPattern(query) is not { } pattern) return true;
            if (StrictMatch(pattern, item.Title)) return true;
            if (item is not Template template) return false;
            if (StrictMatch(pattern, template.TagsCsv) || TagsMatch(query, template.TagsCsv)) return true;
            return includeContent && StrictMatch(pattern, HtmlUtils.ToPlainText(template.Content));
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

    private static bool StrictMatch(Regex pattern, string field) =>
        !string.IsNullOrEmpty(field) && pattern.IsMatch(Spaces.Replace(field, " "));

    private static bool TagsMatch(string query, string tagsCsv)
    {
        var tags = tagsCsv.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return tags.Length > 0 && StrictWordPatterns(query).All(word => tags.Any(tag => StrictMatch(word, tag)));
    }

    // One query is matched against every item of a search; built once per query.
    private static (string Query, Regex? Pattern) _lastStrict;
    private static (string Query, Regex[] Words) _lastStrictWords;

    /// <summary>The strict query as a pattern (null: an empty query, which matches everything). Runs of spaces
    /// match one space (the fields are compared with theirs collapsed too).</summary>
    internal static Regex? StrictPattern(string query)
    {
        var cached = _lastStrict;
        if (cached.Query == query) return cached.Pattern;

        string q = query.Trim();
        var pattern = q.Length > 0 ? BuildStrict(q) : null;
        _lastStrict = (query, pattern);
        return pattern;
    }

    /// <summary>Each word of the strict query as its own pattern; a backslash keeps a space inside a word.</summary>
    private static Regex[] StrictWordPatterns(string query)
    {
        var cached = _lastStrictWords;
        if (cached.Query == query) return cached.Words;

        var words = new List<string>();
        var word = new StringBuilder();
        for (int i = 0; i < query.Length; i++)
        {
            char c = query[i];
            if (c == '\\' && i + 1 < query.Length) word.Append(c).Append(query[++i]);
            else if (char.IsWhiteSpace(c)) { if (word.Length > 0) words.Add(word.ToString()); word.Clear(); }
            else word.Append(c);
        }
        if (word.Length > 0) words.Add(word.ToString());

        var patterns = words.Select(BuildStrict).ToArray();
        _lastStrictWords = (query, patterns);
        return patterns;
    }

    private static Regex BuildStrict(string q)
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
        return new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);
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

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace TextTemplateManager.Tutorial;

public enum WhatsNewKind { Heading, Bullet, Paragraph }

public sealed record WhatsNewBlock(WhatsNewKind Kind, IReadOnlyList<(string Text, bool Bold)> Runs);

/// <summary>The "What's new in this release" section of the release notes (release.md, shipped with the app), as
/// headings, bullets and paragraphs with bold runs. Links keep their text, code keeps its text without the backticks.</summary>
public static partial class WhatsNew
{
    public static IReadOnlyList<WhatsNewBlock> Parse(string releaseNotes)
    {
        var blocks = new List<WhatsNewBlock>();
        bool inSection = false;
        WhatsNewKind? kind = null;
        var text = new StringBuilder();

        void Flush()
        {
            if (kind is { } k && text.Length > 0) blocks.Add(new WhatsNewBlock(k, Runs(text.ToString())));
            kind = null;
            text.Clear();
        }

        foreach (string raw in releaseNotes.Replace("\r\n", "\n").Split('\n'))
        {
            string line = raw.Trim();
            if (!inSection)
            {
                inSection = line.StartsWith("### What's new", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (line.StartsWith("### ") || line.StartsWith("## ") || line == "---") break;
            if (line.Length == 0 || line.Contains("{{")) { Flush(); continue; }

            if (line.StartsWith('#'))
            {
                Flush();
                kind = WhatsNewKind.Heading;
                text.Append(line.TrimStart('#').Trim());
                Flush();
            }
            else if (line.StartsWith("- ") || line.StartsWith("* "))
            {
                Flush();
                kind = WhatsNewKind.Bullet;
                text.Append(line[2..].Trim());
            }
            else
            {
                kind ??= WhatsNewKind.Paragraph;
                if (text.Length > 0) text.Append(' ');
                text.Append(line);
            }
        }
        Flush();
        return blocks;
    }

    private static List<(string, bool)> Runs(string markdown)
    {
        string plain = Code().Replace(Link().Replace(markdown, "$1"), "$1");
        var runs = new List<(string, bool)>();
        string[] parts = plain.Split("**");
        for (int i = 0; i < parts.Length; i++)
            if (parts[i].Length > 0) runs.Add((parts[i], i % 2 == 1));
        return runs;
    }

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]*\)")]
    private static partial Regex Link();

    [GeneratedRegex(@"`([^`]*)`")]
    private static partial Regex Code();
}

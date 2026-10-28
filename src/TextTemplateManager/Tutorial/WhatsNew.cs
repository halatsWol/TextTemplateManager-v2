using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace TextTemplateManager.Tutorial;

public enum WhatsNewKind { Heading, Bullet, Paragraph }

public sealed record WhatsNewBlock(WhatsNewKind Kind, IReadOnlyList<(string Text, bool Bold)> Runs);

/// <summary>The "What's new in this release" section of the release notes (release.md, shipped with the app), as
/// headings, bullets and paragraphs with bold runs. Links keep their text, code keeps its text without the backticks.</summary>
public static partial class WhatsNew
{
    /// <summary>The release notes shipped in <paramref name="whatsNewDirectory"/> (vX.Y.Z\en-US\release.md): those of
    /// <paramref name="version"/>, else the newest (a dev build ships every release's notes), or null.</summary>
    public static string? NotesFile(string whatsNewDirectory, string version)
    {
        string own = Path.Combine(whatsNewDirectory, "v" + version, "en-US", "release.md");
        if (File.Exists(own)) return own;
        if (!Directory.Exists(whatsNewDirectory)) return null;
        return Directory.GetDirectories(whatsNewDirectory, "v*")
            .Select(d => (Path: Path.Combine(d, "en-US", "release.md"), Version: System.Version.TryParse(Path.GetFileName(d)[1..], out var v) ? v : null))
            .Where(n => n.Version != null && File.Exists(n.Path))
            .OrderByDescending(n => n.Version)
            .Select(n => n.Path)
            .FirstOrDefault();
    }

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

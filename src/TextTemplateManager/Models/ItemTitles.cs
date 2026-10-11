using System;
using System.Collections.Generic;
using System.Linq;

namespace TextTemplateManager.Models;

public static class ItemTitles
{
    /// <summary>baseName when no sibling has it (ignoring case), else the first free "baseName N", so gaps
    /// are filled. The naming for new, cloned and connector-created items.</summary>
    public static string Unique(string baseName, IEnumerable<BaseItem> siblings)
    {
        var taken = new HashSet<string>(siblings.Select(s => s.Title ?? string.Empty), StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains(baseName)) return baseName;

        int n = 1;
        while (taken.Contains($"{baseName} {n}")) n++;
        return $"{baseName} {n}";
    }
}

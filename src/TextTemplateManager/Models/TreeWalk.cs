using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace TextTemplateManager.Models;

/// <summary>Walks over the item tree (<see cref="BaseItem.Children"/>), depth first, each item before its
/// children.</summary>
public static class TreeWalk
{
    /// <summary>These items and everything below them.</summary>
    public static IEnumerable<BaseItem> Descendants(this IEnumerable<BaseItem> items)
    {
        foreach (var item in items)
            foreach (var each in item.SelfAndDescendants())
                yield return each;
    }

    /// <summary>The item itself and everything below it.</summary>
    public static IEnumerable<BaseItem> SelfAndDescendants(this BaseItem item)
    {
        yield return item;
        foreach (var child in item.Children.Descendants())
            yield return child;
    }

    /// <summary>The collection holding <paramref name="item"/>: <paramref name="items"/> itself or a
    /// <see cref="BaseItem.Children"/> below it; null when the item isn't in this tree.</summary>
    public static ObservableCollection<BaseItem>? ContainerOf(this ObservableCollection<BaseItem> items, BaseItem item) =>
        items.Contains(item) ? items : items.Select(i => i.Children.ContainerOf(item)).FirstOrDefault(c => c != null);

    /// <summary>The item whose children hold <paramref name="item"/>; null for a top-level item or one
    /// that isn't in this tree.</summary>
    public static BaseItem? ParentOf(this IEnumerable<BaseItem> items, BaseItem item) =>
        items.Descendants().FirstOrDefault(i => i.Children.Contains(item));

    /// <summary>The top-level item <paramref name="item"/> is in (possibly the item itself).</summary>
    public static BaseItem? TopLevelOf(this IEnumerable<BaseItem> items, BaseItem item) =>
        items.FirstOrDefault(top => top.SelfAndDescendants().Contains(item));
}

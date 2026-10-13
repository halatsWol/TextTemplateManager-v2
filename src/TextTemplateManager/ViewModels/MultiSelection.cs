using System.Collections.Generic;
using System.Linq;
using TextTemplateManager.Models;

namespace TextTemplateManager.ViewModels;

/// <summary>Several selected tree items, chosen as in Explorer: a click selects one, Ctrl+click adds or removes
/// one, Shift+click selects the visible rows between the anchor (the last click without Shift) and the clicked one.</summary>
public sealed class MultiSelection
{
    private readonly List<BaseItem> _items = new();
    private BaseItem? _anchor;

    /// <summary>In the order they were selected; always holds the focused item when anything is selected.</summary>
    public IReadOnlyList<BaseItem> Items => _items;

    public bool Contains(BaseItem item) => _items.Contains(item);

    /// <summary>Selects exactly <paramref name="items"/>, the last one focused (e.g. after moving them).</summary>
    public void Set(IReadOnlyList<BaseItem> items)
    {
        _items.Clear();
        _items.AddRange(items);
        _anchor = items.Count > 0 ? items[^1] : null;
    }

    public void Reset(BaseItem? item)
    {
        _items.Clear();
        if (item != null) _items.Add(item);
        _anchor = item;
    }

    /// <summary>Applies a click on <paramref name="item"/>; <paramref name="visible"/> is the tree's rows top to
    /// bottom. Returns the item that gets the focus (the editor).</summary>
    public BaseItem Click(BaseItem item, bool ctrl, bool shift, IList<BaseItem> visible)
    {
        if (shift && _anchor != null && visible.IndexOf(_anchor) is int from and >= 0 && visible.IndexOf(item) is int to and >= 0)
        {
            var range = visible.Skip(System.Math.Min(from, to)).Take(System.Math.Abs(to - from) + 1);
            if (!ctrl) _items.Clear();
            foreach (var each in range) if (!_items.Contains(each)) _items.Add(each);
            _items.Remove(item);
            _items.Add(item);   // the focus last
            return item;
        }

        if (ctrl)
        {
            if (_items.Contains(item))
            {
                if (_items.Count == 1) return item;   // the last one stays
                _items.Remove(item);
                _anchor = _items[^1];
                return _anchor;
            }
            _items.Add(item);
            _anchor = item;
            return item;
        }

        Reset(item);
        return item;
    }

    /// <summary>The selected items without those inside another selected folder (which come along anyway).</summary>
    public static List<BaseItem> TopMost(IReadOnlyList<BaseItem> items) =>
        items.Where(i => !items.Any(other => !ReferenceEquals(other, i) && other.SelfAndDescendants().Skip(1).Contains(i))).ToList();
}

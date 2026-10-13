using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Controls;
using System.Collections.ObjectModel;
using TextTemplateManager.Helpers;
using TextTemplateManager.Models;

namespace TextTemplateManager.ViewModels;

/// <summary>One row of the main tree: the item, the children shown under it (all of them, or only the
/// search matches) and whether it is expanded. One node per item, kept across refreshes.</summary>
public sealed partial class TreeNode(BaseItem item) : ObservableObject
{
    public BaseItem Item { get; } = item;

    public ObservableCollection<TreeNode> Children { get; } = new();

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    /// <summary>Part of a selection of several items (the tree itself only highlights the focused one).</summary>
    [ObservableProperty]
    public partial bool IsMultiSelected { get; set; }

    public Symbol Icon => ItemIconConverter.For(Item);

    // The tree row's accessible name (what a screen reader announces), instead of the class name.
    public override string ToString() => Item.Title;
}

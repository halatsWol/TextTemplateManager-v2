using System.Collections.ObjectModel;
using TextTemplateManager.Models;
using Xunit;

namespace TextTemplateManager.Tests;

public class TreeWalkTests
{
    private readonly Folder _outer = new() { Title = "Outer" };
    private readonly Folder _inner = new() { Title = "Inner" };
    private readonly Template _deep = new() { Title = "Deep" };
    private readonly Template _top = new() { Title = "Top" };
    private readonly ObservableCollection<BaseItem> _root = new();

    public TreeWalkTests()
    {
        _inner.Children.Add(_deep);
        _outer.Children.Add(_inner);
        _root.Add(_outer);
        _root.Add(_top);
    }

    [Fact]
    public void Descendants_lists_each_item_before_its_children()
    {
        Assert.Equal(new[] { "Outer", "Inner", "Deep", "Top" }, _root.Descendants().Select(i => i.Title));
        Assert.Equal(new[] { "Inner", "Deep" }, _inner.SelfAndDescendants().Select(i => i.Title));
    }

    [Fact]
    public void ContainerOf_finds_the_collection_holding_the_item()
    {
        Assert.Same(_inner.Children, _root.ContainerOf(_deep));
        Assert.Same(_root, _root.ContainerOf(_top));
        Assert.Null(_root.ContainerOf(new Template()));
    }

    [Fact]
    public void ParentOf_and_TopLevelOf_find_the_ancestors()
    {
        Assert.Same(_inner, _root.ParentOf(_deep));
        Assert.Null(_root.ParentOf(_top));
        Assert.Same(_outer, _root.TopLevelOf(_deep));
        Assert.Same(_top, _root.TopLevelOf(_top));
        Assert.Null(_root.TopLevelOf(new Template()));
    }
}

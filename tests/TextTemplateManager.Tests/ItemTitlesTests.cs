using TextTemplateManager.Models;
using Xunit;

namespace TextTemplateManager.Tests;

/// <summary>Names for new, cloned and connector-created items: the base name if free, else the first free
/// number, compared without case.</summary>
public class ItemTitlesTests
{
    private static BaseItem[] Siblings(params string[] titles) =>
        titles.Select(t => (BaseItem)new Template { Title = t }).ToArray();

    [Fact]
    public void A_free_base_name_is_used_as_is() =>
        Assert.Equal("New Template", ItemTitles.Unique("New Template", Siblings("Other", "New Template 1")));

    [Fact]
    public void A_taken_name_gets_the_first_free_number_ignoring_case() =>
        Assert.Equal("New Template 2", ItemTitles.Unique("New Template", Siblings("new template", "NEW TEMPLATE 1", "New Template 3")));

    [Fact]
    public void A_clone_name_counts_on_from_its_own_base() =>
        Assert.Equal("Mail (Copy) 1", ItemTitles.Unique("Mail (Copy)", Siblings("Mail", "Mail (Copy)")));
}

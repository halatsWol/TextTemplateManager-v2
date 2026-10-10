using TextTemplateManager.Models;
using Xunit;

namespace TextTemplateManager.Tests;

/// <summary>The one rule for stored shortcuts: uppercase letters and digits only. The editor's input boxes
/// and the model both use it, so what is typed is what gets stored; '-' and '.' stay free to separate a
/// sync folder's prefix from the shortcut in Quick Paste.</summary>
public class TemplateShortcutTests
{
    [Theory]
    [InlineData("msg", "MSG")]
    [InlineData("  msg2 ", "MSG2")]
    [InlineData("a-b.c", "ABC")]
    [InlineData("x+y_z", "XYZ")]
    [InlineData("äö1", "1")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    public void Shortcuts_are_cleaned_to_uppercase_letters_and_digits(string? typed, string expected)
    {
        Assert.Equal(expected, Template.CleanShortcut(typed));
    }

    [Fact]
    public void Setting_a_multi_key_stores_and_announces_the_cleaned_value()
    {
        var t = new Template();
        var announced = new List<(string? Name, string Value)>();
        t.PropertyChanged += (_, e) => announced.Add((e.PropertyName, t.MultiKeyShortcut));

        t.MultiKeyShortcut = "and-msg";

        Assert.Equal("ANDMSG", t.MultiKeyShortcut);
        Assert.Contains((nameof(Template.MultiKeyShortcut), "ANDMSG"), announced);
        Assert.DoesNotContain(announced, a => a.Name == "NormalizeShortcut");
    }

    [Fact]
    public void Raw_input_that_cleans_to_the_stored_key_is_still_announced()
    {
        // The editing TextBox shows "a" after the user typed it; the announcement makes it show "A" again.
        var t = new Template { SingleKeyShortcut = "A" };
        var announced = new List<string?>();
        t.PropertyChanged += (_, e) => announced.Add(e.PropertyName);

        t.SingleKeyShortcut = "a";

        Assert.Equal("A", t.SingleKeyShortcut);
        Assert.Equal(new[] { nameof(Template.SingleKeyShortcut) }, announced);
    }

    [Fact]
    public void Setting_the_same_clean_key_again_announces_nothing()
    {
        var t = new Template { MultiKeyShortcut = "MSG" };
        var announced = new List<string?>();
        t.PropertyChanged += (_, e) => announced.Add(e.PropertyName);

        t.MultiKeyShortcut = "MSG";

        Assert.Empty(announced);
    }

    [Fact]
    public void A_single_key_that_is_not_a_letter_or_digit_is_dropped()
    {
        var t = new Template { SingleKeyShortcut = "-" };

        Assert.Equal("", t.SingleKeyShortcut);
    }
}

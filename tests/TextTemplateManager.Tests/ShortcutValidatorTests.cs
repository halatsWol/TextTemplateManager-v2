using TextTemplateManager.Models;
using Xunit;
using Entry = TextTemplateManager.Models.ShortcutValidator.Entry;

namespace TextTemplateManager.Tests;

public class ShortcutValidatorTests
{
    private static readonly Guid Local = Guid.Empty;
    private static readonly Guid Team = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();

    private static Entry Single(string key, Guid area) => new(new Template { SingleKeyShortcut = key }, area, "");

    private static Entry Multi(string effective, Guid area) =>
        new(new Template { MultiKeyShortcut = effective[(effective.LastIndexOf('-') + 1)..] }, area, effective);

    [Fact]
    public void The_same_single_key_twice_in_one_area_is_a_conflict()
    {
        var a = Single("Q", Local);
        var b = Single("q", Local);
        var c = Single("W", Local);

        ShortcutValidator.Apply(new[] { a, b, c });

        Assert.True(a.Template.HasSingleKeyConflict);
        Assert.True(b.Template.HasSingleKeyConflict);
        Assert.False(c.Template.HasSingleKeyConflict);
        Assert.False(a.Template.HasSingleKeyCrossAreaWarning);
    }

    [Fact]
    public void The_same_single_key_in_different_areas_is_only_a_warning()
    {
        var local = Single("Q", Local);
        var team = Single("Q", Team);

        ShortcutValidator.Apply(new[] { local, team });

        Assert.False(local.Template.HasSingleKeyConflict);
        Assert.True(local.Template.HasSingleKeyCrossAreaWarning);
        Assert.True(team.Template.HasSingleKeyCrossAreaWarning);
    }

    [Fact]
    public void A_conflict_in_one_area_leaves_the_other_area_with_the_warning()
    {
        var local1 = Single("Q", Local);
        var local2 = Single("Q", Local);
        var team = Single("Q", Team);

        ShortcutValidator.Apply(new[] { local1, local2, team });

        Assert.True(local1.Template.HasSingleKeyConflict);
        Assert.False(local1.Template.HasSingleKeyCrossAreaWarning);
        Assert.False(team.Template.HasSingleKeyConflict);
        Assert.True(team.Template.HasSingleKeyCrossAreaWarning);
    }

    [Fact]
    public void Multi_keys_are_compared_as_typed_with_the_sync_prefix()
    {
        var local = Multi("MSG", Local);
        var team = Multi("TM-MSG", Team);       // typed differently: no clash
        var other = Multi("MSG", Other);        // source without a prefix: same as local, a warning
        var team2 = Multi("TM-MSG", Team);      // same area, same key: a conflict

        ShortcutValidator.Apply(new[] { local, team, other, team2 });

        Assert.True(local.Template.HasMultiKeyCrossAreaWarning);
        Assert.True(other.Template.HasMultiKeyCrossAreaWarning);
        Assert.True(team.Template.HasMultiKeyConflict);
        Assert.True(team2.Template.HasMultiKeyConflict);
        Assert.False(local.Template.HasMultiKeyConflict);
    }

    [Fact]
    public void A_resolved_clash_clears_the_old_flags()
    {
        var a = Single("Q", Local);
        var b = Single("Q", Local);
        ShortcutValidator.Apply(new[] { a, b });

        b.Template.SingleKeyShortcut = "W";
        ShortcutValidator.Apply(new[] { a, b });

        Assert.False(a.Template.HasSingleKeyConflict);
        Assert.False(b.Template.HasSingleKeyConflict);
    }
}

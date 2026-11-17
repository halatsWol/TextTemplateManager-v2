using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TextTemplateManager.Models;
using TextTemplateManager.ViewModels;
using Xunit;

namespace TextTemplateManager.Tests;

/// <summary>A template added on this device gets its first language detected once it has text, once; templates
/// that weren't added here, or that got a language some other way, are left alone.</summary>
public class FirstLanguageDetectionTests
{
    private readonly List<string> _asked = [];

    private FirstLanguageDetection Detection(string? answer) => new()
    {
        Detect = html => { _asked.Add(html); return Task.FromResult(answer); },
    };

    private static Template New(string content = "") => new() { Id = Guid.NewGuid(), Title = "New", Content = content };

    [Fact]
    public async Task A_new_template_gets_the_detected_language()
    {
        var detection = Detection("de");
        var t = New("<p>Sehr geehrte Damen und Herren</p>");
        detection.Added(t);

        Assert.Equal("de", await detection.RunAsync(t));

        Assert.Equal("de", t.Language);
        Assert.Null(t.AltLanguages);
        Assert.False(detection.IsPending(t));
    }

    [Fact]
    public async Task Without_text_it_waits_for_a_later_save()
    {
        var detection = Detection("de");
        var t = New("<p> </p>");
        detection.Added(t);

        Assert.Null(await detection.RunAsync(t));
        Assert.Empty(_asked);
        Assert.True(detection.IsPending(t));

        t.Content = "<p>Sehr geehrte Damen und Herren</p>";
        Assert.Equal("de", await detection.RunAsync(t));
    }

    [Fact]
    public async Task It_is_asked_once_and_stays_without_a_language_when_that_cant_be_told()
    {
        var detection = Detection(null);
        var t = New("<p>Hi</p>");
        detection.Added(t);

        Assert.Null(await detection.RunAsync(t));
        Assert.Null(await detection.RunAsync(t));

        Assert.Single(_asked);
        Assert.Null(t.Language);
    }

    [Fact]
    public async Task Templates_not_added_here_are_never_detected()
    {
        var detection = Detection("de");
        var t = New("<p>Sehr geehrte Damen und Herren</p>");

        Assert.Null(await detection.RunAsync(t));

        Assert.Empty(_asked);
        Assert.Null(t.Language);
    }

    [Fact]
    public async Task A_language_given_meanwhile_is_kept()
    {
        var detection = Detection("de");
        var picked = New("<p>Sehr geehrte Damen und Herren</p>");
        picked.Language = "en";
        detection.Added(picked);
        Assert.Null(await detection.RunAsync(picked));
        Assert.Empty(_asked);
        Assert.Equal("en", picked.Language);

        var draftPicked = New("<p>Sehr geehrte Damen und Herren</p>");
        var draft = New(draftPicked.Content);
        draft.Language = "fr";
        detection.Added(draftPicked);
        Assert.Null(await detection.RunAsync(draftPicked, () => draft));
        Assert.Null(draftPicked.Language);
        Assert.Equal("fr", draft.Language);
    }

    [Fact]
    public async Task The_unsaved_copy_gets_the_language_too_so_it_stays_unedited()
    {
        var detection = Detection("de");
        var t = New("<p>Sehr geehrte Damen und Herren</p>");
        detection.Added(t);
        var draft = ItemDraft.For(t);

        await detection.RunAsync(t, () => (Template)draft.Edited);

        Assert.Equal("de", ((Template)draft.Edited).Language);
        Assert.False(draft.IsDirty);
    }
}

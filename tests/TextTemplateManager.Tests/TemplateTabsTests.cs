using System.Collections.Generic;
using System.Linq;
using TextTemplateManager.Models;
using TextTemplateManager.Services.Translation;
using TextTemplateManager.ViewModels;
using Xunit;

namespace TextTemplateManager.Tests;

/// <summary>The editor's language tabs: adding, moving, renaming and deleting tabs writes the template's fields in
/// their stored form, the first tab keeps a language once it has one, and with autosave off the draft tells real
/// edits from the editor's reformatting per tab.</summary>
public class TemplateTabsTests
{
    private static Template Single(string? language = "en") => new() { Title = "Offer", Content = "<p>Hello</p>", Language = language };

    private static Template Three()
    {
        var t = Single();
        TemplateTabs.Add(t, "de");
        TemplateTabs.Add(t, "fr");
        TemplateTabs.SetContent(t, t.AltLanguages![0].Id, "<p>Hallo</p>");
        TemplateTabs.SetContent(t, t.AltLanguages![1].Id, "<p>Bonjour</p>");
        return t;
    }

    private static string Order(Template t) => string.Join(",", TemplateTabs.All(t).Select(x => x.Language));

    [Fact]
    public void A_translation_is_added_as_a_tab_in_the_target_language()
    {
        var t = Single();

        string? id = TemplateTabs.AddTranslation(t, null, "en", "DE", "<p>Hallo</p>");

        Assert.NotNull(id);
        Assert.Equal("en,de", Order(t));
        Assert.Equal("<p>Hallo</p>", TemplateTabs.ContentOf(t, id));
        Assert.Equal("<p>Hello</p>", t.Content);
    }

    [Fact]
    public void A_translation_gives_a_source_tab_without_a_language_the_one_it_came_from()
    {
        var t = Single(null);

        Assert.NotNull(TemplateTabs.AddTranslation(t, null, "en", "de", "<p>Hallo</p>"));
        Assert.Equal("en,de", Order(t));

        var u = Three();
        string blank = TemplateTabs.Add(u);
        Assert.NotNull(TemplateTabs.AddTranslation(u, blank, "it", "es", "<p>Hola</p>"));
        Assert.Equal("en,de,fr,it,es", Order(u));
    }

    [Fact]
    public void A_translation_into_a_language_the_template_has_adds_nothing()
    {
        var t = Three();
        string blank = TemplateTabs.Add(t);

        Assert.Null(TemplateTabs.AddTranslation(t, null, "en", "fr", "<p>Salut</p>"));
        Assert.Null(TemplateTabs.AddTranslation(t, blank, "de", "fr", "<p>Salut</p>"));
        Assert.Null(TemplateTabs.AddTranslation(t, blank, "it", "fr", "<p>Salut</p>"));
        Assert.Equal("en,de,fr,", Order(t));
        Assert.Equal("<p>Bonjour</p>", t.AltLanguages![1].Content);
    }

    [Fact]
    public void A_single_tab_is_the_templates_own_fields_without_an_id()
    {
        var t = Single();
        var tabs = TemplateTabs.All(t);

        Assert.Single(tabs);
        Assert.Equal((null, "en", "<p>Hello</p>"), (tabs[0].Id, tabs[0].Language, tabs[0].Content));
        Assert.Equal(0, TemplateTabs.IndexOf(t, null));
        Assert.Equal(-1, TemplateTabs.IndexOf(t, "abcd"));
    }

    [Fact]
    public void Adding_a_tab_gives_the_first_tab_an_id_and_the_new_one_its_own()
    {
        var t = Single();
        string id = TemplateTabs.Add(t, "DE");

        Assert.NotNull(t.LanguageId);
        Assert.NotEqual(t.LanguageId, id);
        Assert.Matches("^[0-9a-f]{4}$", id);
        var alt = Assert.Single(t.AltLanguages!);
        Assert.Equal((id, "de", ""), (alt.Id, alt.Language, alt.Content));
        Assert.Equal("<p>Hello</p>", t.Content);
        Assert.Equal(1, TemplateTabs.IndexOf(t, id));
        Assert.Equal(0, TemplateTabs.IndexOf(t, t.LanguageId));
    }

    [Fact]
    public void A_tab_added_without_a_language_isnt_pasted_until_it_gets_one()
    {
        var t = Single();
        string id = TemplateTabs.Add(t);

        Assert.Null(Assert.Single(t.AltLanguages!).Language);
        Assert.Single(TemplateLanguages.Offered(t));
        Assert.False(t.HasLanguageConflict);
        Assert.Equal("en,", Order(t));

        TemplateTabs.SetLanguage(t, id, "de");
        Assert.Equal(["en", "de"], TemplateLanguages.Offered(t).Select(o => o.Language));
    }

    [Fact]
    public void A_tab_can_only_be_added_once_the_first_tab_has_a_language()
    {
        var t = Single(language: null);

        Assert.False(TemplateTabs.CanAdd(t));
        Assert.Throws<System.InvalidOperationException>(() => TemplateTabs.Add(t, "de"));

        TemplateTabs.SetLanguage(t, null, "en");
        Assert.True(TemplateTabs.CanAdd(t));
    }

    [Fact]
    public void Text_goes_to_the_tab_it_was_typed_in()
    {
        var t = Three();
        string de = t.AltLanguages![0].Id!;

        TemplateTabs.SetContent(t, de, "<p>Guten Tag</p>");
        TemplateTabs.SetContent(t, null, "<p>Good day</p>");

        Assert.Equal("<p>Good day</p>", t.Content);
        Assert.Equal("<p>Guten Tag</p>", TemplateTabs.ContentOf(t, de));
        Assert.Equal("<p>Bonjour</p>", t.AltLanguages[1].Content);
    }

    [Fact]
    public void Text_for_a_deleted_tab_is_dropped_not_written_to_another()
    {
        var t = Three();
        string de = t.AltLanguages![0].Id!;
        TemplateTabs.Remove(t, de);

        TemplateTabs.SetContent(t, de, "<p>late</p>");

        Assert.Equal("<p>Hello</p>", t.Content);
        Assert.Equal("<p>Bonjour</p>", Assert.Single(t.AltLanguages!).Content);
    }

    [Fact]
    public void The_first_tabs_language_can_be_changed_but_not_removed()
    {
        var t = Three();

        TemplateTabs.SetLanguage(t, t.LanguageId, "en-gb");
        Assert.Equal("en-GB", t.Language);

        TemplateTabs.SetLanguage(t, t.LanguageId, null);
        Assert.Equal("en-GB", t.Language);

        string fr = t.AltLanguages![1].Id!;
        TemplateTabs.SetLanguage(t, fr, "it");
        Assert.Equal("it", t.AltLanguages[1].Language);
    }

    [Fact]
    public void Making_a_tab_the_default_swaps_it_into_the_templates_own_fields()
    {
        var t = Three();
        string first = t.LanguageId!;
        string fr = t.AltLanguages![1].Id!;

        TemplateTabs.Move(t, fr, 0);

        Assert.Equal("fr,en,de", Order(t));
        Assert.Equal((fr, "fr", "<p>Bonjour</p>"), (t.LanguageId, t.Language, t.Content));
        Assert.Equal((first, "<p>Hello</p>"), (t.AltLanguages![0].Id, t.AltLanguages[0].Content));
    }

    [Fact]
    public void Tabs_move_left_and_right_and_take_a_dragged_order()
    {
        var t = Three();
        string de = t.AltLanguages![0].Id!;
        string fr = t.AltLanguages![1].Id!;
        string en = t.LanguageId!;

        TemplateTabs.Move(t, de, 2);
        Assert.Equal("en,fr,de", Order(t));
        TemplateTabs.Move(t, fr, 0);
        Assert.Equal("fr,en,de", Order(t));

        Assert.True(TemplateTabs.Reorder(t, [de, en, fr]));
        Assert.Equal("de,en,fr", Order(t));
        Assert.Equal("<p>Hallo</p>", t.Content);
    }

    [Fact]
    public void An_order_that_doesnt_match_the_tabs_changes_nothing()
    {
        var t = Three();
        string de = t.AltLanguages![0].Id!;

        Assert.False(TemplateTabs.Reorder(t, [de, de, t.LanguageId]));
        Assert.False(TemplateTabs.Reorder(t, [de, t.LanguageId]));
        Assert.False(TemplateTabs.Reorder(t, [de, t.LanguageId, "ffff"]));
        Assert.Equal("en,de,fr", Order(t));
    }

    [Fact]
    public void A_tab_without_a_language_cant_become_the_first()
    {
        var t = Three();
        string de = t.AltLanguages![0].Id!;
        string fr = t.AltLanguages![1].Id!;
        TemplateTabs.SetLanguage(t, de, null);

        Assert.False(TemplateTabs.CanMove(t, de, 0));
        Assert.False(TemplateTabs.CanMove(t, t.LanguageId, 1));   // de would be first
        Assert.True(TemplateTabs.CanMove(t, fr, 0));
        Assert.False(TemplateTabs.Reorder(t, [de, t.LanguageId, fr]));
        Assert.False(TemplateTabs.CanRemove(t, t.LanguageId));
        TemplateTabs.Move(t, de, 0);
        Assert.Equal("en,,fr", Order(t));
    }

    [Fact]
    public void Deleting_the_first_tab_makes_the_next_one_the_default()
    {
        var t = Three();
        string de = t.AltLanguages![0].Id!;

        TemplateTabs.Remove(t, t.LanguageId);

        Assert.Equal("de,fr", Order(t));
        Assert.Equal((de, "<p>Hallo</p>"), (t.LanguageId, t.Content));
    }

    [Fact]
    public void Deleting_down_to_one_tab_keeps_its_language_but_drops_the_ids()
    {
        var t = Three();
        string de = t.AltLanguages![0].Id!;
        string fr = t.AltLanguages![1].Id!;

        TemplateTabs.Remove(t, de);
        TemplateTabs.Remove(t, fr);

        Assert.Equal(("en", null, null), (t.Language, t.LanguageId, t.AltLanguages));
        Assert.False(TemplateTabs.CanRemove(t, null));
    }

    [Fact]
    public void Each_change_is_one_property_change_the_store_sees()
    {
        var t = Three();
        var changed = new List<string?>();
        t.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        TemplateTabs.SetContent(t, t.AltLanguages![0].Id, "<p>Hallo</p>");   // same text: nothing
        Assert.Empty(changed);

        TemplateTabs.SetContent(t, t.AltLanguages![0].Id, "<p>Servus</p>");
        Assert.Equal([nameof(Template.AltLanguages), nameof(Template.HasLanguageConflict)], changed);
    }

    [Fact]
    public void Tabs_written_by_the_editor_round_trip_through_the_load_repair_unchanged()
    {
        var t = Three();
        string before = Order(t) + t.LanguageId + string.Join("", t.AltLanguages!.Select(a => a.Id));

        TemplateLanguages.Repair(t);

        Assert.Equal(before, Order(t) + t.LanguageId + string.Join("", t.AltLanguages!.Select(a => a.Id)));
    }

    // Draft (autosave off)

    [Fact]
    public void A_draft_keeps_tab_changes_until_committed()
    {
        var saved = Single();
        var draft = ItemDraft.For(saved);
        var edited = (Template)draft.Edited;

        string de = TemplateTabs.Add(edited, "de");
        TemplateTabs.SetContent(edited, de, "<p>Hallo</p>");

        Assert.True(draft.IsDirty);
        Assert.Null(saved.AltLanguages);

        draft.Commit();

        Assert.False(draft.IsDirty);
        Assert.Equal("en,de", Order(saved));
        Assert.Equal(edited.LanguageId, saved.LanguageId);
        Assert.Equal("<p>Hallo</p>", saved.ContentFor(de));
        Assert.Equal("<p>Hello</p>", saved.Content);
    }

    [Fact]
    public void Adding_and_deleting_a_tab_again_leaves_the_draft_unedited()
    {
        var draft = ItemDraft.For(Single());
        var edited = (Template)draft.Edited;

        string de = TemplateTabs.Add(edited, "de");
        Assert.True(draft.IsDirty);
        TemplateTabs.Remove(edited, de);

        Assert.False(draft.IsDirty);
    }

    [Fact]
    public void The_editors_reformatting_of_any_tab_is_not_an_edit_and_isnt_written_back()
    {
        var saved = Three();
        string de = saved.AltLanguages![0].Id!;
        var draft = ItemDraft.For(saved);
        var edited = (Template)draft.Edited;

        draft.ContentLoaded("<p>Hello </p>", saved.LanguageId);
        draft.ContentLoaded("<p>Hallo </p>", de);
        Assert.False(draft.IsDirty);
        Assert.Equal("<p>Hallo </p>", edited.ContentFor(de));

        // Moving the tabs is an edit; the texts nobody typed in keep their stored form.
        TemplateTabs.Move(edited, de, 0);
        Assert.True(draft.IsDirty);
        draft.Commit();

        Assert.Equal("de,en,fr", Order(saved));
        Assert.Equal("<p>Hallo</p>", saved.Content);
        Assert.Equal("<p>Hello</p>", saved.AltLanguages![0].Content);

        TemplateTabs.SetContent(edited, de, "<p>Servus</p>");
        Assert.True(draft.IsDirty);
        draft.ContentLoaded("<p>late</p>", de);   // after a real edit the editor's report changes nothing
        Assert.Equal("<p>Servus</p>", edited.Content);
    }

    [Fact]
    public void A_language_change_in_the_draft_is_an_edit()
    {
        var saved = Three();
        var draft = ItemDraft.For(saved);
        var edited = (Template)draft.Edited;

        TemplateTabs.SetLanguage(edited, edited.AltLanguages![1].Id, "it");
        Assert.True(draft.IsDirty);
        TemplateTabs.SetLanguage(edited, edited.AltLanguages![1].Id, "fr");
        Assert.False(draft.IsDirty);
    }

    [Fact]
    public void A_rebased_draft_keeps_its_tabs_and_baselines()
    {
        var saved = Three();
        string de = saved.AltLanguages![0].Id!;
        var draft = ItemDraft.For(saved);
        draft.ContentLoaded("<p>Hallo </p>", de);
        TemplateTabs.SetLanguage((Template)draft.Edited, de, "nl");

        var replacement = Three();
        replacement.Id = saved.Id;
        replacement.LanguageId = saved.LanguageId;
        replacement.AltLanguages = saved.AltLanguages;
        var rebased = draft.RebaseOnto(replacement);

        Assert.True(rebased.IsDirty);
        Assert.Equal("en,nl,fr", Order((Template)rebased.Edited));
        TemplateTabs.SetLanguage((Template)rebased.Edited, de, "de");
        Assert.False(rebased.IsDirty);
    }

    // The language picker

    [Fact]
    public void The_catalog_lists_iso_639_1_languages_with_chinese_by_script()
    {
        var all = LanguageCatalog.All;

        Assert.Contains("de", all);
        Assert.Contains("en", all);
        Assert.Contains("zh-Hans", all);
        Assert.Contains("zh-Hant", all);
        Assert.Contains("nb", all);
        Assert.DoesNotContain("zh", all);
        Assert.DoesNotContain("no", all);
        Assert.DoesNotContain("iw", all);
        Assert.All(all, c => Assert.Equal(c, TemplateLanguages.NormalizeCode(c)));
        Assert.True(all.Count > 100);
    }

    [Fact]
    public void Suggested_languages_are_the_ui_languages_and_installed_translator_languages()
    {
        var suggested = LanguageCatalog.Suggested(["en-US", "de-DE"], ["en", "uk", "zh-hans"]);

        Assert.Equal(["en", "de", "uk", "zh-Hans"], suggested);
    }

    [Fact]
    public void The_picker_puts_suggested_languages_first_and_leaves_out_the_templates_own()
    {
        var (suggested, others) = LanguageCatalog.Choices(["en", "de", "fr"], ["de", null], null, c => c);

        Assert.Equal(["en", "fr"], suggested.Select(c => c.Code));
        Assert.DoesNotContain(others, c => c.Code is "de" or "en" or "fr");
        Assert.Contains(others, c => c.Code == "it");
    }

    [Fact]
    public void The_picker_filter_matches_a_names_words_or_a_code()
    {
        var names = new Dictionary<string, string> { ["zh-Hans"] = "Chinese (Simplified)", ["de"] = "German", ["en"] = "English" };
        string Name(string c) => names.GetValueOrDefault(c, "Zz" + c);

        Assert.Equal(["zh-Hans"], Codes(LanguageCatalog.Choices(["de"], [], "simpl", Name)));
        Assert.Equal(["de"], Codes(LanguageCatalog.Choices(["de"], [], "ger", Name)));
        Assert.Equal(["de"], Codes(LanguageCatalog.Choices(["de"], [], "DE", Name)));
        Assert.Empty(Codes(LanguageCatalog.Choices(["de"], [], "erman", Name)));
    }

    [Fact]
    public void The_detected_language_comes_first_and_only_once()
    {
        var (detected, suggested, others) = LanguageCatalog.PickerChoices("fr", ["en", "fr"], ["de"], null, c => c);

        Assert.Equal("fr", detected?.Code);
        Assert.Equal(["en"], suggested.Select(c => c.Code));
        Assert.DoesNotContain(others, c => c.Code == "fr");
    }

    [Fact]
    public void A_detected_language_the_template_has_or_the_filter_hides_isnt_offered()
    {
        Assert.Null(LanguageCatalog.PickerChoices("de", ["en", "de"], ["de"], null, c => c).Detected);
        Assert.Null(LanguageCatalog.PickerChoices("fr", ["en", "fr"], [], "en", c => c).Detected);
        Assert.Null(LanguageCatalog.PickerChoices(null, ["en"], [], null, c => c).Detected);
    }

    [Fact]
    public void Text_is_detected_among_the_suggested_languages_and_those_the_templates_use()
    {
        var multi = Single("pt");
        TemplateTabs.Add(multi, "sw");
        TemplateTabs.Add(multi);
        var templates = new[] { multi, Single("EN"), Single(null) };

        Assert.Equal(["en", "de", "pt", "sw"], LanguageCatalog.DetectionCandidates(["en", "de"], templates));
    }

    private static List<string> Codes((List<LanguageChoice> Suggested, List<LanguageChoice> Others) choices) =>
        choices.Suggested.Concat(choices.Others).Select(c => c.Code).ToList();
}

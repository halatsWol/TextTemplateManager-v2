using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System.Collections.Generic;
using System.Linq;
using TextTemplateManager.Helpers;
using TextTemplateManager.Services.Translation;
using Windows.System;

namespace TextTemplateManager.Controls;

/// <summary>Picks a language for a template tab: a filter box over the language detected in its text, the suggested
/// languages and all others, without the ones the template already has.</summary>
public sealed partial class LanguagePickerDialog : ContentDialog
{
    private readonly IReadOnlyList<string> _suggested;
    private readonly IReadOnlyList<string?> _taken;
    private readonly string? _detected;

    public LanguagePickerDialog(string title, string? message, IReadOnlyList<string> suggested, IReadOnlyList<string?> taken, string? detected = null)
    {
        InitializeComponent();
        Title = title;
        PrimaryButtonText = Loc.Get("LanguagePicker_ChooseButton");
        CloseButtonText = Loc.Get("Common_CancelButton");
        if (message != null)
        {
            MessageText.Text = message;
            MessageText.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
        }
        _suggested = suggested;
        _taken = taken;
        _detected = detected;
        Fill();
        Opened += (_, _) => FilterBox.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
    }

    /// <summary>The chosen code, once the dialog closed with the primary button.</summary>
    public string? Chosen => (LanguageList.SelectedItem as LanguageChoice)?.Code;

    private void Fill()
    {
        var (detected, suggested, others) = LanguageCatalog.PickerChoices(_detected, _suggested, _taken, FilterBox.Text);
        var groups = new List<LanguageGroup>();
        if (detected != null) groups.Add(new(Loc.Get("LanguagePicker_Detected"), [detected]));
        if (suggested.Count > 0) groups.Add(new(Loc.Get("LanguagePicker_Suggested"), suggested));
        if (others.Count > 0) groups.Add(new(Loc.Get("LanguagePicker_All"), others));
        GroupedLanguages.Source = groups;
        if (groups.Count > 0) LanguageList.SelectedItem = groups[0][0];
    }

    private void FilterBox_TextChanged(object sender, TextChangedEventArgs e) => Fill();

    private void FilterBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Down && LanguageList.Items.Count > 0)
        {
            e.Handled = true;
            if (LanguageList.ContainerFromItem(LanguageList.SelectedItem) is ListViewItem item) item.Focus(Microsoft.UI.Xaml.FocusState.Keyboard);
            else LanguageList.Focus(Microsoft.UI.Xaml.FocusState.Keyboard);
        }
    }

    private void LanguageList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        IsPrimaryButtonEnabled = LanguageList.SelectedItem is LanguageChoice;

    private void LanguageList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => ChooseSelected();

    private void LanguageList_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        e.Handled = true;
        ChooseSelected();
    }

    private bool _choseByList;

    private void ChooseSelected()
    {
        if (Chosen == null) return;
        _choseByList = true;
        Hide();
    }

    /// <summary>Shows the dialog; the chosen code, or null when cancelled.</summary>
    public async System.Threading.Tasks.Task<string?> PickAsync(System.Func<ContentDialog, System.Threading.Tasks.Task<ContentDialogResult>> show)
    {
        var result = await show(this);
        return result == ContentDialogResult.Primary || _choseByList ? Chosen : null;
    }
}

/// <summary>A heading in the language picker and the languages under it.</summary>
public sealed class LanguageGroup(string title, IEnumerable<LanguageChoice> items) : List<LanguageChoice>(items)
{
    public string Title { get; } = title;
}

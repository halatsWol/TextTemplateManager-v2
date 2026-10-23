using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using TextTemplateManager.Common;
using TextTemplateManager.Services.Translation;
using Windows.Storage.Pickers;

namespace TextTemplateManager;

/// <summary>A language in Settings ▸ Translation: what the catalog offers and what is installed of it.</summary>
public sealed partial class TranslationLanguageRow : ObservableObject
{
    public TranslationLanguageRow(string code, string name)
    {
        Code = code;
        Name = name;
    }

    public string Code { get; }
    public string Name { get; }

    /// <summary>The catalog's entry; null while the catalog isn't loaded or doesn't list the language any more.</summary>
    public TranslationLanguage? Offer { get; set; }

    public IReadOnlyList<InstalledPair> Installed { get; set; } = [];

    public CancellationTokenSource? Cancel { get; set; }

    [ObservableProperty] public partial string Detail { get; set; } = "";
    [ObservableProperty] public partial double Progress { get; set; }
    [ObservableProperty] public partial Visibility DownloadVisibility { get; set; } = Visibility.Collapsed;
    [ObservableProperty] public partial Visibility UpdateVisibility { get; set; } = Visibility.Collapsed;
    [ObservableProperty] public partial Visibility RemoveVisibility { get; set; } = Visibility.Collapsed;
    [ObservableProperty] public partial Visibility BusyVisibility { get; set; } = Visibility.Collapsed;

    public string DownloadName => $"Download {Name}";
    public string UpdateName => $"Update {Name}";
    public string RemoveName => $"Remove {Name}";
    public string CancelName => $"Cancel the download of {Name}";
    public string ProgressName => $"Downloading {Name}";

    public bool IsBusy => Cancel != null;
    public bool IsInstalled => Installed.Count > 0;

    /// <summary>The offered directions not installed yet, or installed in an older version.</summary>
    public IReadOnlyList<ModelPair> Missing => Offer?.Pairs
        .Where(p => Installed.FirstOrDefault(i => i.From == p.From && i.To == p.To) is not { } i || !i.Matches(p)).ToList() ?? [];

    public void Refresh()
    {
        var missing = Missing;
        bool outdated = missing.Any(p => Installed.Any(i => i.From == p.From && i.To == p.To));
        DownloadVisibility = Show(!IsBusy && missing.Count > 0 && !outdated);
        UpdateVisibility = Show(!IsBusy && outdated);
        RemoveVisibility = Show(!IsBusy && IsInstalled);
        BusyVisibility = Show(IsBusy);

        string directions = Offer is { } o && (o.ToEnglish == null || o.FromEnglish == null)
            ? (o.ToEnglish == null ? ", only from English" : ", only to English") : "";
        Detail = IsBusy ? "Downloading…"
            : IsInstalled ? $"Installed, {Size(Installed.Sum(i => i.Size))}{(outdated ? ", update available" : "")}{directions}"
            : Offer is { } offer ? $"{Size(offer.DownloadSize)} download, {Size(offer.DiskSize)} on disk{directions}"
            : "";
    }

    private static Visibility Show(bool on) => on ? Visibility.Visible : Visibility.Collapsed;

    internal static string Size(long bytes) => $"{Math.Max(1, (bytes + 500_000) / 1_000_000)} MB";
}

/// <summary>Settings ▸ Translation: the languages from Mozilla's catalog, download, update and remove, and export and
/// import for PCs without internet access. The navigation parameter is the translation model folder.</summary>
public sealed partial class TranslationSettingsPage : Page
{
    private static readonly HttpClient Http = CreateHttpClient();

    // Downloads outlive the page (leaving Settings doesn't stop them); a page opened meanwhile shows the same rows.
    private static readonly Dictionary<string, TranslationLanguageRow> Running = new();
    private static IReadOnlyList<ModelPair>? _catalog;

    private ModelStore? _store;
    private readonly List<TranslationLanguageRow> _rows = new();
    private readonly ObservableCollection<TranslationLanguageRow> _shown = new();

    public TranslationSettingsPage()
    {
        InitializeComponent();
        LanguageList.ItemsSource = _shown;
    }

    private static HttpClient CreateHttpClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"TextTemplateManager/{AppInfo.Version}");
        return http;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _store = new ModelStore(e.Parameter as string ?? "");
        BuildRows();
        if (_catalog == null) await LoadCatalogAsync();
    }

    private async Task LoadCatalogAsync()
    {
        CatalogProgress.IsActive = true;
        CatalogStatus.Text = "Loading the languages from Mozilla…";
        Info.IsOpen = false;
        try
        {
            _catalog = await ModelCatalog.LoadAsync(Http);
            CatalogStatus.Text = "";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException
            or InvalidOperationException or KeyNotFoundException)
        {
            CatalogStatus.Text = "";
            ShowInfo(InfoBarSeverity.Warning, "The list of languages couldn't be loaded from Mozilla's server. Installed languages still work.", retry: true);
        }
        finally { CatalogProgress.IsActive = false; }
        BuildRows();
    }

    private void BuildRows()
    {
        if (_store == null) return;
        var installed = _store.Installed();
        var offers = _catalog != null ? ModelCatalog.Languages(_catalog) : [];
        var codes = installed.SelectMany(p => new[] { p.From, p.To }).Concat(offers.Select(o => o.Code))
            .Where(c => c != "en").Distinct();

        _rows.Clear();
        foreach (var code in codes)
        {
            var offer = offers.FirstOrDefault(o => o.Code == code);
            var row = Running.GetValueOrDefault(code) ?? new TranslationLanguageRow(code, offer?.Name ?? TranslationLanguage.NameOf(code));
            row.Offer = offer ?? row.Offer;
            row.Installed = installed.Where(p => p.From == code || p.To == code).ToList();
            row.Refresh();
            _rows.Add(row);
        }
        _rows.Sort((a, b) => a.IsInstalled != b.IsInstalled ? (a.IsInstalled ? -1 : 1)
            : string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));

        long used = installed.Sum(p => p.Size);
        DiskUsage.Text = installed.Count == 0 ? "No languages installed." : $"Installed languages use {TranslationLanguageRow.Size(used)}.";
        ExportButton.IsEnabled = installed.Count > 0;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        string filter = FilterBox.Text.Trim();
        _shown.Clear();
        foreach (var row in _rows)
            if (filter.Length == 0 || row.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
                || row.Code.Equals(filter, StringComparison.OrdinalIgnoreCase))
                _shown.Add(row);
    }

    private void FilterBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private async void Retry_Click(object sender, RoutedEventArgs e) => await LoadCatalogAsync();

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not TranslationLanguageRow row || row.IsBusy || _store is not { } store) return;
        var pairs = row.Missing;
        if (pairs.Count == 0) return;

        row.Cancel = new CancellationTokenSource();
        Running[row.Code] = row;
        row.Progress = 0;
        row.Refresh();
        long total = pairs.Sum(p => p.DownloadSize), done = 0;
        string? problem = null;
        try
        {
            foreach (var pair in pairs)
            {
                long before = done;
                var progress = new Progress<long>(n => row.Progress = 100.0 * (before + n) / total);
                await store.InstallAsync(Http, pair, progress, row.Cancel.Token);
                done += pair.DownloadSize;
            }
        }
        catch (OperationCanceledException) when (row.Cancel.IsCancellationRequested) { }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException
            or UnauthorizedAccessException or TaskCanceledException)
        {
            problem = ex.Message;
        }
        finally
        {
            row.Cancel.Dispose();
            row.Cancel = null;
            Running.Remove(row.Code);
        }

        if (problem != null) ShowInfo(InfoBarSeverity.Error, $"{row.Name} couldn't be downloaded: {problem}");
        if (IsLoaded) BuildRows();
        else row.Refresh();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is TranslationLanguageRow row) row.Cancel?.Cancel();
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not TranslationLanguageRow row || _store is not { } store) return;
        foreach (var pair in row.Installed) store.Remove(pair.From, pair.To);
        BuildRows();
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_store is not { } store) return;
        var picker = new FileSavePicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
        picker.FileTypeChoices.Add("TextTemplateManager languages", new List<string> { ".zip" });
        picker.SuggestedFileName = "TextTemplateManager languages";
        if ((await picker.PickSaveFileAsync())?.Path is not { } path) return;

        var installed = store.Installed();
        string? problem = await Task.Run(() =>
        {
            try { store.Export(installed, path); return null; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return ex.Message; }
        });
        if (problem == null) ShowInfo(InfoBarSeverity.Success, $"Saved to {path}.");
        else ShowInfo(InfoBarSeverity.Error, $"The languages couldn't be exported: {problem}");
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        if (_store is not { } store) return;
        var picker = new FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
        picker.FileTypeFilter.Add(".zip");
        if ((await picker.PickSingleFileAsync())?.Path is not { } path) return;

        (IReadOnlyList<ModelInfo>? imported, string? problem) = await Task.Run<(IReadOnlyList<ModelInfo>?, string?)>(() =>
        {
            try { return (store.Import(path), null); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { return (null, ex.Message); }
        });
        BuildRows();
        if (imported != null)
        {
            var names = imported.SelectMany(i => new[] { i.From, i.To }).Where(c => c != "en").Distinct().Select(TranslationLanguage.NameOf);
            ShowInfo(InfoBarSeverity.Success, $"Imported: {string.Join(", ", names)}.");
        }
        else ShowInfo(InfoBarSeverity.Error, $"The file couldn't be imported: {problem}");
    }

    private void ShowInfo(InfoBarSeverity severity, string message, bool retry = false)
    {
        Info.Severity = severity;
        Info.Message = message;
        RetryButton.Visibility = retry ? Visibility.Visible : Visibility.Collapsed;
        Info.IsOpen = true;
    }
}

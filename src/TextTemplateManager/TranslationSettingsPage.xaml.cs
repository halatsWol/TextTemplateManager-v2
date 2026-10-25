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
using TextTemplateManager.Helpers;
using TextTemplateManager.Models;
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
    [ObservableProperty] public partial bool UpdateEnabled { get; set; }
    [ObservableProperty] public partial Visibility RemoveVisibility { get; set; } = Visibility.Collapsed;
    [ObservableProperty] public partial Visibility BusyVisibility { get; set; } = Visibility.Collapsed;

    public string DownloadName => Loc.Format("Translation_DownloadName", Name);
    public string UpdateName => Loc.Format("Translation_UpdateName", Name);
    public string RemoveName => Loc.Format("Translation_RemoveName", Name);
    public string CancelName => Loc.Format("Translation_CancelName", Name);
    public string ProgressName => Loc.Format("Translation_ProgressName", Name);

    public bool IsBusy => Cancel != null;
    public bool IsInstalled => Installed.Count > 0;
    public bool IsOutdated => Missing.Any(p => Installed.Any(i => i.From == p.From && i.To == p.To));

    /// <summary>The offered directions not installed yet, or installed in an older version.</summary>
    public IReadOnlyList<ModelPair> Missing => Offer?.Pairs
        .Where(p => Installed.FirstOrDefault(i => i.From == p.From && i.To == p.To) is not { } i || !i.Matches(p)).ToList() ?? [];

    public void Refresh()
    {
        var missing = Missing;
        bool outdated = IsOutdated;
        DownloadVisibility = Show(!IsBusy && missing.Count > 0 && !outdated);
        // Shown on every installed language; it can only be clicked when Mozilla has a newer version.
        UpdateVisibility = Show(!IsBusy && IsInstalled);
        UpdateEnabled = outdated;
        RemoveVisibility = Show(!IsBusy && IsInstalled);
        BusyVisibility = Show(IsBusy);

        // A list of facts joined by commas, each one translated on its own.
        var facts = new List<string>();
        if (IsBusy) facts.Add(Loc.Get("Translation_RowDownloading"));
        else if (IsInstalled)
        {
            facts.Add(Loc.Get("Translation_RowInstalled"));
            facts.Add(Size(Installed.Sum(i => i.Size)));
            if (outdated) facts.Add(Loc.Get("Translation_RowUpdateAvailable"));
            else if (Offer != null) facts.Add(Loc.Get("Translation_RowUpToDate"));
        }
        else if (Offer is { } offer)
        {
            facts.Add(Loc.Format("Translation_RowDownloadSize", Size(offer.DownloadSize)));
            facts.Add(Loc.Format("Translation_RowDiskSize", Size(offer.DiskSize)));
        }
        if (facts.Count > 0 && !IsBusy && Offer is { } o && (o.ToEnglish == null || o.FromEnglish == null))
            facts.Add(o.ToEnglish == null ? Loc.Get("Translation_RowOnlyFromEnglish") : Loc.Get("Translation_RowOnlyToEnglish"));
        Detail = string.Join(", ", facts);
    }

    private static Visibility Show(bool on) => on ? Visibility.Visible : Visibility.Collapsed;

    internal static string Size(long bytes) => Loc.Format("Translation_SizeMB", Math.Max(1, (bytes + 500_000) / 1_000_000));
}

/// <summary>Settings ▸ Translation: the languages from Mozilla's catalog, download, update and remove, and export and
/// import for PCs without internet access.</summary>
public sealed partial class TranslationSettingsPage : Page
{
    /// <summary>The translation model folder, the settings (daily update check), and whether to load Mozilla's list of
    /// languages (not in the tour).</summary>
    public sealed record Parameters(string ModelDirectory, AppSettings Settings, bool LoadCatalog);

    private AppSettings? _settings;

    private static HttpClient Http => ModelStore.Http;

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
        // Closing Settings unloads the page without navigating away from it.
        Loaded += (_, _) => DownloadEnded += OnDownloadEnded;
        Unloaded += (_, _) => DownloadEnded -= OnDownloadEnded;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        var parameters = e.Parameter as Parameters;
        _store = new ModelStore(parameters?.ModelDirectory ?? "");
        _settings = parameters?.Settings;
        AutoCheckBox.IsChecked = _settings?.TranslationAutoCheck == true;
        AutoInstallBox.IsChecked = _settings?.TranslationAutoInstall == true;
        AutoInstallBox.IsEnabled = AutoCheckBox.IsChecked == true;
        BuildRows();
        if (_catalog == null && parameters?.LoadCatalog == true) await LoadCatalogAsync();
    }

    private async Task LoadCatalogAsync()
    {
        CatalogProgress.IsActive = true;
        CatalogStatus.Text = Loc.Get("Translation_CatalogLoading");
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
            ShowInfo(InfoBarSeverity.Warning, Loc.Get("Translation_CatalogFailed"), retry: true);
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
        DiskUsage.Text = installed.Count == 0 ? Loc.Get("Translation_NoneInstalled") : Loc.Format("Translation_DiskUsage", TranslationLanguageRow.Size(used));
        ExportButton.IsEnabled = installed.Count > 0;
        RefreshUpdateAll();
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

    private void AutoCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_settings == null) return;
        _settings.TranslationAutoCheck = AutoCheckBox.IsChecked == true;
        AutoInstallBox.IsEnabled = _settings.TranslationAutoCheck;
        RefreshUpdateAll();
    }

    private void AutoInstall_Changed(object sender, RoutedEventArgs e)
    {
        if (_settings == null) return;
        _settings.TranslationAutoInstall = AutoInstallBox.IsChecked == true;
        RefreshUpdateAll();
    }

    private async void Retry_Click(object sender, RoutedEventArgs e) => await LoadCatalogAsync();

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is TranslationLanguageRow row) await DownloadAsync(row);
    }

    private async void UpdateAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in _rows.Where(r => r.IsOutdated && !r.IsBusy).ToList())
            await DownloadAsync(row);
    }

    private async Task DownloadAsync(TranslationLanguageRow row)
    {
        if (row.IsBusy || _store is not { } store) return;
        var pairs = row.Missing;
        if (pairs.Count == 0) return;

        row.Cancel = new CancellationTokenSource();
        Running[row.Code] = row;
        row.Progress = 0;
        row.Refresh();
        RefreshUpdateAll();
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
        catch (OperationCanceledException) when (row.Cancel.IsCancellationRequested || ModelStore.Stopped) { }
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

        // The page may have been left and opened again meanwhile: the open one shows the result, from disk.
        row.Installed = store.Installed().Where(p => p.From == row.Code || p.To == row.Code).ToList();
        row.Refresh();
        DownloadEnded?.Invoke(row, problem);
    }

    // A download ended, started by this page or an earlier one.
    private static event Action<TranslationLanguageRow, string?>? DownloadEnded;

    private void OnDownloadEnded(TranslationLanguageRow row, string? problem)
    {
        if (problem != null) ShowInfo(InfoBarSeverity.Error, Loc.Format("Translation_DownloadFailed", row.Name, problem));
        BuildRows();
    }

    // Shown while newer versions aren't downloaded on their own; clickable when there is one to download.
    private void RefreshUpdateAll()
    {
        bool automatic = _settings is { TranslationAutoCheck: true, TranslationAutoInstall: true };
        UpdateAllButton.Visibility = automatic ? Visibility.Collapsed : Visibility.Visible;
        UpdateAllButton.IsEnabled = _rows.Any(r => r.IsOutdated && !r.IsBusy);
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
        picker.FileTypeChoices.Add(Loc.Get("Translation_ExportFileType"), new List<string> { ".zip" });
        picker.SuggestedFileName = Loc.Get("Translation_ExportFileName");
        if ((await picker.PickSaveFileAsync())?.Path is not { } path) return;

        var installed = store.Installed();
        string? problem = await Task.Run(() =>
        {
            try { store.Export(installed, path); return null; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return ex.Message; }
        });
        if (problem == null) ShowInfo(InfoBarSeverity.Success, Loc.Format("Translation_ExportSaved", path));
        else ShowInfo(InfoBarSeverity.Error, Loc.Format("Translation_ExportFailed", problem));
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
            ShowInfo(InfoBarSeverity.Success, Loc.Format("Translation_Imported", string.Join(", ", names)));
        }
        else ShowInfo(InfoBarSeverity.Error, Loc.Format("Translation_ImportFailed", problem));
    }

    private void ShowInfo(InfoBarSeverity severity, string message, bool retry = false)
    {
        Info.Severity = severity;
        Info.Message = message;
        RetryButton.Visibility = retry ? Visibility.Visible : Visibility.Collapsed;
        Info.IsOpen = true;
    }
}

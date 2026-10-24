using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TextTemplateManager.Models;

namespace TextTemplateManager.Services.Translation;

/// <summary>What one update check found and did.</summary>
public sealed record TranslationUpdateResult(IReadOnlyList<ModelPair> Outdated, IReadOnlyList<ModelPair> Installed, string? Problem);

/// <summary>Settings ▸ Translation's daily update check: once a day (an hourly tick asks whether a day has passed since
/// the last check) it compares the installed languages with Mozilla's list and, with automatic download on, downloads
/// the newer versions; else it tells the user. Only languages already installed are updated. Off by default; nothing
/// goes online while it is off.</summary>
public sealed class TranslationUpdater : IDisposable
{
    public static readonly TimeSpan Interval = TimeSpan.FromDays(1);
    private static readonly TimeSpan FirstTick = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan TickInterval = TimeSpan.FromHours(1);

    private readonly AppSettings _settings;
    private readonly ModelStore _store;
    private readonly Func<CancellationToken, Task<IReadOnlyList<ModelPair>>> _loadCatalog;
    private readonly Func<ModelPair, CancellationToken, Task> _install;
    private readonly Func<DateTime> _now;
    private readonly Action<string, string>? _notify;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _loop;

    public TranslationUpdater(AppSettings settings, ModelStore store, HttpClient http, Action<string, string>? notify = null)
        : this(settings, store, ct => ModelCatalog.LoadAsync(http, ct), (pair, ct) => store.InstallAsync(http, pair, null, ct),
            () => DateTime.UtcNow, notify) { }

    internal TranslationUpdater(AppSettings settings, ModelStore store, Func<CancellationToken, Task<IReadOnlyList<ModelPair>>> loadCatalog,
        Func<ModelPair, CancellationToken, Task> install, Func<DateTime> now, Action<string, string>? notify)
    {
        _settings = settings;
        _store = store;
        _loadCatalog = loadCatalog;
        _install = install;
        _now = now;
        _notify = notify;
    }

    /// <summary>A check is due when none was made yet or the last one is a day old (or in the future: a clock that
    /// was set back).</summary>
    public static bool IsDue(DateTime? lastUtc, DateTime nowUtc) => lastUtc is not { } last || nowUtc - last >= Interval || last > nowUtc;

    public static DateTime? ParseLastCheck(string? value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t) ? t.ToUniversalTime() : null;

    public void Start()
    {
        _loop = new CancellationTokenSource();
        var ct = _loop.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(FirstTick, ct);
                using var timer = new PeriodicTimer(TickInterval);
                do { await TickAsync(ct); } while (await timer.WaitForNextTickAsync(ct));
            }
            catch (OperationCanceledException) { }
        }, ct);
    }

    /// <summary>Checks when the option is on and a check is due.</summary>
    public async Task<TranslationUpdateResult?> TickAsync(CancellationToken ct = default)
    {
        if (!_settings.TranslationAutoCheck || !IsDue(ParseLastCheck(_settings.TranslationLastCheck), _now())) return null;
        if (_store.Installed().Count == 0) return null;
        var result = await CheckAsync(ct);
        if (result.Problem == null) _settings.TranslationLastCheck = _now().ToString("o", CultureInfo.InvariantCulture);
        return result;
    }

    public async Task<TranslationUpdateResult> CheckAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            IReadOnlyList<ModelPair> catalog;
            try { catalog = await _loadCatalog(ct); }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException
                or InvalidOperationException or KeyNotFoundException) { return new([], [], ex.Message); }

            var installed = _store.Installed();
            var outdated = catalog.Where(p => installed.FirstOrDefault(i => i.From == p.From && i.To == p.To) is { } i && !i.Matches(p)).ToList();
            if (outdated.Count == 0) return new([], [], null);

            var names = string.Join(", ", outdated.SelectMany(p => new[] { p.From, p.To }).Where(c => c != "en").Distinct().Select(TranslationLanguage.NameOf));
            if (!_settings.TranslationAutoInstall)
            {
                _notify?.Invoke("Translation updates", $"Newer versions of {names} are available. Install them in Settings ▸ Translation.");
                return new(outdated, [], null);
            }

            var done = new List<ModelPair>();
            string? problem = null;
            foreach (var pair in outdated)
            {
                try { await _install(pair, ct); done.Add(pair); }
                catch (Exception ex) when (ex is HttpRequestException or System.IO.IOException or System.IO.InvalidDataException
                    or UnauthorizedAccessException or TaskCanceledException) { problem = ex.Message; }
            }
            return new(outdated, done, problem);
        }
        finally { _gate.Release(); }
    }

    public void Dispose()
    {
        _loop?.Cancel();
        _loop?.Dispose();
        _gate.Dispose();
    }
}

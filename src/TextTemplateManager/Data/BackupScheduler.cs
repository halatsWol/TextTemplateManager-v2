using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TextTemplateManager.Data;

/// <summary>Decides when the automatic backups are made, each tier on its own: recent 2 minutes after the last
/// change (keep 3), 2-hourly while running (keep 5), daily at startup and at the first change after midnight
/// (keep 5). A recent or 2-hourly backup that would match the newest of its tier isn't written.</summary>
public sealed class BackupScheduler : IDisposable
{
    public static readonly TimeSpan RecentDelay = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan TwoHourlyInterval = TimeSpan.FromHours(2);
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(30);

    private static readonly Dictionary<BackupTier, int> Keep = new()
    {
        [BackupTier.Recent] = 3,
        [BackupTier.TwoHourly] = 5,
        [BackupTier.Daily] = 5,
    };

    private readonly BackupService _backups;
    private readonly StorageService _storage;
    private readonly BackupSource _source;
    private readonly Func<DateTime> _now;
    private readonly int _quietMs;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<BackupTier, DateTime> _lastDone = new();
    private CancellationTokenSource? _loop;

    private bool _started;
    private long _seenWrites;
    private DateTime? _pendingSince;   // a change no recent backup has covered yet
    private DateTime? _lastChange;

    public BackupScheduler(BackupService backups, StorageService storage, Func<DateTime>? now = null, int quietMs = 2000)
    {
        _backups = backups;
        _storage = storage;
        _source = BackupSource.Live(storage);
        _now = now ?? (() => DateTime.Now);
        _quietMs = quietMs;
    }

    /// <summary>Checks every 30 seconds on a background thread, starting now.</summary>
    public void Start()
    {
        _loop = new CancellationTokenSource();
        var ct = _loop.Token;
        _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TickInterval);
            try
            {
                do
                {
                    try { await TickAsync(); }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Backup] {ex.Message}"); }
                }
                while (await timer.WaitForNextTickAsync(ct));
            }
            catch (OperationCanceledException) { }
        });
    }

    public void Dispose() => _loop?.Cancel();

    private bool _stopped;

    /// <summary>Waits for a running backup, then makes no more (before a restore swaps the files).</summary>
    public async Task StopAsync()
    {
        _loop?.Cancel();
        await _gate.WaitAsync();
        _stopped = true;
        _gate.Release();
    }

    /// <summary>One pass: makes whatever backups are due now.</summary>
    public async Task TickAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_stopped) return;
            DateTime now = _now();
            bool startup = !_started;
            if (startup)
            {
                foreach (var b in _backups.List().Where(b => b.IsValid))
                    if (!_lastDone.TryGetValue(b.Tier, out var t) || b.Created > t) _lastDone[b.Tier] = b.Created;
                _seenWrites = _storage.WriteCount;
                _backups.DeleteStalePartials();
                _started = true;
            }
            NoteChanges(now);

            if (_pendingSince is { } since && now - since >= RecentDelay)
                await RunAsync(BackupTier.Recent, now, _quietMs, null);

            if (!_lastDone.TryGetValue(BackupTier.TwoHourly, out var twoHourly) || now - twoHourly >= TwoHourlyInterval)
                await RunAsync(BackupTier.TwoHourly, now, _quietMs, null);

            bool noDailyToday = !_lastDone.TryGetValue(BackupTier.Daily, out var daily) || daily.Date < now.Date;
            if (noDailyToday && (startup || _lastChange?.Date == now.Date))
                await RunAsync(BackupTier.Daily, now, _quietMs, null);
        }
        finally { _gate.Release(); }
    }

    /// <summary>Before exit: waits for a running backup, then covers changes no recent backup has yet.</summary>
    public async Task FlushAsync(TimeSpan quietTimeout)
    {
        await _gate.WaitAsync();
        try
        {
            if (!_started) return;
            DateTime now = _now();
            NoteChanges(now);
            if (_pendingSince != null) await RunAsync(BackupTier.Recent, now, 500, quietTimeout);
        }
        finally { _gate.Release(); }
    }

    // A completed write since the last look restarts the recent backup's 2 minutes.
    private void NoteChanges(DateTime now)
    {
        long writes = _storage.WriteCount;
        if (writes == _seenWrites) return;
        _seenWrites = writes;
        _pendingSince = now;
        _lastChange = now;
    }

    private async Task RunAsync(BackupTier tier, DateTime now, int quietMs, TimeSpan? quietTimeout)
    {
        // Daily backups are kept even when nothing changed, so each day has one to go back to.
        var newest = tier == BackupTier.Daily ? null
            : _backups.List().FirstOrDefault(b => b.Tier == tier && b.IsValid)?.Manifest;
        var result = await _backups.CreateAsync(tier, _source, quietMs, quietTimeout, newest);

        switch (result.Outcome)
        {
            case BackupOutcome.Created:
                _backups.Prune(tier, Keep[tier]);
                goto case BackupOutcome.Unchanged;
            case BackupOutcome.Unchanged:
            case BackupOutcome.NothingToBackUp:
                _lastDone[tier] = now;
                if (tier == BackupTier.Recent) _pendingSince = null;
                break;
            default:
                System.Diagnostics.Debug.WriteLine($"[Backup] {tier}: {result.Outcome} {result.Problem}");
                break;   // still due; the next pass tries again
        }
    }
}

using System.Collections.Generic;

namespace TextTemplateManager.Services.Platform;

/// <summary>Launches redirected to the first instance while it is still starting. Showing the window then crashed the
/// app (a second Start menu click during a slow first start: no window yet), so they wait here until startup is done.
/// UI thread only.</summary>
public sealed class PendingRedirects
{
    private List<LaunchRequest>? _held = new();

    /// <summary>True while startup isn't done: <paramref name="request"/> is kept for <see cref="Release"/>.</summary>
    public bool Hold(LaunchRequest request)
    {
        if (_held == null) return false;
        _held.Add(request);
        return true;
    }

    /// <summary>Startup is done: the held requests, in order. Later ones aren't held any more.</summary>
    public IReadOnlyList<LaunchRequest> Release()
    {
        var held = _held ?? [];
        _held = null;
        return held;
    }
}

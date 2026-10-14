using System;
using TextTemplateManager.Models;

namespace TextTemplateManager.Tutorial;

public enum Greeting { None, TourOffer, WhatsNew }

/// <summary>What a start shows once the other startup questions are answered: the tour offer until the tour is done
/// with (started, declined, or the first start restored a backup), after that What's new once per newer
/// version. A first run (no version recorded yet) has nothing new to show.</summary>
public static class StartupGreeting
{
    public static Greeting For(AppSettings settings, string version)
    {
        if (!settings.TutorialOfferDone) return Greeting.TourOffer;
        return IsNewer(version, settings.LastRunVersion) ? Greeting.WhatsNew : Greeting.None;
    }

    private static bool IsNewer(string version, string last)
    {
        if (string.IsNullOrEmpty(last)) return false;
        if (Version.TryParse(version, out var v) && Version.TryParse(last, out var l)) return v > l;
        return !string.Equals(version, last, StringComparison.OrdinalIgnoreCase);
    }
}

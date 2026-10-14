using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;

namespace TextTemplateManager.Tutorial;

/// <summary>One step of the guided tour: the control it explains (null: the card shows centered, without a
/// cutout), what to do before showing it (select a sample item, open a page) and what to undo when the tour moves
/// on or ends (a control shown only for the step). <paramref name="Corner"/> puts the card in a corner of the window
/// instead of beside the control, where it would cover what the step is about.</summary>
public sealed record TourStep(string Title, string Text, Func<FrameworkElement?>? Target = null, Func<Task>? Enter = null,
    Action? Leave = null, TourCorner? Corner = null);

public enum TourCorner { TopLeft, TopRight, BottomLeft, BottomRight }

/// <summary>How a part of the tour ended: finished, skipped (or ended from outside), or Back on its first step
/// (the previous part continues).</summary>
public enum TourOutcome { Done, Skipped, BackFromFirst }

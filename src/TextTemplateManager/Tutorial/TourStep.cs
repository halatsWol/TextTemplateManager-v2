using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;

namespace TextTemplateManager.Tutorial;

/// <summary>One step of the guided tour: the control it explains (null: the card shows centered, without a
/// cutout) and what to do before showing it (select a sample item, open a page).</summary>
public sealed record TourStep(string Title, string Text, Func<FrameworkElement?>? Target = null, Func<Task>? Enter = null);

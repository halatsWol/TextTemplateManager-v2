using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TextTemplateManager.Tutorial;
using Windows.Foundation;
using VirtualKey = Windows.System.VirtualKey;

namespace TextTemplateManager.Controls;

/// <summary>The guided tour on top of a window: everything dimmed but the step's control, with the explanation card
/// beside it. The window can't be used while it runs; only the card's buttons (and Esc to skip) work.</summary>
public sealed partial class TutorialOverlay : UserControl
{
    private const double Gap = 12, Edge = 12, Pad = 6, CardWidth = 360;

    private IReadOnlyList<TourStep> _steps = Array.Empty<TourStep>();
    private int _index;
    private int _showing;
    private TaskCompletionSource<bool>? _done;
    private DispatcherQueueTimer? _follow;
    private FrameworkElement? _target;
    private (Rect? Cut, Size Size) _placed;

    public TutorialOverlay()
    {
        InitializeComponent();
        SizeChanged += (_, _) => Place(force: true);
    }

    public bool IsRunning => _done != null;

    /// <summary>Shows the steps one after another. True when the last one was finished with Done; false when the
    /// tour was skipped or ended from outside (<see cref="End"/>).</summary>
    public Task<bool> RunAsync(IReadOnlyList<TourStep> steps)
    {
        _steps = steps;
        _index = 0;
        _done = new TaskCompletionSource<bool>();
        Visibility = Visibility.Visible;
        // The cutout follows its control through resizes, scrolling and layout changes.
        _follow ??= CreateFollowTimer();
        _follow.Start();
        _ = ShowStepAsync();
        return _done.Task;
    }

    /// <summary>Ends the tour as if skipped (the window closes, the app exits).</summary>
    public void End() => Finish(completed: false);

    private DispatcherQueueTimer CreateFollowTimer()
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(200);
        timer.Tick += (_, _) => Place(force: false);
        return timer;
    }

    private void Finish(bool completed)
    {
        if (_done is not { } done) return;
        _done = null;
        _showing++;
        _follow?.Stop();
        _target = null;
        Visibility = Visibility.Collapsed;
        done.TrySetResult(completed);
    }

    private async Task ShowStepAsync()
    {
        var step = _steps[_index];
        int ticket = ++_showing;
        if (step.Enter != null)
        {
            try { await step.Enter(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Tour] step setup failed: {ex.Message}"); }
        }
        if (ticket != _showing || _done == null) return;   // skipped, or another step was asked for meanwhile

        _target = step.Target?.Invoke();
        Counter.Text = $"{_index + 1} / {_steps.Count}";
        StepTitle.Text = step.Title;
        StepText.Text = step.Text;
        BackButton.IsEnabled = _index > 0;
        NextButton.Content = _index == _steps.Count - 1 ? "Done" : "Next";
        UpdateLayout();
        Place(force: true);
        NextButton.Focus(FocusState.Programmatic);
    }

    // The control's bounds on the overlay, with a little room around it; null when it isn't on screen.
    private Rect? TargetBounds()
    {
        if (_target is not { } t || t.XamlRoot == null || t.ActualWidth < 1 || t.ActualHeight < 1) return null;
        try
        {
            var r = t.TransformToVisual(Root).TransformBounds(new Rect(0, 0, t.ActualWidth, t.ActualHeight));
            double left = Math.Max(0, r.X - Pad), top = Math.Max(0, r.Y - Pad);
            double right = Math.Min(Root.ActualWidth, r.X + r.Width + Pad), bottom = Math.Min(Root.ActualHeight, r.Y + r.Height + Pad);
            return right - left < 2 || bottom - top < 2 ? null : new Rect(left, top, right - left, bottom - top);
        }
        catch { return null; }   // not in the visual tree any more
    }

    private void Place(bool force)
    {
        if (_done == null) return;
        double w = Root.ActualWidth, h = Root.ActualHeight;
        if (w < 1 || h < 1) return;
        var cut = TargetBounds();
        if (!force && _placed.Cut == cut && _placed.Size == new Size(w, h)) return;
        _placed = (cut, new Size(w, h));

        var dim = new GeometryGroup { FillRule = FillRule.EvenOdd };
        dim.Children.Add(new RectangleGeometry { Rect = new Rect(0, 0, w, h) });
        if (cut is { } c) dim.Children.Add(new RectangleGeometry { Rect = c });
        Dim.Data = dim;

        if (cut is { } hole)
        {
            Highlight.Margin = new Thickness(hole.X, hole.Y, 0, 0);
            Highlight.Width = hole.Width;
            Highlight.Height = hole.Height;
            Highlight.Visibility = Visibility.Visible;
        }
        else Highlight.Visibility = Visibility.Collapsed;

        Card.Width = Math.Min(CardWidth, Math.Max(200, w - 2 * Edge));
        Card.Measure(new Size(Card.Width, double.PositiveInfinity));
        var size = Card.DesiredSize;
        var (x, y) = cut is { } t ? Beside(t, size, w, h) : ((w - size.Width) / 2, (h - size.Height) / 2);
        Card.Margin = new Thickness(Math.Clamp(x, Edge, Math.Max(Edge, w - size.Width - Edge)),
                                    Math.Clamp(y, Edge, Math.Max(Edge, h - size.Height - Edge)), 0, 0);
    }

    // The first side of the control with room for the card: right, left, below, above. Along that side the card
    // slides to stay in the window; with no room anywhere it goes below and overlaps (clamped by the caller).
    internal static (double X, double Y) Beside(Rect t, Size card, double w, double h)
    {
        double alongX = Math.Clamp(t.X, Edge, Math.Max(Edge, w - Edge - card.Width));
        double alongY = Math.Clamp(t.Y, Edge, Math.Max(Edge, h - Edge - card.Height));
        if (t.Right + Gap + card.Width <= w - Edge) return (t.Right + Gap, alongY);
        if (t.X - Gap - card.Width >= Edge) return (t.X - Gap - card.Width, alongY);
        if (t.Bottom + Gap + card.Height <= h - Edge) return (alongX, t.Bottom + Gap);
        if (t.Y - Gap - card.Height >= Edge) return (alongX, t.Y - Gap - card.Height);
        return (alongX, t.Bottom + Gap);
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_index >= _steps.Count - 1) { Finish(completed: true); return; }
        _index++;
        _ = ShowStepAsync();
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_index == 0) return;
        _index--;
        _ = ShowStepAsync();
    }

    private void Skip_Click(object sender, RoutedEventArgs e) => Finish(completed: false);

    // A click on the dimmed window keeps the keyboard on the card, so Esc and Enter still work.
    private void Root_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        e.Handled = true;
        NextButton.Focus(FocusState.Programmatic);
    }

    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape) return;
        e.Handled = true;
        Finish(completed: false);
    }
}

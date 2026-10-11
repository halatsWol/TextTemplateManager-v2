using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using TextTemplateManager.ViewModels;
using Windows.Foundation;
using Windows.UI.Text;

namespace TextTemplateManager.Controls;

/// <summary>The floating shortcut-conflict panel. It sits on a Canvas, anchors to its top-right until the
/// user drags it elsewhere, and hides a dismissed set of notes until the set changes.</summary>
public sealed partial class ConflictPanel : UserControl
{
    private readonly ConflictNotice _notice = new();
    private bool _shown;
    private bool _userMoved;   // dragged by the user: stop anchoring it to the top-right
    private bool _dragging;
    private Point _dragStart;

    public ConflictPanel()
    {
        InitializeComponent();
        // The first show can come before the canvas has a real width (and before this is loaded), so
        // anchor again on load, on the first real layout and whenever the canvas is resized.
        Loaded += (_, _) =>
        {
            if (Host is { } canvas) canvas.SizeChanged += (_, _) => KeepAnchored();
            KeepAnchored();
        };
        SizeChanged += (_, _) => KeepAnchored();
    }

    private void KeepAnchored()
    {
        if (_shown && !_userMoved) AnchorTopRight();
    }

    private Canvas? Host => VisualTreeHelper.GetParent(this) as Canvas;

    public void Show(ConflictReport report)
    {
        LineList.ItemsSource = report.Lines;
        if (!_notice.ShouldShow(report))
        {
            Visibility = Visibility.Collapsed;
            _shown = false;
            return;
        }

        DismissButton.Visibility = _notice.CanDismiss ? Visibility.Visible : Visibility.Collapsed;
        Visibility = Visibility.Visible;
        if (!_userMoved) AnchorTopRight();
        if (!_shown)
        {
            var fadeIn = (Storyboard)Resources["FadeIn"];
            fadeIn.Stop();
            fadeIn.Begin();
            _shown = true;
        }
    }

    public static FontWeight WeightOf(ConflictLineKind kind) => kind switch
    {
        ConflictLineKind.Heading => FontWeights.Bold,
        ConflictLineKind.Key or ConflictLineKind.NoteHeading => FontWeights.SemiBold,
        _ => FontWeights.Normal,
    };

    public static Thickness MarginOf(ConflictLineKind kind) => kind switch
    {
        ConflictLineKind.Heading => new Thickness(0, 5, 0, 5),
        ConflictLineKind.Key => new Thickness(10, 2, 0, 0),
        ConflictLineKind.NoteHeading => new Thickness(0, 8, 0, 4),
        _ => new Thickness(20, 0, 0, 0),
    };

    public static Brush ForegroundOf(ConflictLineKind kind) =>
        (Brush)Application.Current.Resources[kind is ConflictLineKind.Note or ConflictLineKind.NoteHeading
            ? "TextFillColorSecondaryBrush"
            : "TextFillColorPrimaryBrush"];

    private void Dismiss_Click(object sender, RoutedEventArgs e)
    {
        _notice.Dismiss();
        Visibility = Visibility.Collapsed;
        _shown = false;
    }

    // Places the panel in the top-right of the canvas. Skips while the layout isn't ready (width 0).
    private void AnchorTopRight()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (Host is not { } canvas) return;
            canvas.UpdateLayout();
            UpdateLayout();
            if (canvas.ActualWidth <= 0 || ActualWidth <= 0) return;
            Canvas.SetLeft(this, Math.Max(0, canvas.ActualWidth - ActualWidth - 16));
            Canvas.SetTop(this, 52);   // clear the menu bar / top-right buttons
        });
    }

    private void Panel_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        // Pressing the dismiss button must not start a drag.
        if (Host is not { } canvas || IsWithin(e.OriginalSource as DependencyObject, DismissButton)) return;
        _dragging = true;
        _dragStart = e.GetCurrentPoint(canvas).Position;
        Panel.CapturePointer(e.Pointer);
    }

    private void Panel_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _dragging = false;
        Panel.ReleasePointerCapture(e.Pointer);
    }

    private void Panel_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging || Host is not { } canvas) return;
        _userMoved = true;

        var point = e.GetCurrentPoint(canvas).Position;
        double left = Canvas.GetLeft(this) + point.X - _dragStart.X;
        double top = Canvas.GetTop(this) + point.Y - _dragStart.Y;
        Canvas.SetLeft(this, Math.Max(0, Math.Min(left, canvas.ActualWidth - ActualWidth)));
        Canvas.SetTop(this, Math.Max(0, Math.Min(top, canvas.ActualHeight - ActualHeight)));
        _dragStart = point;
    }

    private static bool IsWithin(DependencyObject? node, DependencyObject ancestor)
    {
        while (node != null)
        {
            if (node == ancestor) return true;
            node = VisualTreeHelper.GetParent(node);
        }
        return false;
    }
}

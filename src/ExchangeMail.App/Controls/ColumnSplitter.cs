using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace ExchangeMail.App.Controls;

/// <summary>
/// Drag handle on the left edge of its grid column that resizes <see cref="TargetColumn"/> (the column on either side
/// of the edge). Star columns absorb the change and keep at least <see cref="MinRemaining"/> pixels.
/// Arrow keys resize too, so the handle works without a mouse.
/// </summary>
public sealed partial class ColumnSplitter : UserControl
{
    private const double HandleWidth = 8;
    private const double KeyStep = 16;

    private readonly Border _line = new() { Width = 2, HorizontalAlignment = HorizontalAlignment.Center, Opacity = 0 };
    private bool _dragging;
    private double _startX;
    private double _startWidth;

    public ColumnSplitter()
    {
        Width = HandleWidth;
        HorizontalAlignment = HorizontalAlignment.Left;
        Margin = new Thickness(-HandleWidth / 2, 0, 0, 0);
        IsTabStop = true;
        UseSystemFocusVisuals = true;
        // A transparent (not null) background is what makes the empty area hit-testable.
        Content = new Grid { Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), Children = { _line } };
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
        AutomationProperties.SetName(this, Strings.Common_ResizeColumn);
        Canvas.SetZIndex(this, 10);
        Loaded += (_, _) =>
        {
            if (Application.Current.Resources.TryGetValue("AccentFillColorDefaultBrush", out var brush)) _line.Background = (Brush)brush;
        };
    }

    /// <summary>Index of the column to resize: the one left of this handle's edge, or its own column.</summary>
    public int TargetColumn { get; set; }

    /// <summary>Minimum width left for the star (*) columns.</summary>
    public double MinRemaining { get; set; } = 320;

    /// <summary>Raised when a drag or key press finished, with the column's new width.</summary>
    public event EventHandler<double>? Resized;

    private Grid? Host => Parent as Grid;

    private ColumnDefinition? Target =>
        Host is { } grid && TargetColumn >= 0 && TargetColumn < grid.ColumnDefinitions.Count ? grid.ColumnDefinitions[TargetColumn] : null;

    /// <summary>Dragging right widens a column left of the handle and narrows one to its right.</summary>
    private int Direction => TargetColumn < Grid.GetColumn(this) ? 1 : -1;

    protected override void OnPointerEntered(PointerRoutedEventArgs e) => _line.Opacity = 1;

    protected override void OnPointerExited(PointerRoutedEventArgs e)
    {
        if (!_dragging) _line.Opacity = 0;
    }

    protected override void OnPointerPressed(PointerRoutedEventArgs e)
    {
        if (Target is not { } column || Host is not { } grid) return;
        if (!CapturePointer(e.Pointer)) return;
        _dragging = true;
        _startX = e.GetCurrentPoint(grid).Position.X;
        _startWidth = column.ActualWidth;
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerRoutedEventArgs e)
    {
        if (!_dragging || Host is not { } grid) return;
        SetWidth(_startWidth + Direction * (e.GetCurrentPoint(grid).Position.X - _startX));
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerRoutedEventArgs e)
    {
        ReleasePointerCapture(e.Pointer);
        EndDrag();
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerRoutedEventArgs e) => EndDrag();

    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        var step = e.Key switch { VirtualKey.Left => -KeyStep, VirtualKey.Right => KeyStep, _ => 0 };
        if (step == 0 || Target is not { } column) return;
        SetWidth(column.ActualWidth + Direction * step);
        Resized?.Invoke(this, column.Width.Value);
        e.Handled = true;
    }

    private void EndDrag()
    {
        if (!_dragging) return;
        _dragging = false;
        _line.Opacity = 0;
        if (Target is { } column) Resized?.Invoke(this, column.Width.Value);
    }

    private void SetWidth(double width)
    {
        if (Target is not { } column || Host is not { } grid) return;

        // Room the other fixed columns leave, so the star column never disappears.
        var others = grid.ColumnDefinitions.Where(c => c != column && !c.Width.IsStar).Sum(c => c.ActualWidth);
        var max = Math.Min(column.MaxWidth, grid.ActualWidth - others - MinRemaining);
        var min = Math.Max(column.MinWidth, 120);
        column.Width = new GridLength(Math.Round(Math.Clamp(width, min, Math.Max(min, max))));
    }
}

using System.Globalization;
using ExchangeMail.App.Helpers;
using ExchangeMail.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace ExchangeMail.App.Controls;

/// <summary>
/// Outlook-style day/week grid: one column per day, events positioned by time, overlapping events side by side.
/// Events you can edit are moved by dragging (15-minute steps, across days); double-click a slot to create one.
/// </summary>
public sealed partial class WeekGridView : UserControl
{
    private const double HourHeight = 48;
    private const double TimeColumnWidth = 56;
    private const int SnapMinutes = 15;
    private const double DragThreshold = 4;

    private static readonly SolidColorBrush LineBrush = new(ColorHelper.FromArgb(0x30, 0x80, 0x80, 0x80));
    private static readonly SolidColorBrush TodayBrush = new(ColorHelper.FromArgb(0xFF, 0x0F, 0x6C, 0xBD));
    private static readonly SolidColorBrush NowBrush = new(ColorHelper.FromArgb(0xFF, 0xC4, 0x2B, 0x1C));
    private static readonly SolidColorBrush ChipText = new(Colors.White);

    private readonly Grid _header = new() { Padding = new Thickness(0, 0, 0, 4) };
    private readonly Grid _allDay = new();
    private readonly Grid _body = new() { Height = 24 * HourHeight };
    private readonly ScrollViewer _scroll;

    private DateTime _start = DateTime.Today;
    private int _days = 7;
    private IReadOnlyList<CalendarEntryViewModel> _events = [];
    private CalendarEntryViewModel? _selected;
    private bool _scrolledToMorning;

    private DragState? _drag;

    public WeekGridView()
    {
        _scroll = new ScrollViewer
        {
            Content = _body,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Disabled,
        };

        var root = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition(),
            },
        };
        root.Children.Add(_header);
        root.Children.Add(_allDay);
        root.Children.Add(_scroll);
        Grid.SetRow(_allDay, 1);
        Grid.SetRow(_scroll, 2);
        Content = root;

        Loaded += (_, _) => ScrollToMorning();
    }

    /// <summary>An event block was clicked.</summary>
    public event EventHandler<CalendarEntryViewModel>? EventSelected;

    /// <summary>An event block was double-clicked.</summary>
    public event EventHandler<CalendarEntryViewModel>? EventOpened;

    /// <summary>An empty slot was double-clicked; the argument is the slot's local start time.</summary>
    public event EventHandler<DateTime>? SlotDoubleTapped;

    /// <summary>An event was dragged to a new local start time.</summary>
    public event EventHandler<(CalendarEntryViewModel Entry, DateTime NewStart)>? EventMoved;

    public void Show(DateTime start, int days, IReadOnlyList<CalendarEntryViewModel> events, CalendarEntryViewModel? selected)
    {
        _start = start.Date;
        _days = Math.Max(1, days);
        _events = events;
        _selected = selected;
        Render();
    }

    public void Select(CalendarEntryViewModel? selected)
    {
        if (_selected == selected) return;
        _selected = selected;
        Render();
    }

    private void ScrollToMorning()
    {
        if (_scrolledToMorning || !IsLoaded) return;
        _scrolledToMorning = true;
        _scroll.ChangeView(null, 7.5 * HourHeight, null, disableAnimation: true);
    }

    private void Render()
    {
        _drag = null;
        foreach (var grid in new[] { _header, _allDay, _body })
        {
            grid.Children.Clear();
            grid.ColumnDefinitions.Clear();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(TimeColumnWidth) });
            for (var d = 0; d < _days; d++) grid.ColumnDefinitions.Add(new ColumnDefinition());
        }

        _body.Children.Add(TimeLabels());
        var culture = CultureInfo.CurrentCulture;
        var anyAllDay = false;

        for (var d = 0; d < _days; d++)
        {
            var day = _start.AddDays(d);
            var isToday = day == DateTime.Today;

            var title = new TextBlock
            {
                Text = day.ToString(_days == 1 ? "dddd, d MMMM" : "ddd d", culture),
                HorizontalAlignment = HorizontalAlignment.Center,
                FontWeight = isToday ? FontWeights.SemiBold : FontWeights.Normal,
            };
            if (isToday) title.Foreground = TodayBrush;
            AddToColumn(_header, title, d);

            var allDayEvents = _events.Where(e => IsAllDayLike(e) && Covers(e, day)).ToList();
            anyAllDay |= allDayEvents.Count > 0;
            var strip = new StackPanel { Spacing = 2, Margin = new Thickness(2, 0, 2, 4) };
            foreach (var e in allDayEvents) strip.Children.Add(Chip(e, compact: true));
            AddToColumn(_allDay, strip, d);

            AddToColumn(_body, DayColumn(day, isToday), d);
        }

        _allDay.Visibility = anyAllDay ? Visibility.Visible : Visibility.Collapsed;
        DispatcherQueue.TryEnqueue(ScrollToMorning);
    }

    private static void AddToColumn(Grid grid, FrameworkElement element, int day)
    {
        Grid.SetColumn(element, day + 1);
        grid.Children.Add(element);
    }

    private static Canvas TimeLabels()
    {
        var canvas = new Canvas();
        for (var h = 1; h < 24; h++)
        {
            var label = new TextBlock { Text = $"{h:00}:00", FontSize = 11 * Ui.TextScale, Opacity = 0.7 };
            Canvas.SetLeft(label, 8);
            Canvas.SetTop(label, h * HourHeight - 8);
            canvas.Children.Add(label);
        }
        return canvas;
    }

    private Grid DayColumn(DateTime day, bool isToday)
    {
        var lines = new StackPanel();
        for (var h = 0; h < 24; h++)
            lines.Children.Add(new Border { Height = HourHeight, BorderBrush = LineBrush, BorderThickness = new Thickness(1, 0, 0, 1) });

        var canvas = new Canvas { Background = new SolidColorBrush(Colors.Transparent) };
        canvas.DoubleTapped += (_, e) =>
        {
            var minutes = Math.Clamp((int)(e.GetPosition(canvas).Y / HourHeight * 60) / 30 * 30, 0, 23 * 60 + 30);
            SlotDoubleTapped?.Invoke(this, day.AddMinutes(minutes));
        };

        foreach (var layout in LayoutDay(day))
        {
            var chip = Chip(layout.Entry, compact: false);
            chip.Tag = layout;
            chip.Height = Math.Max(20, layout.Height - 2);
            Canvas.SetTop(chip, layout.Top);
            canvas.Children.Add(chip);
        }
        canvas.SizeChanged += (_, _) => PositionChips(canvas);

        var column = new Grid();
        column.Children.Add(lines);
        column.Children.Add(canvas);

        if (isToday)
        {
            var now = DateTime.Now;
            var line = new Rectangle { Height = 2, Fill = NowBrush, IsHitTestVisible = false };
            Canvas.SetTop(line, now.TimeOfDay.TotalHours * HourHeight);
            canvas.Children.Add(line);
            canvas.SizeChanged += (_, _) => line.Width = canvas.ActualWidth;
        }
        return column;
    }

    private static void PositionChips(Canvas canvas)
    {
        var width = canvas.ActualWidth;
        foreach (var child in canvas.Children)
        {
            if (child is not Border { Tag: ChipLayout layout } chip) continue;
            var columnWidth = (width - 4) / layout.Columns;
            Canvas.SetLeft(chip, 2 + layout.Column * columnWidth);
            chip.Width = Math.Max(10, columnWidth - 2);
        }
    }

    /// <summary>Positions the day's timed events; overlapping events share the width in columns.</summary>
    private List<ChipLayout> LayoutDay(DateTime day)
    {
        var dayEnd = day.AddDays(1);
        var items = _events
            .Where(e => !IsAllDayLike(e))
            .Select(e => (Entry: e, Start: Max(e.Event.Start.LocalDateTime, day), End: Min(e.Event.End.LocalDateTime, dayEnd)))
            .Where(x => x.End > x.Start || (x.Start == x.End && x.Start >= day && x.Start < dayEnd))
            .OrderBy(x => x.Start).ThenByDescending(x => x.End - x.Start)
            .ToList();

        var result = new List<ChipLayout>();
        var cluster = new List<(int Index, int Column)>();
        var columnEnds = new List<DateTime>();
        var clusterEnd = DateTime.MinValue;

        void FinishCluster()
        {
            foreach (var (index, column) in cluster)
                result[index] = result[index] with { Column = column, Columns = Math.Max(1, columnEnds.Count) };
            cluster.Clear();
            columnEnds.Clear();
        }

        foreach (var (entry, start, end) in items)
        {
            var effectiveEnd = end > start ? end : start.AddMinutes(15);
            if (cluster.Count > 0 && start >= clusterEnd) FinishCluster();

            var column = columnEnds.FindIndex(e => e <= start);
            if (column < 0)
            {
                column = columnEnds.Count;
                columnEnds.Add(effectiveEnd);
            }
            else columnEnds[column] = effectiveEnd;
            clusterEnd = cluster.Count == 0 ? effectiveEnd : Max(clusterEnd, effectiveEnd);

            result.Add(new ChipLayout(entry, (start - day).TotalHours * HourHeight, (effectiveEnd - start).TotalHours * HourHeight, 0, 1));
            cluster.Add((result.Count - 1, column));
        }
        if (cluster.Count > 0) FinishCluster();
        return result;
    }

    private Border Chip(CalendarEntryViewModel entry, bool compact)
    {
        var text = new StackPanel();
        text.Children.Add(new TextBlock
        {
            Text = entry.Subject,
            FontSize = 12 * Ui.TextScale,
            FontWeight = FontWeights.SemiBold,
            Foreground = ChipText,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = compact ? TextWrapping.NoWrap : TextWrapping.Wrap,
            MaxLines = compact ? 1 : 3,
        });
        if (!compact)
        {
            text.Children.Add(new TextBlock
            {
                Text = entry.HasLocation ? $"{entry.TimeText} · {entry.Location}" : entry.TimeText,
                FontSize = 11 * Ui.TextScale,
                Foreground = ChipText,
                Opacity = 0.9,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }

        var selected = entry == _selected;
        var chip = new Border
        {
            Child = text,
            Background = Ui.Brush(entry.StatusColor),
            Padding = new Thickness(6, 3, 6, 3),
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(selected ? 2 : 0),
            BorderBrush = selected ? (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"] : null,
            Opacity = entry.Event.IsCancelled ? 0.55 : 1,
        };
        ToolTipService.SetToolTip(chip, $"{entry.Subject}\n{entry.TimeText}{(entry.HasLocation ? "\n" + entry.Location : "")}");

        chip.DoubleTapped += (_, e) =>
        {
            e.Handled = true;
            EventOpened?.Invoke(this, entry);
        };
        if (compact)
        {
            chip.Tapped += (_, e) =>
            {
                e.Handled = true;
                EventSelected?.Invoke(this, entry);
            };
        }
        else
        {
            chip.PointerPressed += (s, e) => OnChipPressed((Border)s, entry, e);
            chip.PointerMoved += (s, e) => OnChipMoved((Border)s, e);
            chip.PointerReleased += (s, e) => OnChipReleased((Border)s, e);
            chip.PointerCaptureLost += (s, _) => CancelDrag((Border)s);
        }
        return chip;
    }

    private void OnChipPressed(Border chip, CalendarEntryViewModel entry, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(_body);
        if (!point.Properties.IsLeftButtonPressed) return;
        e.Handled = true;
        _drag = new DragState(chip, entry, point.Position);
        chip.CapturePointer(e.Pointer);
    }

    private void OnChipMoved(Border chip, PointerRoutedEventArgs e)
    {
        if (_drag is not { } drag || drag.Chip != chip) return;
        var position = e.GetCurrentPoint(_body).Position;
        var dx = position.X - drag.Origin.X;
        var dy = position.Y - drag.Origin.Y;

        if (!drag.IsDragging)
        {
            if (Math.Abs(dx) < DragThreshold && Math.Abs(dy) < DragThreshold) return;
            if (!drag.Entry.CanEdit) return; // read-only: treat as a click on release
            drag.IsDragging = true;
            chip.Opacity = 0.75;
            ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeAll);
        }

        var (days, minutes) = Snap(chip, dx, dy);
        chip.RenderTransform = new TranslateTransform { X = days * DayWidth(chip), Y = minutes / 60.0 * HourHeight };
        e.Handled = true;
    }

    private void OnChipReleased(Border chip, PointerRoutedEventArgs e)
    {
        if (_drag is not { } drag || drag.Chip != chip) return;
        e.Handled = true;
        _drag = null;
        chip.ReleasePointerCapture(e.Pointer);
        ProtectedCursor = null;

        if (!drag.IsDragging)
        {
            EventSelected?.Invoke(this, drag.Entry);
            return;
        }

        var position = e.GetCurrentPoint(_body).Position;
        var (days, minutes) = Snap(chip, position.X - drag.Origin.X, position.Y - drag.Origin.Y);
        if (days == 0 && minutes == 0)
        {
            chip.RenderTransform = null;
            chip.Opacity = 1;
            return;
        }
        EventMoved?.Invoke(this, (drag.Entry, drag.Entry.Event.Start.LocalDateTime.AddDays(days).AddMinutes(minutes)));
    }

    private void CancelDrag(Border chip)
    {
        if (_drag is not { } drag || drag.Chip != chip) return;
        _drag = null;
        chip.RenderTransform = null;
        chip.Opacity = 1;
        ProtectedCursor = null;
    }

    private (int Days, int Minutes) Snap(Border chip, double dx, double dy)
    {
        var dayWidth = DayWidth(chip);
        var days = dayWidth > 0 ? (int)Math.Round(dx / dayWidth) : 0;
        var minutes = (int)Math.Round(dy / HourHeight * 60 / SnapMinutes) * SnapMinutes;
        return (days, minutes);
    }

    private static double DayWidth(Border chip) => (chip.Parent as Canvas)?.ActualWidth ?? 0;

    private static bool IsAllDayLike(CalendarEntryViewModel e) =>
        e.Event.IsAllDay || e.Event.End - e.Event.Start >= TimeSpan.FromHours(24);

    private static bool Covers(CalendarEntryViewModel e, DateTime day)
    {
        // All-day items run from local midnight to local midnight (Exchange returns them in UTC).
        var start = e.Event.IsAllDay ? e.Event.Start.LocalDateTime.Date : e.Event.Start.LocalDateTime;
        var end = e.Event.IsAllDay ? e.Event.End.LocalDateTime.Date : e.Event.End.LocalDateTime;
        return start < day.AddDays(1) && end > day;
    }

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;

    private sealed record ChipLayout(CalendarEntryViewModel Entry, double Top, double Height, int Column, int Columns);

    private sealed class DragState(Border chip, CalendarEntryViewModel entry, Point origin)
    {
        public Border Chip { get; } = chip;
        public CalendarEntryViewModel Entry { get; } = entry;
        public Point Origin { get; } = origin;
        public bool IsDragging { get; set; }
    }
}

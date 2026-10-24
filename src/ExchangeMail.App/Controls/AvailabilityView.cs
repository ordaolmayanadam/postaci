using System.Globalization;
using ExchangeMail.App.Helpers;
using ExchangeMail.Core.Models;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace ExchangeMail.App.Controls;

/// <summary>Scheduling-assistant style timeline: one row per attendee with busy blocks and the proposed meeting time.</summary>
public sealed partial class AvailabilityView : UserControl
{
    private const int FirstHour = 7;
    private const int LastHour = 20;
    private const double RowHeight = 24;
    private const double NameWidth = 170;

    private static readonly SolidColorBrush TrackBrush = new(ColorHelper.FromArgb(0x20, 0x80, 0x80, 0x80));
    private static readonly SolidColorBrush ProposalBrush = new(ColorHelper.FromArgb(0x30, 0x0F, 0x6C, 0xBD));
    private static readonly SolidColorBrush ProposalEdge = new(ColorHelper.FromArgb(0xFF, 0x0F, 0x6C, 0xBD));

    private readonly Grid _grid = new() { ColumnSpacing = 8, RowSpacing = 4 };

    public AvailabilityView()
    {
        _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(NameWidth) });
        _grid.ColumnDefinitions.Add(new ColumnDefinition());
        Content = _grid;
    }

    public void Show(DateTime day, IReadOnlyList<AttendeeAvailability> people, DateTimeOffset? proposedStart, DateTimeOffset? proposedEnd)
    {
        _grid.Children.Clear();
        _grid.RowDefinitions.Clear();
        var windowStart = day.Date.AddHours(FirstHour);
        var windowLength = TimeSpan.FromHours(LastHour - FirstHour);

        double Fraction(DateTime local) => Math.Clamp((local - windowStart) / windowLength, 0, 1);

        // Hour labels.
        _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var labels = new Canvas { Height = 16 };
        var labelItems = new List<(TextBlock Label, double Fraction)>();
        for (var h = FirstHour; h <= LastHour; h += 2)
        {
            var label = new TextBlock { Text = $"{h:00}", FontSize = 11 * Ui.TextScale, Opacity = 0.7 };
            labels.Children.Add(label);
            labelItems.Add((label, (double)(h - FirstHour) / (LastHour - FirstHour)));
        }
        labels.SizeChanged += (_, _) =>
        {
            foreach (var (label, f) in labelItems) Canvas.SetLeft(label, f * labels.ActualWidth - 6);
        };
        Place(labels, 0, 1);

        var row = 1;
        foreach (var person in people)
        {
            _grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(RowHeight) });
            var name = new TextBlock
            {
                Text = person.Address,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Opacity = person.Error is null ? 1 : 0.6,
            };
            ToolTipService.SetToolTip(name, person.Error ?? person.Address);
            Place(name, row, 0);

            var track = new Canvas { Background = TrackBrush };
            var blocks = new List<(FrameworkElement Element, double From, double To)>();
            if (person.Error is not null)
            {
                var info = new TextBlock { Text = Strings.Availability_NoInfo, FontSize = 11 * Ui.TextScale, Opacity = 0.7, Margin = new Thickness(6, 3, 0, 0) };
                track.Children.Add(info);
            }
            foreach (var busy in person.Busy)
            {
                var rect = new Rectangle { Fill = Ui.Brush(StatusColor(busy.Status)), Height = RowHeight, RadiusX = 2, RadiusY = 2 };
                ToolTipService.SetToolTip(rect, $"{busy.Start.LocalDateTime.ToString("t", CultureInfo.CurrentCulture)} – {busy.End.LocalDateTime.ToString("t", CultureInfo.CurrentCulture)} · {StatusText(busy.Status)}");
                track.Children.Add(rect);
                blocks.Add((rect, Fraction(busy.Start.LocalDateTime), Fraction(busy.End.LocalDateTime)));
            }
            if (proposedStart is { } ps && proposedEnd is { } pe)
            {
                var proposal = new Rectangle
                {
                    Fill = ProposalBrush,
                    Stroke = ProposalEdge,
                    StrokeThickness = 1.5,
                    Height = RowHeight,
                    IsHitTestVisible = false,
                };
                track.Children.Add(proposal);
                blocks.Add((proposal, Fraction(ps.LocalDateTime), Fraction(pe.LocalDateTime)));
            }
            track.SizeChanged += (_, _) =>
            {
                foreach (var (element, from, to) in blocks)
                {
                    Canvas.SetLeft(element, from * track.ActualWidth);
                    element.Width = Math.Max(2, (to - from) * track.ActualWidth);
                }
            };
            Place(track, row, 1);
            row++;
        }

        // Legend.
        _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var legend = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 4, 0, 0) };
        foreach (var status in new[] { FreeBusyStatus.Busy, FreeBusyStatus.Tentative, FreeBusyStatus.OOF })
        {
            var item = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            item.Children.Add(new Rectangle { Width = 12, Height = 12, Fill = Ui.Brush(StatusColor(status)), RadiusX = 2, RadiusY = 2 });
            item.Children.Add(new TextBlock { Text = StatusText(status), FontSize = 11 * Ui.TextScale });
            legend.Children.Add(item);
        }
        Place(legend, row, 1);
    }

    private void Place(FrameworkElement element, int row, int column)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        _grid.Children.Add(element);
    }

    private static string StatusColor(FreeBusyStatus status) => status switch
    {
        FreeBusyStatus.Tentative => "#C19C00",
        FreeBusyStatus.OOF => "#8764B8",
        FreeBusyStatus.WorkingElsewhere or FreeBusyStatus.NoData => "#8A8A8A",
        _ => "#0F6CBD",
    };

    private static string StatusText(FreeBusyStatus status) => status switch
    {
        FreeBusyStatus.Tentative => Strings.Response_Tentative,
        FreeBusyStatus.OOF => Strings.FreeBusy_Away,
        FreeBusyStatus.WorkingElsewhere => Strings.Availability_WorkingElsewhere,
        FreeBusyStatus.NoData => Strings.Availability_NoInfo,
        _ => Strings.FreeBusy_Busy,
    };
}

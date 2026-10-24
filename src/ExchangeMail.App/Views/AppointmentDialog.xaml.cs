using ExchangeMail.Core.Models;
using ExchangeMail.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ExchangeMail.App.Views;

public delegate Task<IReadOnlyList<AttendeeAvailability>> AvailabilityLookup(IReadOnlyList<string> attendees, DateTime day);

/// <summary>Creates a new appointment/meeting or edits an existing one.</summary>
public sealed partial class AppointmentDialog : ContentDialog
{
    private static readonly int?[] ReminderMinutes = [null, 5, 15, 30, 60, 1440];
    private static readonly FreeBusyStatus[] ShowAs = [FreeBusyStatus.Free, FreeBusyStatus.Tentative, FreeBusyStatus.Busy, FreeBusyStatus.OOF];

    private readonly AvailabilityLookup? _availability;
    private string _initialBody = "";
    private string _initialAttendees = "";

    public AppointmentDialog(DateTime suggestedStart, AvailabilityLookup? availability = null)
    {
        InitializeComponent();
        _availability = availability;

        // Round up to the next half hour.
        var start = suggestedStart.Date.AddMinutes(Math.Ceiling(suggestedStart.TimeOfDay.TotalMinutes / 30) * 30);
        var end = start.AddMinutes(30);
        StartDate.Date = new DateTimeOffset(start.Date);
        StartTime.Time = start.TimeOfDay;
        EndDate.Date = new DateTimeOffset(end.Date);
        EndTime.Time = end.TimeOfDay;
        Loaded += (_, _) => SubjectBox.Focus(FocusState.Programmatic);
    }

    /// <summary>Opens the dialog prefilled with an existing event.</summary>
    public static AppointmentDialog ForEdit(CalendarDetailViewModel detail, AvailabilityLookup? availability = null)
    {
        var e = detail.Event;
        var dialog = new AppointmentDialog(e.Start.LocalDateTime, availability)
        {
            Title = e.IsMeeting ? "Toplantıyı düzenle" : "Etkinliği düzenle",
        };
        dialog.SubjectBox.Text = e.Subject;
        dialog.LocationBox.Text = e.Location;
        dialog.AllDayCheck.IsChecked = e.IsAllDay;

        var start = e.Start.LocalDateTime;
        var end = e.End.LocalDateTime;
        dialog.StartDate.Date = new DateTimeOffset(start.Date);
        dialog.StartTime.Time = start.TimeOfDay;
        // An all-day event ends at midnight after its last day; show that last day.
        dialog.EndDate.Date = new DateTimeOffset(e.IsAllDay ? end.Date.AddDays(-1) : end.Date);
        dialog.EndTime.Time = end.TimeOfDay;

        dialog._initialAttendees = string.Join("; ", detail.Source.RequiredAttendees.Select(a => a.Mailbox.Address));
        dialog.AttendeesBox.Text = dialog._initialAttendees;
        dialog._initialBody = MailFormat.HtmlToPlainText(detail.Source.Body);
        dialog.BodyBox.Text = dialog._initialBody;

        var minutes = detail.Source.ReminderMinutes;
        dialog.ReminderBox.SelectedIndex = minutes is null
            ? 0
            : Array.FindIndex(ReminderMinutes, m => m is { } x && x >= minutes) is var i and >= 0 ? i : ReminderMinutes.Length - 1;
        dialog.ShowAsBox.SelectedIndex = Math.Max(0, Array.IndexOf(ShowAs, e.FreeBusy));
        return dialog;
    }

    /// <summary>The appointment to create or the new values, set when the user pressed Kaydet with valid input.</summary>
    public NewAppointment? Result { get; private set; }

    /// <summary>When editing: the description text was changed (otherwise the original formatted body is kept).</summary>
    public bool BodyChanged => BodyBox.Text != _initialBody;

    /// <summary>When editing: the attendee list was changed.</summary>
    public bool AttendeesChanged => AttendeesBox.Text.Trim() != _initialAttendees;

    private void OnAllDayChanged(object sender, RoutedEventArgs e)
    {
        var timed = AllDayCheck.IsChecked != true;
        StartTime.IsEnabled = timed;
        EndTime.IsEnabled = timed;
    }

    private async void OnShowAvailability(object sender, RoutedEventArgs e)
    {
        if (_availability is null || StartDate.Date is not { } date) return;
        var attendees = RecipientParser.Parse(AttendeesBox.Text, out _);
        AvailabilityRing.IsActive = true;
        try
        {
            var people = await _availability(attendees, date.Date);
            var (start, end) = SelectedTimes();
            Availability.Show(date.Date, people, start, end);
            Availability.Visibility = Visibility.Visible;
            AvailabilityHint.Text = date.Date.ToString("D") + " · 07:00–20:00";
        }
        catch (Exception ex)
        {
            AvailabilityHint.Text = "Uygunluk alınamadı: " + ex.Message;
        }
        finally
        {
            AvailabilityRing.IsActive = false;
        }
    }

    private (DateTimeOffset? Start, DateTimeOffset? End) SelectedTimes()
    {
        if (StartDate.Date is not { } startDate || EndDate.Date is not { } endDate || AllDayCheck.IsChecked == true) return (null, null);
        return (Local(startDate.Date + StartTime.Time), Local(endDate.Date + EndTime.Time));
    }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        ErrorBar.IsOpen = false;
        var error = TryBuild(out var appointment);
        if (error is not null)
        {
            ErrorBar.Message = error;
            ErrorBar.IsOpen = true;
            args.Cancel = true;
            return;
        }
        Result = appointment;
    }

    private string? TryBuild(out NewAppointment? appointment)
    {
        appointment = null;
        if (string.IsNullOrWhiteSpace(SubjectBox.Text)) return "Konu girin.";
        if (StartDate.Date is not { } startDate || EndDate.Date is not { } endDate) return "Başlangıç ve bitiş tarihini seçin.";

        var attendees = RecipientParser.Parse(AttendeesBox.Text, out var invalid);
        if (invalid.Count > 0) return "Geçersiz katılımcı adresi: " + string.Join(", ", invalid);

        var allDay = AllDayCheck.IsChecked == true;
        var start = allDay ? Local(startDate.Date) : Local(startDate.Date + StartTime.Time);
        var end = allDay ? Local(endDate.Date.AddDays(1)) : Local(endDate.Date + EndTime.Time);
        if (end <= start) return "Bitiş zamanı başlangıçtan sonra olmalı.";

        appointment = new NewAppointment
        {
            Subject = SubjectBox.Text.Trim(),
            Location = LocationBox.Text.Trim(),
            Start = start,
            End = end,
            IsAllDay = allDay,
            HtmlBody = MailFormat.PlainTextToHtml(BodyBox.Text),
            RequiredAttendees = attendees,
            ReminderMinutes = ReminderMinutes[Math.Clamp(ReminderBox.SelectedIndex, 0, ReminderMinutes.Length - 1)],
            FreeBusy = ShowAs[Math.Clamp(ShowAsBox.SelectedIndex, 0, ShowAs.Length - 1)],
        };
        return null;
    }

    private static DateTimeOffset Local(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Local));
}

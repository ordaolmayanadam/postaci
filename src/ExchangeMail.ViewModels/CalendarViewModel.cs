using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExchangeMail.Core.Ews;
using ExchangeMail.Core.Models;

namespace ExchangeMail.ViewModels;

public enum CalendarRange { Day, Week, Month }

/// <summary>What a change to an occurrence of a recurring event applies to.</summary>
public enum RecurrenceScope { Occurrence, Series }

public sealed partial class CalendarViewModel : ObservableObject
{
    private readonly MailSession _session;
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _detailCts;
    private bool _initialized;

    public CalendarViewModel(MailSession session)
    {
        _session = session;
        Events.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
    }

    public ObservableCollection<CalendarEntryViewModel> Events { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RangeText))]
    private DateTime _anchorDate = DateTime.Today;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RangeText), nameof(RangeIndex), nameof(IsGridMode), nameof(IsListMode), nameof(DayCount))]
    private CalendarRange _range = CalendarRange.Week;

    /// <summary>Day and week are shown as an hour grid, month as an agenda list.</summary>
    public bool IsGridMode => Range != CalendarRange.Month;
    public bool IsListMode => !IsGridMode;
    public int DayCount => Range == CalendarRange.Day ? 1 : 7;

    /// <summary>Raised after the visible range was (re)loaded; the hour grid redraws itself.</summary>
    public event EventHandler? EventsReloaded;

    /// <summary>Raised by the Edit command; the view shows the editor and calls <see cref="UpdateAsync"/>.</summary>
    public event EventHandler<CalendarDetailViewModel>? EditRequested;

    /// <summary>ComboBox-friendly view of <see cref="Range"/>.</summary>
    public int RangeIndex
    {
        get => (int)Range;
        set => Range = (CalendarRange)Math.Clamp(value, 0, 2);
    }

    [ObservableProperty]
    private CalendarEntryViewModel? _selectedEvent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetail))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand), nameof(RespondCommand), nameof(EditCommand))]
    private CalendarDetailViewModel? _detail;

    public bool HasDetail => Detail is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _isLoading;

    [ObservableProperty] private bool _isLoadingDetail;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string _statusText = "";

    public bool IsEmpty => Events.Count == 0 && !IsLoading;

    public string RangeText
    {
        get
        {
            var (start, end) = GetRange();
            var culture = CultureInfo.CurrentCulture;
            return Range switch
            {
                CalendarRange.Day => start.ToString("D", culture),
                CalendarRange.Month => start.ToString("Y", culture),
                _ => $"{MailFormat.DayMonth(start)} – {MailFormat.DayMonthYear(end.AddDays(-1))}",
            };
        }
    }

    /// <summary>Asks the user a yes/no question; supplied by the view.</summary>
    public Func<string, string, Task<bool>>? ConfirmAsync { get; set; }

    /// <summary>Asks whether a change applies to one occurrence or the whole series (null: cancelled); supplied by the view.</summary>
    public Func<string, string, Task<RecurrenceScope?>>? AskScopeAsync { get; set; }

    public string AccountEmail => _session.Account.Email;

    public Task InitializeAsync()
    {
        if (_initialized) return Task.CompletedTask;
        _initialized = true;
        return LoadAsync();
    }

    partial void OnAnchorDateChanged(DateTime value) => _ = LoadAsync();
    partial void OnRangeChanged(CalendarRange value) => _ = LoadAsync();
    partial void OnSelectedEventChanged(CalendarEntryViewModel? value) => _ = LoadDetailAsync(value);

    public (DateTime Start, DateTime End) GetRange()
    {
        var day = AnchorDate.Date;
        switch (Range)
        {
            case CalendarRange.Day:
                return (day, day.AddDays(1));
            case CalendarRange.Month:
                var first = new DateTime(day.Year, day.Month, 1);
                return (first, first.AddMonths(1));
            default:
                var firstDay = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
                var start = day.AddDays(-(((int)day.DayOfWeek - (int)firstDay + 7) % 7));
                return (start, start.AddDays(7));
        }
    }

    [RelayCommand]
    private Task RefreshAsync() => LoadAsync();

    [RelayCommand]
    private void Today() => AnchorDate = DateTime.Today;

    [RelayCommand]
    private void Previous() => AnchorDate = Range switch
    {
        CalendarRange.Day => AnchorDate.AddDays(-1),
        CalendarRange.Month => AnchorDate.AddMonths(-1),
        _ => AnchorDate.AddDays(-7),
    };

    [RelayCommand]
    private void Next() => AnchorDate = Range switch
    {
        CalendarRange.Day => AnchorDate.AddDays(1),
        CalendarRange.Month => AnchorDate.AddMonths(1),
        _ => AnchorDate.AddDays(7),
    };

    public async Task LoadAsync()
    {
        _loadCts?.Cancel();
        var cts = _loadCts = new CancellationTokenSource();
        var (start, end) = GetRange();
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var events = await _session.Client.FindAppointmentsAsync(new DateTimeOffset(start), new DateTimeOffset(end), cts.Token);
            if (cts.IsCancellationRequested) return;

            var selectedId = SelectedEvent?.Event.ItemId.Id;
            Events.Clear();
            DateTime? lastDay = null;
            foreach (var e in events)
            {
                // Events that started before the visible range are listed under its first day.
                var day = e.Start.ToLocalTime().Date < start ? start : e.Start.ToLocalTime().Date;
                Events.Add(new CalendarEntryViewModel(e, day, showDayHeader: day != lastDay));
                lastDay = day;
            }
            SelectedEvent = Events.FirstOrDefault(e => e.Event.ItemId.Id == selectedId);
            StatusText = events.Count == 0 ? "" : Loc.Count(events.Count, Strings.Calendar_EventCount_One, Strings.Calendar_EventCount);
            EventsReloaded?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
        }
        catch (EwsException ex)
        {
            ErrorMessage = Loc.Format(Strings.Calendar_LoadFailed, ex.Message);
        }
        finally
        {
            if (_loadCts == cts) IsLoading = false;
        }
    }

    private async Task LoadDetailAsync(CalendarEntryViewModel? entry)
    {
        _detailCts?.Cancel();
        if (entry is null)
        {
            Detail = null;
            return;
        }

        var cts = _detailCts = new CancellationTokenSource();
        IsLoadingDetail = true;
        try
        {
            var detail = await _session.Client.GetAppointmentAsync(entry.Event.ItemId, cts.Token);
            if (!cts.IsCancellationRequested)
                Detail = DetailOf(detail);
        }
        catch (OperationCanceledException)
        {
        }
        catch (EwsException ex)
        {
            if (!cts.IsCancellationRequested)
            {
                Detail = null;
                ErrorMessage = Loc.Format(Strings.Calendar_OpenFailed, ex.Message);
            }
        }
        finally
        {
            if (_detailCts == cts) IsLoadingDetail = false;
        }
    }

    private CalendarDetailViewModel DetailOf(CalendarEventDetail detail, bool isSeries = false) =>
        new(detail, _session.Preferences.AlwaysShowRemoteImages, isSeries,
            MeetingLink.Find(detail.JoinUrl, detail.Event.Location, detail.Body, MeetingLink.ParseHosts(_session.Preferences.MeetingHosts)));

    /// <summary>Creates an appointment or meeting from the editor dialog and reloads the view.</summary>
    public async Task<bool> CreateAsync(NewAppointment appointment)
    {
        try
        {
            await _session.Client.CreateAppointmentAsync(appointment);
            AnchorDate = appointment.Start.LocalDateTime.Date;
            await LoadAsync();
            // After reloading, which shows the event count there.
            StatusText = appointment.RequiredAttendees.Count + appointment.OptionalAttendees.Count > 0
                ? Strings.Calendar_MeetingCreated
                : Strings.Calendar_EventCreated;
            return true;
        }
        catch (Exception ex) when (ex is EwsException or ArgumentException)
        {
            ErrorMessage = Loc.Format(Strings.Calendar_CreateFailed, ex.Message);
            return false;
        }
    }

    private bool CanEdit() => Detail?.CanEdit == true;

    /// <summary>Opens the editor; for an occurrence of a recurring event, on that occurrence or on the whole series.</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task EditAsync()
    {
        if (Detail is not { } detail) return;
        if (detail.Event.IsRecurring && AskScopeAsync is not null)
        {
            var scope = await AskScopeAsync(Strings.Calendar_RecurringEditTitle, Strings.Calendar_RecurringEditQuestion);
            if (scope is null) return;
            if (scope == RecurrenceScope.Series)
            {
                if (await LoadSeriesAsync(detail) is not { } series) return;
                detail = series;
            }
        }
        EditRequested?.Invoke(this, detail);
    }

    private async Task<CalendarDetailViewModel?> LoadSeriesAsync(CalendarDetailViewModel occurrence)
    {
        try
        {
            var master = await _session.Client.GetRecurringMasterAsync(occurrence.Event.ItemId);
            return DetailOf(master, isSeries: true);
        }
        catch (EwsException ex)
        {
            ErrorMessage = Loc.Format(Strings.Calendar_SeriesLoadFailed, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Saves the editor's changes (to the series when <paramref name="original"/> is one). Attendees of a meeting
    /// receive the update. Unchanged times are not written, so the changed occurrences of a series keep their times.
    /// </summary>
    public async Task<bool> UpdateAsync(CalendarDetailViewModel original, NewAppointment changes, bool bodyChanged, bool attendeesChanged)
    {
        var notify = original.Event.IsMeeting || changes.RequiredAttendees.Count > 0;
        var timesChanged = changes.Start != original.Event.Start || changes.End != original.Event.End || changes.IsAllDay != original.Event.IsAllDay;
        try
        {
            await _session.Client.UpdateAppointmentAsync(original.Event.ItemId, changes, bodyChanged, attendeesChanged, timesChanged, notify);
            await LoadAsync();
            if (SelectedEvent is { } selected) await LoadDetailAsync(selected);
            StatusText = notify ? Strings.Calendar_MeetingUpdated : Strings.Calendar_EventUpdated;
            return true;
        }
        catch (Exception ex) when (ex is EwsException or ArgumentException)
        {
            ErrorMessage = Loc.Format(Strings.Calendar_UpdateFailed, ex.Message);
            return false;
        }
    }

    /// <summary>Moves an event to a new start time, keeping its duration (drag and drop in the grid).</summary>
    public async Task MoveAsync(CalendarEntryViewModel entry, DateTime newLocalStart)
    {
        if (!entry.CanEdit) return;
        var e = entry.Event;
        var start = new DateTimeOffset(DateTime.SpecifyKind(newLocalStart, DateTimeKind.Local));
        if (start == e.Start) return;

        if (e.IsMeeting && ConfirmAsync is not null
            && !await ConfirmAsync(Strings.Calendar_MoveMeetingTitle,
                Loc.Format(Strings.Calendar_MoveMeetingQuestion, start.LocalDateTime.ToString("g", CultureInfo.CurrentCulture))))
        {
            EventsReloaded?.Invoke(this, EventArgs.Empty); // put the dragged block back
            return;
        }

        var moved = false;
        try
        {
            await _session.Client.MoveAppointmentAsync(e.ItemId, start, start + (e.End - e.Start), notifyAttendees: e.IsMeeting);
            moved = true;
        }
        catch (EwsException ex)
        {
            ErrorMessage = Loc.Format(Strings.Calendar_MoveFailed, ex.Message);
        }
        await LoadAsync();
        if (moved) StatusText = Strings.Calendar_EventMoved;
    }

    /// <summary>Gives an event a new end time, keeping its start (its lower edge dragged in the grid).</summary>
    public async Task ResizeAsync(CalendarEntryViewModel entry, DateTime newLocalEnd)
    {
        if (!entry.CanEdit) return;
        var e = entry.Event;
        var end = new DateTimeOffset(DateTime.SpecifyKind(newLocalEnd, DateTimeKind.Local));
        if (end == e.End || end <= e.Start) return;

        if (e.IsMeeting && ConfirmAsync is not null
            && !await ConfirmAsync(Strings.Calendar_ResizeMeetingTitle,
                Loc.Format(Strings.Calendar_ResizeMeetingQuestion, end.LocalDateTime.ToString("t", CultureInfo.CurrentCulture))))
        {
            EventsReloaded?.Invoke(this, EventArgs.Empty); // put the resized block back
            return;
        }

        var resized = false;
        try
        {
            await _session.Client.MoveAppointmentAsync(e.ItemId, e.Start, end, notifyAttendees: e.IsMeeting);
            resized = true;
        }
        catch (EwsException ex)
        {
            ErrorMessage = Loc.Format(Strings.Calendar_ResizeFailed, ex.Message);
        }
        await LoadAsync();
        if (resized) StatusText = Strings.Calendar_EventResized;
    }

    /// <summary>Free/busy of the given attendees and the current user on <paramref name="day"/>.</summary>
    public async Task<IReadOnlyList<AttendeeAvailability>> GetAvailabilityAsync(IReadOnlyList<string> attendees, DateTime day)
    {
        var addresses = new[] { _session.Account.Email }.Concat(attendees)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var start = new DateTimeOffset(DateTime.SpecifyKind(day.Date, DateTimeKind.Local));
        return await _session.Client.GetAvailabilityAsync(addresses, start, start.AddDays(1));
    }

    private bool CanDelete() => Detail?.CanDelete == true;

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task DeleteAsync()
    {
        if (Detail is not { } detail) return;
        var cancelMeeting = detail.IsOrganizer && detail.Event.IsMeeting && !detail.Event.IsCancelled;
        var scope = RecurrenceScope.Occurrence;
        if (detail.Event.IsRecurring && AskScopeAsync is not null)
        {
            // The choice between this occurrence and the series is also the confirmation.
            var chosen = cancelMeeting
                ? await AskScopeAsync(Strings.Calendar_RecurringCancelTitle, Strings.Calendar_RecurringCancelQuestion)
                : await AskScopeAsync(Strings.Calendar_RecurringDeleteTitle, Strings.Calendar_RecurringDeleteQuestion);
            if (chosen is not { } s) return;
            scope = s;
        }
        else
        {
            var question = cancelMeeting
                ? Strings.Calendar_CancelMeetingQuestion
                : Strings.Calendar_DeleteEventQuestion;
            if (ConfirmAsync is not null && !await ConfirmAsync(cancelMeeting ? Strings.Calendar_CancelMeetingTitle : Strings.Calendar_DeleteEventTitle, question))
                return;
        }

        try
        {
            var id = scope == RecurrenceScope.Series
                ? (await _session.Client.GetRecurringMasterAsync(detail.Event.ItemId)).Event.ItemId
                : detail.Event.ItemId;
            await _session.Client.DeleteAppointmentAsync(id, sendCancellations: cancelMeeting);
            SelectedEvent = null;
            await LoadAsync();
        }
        catch (EwsException ex)
        {
            ErrorMessage = Loc.Format(Strings.Common_DeleteFailed, ex.Message);
        }
    }

    private bool CanRespond(string? response) => Detail?.CanRespond == true;

    /// <param name="response">"Accept", "Tentative" or "Decline".</param>
    [RelayCommand(CanExecute = nameof(CanRespond))]
    private async Task RespondAsync(string? response)
    {
        if (Detail is not { } detail || !Enum.TryParse<MeetingResponse>(response, out var kind)) return;
        try
        {
            await _session.Client.RespondToMeetingAsync(detail.Event.ItemId, kind);
            await LoadAsync();
            StatusText = kind switch
            {
                MeetingResponse.Accept => Strings.Meeting_Accepted,
                MeetingResponse.Tentative => Strings.Meeting_TentativeSent,
                _ => Strings.Meeting_Declined,
            };
        }
        catch (EwsException ex)
        {
            ErrorMessage = Loc.Format(Strings.Meeting_ResponseFailed, ex.Message);
        }
    }
}

public sealed class CalendarEntryViewModel(CalendarEvent e, DateTime day, bool showDayHeader)
{
    public CalendarEvent Event { get; } = e;
    public bool ShowDayHeader { get; } = showDayHeader;

    public string DayHeader
    {
        get
        {
            var text = day.ToString("dddd", CultureInfo.CurrentCulture) + ", " + MailFormat.DayMonth(day);
            if (day == DateTime.Today) return Strings.Calendar_Today + " · " + text;
            if (day == DateTime.Today.AddDays(1)) return Strings.Calendar_Tomorrow + " · " + text;
            return text;
        }
    }

    public string TimeText => Event.IsAllDay
        ? Strings.Calendar_AllDay
        : $"{Event.Start.LocalDateTime.ToString("t", CultureInfo.CurrentCulture)} – {Event.End.LocalDateTime.ToString("t", CultureInfo.CurrentCulture)}";

    public string Subject => (Event.IsCancelled ? Strings.Calendar_CancelledPrefix : "") + (string.IsNullOrWhiteSpace(Event.Subject) ? Strings.Common_NoSubject : Event.Subject);
    public string Location => Event.Location;
    public bool HasLocation => !string.IsNullOrWhiteSpace(Event.Location);
    public string ResponseText => CalendarFormat.Response(Event);

    public string AccessibleName => AccessibleText.Join(Subject, DayHeader, TimeText, Location, ResponseText);
    public override string ToString() => AccessibleName;

    /// <summary>Colour of the side bar: busy = accent, tentative = amber, free = grey, cancelled = red.</summary>
    /// <summary>Only your own appointments and meetings you organize can be changed.</summary>
    public bool CanEdit => (!Event.IsMeeting || Event.MyResponse == ResponseType.Organizer) && !Event.IsCancelled;

    public string StatusColor => Event.IsCancelled ? "#C42B1C"
        : Event.FreeBusy switch
        {
            FreeBusyStatus.Free => "#8A8A8A",
            FreeBusyStatus.Tentative => "#C19C00",
            FreeBusyStatus.OOF => "#8764B8",
            _ => "#0F6CBD",
        };
}

public sealed partial class CalendarDetailViewModel(CalendarEventDetail detail, bool allowRemoteContent, bool isSeries = false, string? joinUrl = null)
    : ObservableObject
{
    public CalendarEventDetail Source => detail;
    public CalendarEvent Event => detail.Event;

    /// <summary>The online meeting the event links to (see <see cref="MeetingLink"/>), or null.</summary>
    public string? JoinUrl => joinUrl;
    public bool HasJoinUrl => joinUrl is not null;
    public string JoinTooltip => joinUrl is null ? "" : Loc.Format(Strings.Meeting_JoinTooltip, new Uri(joinUrl).Host);

    /// <summary>The whole series of a recurring event (its start and end are the first occurrence's).</summary>
    public bool IsSeries => isSeries;
    public bool CanEdit => IsOrganizer && !Event.IsCancelled;
    public string Subject => string.IsNullOrWhiteSpace(Event.Subject) ? Strings.Common_NoSubject : Event.Subject;

    public string WhenText
    {
        get
        {
            var culture = CultureInfo.CurrentCulture;
            var start = Event.Start.ToLocalTime();
            var end = Event.End.ToLocalTime();
            if (Event.IsAllDay)
            {
                var firstDay = start.Date;
                var lastDay = end.Date.AddDays(-1);
                return lastDay <= firstDay
                    ? firstDay.ToString("D", culture) + " · " + Strings.Calendar_AllDay
                    : $"{MailFormat.DayMonth(firstDay)} – {lastDay.ToString("D", culture)} · {Strings.Calendar_AllDay}";
            }
            return start.Date == end.Date
                ? $"{start.ToString("D", culture)} · {start.ToString("t", culture)} – {end.ToString("t", culture)}"
                : $"{start.ToString("f", culture)} – {end.ToString("f", culture)}";
        }
    }

    public string Location => Event.Location;
    public bool HasLocation => !string.IsNullOrWhiteSpace(Event.Location);
    public string OrganizerText => Event.Organizer is { } o ? Loc.Format(Strings.Calendar_OrganizerFormat, o.Display) : "";
    public bool IsOrganizer => Event.MyResponse == ResponseType.Organizer || !Event.IsMeeting;
    public string ResponseText => CalendarFormat.Response(Event);

    public string AttendeesText => string.Join(", ", detail.RequiredAttendees.Select(CalendarFormat.Attendee));
    public string OptionalAttendeesText => string.Join(", ", detail.OptionalAttendees.Select(CalendarFormat.Attendee));
    public bool HasAttendees => detail.RequiredAttendees.Count > 0;
    public bool HasOptionalAttendees => detail.OptionalAttendees.Count > 0;

    /// <summary>Attendees can accept/decline; organizers cannot.</summary>
    public bool CanRespond => Event.IsMeeting && !IsOrganizer && !Event.IsCancelled;

    /// <summary>Organizers cancel, attendees remove cancelled meetings; attendees decline instead of deleting.</summary>
    public bool CanDelete => IsOrganizer || Event.IsCancelled;

    public string DisplayHtml => MailFormat.PrepareForDisplay(detail.Body, detail.IsHtml, allowRemoteContent);
}

public static class CalendarFormat
{
    public static string Response(CalendarEvent e) => !e.IsMeeting ? "" : e.MyResponse switch
    {
        ResponseType.Organizer => Strings.Response_Organizer,
        ResponseType.Accept => Strings.Response_Accepted,
        ResponseType.Tentative => Strings.Response_Tentative,
        ResponseType.Decline => Strings.Response_Declined,
        _ => Strings.Response_NotResponded,
    };

    public static string Attendee(Attendee a) => a.Mailbox.Display + a.Response switch
    {
        ResponseType.Accept => " ✓",
        ResponseType.Decline => " ✗",
        ResponseType.Tentative => " ?",
        _ => "",
    };
}

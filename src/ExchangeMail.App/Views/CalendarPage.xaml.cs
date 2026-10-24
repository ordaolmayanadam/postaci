using System.ComponentModel;
using ExchangeMail.App.Helpers;
using ExchangeMail.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace ExchangeMail.App.Views;

public sealed partial class CalendarPage : Page
{
    private readonly HtmlViewer _body;
    private bool _syncingMonthView;

    public CalendarPage()
    {
        InitializeComponent();
        _body = new HtmlViewer(BodyView);

        var preferences = App.Preferences;
        // The month view scales with its column; by default it gets its natural size at the chosen text size.
        Ui.RestoreWidth(SidebarColumn, preferences.CalendarSidebarWidth, 300 * Ui.TextScale + 16);
        Ui.RestoreWidth(DetailColumn, preferences.CalendarDetailPaneWidth);
        SidebarSplitter.Resized += (_, width) => Ui.SaveWidth(w => preferences.CalendarSidebarWidth = w, width);
        DetailSplitter.Resized += (_, width) => Ui.SaveWidth(w => preferences.CalendarDetailPaneWidth = w, width);

        WeekGrid.EventSelected += (_, entry) => ViewModel.SelectedEvent = entry;
        WeekGrid.EventOpened += (_, entry) =>
        {
            ViewModel.SelectedEvent = entry;
            if (entry.CanEdit) _ = EditWhenLoadedAsync(entry);
        };
        WeekGrid.SlotDoubleTapped += async (_, start) => await CreateEventAsync(start);
        WeekGrid.EventMoved += async (_, move) => await ViewModel.MoveAsync(move.Entry, move.NewStart);
    }

    public CalendarViewModel ViewModel { get; private set; } = null!;

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is not CalendarViewModel vm || vm == ViewModel) return;

        ViewModel = vm;
        ViewModel.ConfirmAsync = (title, message) => Dialogs.ConfirmAsync(XamlRoot, title, message);
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ViewModel.EventsReloaded += (_, _) => ShowGrid();
        ViewModel.EditRequested += async (_, detail) => await EditEventAsync(detail);
        Bindings.Update();
        SyncMonthView();

        try
        {
            await _body.InitializeAsync();
        }
        catch (Exception ex)
        {
            ViewModel.ErrorMessage = Loc.Format(Strings.Calendar_WebViewFailed, ex.Message);
        }

        await ViewModel.InitializeAsync();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(CalendarViewModel.Detail):
                _body.Show(ViewModel.Detail?.DisplayHtml);
                break;
            case nameof(CalendarViewModel.AnchorDate):
                SyncMonthView();
                break;
            case nameof(CalendarViewModel.SelectedEvent):
                WeekGrid.Select(ViewModel.SelectedEvent);
                break;
        }
    }

    private void ShowGrid()
    {
        if (!ViewModel.IsGridMode) return;
        WeekGrid.Show(ViewModel.GetRange().Start, ViewModel.DayCount, ViewModel.Events.ToList(), ViewModel.SelectedEvent);
    }

    private void SyncMonthView()
    {
        _syncingMonthView = true;
        try
        {
            var date = new DateTimeOffset(ViewModel.AnchorDate);
            MonthView.SelectedDates.Clear();
            MonthView.SelectedDates.Add(date);
            MonthView.SetDisplayDate(date);
        }
        finally
        {
            _syncingMonthView = false;
        }
    }

    private void OnSelectedDatesChanged(CalendarView sender, CalendarViewSelectedDatesChangedEventArgs args)
    {
        if (_syncingMonthView || args.AddedDates.Count == 0) return;
        ViewModel.AnchorDate = args.AddedDates[0].Date;
    }

    private async void OnNewEvent(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var day = ViewModel.AnchorDate.Date;
        await CreateEventAsync(day == DateTime.Today ? DateTime.Now : day.AddHours(9));
    }

    private async Task CreateEventAsync(DateTime start)
    {
        var dialog = new AppointmentDialog(start, ViewModel.GetAvailabilityAsync) { XamlRoot = XamlRoot, RequestedTheme = ActualTheme };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && dialog.Result is { } appointment)
            await ViewModel.CreateAsync(appointment);
    }

    private async Task EditEventAsync(CalendarDetailViewModel detail)
    {
        var dialog = AppointmentDialog.ForEdit(detail, ViewModel.GetAvailabilityAsync);
        dialog.XamlRoot = XamlRoot;
        dialog.RequestedTheme = ActualTheme;
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && dialog.Result is { } changes)
            await ViewModel.UpdateAsync(detail, changes, dialog.BodyChanged, dialog.AttendeesChanged);
    }

    /// <summary>Double-clicking a block selects it; the editor opens once its details have loaded.</summary>
    private async Task EditWhenLoadedAsync(CalendarEntryViewModel entry)
    {
        for (var i = 0; i < 50; i++)
        {
            if (ViewModel.SelectedEvent != entry) return;
            if (ViewModel.Detail?.Event.ItemId.Id == entry.Event.ItemId.Id && !ViewModel.IsLoadingDetail)
            {
                if (ViewModel.EditCommand.CanExecute(null)) ViewModel.EditCommand.Execute(null);
                return;
            }
            await Task.Delay(100);
        }
    }

    private void OnErrorClosed(InfoBar sender, object args) => ViewModel.ErrorMessage = null;
}

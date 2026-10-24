using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExchangeMail.Core.Ews;
using ExchangeMail.Core.Models;

namespace ExchangeMail.ViewModels;

/// <summary>Out of Office ("Otomatik yanıtlar"): edited on the settings page, shown as a banner while active.</summary>
public sealed partial class AutoReplyViewModel(MailSession session) : ObservableObject
{
    private bool _loaded;

    [ObservableProperty] private bool _isEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditSchedule))]
    private bool _isScheduled;

    [ObservableProperty] private DateTimeOffset? _startDate = DateTimeOffset.Now.Date;
    [ObservableProperty] private TimeSpan _startTime = new(9, 0, 0);
    [ObservableProperty] private DateTimeOffset? _endDate = DateTimeOffset.Now.Date.AddDays(1);
    [ObservableProperty] private TimeSpan _endTime = new(9, 0, 0);
    [ObservableProperty] private string _internalMessage = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditExternal))]
    private bool _replyToExternal;

    /// <summary>0 = only my contacts, 1 = everyone outside the organization.</summary>
    [ObservableProperty] private int _externalAudienceIndex = 1;

    [ObservableProperty] private string _externalMessage = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(TurnOffCommand))]
    private bool _isBusy;

    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _infoMessage;

    /// <summary>Replies are currently being sent (drives the banner).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BannerText))]
    private bool _isActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BannerText))]
    private AutoReplySettings? _serverState;

    public bool CanEditSchedule => IsScheduled;
    public bool CanEditExternal => ReplyToExternal;

    public string BannerText
    {
        get
        {
            if (ServerState is not { } s) return "";
            var culture = CultureInfo.CurrentCulture;
            return s.State == AutoReplyState.Scheduled && s.End is { } end
                ? $"Otomatik yanıtlar {end.ToLocalTime().ToString("d MMMM HH:mm", culture)} tarihine kadar gönderiliyor."
                : "Otomatik yanıtlar açık. Size gelen iletilere otomatik yanıt gönderiliyor.";
        }
    }

    public async Task LoadAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            Apply(await session.Client.GetAutoReplyAsync(session.Account.Email));
            _loaded = true;
        }
        catch (EwsException ex)
        {
            ErrorMessage = "Otomatik yanıt ayarları okunamadı: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Apply(AutoReplySettings s)
    {
        ServerState = s;
        IsActive = s.IsActive(DateTimeOffset.Now);
        IsEnabled = s.State != AutoReplyState.Disabled;
        IsScheduled = s.State == AutoReplyState.Scheduled;
        if (s.Start is { } start && s.End is { } end && s.State == AutoReplyState.Scheduled)
        {
            StartDate = start.ToLocalTime().Date;
            StartTime = start.ToLocalTime().TimeOfDay;
            EndDate = end.ToLocalTime().Date;
            EndTime = end.ToLocalTime().TimeOfDay;
        }
        InternalMessage = MailFormat.HtmlToPlainText(s.InternalReplyHtml);
        ExternalMessage = MailFormat.HtmlToPlainText(s.ExternalReplyHtml);
        ReplyToExternal = s.ExternalAudience != ExternalAudience.None;
        ExternalAudienceIndex = s.ExternalAudience == ExternalAudience.Known ? 0 : 1;
    }

    private bool CanSave() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        ErrorMessage = null;
        InfoMessage = null;
        if (!_loaded) await LoadAsync();

        DateTimeOffset? start = null, end = null;
        if (IsEnabled && IsScheduled)
        {
            if (StartDate is not { } sd || EndDate is not { } ed)
            {
                ErrorMessage = "Başlangıç ve bitiş tarihini seçin.";
                return;
            }
            start = new DateTimeOffset(DateTime.SpecifyKind(sd.Date + StartTime, DateTimeKind.Local));
            end = new DateTimeOffset(DateTime.SpecifyKind(ed.Date + EndTime, DateTimeKind.Local));
            if (end <= start)
            {
                ErrorMessage = "Bitiş zamanı başlangıçtan sonra olmalı.";
                return;
            }
        }
        if (IsEnabled && string.IsNullOrWhiteSpace(InternalMessage))
        {
            ErrorMessage = "Kuruluş içindekilere gidecek yanıt metnini yazın.";
            return;
        }

        var settings = new AutoReplySettings(
            !IsEnabled ? AutoReplyState.Disabled : IsScheduled ? AutoReplyState.Scheduled : AutoReplyState.Enabled,
            start,
            end,
            MailFormat.PlainTextToHtml(InternalMessage),
            MailFormat.PlainTextToHtml(ReplyToExternal ? ExternalMessage : ""),
            !ReplyToExternal ? ExternalAudience.None : ExternalAudienceIndex == 0 ? ExternalAudience.Known : ExternalAudience.All);
        await SaveCoreAsync(settings, settings.State == AutoReplyState.Disabled ? "Otomatik yanıtlar kapatıldı." : "Otomatik yanıtlar kaydedildi.");
    }

    /// <summary>The banner's "Kapat" button.</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private Task TurnOffAsync()
    {
        IsEnabled = false;
        var current = ServerState ?? new AutoReplySettings(AutoReplyState.Disabled, null, null, "", "", ExternalAudience.None);
        return SaveCoreAsync(current with { State = AutoReplyState.Disabled }, "Otomatik yanıtlar kapatıldı.");
    }

    private async Task SaveCoreAsync(AutoReplySettings settings, string doneText)
    {
        IsBusy = true;
        try
        {
            await session.Client.SetAutoReplyAsync(session.Account.Email, settings);
            Apply(settings);
            InfoMessage = doneText;
        }
        catch (Exception ex) when (ex is EwsException or ArgumentException)
        {
            ErrorMessage = "Otomatik yanıtlar kaydedilemedi: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}

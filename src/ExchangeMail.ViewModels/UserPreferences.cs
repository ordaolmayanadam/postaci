using CommunityToolkit.Mvvm.ComponentModel;

namespace ExchangeMail.ViewModels;

public enum MarkAsReadMode { WhenSelected, AfterDelay, Never }

public enum AppTheme { System, Light, Dark }

/// <summary>User-adjustable behaviour, stored in preferences.json next to settings.json.</summary>
public sealed partial class UserPreferences : ObservableObject
{
    [ObservableProperty] private MarkAsReadMode _markAsRead = MarkAsReadMode.WhenSelected;
    [ObservableProperty] private int _markAsReadDelaySeconds = 5;
    [ObservableProperty] private int _refreshIntervalSeconds = 60;
    [ObservableProperty] private bool _alwaysShowRemoteImages;
    /// <summary>Plain-text signature from earlier versions; superseded by <see cref="SignatureHtml"/>.</summary>
    [ObservableProperty] private string _signature = "";
    [ObservableProperty] private string _signatureHtml = "";
    [ObservableProperty] private AppTheme _theme = AppTheme.System;
    [ObservableProperty] private bool _showNotifications = true;

    /// <summary>Closing the main window hides it to the notification area instead of quitting.</summary>
    [ObservableProperty] private bool _minimizeToTray = true;

    /// <summary>The "still running in the tray" hint was shown once.</summary>
    [ObservableProperty] private bool _trayHintShown;

    /// <summary>Interface font; empty = Windows default (Segoe UI Variable). Applied at startup.</summary>
    [ObservableProperty] private string _uiFontFamily = "";

    /// <summary>Interface text size in percent of the default. Applied at startup.</summary>
    [ObservableProperty] private int _uiTextScale = 100;

    /// <summary>Default font of the reading pane; empty = Segoe UI.</summary>
    [ObservableProperty] private string _readingFontFamily = "";

    /// <summary>Also replace fonts that messages set themselves.</summary>
    [ObservableProperty] private bool _readingFontOverride;

    /// <summary>Reading pane zoom in percent.</summary>
    [ObservableProperty] private int _readingZoom = 100;

    /// <summary>Font of new messages; empty = Segoe UI.</summary>
    [ObservableProperty] private string _composeFontFamily = "";

    [ObservableProperty] private int _composeFontSize = MailFormat.DefaultComposeFontSize;

    /// <summary>Pane widths in pixels set with the column splitters; 0 = the layout's default.</summary>
    [ObservableProperty] private int _mailFolderPaneWidth;
    [ObservableProperty] private int _mailListPaneWidth;
    [ObservableProperty] private int _calendarSidebarWidth;
    [ObservableProperty] private int _calendarDetailPaneWidth;

    /// <summary>Folder rows with less spacing.</summary>
    [ObservableProperty] private bool _compactFolderList = true;

    /// <summary>The message list groups mail by conversation.</summary>
    [ObservableProperty] private bool _conversationView;

    /// <summary>Interface language: "tr", "en", or empty to follow Windows. Applied at startup.</summary>
    [ObservableProperty] private string _language = "";

    /// <summary>The signature as HTML, converting a legacy plain-text signature if needed. Empty when none.</summary>
    public string GetSignatureHtml() =>
        !string.IsNullOrWhiteSpace(SignatureHtml) ? SignatureHtml
        : !string.IsNullOrWhiteSpace(Signature) ? MailFormat.PlainTextToHtml(Signature.TrimEnd())
        : "";
}

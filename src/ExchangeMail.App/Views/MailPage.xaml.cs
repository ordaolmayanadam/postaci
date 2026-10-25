using System.ComponentModel;
using ExchangeMail.App.Controls;
using ExchangeMail.App.Helpers;
using ExchangeMail.Core.Models;
using ExchangeMail.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using Windows.System;

namespace ExchangeMail.App.Views;

/// <summary>Mail view. Cached by the shell frame, so it is initialized once per session.</summary>
public sealed partial class MailPage : Page
{
    private readonly HtmlViewer _body;
    private MessageDetailViewModel? _shownDetail;

    /// <summary>Rows being dragged from the message list, until the drag ends.</summary>
    private IReadOnlyList<MessageItemViewModel>? _dragged;

    /// <summary>Set while the list selection is changed from the view model, so it is not echoed back.</summary>
    private bool _syncingSelection;

    public MailPage()
    {
        InitializeComponent();
        _body = new HtmlViewer(BodyView);

        var preferences = App.Preferences;
        Ui.RestoreWidth(FolderColumn, preferences.MailFolderPaneWidth);
        Ui.RestoreWidth(ListColumn, preferences.MailListPaneWidth);
        Ui.SaveWidthOnResize(FolderSplitter, FolderColumn, w => preferences.MailFolderPaneWidth = w);
        Ui.SaveWidthOnResize(ListSplitter, ListColumn, w => preferences.MailListPaneWidth = w);

        ApplyFolderDensity(preferences.CompactFolderList);
        preferences.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(UserPreferences.CompactFolderList)) ApplyFolderDensity(preferences.CompactFolderList);
        };
    }

    /// <summary>
    /// Folder row height: compact (32 px) or the standard list spacing (40 px). The style covers rows created later;
    /// rows already on screen are adjusted directly, so a change in Settings applies right away without reloading.
    /// </summary>
    private void ApplyFolderDensity(bool compact)
    {
        var minHeight = compact ? 32d : 40d;
        var padding = compact ? new Thickness(12, 0, 8, 0) : new Thickness(14, 0, 12, 0);

        var style = new Style(typeof(ListViewItem));
        if (Application.Current.Resources.TryGetValue("DefaultListViewItemStyle", out var standard) && standard is Style basedOn)
            style.BasedOn = basedOn;
        style.Setters.Add(new Setter(MinHeightProperty, minHeight));
        style.Setters.Add(new Setter(PaddingProperty, padding));
        FolderList.ItemContainerStyle = style;

        foreach (var item in FolderList.Items)
        {
            if (FolderList.ContainerFromItem(item) is ListViewItem row)
            {
                row.MinHeight = minHeight;
                row.Padding = padding;
            }
        }
    }

    public MainViewModel ViewModel { get; private set; } = null!;

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is not MainViewModel vm || vm == ViewModel) return;

        ViewModel = vm;
        ViewModel.ConfirmAsync = (title, message) => Dialogs.ConfirmAsync(XamlRoot, title, message);
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        Bindings.Update();

        try
        {
            await _body.InitializeAsync();
        }
        catch (Exception ex)
        {
            ViewModel.ErrorMessage = Loc.Format(Strings.Mail_WebViewFailed, ex.Message);
        }

        await ViewModel.InitializeAsync();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedItems))
        {
            SyncListSelection();
            return;
        }
        if (e.PropertyName != nameof(MainViewModel.Current)) return;
        if (_shownDetail is not null) _shownDetail.PropertyChanged -= OnDetailPropertyChanged;
        _shownDetail = ViewModel.Current;
        if (_shownDetail is not null) _shownDetail.PropertyChanged += OnDetailPropertyChanged;
        _body.Show(_shownDetail?.DisplayHtml);
        ShowAddresses(_shownDetail?.Detail);
    }

    private void ShowAddresses(MailDetail? detail)
    {
        FillAddresses(ToLine, detail?.To ?? []);
        FillAddresses(CcLine, detail?.Cc ?? []);
        _ = ShowSenderPhotoAsync(detail);
    }

    /// <summary>The sender's directory photo instead of initials, when the directory has one.</summary>
    private async Task ShowSenderPhotoAsync(MailDetail? detail)
    {
        SenderPicture.ProfilePicture = null;
        if (detail?.From?.Address is not { } address) return;
        var photo = await ViewModel.GetPhotoAsync(address);
        if (photo is null || ViewModel.Current?.Detail != detail) return; // another message was opened meanwhile
        try
        {
            using var stream = new MemoryStream(photo);
            var image = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
            await image.SetSourceAsync(stream.AsRandomAccessStream());
            if (ViewModel.Current?.Detail == detail) SenderPicture.ProfilePicture = image;
        }
        catch (Exception)
        {
            // An unreadable photo keeps the initials.
        }
    }

    private void OnFromClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Current?.Detail.From is { } from) ShowPersonCard(from, FromButton);
    }

    /// <summary>One link per address, separated like Outlook's "; ". Link colors come from the theme.</summary>
    private void FillAddresses(TextBlock line, IReadOnlyList<MailboxAddress> people)
    {
        line.Inlines.Clear();
        for (var i = 0; i < people.Count; i++)
        {
            if (i > 0) line.Inlines.Add(new Run { Text = "; " });
            var person = people[i];
            var link = new Hyperlink { UnderlineStyle = UnderlineStyle.None };
            link.Inlines.Add(new Run { Text = person.Display });
            link.Click += (_, _) => ShowPersonCard(person, line);
            line.Inlines.Add(link);
        }
    }

    private void OnSenderPictureTapped(object sender, TappedRoutedEventArgs e)
    {
        if (ViewModel.Current?.Detail.From is { } from && sender is FrameworkElement anchor) ShowPersonCard(from, anchor);
    }

    private void ShowPersonCard(MailboxAddress person, FrameworkElement anchor)
    {
        var card = new PersonCardViewModel(ViewModel.Client);
        // Popups don't inherit the window's theme; give the flyout the page's own.
        var theme = Ui.ThemeOf(this);
        var presenterStyle = new Style(typeof(FlyoutPresenter));
        presenterStyle.Setters.Add(new Setter(RequestedThemeProperty, theme));
        Flyout? flyout = null;
        flyout = new Flyout
        {
            Content = new PersonCard(card, theme, address =>
            {
                flyout?.Hide();
                ViewModel.NewMailTo(address);
            }),
            FlyoutPresenterStyle = presenterStyle,
            Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft,
        };
        flyout.ShowAt(anchor);
        _ = card.LoadAsync(person);
    }

    private void OnDetailPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MessageDetailViewModel.DisplayHtml)) _body.Show(_shownDetail?.DisplayHtml);
    }

    private async void OnSearchSubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        ViewModel.SearchText = args.QueryText;
        await ViewModel.SearchCommand.ExecuteAsync(null);
    }

    private void OnFocusSearch(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        SearchBox.Focus(FocusState.Keyboard);
    }

    private async void OnToggleConversation(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: MessageItemViewModel row }) await ViewModel.ToggleConversationCommand.ExecuteAsync(row);
    }

    private async void OnMessageListKeyDown(object sender, KeyRoutedEventArgs e)
    {
        // Right opens the selected conversation, Left closes it (or goes back up from one of its messages).
        if (e.Key is VirtualKey.Right or VirtualKey.Left && ViewModel.SelectedMessage is { } row)
        {
            var open = e.Key == VirtualKey.Right;
            if (row.CanExpand && row.IsExpanded != open)
            {
                e.Handled = true;
                await ViewModel.ToggleConversationCommand.ExecuteAsync(row);
                return;
            }
            if (!open && row.Parent is { } parent)
            {
                e.Handled = true;
                ViewModel.SelectedMessage = parent;
                return;
            }
        }

        if (e.Key == VirtualKey.Delete && ViewModel.DeleteCommand.CanExecute(null))
        {
            e.Handled = true;
            ViewModel.DeleteCommand.Execute(null);
        }
    }

    private void OnMessageSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection || ViewModel is null) return;
        ViewModel.SetSelection(MessageList.SelectedItems.OfType<MessageItemViewModel>().ToList());
    }

    /// <summary>Shows a selection the view model made itself (after a delete, from a notification).</summary>
    private void SyncListSelection()
    {
        var wanted = ViewModel.SelectedItems;
        var shown = MessageList.SelectedItems.OfType<MessageItemViewModel>().ToList();
        if (shown.Count == wanted.Count && shown.All(wanted.Contains)) return;

        _syncingSelection = true;
        try
        {
            MessageList.SelectedItems.Clear();
            foreach (var item in wanted) MessageList.SelectedItems.Add(item);
        }
        finally
        {
            _syncingSelection = false;
        }
        if (wanted.Count > 0) MessageList.ScrollIntoView(wanted[0]);
    }

    private void OnMessagesDragStarting(object sender, DragItemsStartingEventArgs e)
    {
        _dragged = e.Items.OfType<MessageItemViewModel>().ToList();
        e.Data.RequestedOperation = DataPackageOperation.Move;
        e.Data.SetText(string.Join(Environment.NewLine, _dragged.Select(m => m.Subject)));
    }

    private void OnMessagesDragCompleted(ListViewBase sender, DragItemsCompletedEventArgs args) => _dragged = null;

    private void OnFolderDragOver(object sender, DragEventArgs e)
    {
        if (_dragged is { Count: > 0 } && sender is Panel { DataContext: FolderViewModel folder } row && folder != ViewModel.SelectedFolder)
        {
            e.AcceptedOperation = DataPackageOperation.Move;
            e.DragUIOverride.Caption = Loc.Format(Strings.Mail_MoveToFolder, folder.DisplayName);
            row.Style = Ui.AppStyle("FolderRowDropTargetStyle");
        }
        else
        {
            e.AcceptedOperation = DataPackageOperation.None;
        }
    }

    private void OnFolderDragLeave(object sender, DragEventArgs e)
    {
        if (sender is Panel row) row.Style = Ui.AppStyle("FolderRowStyle");
    }

    private async void OnFolderDrop(object sender, DragEventArgs e)
    {
        if (sender is not Panel { DataContext: FolderViewModel folder } row || _dragged is not { Count: > 0 } items) return;
        row.Style = Ui.AppStyle("FolderRowStyle");
        _dragged = null;
        await ViewModel.MoveAsync(items, folder);
    }

    private void OnMessageDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (ViewModel.EditDraftCommand.CanExecute(null)) ViewModel.EditDraftCommand.Execute(null);
    }

    private void OnErrorClosed(InfoBar sender, object args) => ViewModel.ErrorMessage = null;

    private void OnShowRemoteContent(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Current is { } current) current.AllowRemoteContent = true;
    }

    private void OnMoveFlyoutOpening(object sender, object e)
    {
        MoveFlyout.Items.Clear();
        foreach (var folder in ViewModel.Folders)
        {
            if (folder == ViewModel.SelectedFolder) continue;
            MoveFlyout.Items.Add(new MenuFlyoutItem
            {
                Text = new string(' ', folder.Depth) + folder.DisplayName,
                Icon = new FontIcon { Glyph = folder.Glyph },
                Command = ViewModel.MoveToCommand,
                CommandParameter = folder,
            });
        }
    }

    private async void OnAttachmentClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not AttachmentViewModel attachment) return;
        try
        {
            var extension = attachment.Info.IsItemAttachment ? ".eml" : Path.GetExtension(attachment.Name);
            var picker = new FileSavePicker
            {
                SuggestedFileName = attachment.Info.IsItemAttachment ? attachment.Name : Path.GetFileNameWithoutExtension(attachment.Name),
                SuggestedStartLocation = PickerLocationId.Downloads,
            };
            picker.FileTypeChoices.Add(
                extension.Length > 0 ? Loc.Format(Strings.Mail_FileTypeFormat, extension.TrimStart('.').ToUpperInvariant()) : Strings.Mail_FileType,
                [extension.Length > 0 ? extension : ".bin"]);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));

            var file = await picker.PickSaveFileAsync();
            if (file is null) return;

            var content = await ViewModel.DownloadAttachmentAsync(attachment);
            await File.WriteAllBytesAsync(file.Path, content.Content);
            ViewModel.StatusText = Loc.Format(Strings.Mail_SavedTo, file.Path);
        }
        catch (Exception ex)
        {
            ViewModel.ErrorMessage = Loc.Format(Strings.Mail_AttachmentSaveFailed, ex.Message);
        }
    }
}

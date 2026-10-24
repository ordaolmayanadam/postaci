using System.ComponentModel;
using ExchangeMail.App.Helpers;
using ExchangeMail.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage.Pickers;
using Windows.System;

namespace ExchangeMail.App.Views;

/// <summary>Mail view. Cached by the shell frame, so it is initialized once per session.</summary>
public sealed partial class MailPage : Page
{
    private readonly HtmlViewer _body;
    private MessageDetailViewModel? _shownDetail;

    public MailPage()
    {
        InitializeComponent();
        _body = new HtmlViewer(BodyView);
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
            ViewModel.ErrorMessage = "WebView2 başlatılamadı (Microsoft Edge WebView2 Runtime kurulu mu?): " + ex.Message;
        }

        await ViewModel.InitializeAsync();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.Current)) return;
        if (_shownDetail is not null) _shownDetail.PropertyChanged -= OnDetailPropertyChanged;
        _shownDetail = ViewModel.Current;
        if (_shownDetail is not null) _shownDetail.PropertyChanged += OnDetailPropertyChanged;
        _body.Show(_shownDetail?.DisplayHtml);
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

    private void OnMessageListKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Delete && ViewModel.DeleteCommand.CanExecute(null))
        {
            e.Handled = true;
            ViewModel.DeleteCommand.Execute(null);
        }
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
                extension.Length > 0 ? extension.TrimStart('.').ToUpperInvariant() + " dosyası" : "Dosya",
                [extension.Length > 0 ? extension : ".bin"]);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));

            var file = await picker.PickSaveFileAsync();
            if (file is null) return;

            var content = await ViewModel.DownloadAttachmentAsync(attachment);
            await File.WriteAllBytesAsync(file.Path, content.Content);
            ViewModel.StatusText = $"Kaydedildi: {file.Path}";
        }
        catch (Exception ex)
        {
            ViewModel.ErrorMessage = "Ek kaydedilemedi: " + ex.Message;
        }
    }
}

using ExchangeMail.App.Helpers;
using ExchangeMail.Core.Models;
using ExchangeMail.ViewModels;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace ExchangeMail.App.Views;

public sealed partial class ComposeWindow : Window
{
    private readonly Dictionary<AutoSuggestBox, CancellationTokenSource> _suggestionRequests = [];
    private bool _canClose;

    /// <summary>Editor HTML at open or last save; differences count as unsaved changes.</summary>
    private string? _savedHtml;

    public ComposeWindow(ComposeViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();

        Title = ViewModel.WindowTitle;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarArea);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Standard;
        AppWindow.SetIcon(MainWindow.IconPath);
        WindowHelper.Resize(this, 920, 760);

        ViewModel.ConfirmAsync = ConfirmAsync;
        ViewModel.ReadBodyHtmlAsync = Editor.GetHtmlAsync;
        _ = Editor.SetHtmlAsync(ViewModel.InitialHtml);
        ViewModel.DraftSaved += async (_, _) => _savedHtml = await Editor.GetHtmlAsync();
        ViewModel.BodyReplaced += async (_, html) =>
        {
            await Editor.SetHtmlAsync(html);
            _savedHtml = await Editor.GetHtmlAsync();
        };
        ViewModel.Sent += (_, _) =>
        {
            _canClose = true;
            Close();
        };
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ComposeViewModel.WindowTitle)) Title = ViewModel.WindowTitle;
        };
        AppWindow.Closing += OnClosing;
        Root.RequestedTheme = App.MainWindow.CurrentTheme;
        Root.Loaded += async (_, _) =>
        {
            try
            {
                await Editor.EnsureInitializedAsync();
                _savedHtml = await Editor.GetHtmlAsync();
                if (ViewModel.IsResponse || ViewModel.IsEditingDraft) await Editor.FocusEditorAsync();
                else ToBox.Focus(FocusState.Programmatic);
            }
            catch (Exception ex)
            {
                ViewModel.ErrorMessage = Loc.Format(Strings.Compose_EditorFailed, ex.Message);
            }
        };
    }

    public ComposeViewModel ViewModel { get; }

    private async void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_canClose) return;

        // The editor can only be read asynchronously, so cancel first and close again if nothing would be lost.
        args.Cancel = true;
        var changed = ViewModel.HasAttachmentChanges;
        try
        {
            changed |= _savedHtml is not null && await Editor.GetHtmlAsync() != _savedHtml;
        }
        catch (Exception)
        {
            // editor never started; nothing typed
        }

        if (!changed || await ConfirmAsync(Strings.Compose_DiscardTitle, Strings.Compose_DiscardQuestion))
        {
            _canClose = true;
            Close();
        }
    }

    private async void OnRecipientTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;

        if (_suggestionRequests.Remove(sender, out var previous)) previous.Cancel();
        var cts = new CancellationTokenSource();
        _suggestionRequests[sender] = cts;

        try
        {
            await Task.Delay(300, cts.Token); // debounce typing
            var suggestions = await ViewModel.SuggestRecipientsAsync(sender.Text, cts.Token);
            if (!cts.IsCancellationRequested) sender.ItemsSource = suggestions;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void OnRecipientQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (args.ChosenSuggestion is not MailboxAddress chosen) return;
        sender.Text = RecipientParser.ReplaceLastToken(sender.Text, chosen.ToString());
        sender.ItemsSource = null;
    }

    private async void OnAttachClick(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));

        var files = await picker.PickMultipleFilesAsync();
        await AddFilesAsync(files);
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = Strings.Compose_DropCaption;
        }
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        var items = await e.DataView.GetStorageItemsAsync();
        await AddFilesAsync(items.OfType<StorageFile>());
    }

    private async Task AddFilesAsync(IEnumerable<StorageFile> files)
    {
        foreach (var file in files)
        {
            try
            {
                ViewModel.AddAttachment(file.Name, await File.ReadAllBytesAsync(file.Path));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ViewModel.ErrorMessage = Loc.Format(Strings.Compose_AttachFailed, file.Name, ex.Message);
            }
        }
    }

    private void OnRemoveAttachment(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is OutgoingAttachment attachment)
            ViewModel.RemoveAttachmentCommand.Execute(attachment);
    }

    private void OnRemoveExistingAttachment(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is AttachmentInfo attachment)
            ViewModel.RemoveExistingAttachmentCommand.Execute(attachment);
    }

    private void OnErrorClosed(InfoBar sender, object args) => ViewModel.ErrorMessage = null;

    private Task<bool> ConfirmAsync(string title, string message) => Dialogs.ConfirmAsync(Root.XamlRoot, title, message);
}

using System.ComponentModel;
using ExchangeMail.App.Helpers;
using ExchangeMail.Core.Models;
using ExchangeMail.ViewModels;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;

namespace ExchangeMail.App.Controls;

/// <summary>
/// Card shown in a flyout for a sender or recipient: directory details, the manager chain above the person (top first,
/// each level indented) and direct reports. Names in the chain open their own card in place.
/// </summary>
public sealed partial class PersonCard : UserControl
{
    private const double IndentStep = 14;

    private readonly PersonCardViewModel _vm;
    private readonly Action<MailboxAddress>? _compose;
    private readonly StackPanel _root = new() { Spacing = 12, Width = 340 };

    /// <param name="theme">The theme of the page it opens from (a flyout is outside the page and doesn't inherit it).</param>
    public PersonCard(PersonCardViewModel vm, ElementTheme theme, Action<MailboxAddress>? compose)
    {
        _vm = vm;
        RequestedTheme = theme;
        _compose = compose;
        Content = new ScrollViewer { Content = _root, MaxHeight = 560, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 8, 0) };
        _vm.PropertyChanged += OnChanged;
        Unloaded += (_, _) => _vm.PropertyChanged -= OnChanged;
        Render();
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e) => Render();

    private void Render()
    {
        _root.Children.Clear();
        var person = _vm.Person;
        var address = _vm.Address;

        // Header: photo, name, title.
        var picture = new PersonPicture { Width = 56, Height = 56, DisplayName = person?.DisplayName ?? address?.Display ?? "" };
        if (person?.Photo is { } photo) _ = SetPhotoAsync(picture, photo);
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        names.Children.Add(Text(person?.DisplayName ?? address?.Display ?? "", 16 * Ui.TextScale, FontWeights.SemiBold, selectable: true));
        if (_vm.Title.Length > 0) names.Children.Add(Secondary(_vm.Title));
        if (person?.Company is { } company) names.Children.Add(Secondary(company));
        var header = new Grid { ColumnSpacing = 12, ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition() } };
        header.Children.Add(picture);
        Grid.SetColumn(names, 1);
        header.Children.Add(names);
        _root.Children.Add(header);

        if (_vm.IsLoading)
        {
            _root.Children.Add(new ProgressRing { IsActive = true, Width = 20, Height = 20, HorizontalAlignment = HorizontalAlignment.Left });
            return;
        }
        if (_vm.ErrorMessage is { } error)
        {
            _root.Children.Add(Text(error, 12 * Ui.TextScale, FontWeights.Normal, wrap: true));
            return;
        }

        // Contact lines.
        var email = person?.Email ?? address?.Address;
        var lines = new StackPanel { Spacing = 6 };
        AddLine(lines, "\uE715", email);
        AddLine(lines, "\uE821", person?.Office);
        AddLine(lines, "\uE717", person?.BusinessPhone);
        AddLine(lines, "\uE8EA", person?.MobilePhone);
        if (lines.Children.Count > 0) _root.Children.Add(lines);

        if (!string.IsNullOrEmpty(email) && email.Contains('@'))
        {
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            if (_compose is not null)
            {
                var send = new Button { Content = Strings.Contacts_SendEmail, Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
                send.Click += (_, _) => _compose(new MailboxAddress(person?.DisplayName ?? address?.Name, email));
                actions.Children.Add(send);
            }
            var copy = new Button { Content = Strings.Person_CopyAddress };
            copy.Click += (_, _) =>
            {
                var package = new DataPackage();
                package.SetText(email);
                Clipboard.SetContent(package);
            };
            actions.Children.Add(copy);
            _root.Children.Add(actions);
        }

        if (person is null)
        {
            _root.Children.Add(Secondary(Strings.Person_NotInDirectory));
            return;
        }

        // Organization: managers from the top down, then the person, each one level further in.
        if (_vm.Managers.Count > 0 || _vm.IsLoadingManagers)
        {
            _root.Children.Add(Section(Strings.Person_Organization));
            var chain = new StackPanel { Spacing = 2 };
            for (var i = 0; i < _vm.Managers.Count; i++)
                chain.Children.Add(PersonRow(_vm.Managers[i], i, isSelf: false));
            if (_vm.IsLoadingManagers)
                chain.Children.Add(new ProgressRing { IsActive = true, Width = 14, Height = 14, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(_vm.Managers.Count * IndentStep, 4, 0, 4) });
            chain.Children.Add(PersonRow(person, _vm.Managers.Count, isSelf: true));
            _root.Children.Add(chain);
        }

        if (person.DirectReports.Count > 0)
        {
            _root.Children.Add(Section(Loc.Format(Strings.Person_DirectReports, person.DirectReports.Count)));
            var reports = new StackPanel { Spacing = 0 };
            foreach (var report in person.DirectReports.OrderBy(r => r.Display, StringComparer.CurrentCultureIgnoreCase))
                reports.Children.Add(Link(report.Display, report, 0));
            _root.Children.Add(reports);
        }
    }

    private FrameworkElement PersonRow(DirectoryPerson p, int level, bool isSelf)
    {
        var indent = level * IndentStep;
        if (isSelf)
        {
            var self = new StackPanel { Margin = new Thickness(indent + 4, 4, 0, 4), Style = Ui.AppStyle("PersonCardSelfStyle") };
            self.Children.Add(Text(p.DisplayName, 14 * Ui.TextScale, FontWeights.SemiBold));
            if (p.JobTitle is { } title) self.Children.Add(Secondary(title));
            return self;
        }
        return Link(p.DisplayName + (p.JobTitle is { } t ? " · " + t : ""), new MailboxAddress(p.DisplayName, p.Email ?? ""), indent);
    }

    /// <summary>A name that opens that person's card in place.</summary>
    private HyperlinkButton Link(string text, MailboxAddress target, double indent)
    {
        var link = new HyperlinkButton
        {
            Content = new TextBlock { Text = text, TextTrimming = TextTrimming.CharacterEllipsis },
            Padding = new Thickness(4, 2, 4, 2),
            Margin = new Thickness(indent, 0, 0, 0),
            HorizontalContentAlignment = HorizontalAlignment.Left,
        };
        link.Click += (_, _) => _ = _vm.LoadAsync(target);
        return link;
    }

    private static void AddLine(Panel lines, string glyph, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        row.Children.Add(new FontIcon { Glyph = glyph, FontSize = 14 * Ui.TextScale, Opacity = 0.7 });
        row.Children.Add(Text(value, 13 * Ui.TextScale, FontWeights.Normal, selectable: true));
        lines.Children.Add(row);
    }

    private static TextBlock Section(string text) =>
        new() { Text = text, FontWeight = FontWeights.SemiBold, FontSize = 13 * Ui.TextScale, Margin = new Thickness(0, 4, 0, 0) };

    private static TextBlock Secondary(string text)
    {
        var block = Text(text, 12 * Ui.TextScale, FontWeights.Normal, wrap: true);
        block.Style = Ui.AppStyle("SecondaryTextStyle");
        return block;
    }

    private static TextBlock Text(string text, double size, Windows.UI.Text.FontWeight weight, bool selectable = false, bool wrap = false) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = weight,
        IsTextSelectionEnabled = selectable,
        TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    private static async Task SetPhotoAsync(PersonPicture picture, byte[] photo)
    {
        try
        {
            using var stream = new MemoryStream(photo);
            var image = new BitmapImage();
            await image.SetSourceAsync(stream.AsRandomAccessStream());
            picture.ProfilePicture = image;
        }
        catch (Exception)
        {
            // An unreadable photo leaves the initials.
        }
    }
}

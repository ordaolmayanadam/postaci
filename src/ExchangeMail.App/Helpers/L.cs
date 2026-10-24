using Microsoft.UI.Xaml.Markup;

namespace ExchangeMail.App.Helpers;

/// <summary>
/// Localized text in XAML: <c>Text="{h:L Key=Mail_Reply}"</c>. Looks the key up in <see cref="Strings"/> for the
/// language chosen at startup; a missing key shows as [Key] so it stands out instead of failing silently.
/// </summary>
[MarkupExtensionReturnType(ReturnType = typeof(string))]
public sealed partial class L : MarkupExtension
{
    public string Key { get; set; } = "";

    protected override object ProvideValue() => Strings.ResourceManager.GetString(Key, Strings.Culture) ?? $"[{Key}]";
}

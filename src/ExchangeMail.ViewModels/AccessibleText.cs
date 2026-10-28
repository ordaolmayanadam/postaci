namespace ExchangeMail.ViewModels;

/// <summary>
/// Names that screen readers announce for list items. A list item without AutomationProperties.Name is announced by
/// its data object's ToString(), so the item view models return these from ToString().
/// </summary>
internal static class AccessibleText
{
    /// <summary>The non-empty parts, comma-separated (screen readers pause at commas).</summary>
    public static string Join(params string?[] parts) =>
        string.Join(", ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
}

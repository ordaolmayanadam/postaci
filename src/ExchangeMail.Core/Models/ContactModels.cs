namespace ExchangeMail.Core.Models;

/// <summary>A personal contact (has an ItemId) or a global address list entry (ItemId is null).</summary>
public sealed record ContactInfo(
    EwsItemId? ItemId,
    string DisplayName,
    string? Email,
    string? Company,
    string? JobTitle,
    string? Department,
    string? BusinessPhone,
    string? MobilePhone,
    bool IsDistributionList = false,
    string? GivenName = null,
    string? Surname = null)
{
    public bool IsDirectoryEntry => ItemId is null;
}

public sealed record ContactPage(IReadOnlyList<ContactInfo> Items, int TotalCount, int NextOffset, bool IsLastPage);

public sealed class NewContact
{
    public string GivenName { get; init; } = "";
    public string Surname { get; init; } = "";
    public string Email { get; init; } = "";
    public string Company { get; init; } = "";
    public string JobTitle { get; init; } = "";
    public string Department { get; init; } = "";
    public string BusinessPhone { get; init; } = "";
    public string MobilePhone { get; init; } = "";
}

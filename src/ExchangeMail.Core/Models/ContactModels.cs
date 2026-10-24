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

/// <summary>A company directory (Active Directory) entry with its place in the organization.</summary>
public sealed record DirectoryPerson(
    string DisplayName,
    string? Email,
    string? JobTitle,
    string? Department,
    string? Company,
    string? Office,
    string? BusinessPhone,
    string? MobilePhone,
    string? ManagerName,
    string? ManagerEmail,
    IReadOnlyList<MailboxAddress> DirectReports,
    byte[]? Photo);

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

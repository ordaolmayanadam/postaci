using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ExchangeMail.Core.Models;
using ExchangeMail.ViewModels;

namespace ExchangeMail.Core.Tests;

/// <summary>Turkish/English resources stay complete, and no UI text bypasses them.</summary>
public partial class LocalizationTests
{
    private static readonly string Root = FindRepositoryRoot();
    private static readonly string ResourceDir = Path.Combine(Root, "src", "ExchangeMail.Core", "Localization");

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ExchangeMail.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static Dictionary<string, string> ReadResx(string fileName) =>
        XDocument.Load(Path.Combine(ResourceDir, fileName)).Root!.Elements("data")
            .ToDictionary(d => (string)d.Attribute("name")!, d => (string?)d.Element("value") ?? "");

    private static IEnumerable<string> SourceFiles(string pattern) =>
        Directory.EnumerateFiles(Path.Combine(Root, "src"), pattern, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    [Fact]
    public void Every_language_has_the_same_keys_and_placeholders()
    {
        var turkish = ReadResx("Strings.resx");
        var english = ReadResx("Strings.en.resx");

        Assert.Empty(turkish.Keys.Except(english.Keys));
        Assert.Empty(english.Keys.Except(turkish.Keys));
        foreach (var (key, tr) in turkish)
        {
            Assert.False(string.IsNullOrWhiteSpace(tr), key);
            Assert.False(string.IsNullOrWhiteSpace(english[key]), key);
            Assert.True(Placeholders(tr).SetEquals(Placeholders(english[key])), $"{key}: placeholders differ");
        }
    }

    private static HashSet<string> Placeholders(string text) =>
        PlaceholderPattern().Matches(text).Select(m => m.Groups[1].Value).ToHashSet();

    [Fact]
    public void Keys_used_in_xaml_exist()
    {
        var keys = ReadResx("Strings.resx").Keys.ToHashSet();
        var used = SourceFiles("*.xaml")
            .SelectMany(f => XamlKeyPattern().Matches(File.ReadAllText(f)).Select(m => (File: Path.GetFileName(f), Key: m.Groups[1].Value)))
            .ToList();

        Assert.NotEmpty(used);
        Assert.DoesNotContain(used, u => !keys.Contains(u.Key));
    }

    [Fact]
    public void Xaml_has_no_hard_coded_text()
    {
        // Product and language names are the same in every language.
        string[] allowed = ["Postacı", "Türkçe", "English"];
        var offenders = SourceFiles("*.xaml")
            .SelectMany(f => XamlTextAttributePattern().Matches(File.ReadAllText(f))
                .Select(m => $"{Path.GetFileName(f)}: {m.Value}")
                .Where(v => !allowed.Any(a => v.EndsWith($"\"{a}\"", StringComparison.Ordinal))))
            .Where(v => LetterPattern().IsMatch(v.Split('"')[1]) && !v.Contains(" pt\"", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Code_has_no_hard_coded_turkish_text()
    {
        // Subject prefixes recognised in replies from other clients, not shown as UI text.
        string[] allowed = ["\"Postacı\"", "\"İLT:\""];
        var offenders = SourceFiles("*.cs")
            .SelectMany(f => File.ReadAllLines(f).Select((line, i) => (File: Path.GetFileName(f), Line: i + 1, Text: line.Trim())))
            .Where(l => !l.Text.StartsWith("//", StringComparison.Ordinal))
            .Where(l => TurkishLiteralPattern().Matches(l.Text).Any(m => !allowed.Contains(m.Value)))
            .Select(l => $"{l.File}:{l.Line}: {l.Text}")
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void English_resources_are_embedded()
    {
        var en = CultureInfo.GetCultureInfo(Loc.English);
        Assert.Equal("Cancel", Strings.ResourceManager.GetString(nameof(Strings.Common_Cancel), en));
        Assert.Equal("İptal", Strings.ResourceManager.GetString(nameof(Strings.Common_Cancel), CultureInfo.GetCultureInfo(Loc.Turkish)));
        // Regional variants fall back to their language.
        Assert.Equal("Cancel", Strings.ResourceManager.GetString(nameof(Strings.Common_Cancel), CultureInfo.GetCultureInfo("en-GB")));
    }

    [Theory]
    [InlineData("", "tr-TR", "tr")]
    [InlineData("", "en-US", "en")]
    [InlineData("", "de-DE", "en")]
    [InlineData("tr", "en-US", "tr")]
    [InlineData("en", "tr-TR", "en")]
    [InlineData("fr", "tr-TR", "tr")]
    public void Language_preference_resolves(string preference, string system, string expected) =>
        Assert.Equal(expected, Loc.Resolve(preference, CultureInfo.GetCultureInfo(system)));

    [Fact]
    public void Counts_pick_singular_or_plural()
    {
        Assert.Equal("1 event", Loc.Count(1, "{0} event", "{0} events"));
        Assert.Equal("3 events", Loc.Count(3, "{0} event", "{0} events"));
    }

    [Fact]
    public void Day_and_month_follow_the_culture_order()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            var day = new DateTime(2026, 3, 9);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            Assert.Equal("9 Mart", MailFormat.DayMonth(day));
            Assert.Equal("9 Mart 2026", MailFormat.DayMonthYear(day));

            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            Assert.Equal("March 9", MailFormat.DayMonth(day));
            Assert.Equal("March 9, 2026", MailFormat.DayMonthYear(day));

            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-GB");
            Assert.Equal("9 March", MailFormat.DayMonth(day));
            Assert.Equal("9 March 2026", MailFormat.DayMonthYear(day));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Changing_the_language_asks_for_a_restart()
    {
        var session = new MailSession(new FakeEwsServer().CreateClient(), new AccountSettings { Email = "ben@sirket.com" });
        var store = new SettingsStore(Path.Combine(Path.GetTempPath(), "postaci-tests-" + Guid.NewGuid().ToString("N")));
        var vm = new SettingsViewModel(session, store, new NullCredentials());

        Assert.Equal(0, vm.LanguageIndex); // follow Windows
        Assert.False(vm.IsLanguageRestartNeeded);

        vm.LanguageIndex = 2;
        Assert.Equal(Loc.English, session.Preferences.Language);
        Assert.True(vm.IsLanguageRestartNeeded);
        Assert.False(vm.IsRestartNeeded); // the font notice stays closed
        Assert.Equal(Loc.English, store.LoadPreferences().Language);

        vm.LanguageIndex = 0;
        Assert.False(vm.IsLanguageRestartNeeded);
    }

    private sealed class NullCredentials : ICredentialStore
    {
        public string? GetPassword(string userName) => null;
        public void SavePassword(string userName, string password) { }
        public void RemovePassword(string userName) { }
    }

    [GeneratedRegex(@"\{(\d+)")]
    private static partial Regex PlaceholderPattern();

    [GeneratedRegex(@"\{h:L Key=(\w+)\}")]
    private static partial Regex XamlKeyPattern();

    [GeneratedRegex(@"(?<![\w.:])(Text|Content|Header|PlaceholderText|Title|Label|Description|ToolTipService\.ToolTip|OnContent|OffContent|PrimaryButtonText|CloseButtonText|SecondaryButtonText|Message|AutomationProperties\.Name)=""[^""{]*""")]
    private static partial Regex XamlTextAttributePattern();

    [GeneratedRegex(@"[A-Za-zÇĞİÖŞÜçğıöşü]")]
    private static partial Regex LetterPattern();

    [GeneratedRegex(@"""[^""\n]*[çğıİöşüÇĞÖŞÜ][^""\n]*""")]
    private static partial Regex TurkishLiteralPattern();
}

public class FolderNameTests
{
    [Fact]
    public void Standard_folders_use_the_interface_language_and_others_keep_the_server_name()
    {
        static FolderViewModel Folder(string name, WellKnownFolder kind) =>
            new(new MailFolder { Id = name, DisplayName = name, WellKnown = kind }, 0);

        Assert.Equal(Strings.Folder_Inbox, Folder("Posteingang", WellKnownFolder.Inbox).DisplayName);
        Assert.Equal(Strings.Folder_SentItems, Folder("Sent Items", WellKnownFolder.SentItems).DisplayName);
        Assert.Equal("Projeler", Folder("Projeler", WellKnownFolder.None).DisplayName);
        Assert.Equal("Inbox", Strings.ResourceManager.GetString(nameof(Strings.Folder_Inbox), System.Globalization.CultureInfo.GetCultureInfo(Loc.English)));
    }
}

using ExchangeMail.ViewModels;

namespace ExchangeMail.Core.Tests;

/// <summary>Moving to the packaged app keeps the account and preferences of the zip releases.</summary>
public class AppInfoTests
{
    [Fact]
    public void Settings_of_the_newest_earlier_folder_are_copied_once()
    {
        var root = Path.Combine(Path.GetTempPath(), "postaci-test-" + Guid.NewGuid().ToString("N"));
        var target = Path.Combine(root, "LocalState");
        var zip = Path.Combine(root, "Postaci");
        var old = Path.Combine(root, "ExchangeMail");
        try
        {
            Directory.CreateDirectory(zip);
            Directory.CreateDirectory(old);
            File.WriteAllText(Path.Combine(zip, "settings.json"), "zip");
            File.WriteAllText(Path.Combine(zip, "preferences.json"), "zip-prefs");
            File.WriteAllText(Path.Combine(old, "settings.json"), "old");

            AppInfo.MigrateEarlierData(target, [zip, old]);

            Assert.Equal("zip", File.ReadAllText(Path.Combine(target, "settings.json")));
            Assert.Equal("zip-prefs", File.ReadAllText(Path.Combine(target, "preferences.json")));

            // Once the app has its own settings, nothing is copied over them.
            File.WriteAllText(Path.Combine(target, "settings.json"), "mine");
            AppInfo.MigrateEarlierData(target, [zip, old]);
            Assert.Equal("mine", File.ReadAllText(Path.Combine(target, "settings.json")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Nothing_happens_without_earlier_settings()
    {
        var root = Path.Combine(Path.GetTempPath(), "postaci-test-" + Guid.NewGuid().ToString("N"));
        AppInfo.MigrateEarlierData(Path.Combine(root, "LocalState"), [Path.Combine(root, "Postaci")]);
        Assert.False(Directory.Exists(root));
    }
}

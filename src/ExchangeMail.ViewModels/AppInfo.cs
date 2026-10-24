namespace ExchangeMail.ViewModels;

/// <summary>Product name and per-user storage locations.</summary>
public static class AppInfo
{
    public const string Name = "Postacı";

    /// <summary>ASCII form used for folders, the executable and registry/credential keys.</summary>
    public const string Id = "Postaci";

    /// <summary>Name used by versions before the rename; its data is migrated once.</summary>
    public const string LegacyId = "ExchangeMail";

    public static string DataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Id);

    public static string LegacyDataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), LegacyId);

    /// <summary>Copies settings written under the old name, so an upgrade keeps the account.</summary>
    public static void MigrateLegacyData()
    {
        try
        {
            if (!Directory.Exists(LegacyDataDirectory) || File.Exists(Path.Combine(DataDirectory, "settings.json"))) return;
            Directory.CreateDirectory(DataDirectory);
            foreach (var name in new[] { "settings.json", "preferences.json" })
            {
                var source = Path.Combine(LegacyDataDirectory, name);
                if (File.Exists(source)) File.Copy(source, Path.Combine(DataDirectory, name), overwrite: false);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not fatal: the user signs in again.
        }
    }
}

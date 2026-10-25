namespace ExchangeMail.ViewModels;

/// <summary>Product name and per-user storage locations.</summary>
public static class AppInfo
{
    public const string Name = "Postacı";

    /// <summary>ASCII form used for folders, the executable and credential keys.</summary>
    public const string Id = "Postaci";

    /// <summary>Name used by versions before the rename; its data is migrated once.</summary>
    public const string LegacyId = "ExchangeMail";

    private static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    /// <summary>
    /// Settings, preferences, the WebView2 profile and the diagnostics log. The app points it at its package's app data
    /// folder with <see cref="UseDataDirectory"/> before anything is read or stored.
    /// </summary>
    public static string DataDirectory { get; private set; } = Path.Combine(LocalAppData, Id);

    /// <summary>
    /// Where earlier versions kept their data: the unpackaged (zip) releases, and before that the old name. A packaged
    /// app still sees these folders, so the newest one with settings is copied once.
    /// </summary>
    public static IReadOnlyList<string> EarlierDataDirectories { get; } =
        [Path.Combine(LocalAppData, Id), Path.Combine(LocalAppData, LegacyId)];

    public static void UseDataDirectory(string directory) => DataDirectory = directory;

    /// <summary>Copies settings written by an earlier version, so an upgrade keeps the account and preferences.</summary>
    public static void MigrateEarlierData() => MigrateEarlierData(DataDirectory, EarlierDataDirectories);

    public static void MigrateEarlierData(string target, IEnumerable<string> earlier)
    {
        try
        {
            if (File.Exists(Path.Combine(target, "settings.json"))) return;
            var source = earlier.FirstOrDefault(d =>
                !string.Equals(Path.GetFullPath(d), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase)
                && File.Exists(Path.Combine(d, "settings.json")));
            if (source is null) return;
            Directory.CreateDirectory(target);
            foreach (var name in new[] { "settings.json", "preferences.json" })
            {
                var file = Path.Combine(source, name);
                if (File.Exists(file)) File.Copy(file, Path.Combine(target, name), overwrite: false);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not fatal: the user signs in again.
        }
    }
}

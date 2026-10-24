using ExchangeMail.ViewModels;
using Microsoft.Win32;

namespace ExchangeMail.App.Services;

/// <summary>Starts Postacı minimized to the tray when the user signs in to Windows (HKCU Run key, no admin rights needed).</summary>
public sealed class RegistryStartupService : IStartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>Command-line switch that starts the app hidden in the tray.</summary>
    public const string TrayArgument = "--tray";

    private static string Command => $"\"{Environment.ProcessPath}\" {TrayArgument}";

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(AppInfo.Id) is string value && value.Equals(Command, StringComparison.OrdinalIgnoreCase);
        }
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (enabled) key.SetValue(AppInfo.Id, Command);
        else key.DeleteValue(AppInfo.Id, throwOnMissingValue: false);
    }
}

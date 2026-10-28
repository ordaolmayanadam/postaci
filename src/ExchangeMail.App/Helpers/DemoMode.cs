using ExchangeMail.ViewModels;
#if DEBUG
using ExchangeMail.Core.Ews;
using ExchangeMail.Demo;
#endif

namespace ExchangeMail.App.Helpers;

/// <summary>
/// Debug builds started with --demo open a made-up mailbox instead of signing in, for screenshots. Its settings live
/// in a folder of their own, so the real account is neither read nor changed. Optional: --demo-language=tr|en and
/// --demo-theme=light|dark. Release builds have no demo mailbox and ignore the arguments.
/// </summary>
internal static class DemoMode
{
    private static readonly string[] Arguments = Environment.GetCommandLineArgs();

#if DEBUG
    public static bool IsRequested { get; } = Arguments.Contains("--demo");
#else
    public static bool IsRequested => false;
#endif

    public static void ApplyTo(UserPreferences preferences)
    {
        if (Value("--demo-language") is { } language) preferences.Language = language;
        if (Value("--demo-theme") is { } theme && Enum.TryParse<AppTheme>(theme, ignoreCase: true, out var parsed)) preferences.Theme = parsed;
    }

#if DEBUG
    public static MailSession CreateSession(UserPreferences preferences)
    {
        var mailbox = new DemoMailbox(Loc.Current);
        var account = new AccountSettings { Email = mailbox.Me.Address, EwsUrl = DemoEwsHandler.EwsUrl.ToString() };
        return new MailSession(new EwsClient(DemoEwsHandler.EwsUrl, new DemoEwsHandler(mailbox)), account, preferences);
    }
#endif

    private static string? Value(string name) =>
        Arguments.FirstOrDefault(a => a.StartsWith(name + "=", StringComparison.Ordinal))?[(name.Length + 1)..];
}

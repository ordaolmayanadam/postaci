using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace ExchangeMail.App;

/// <summary>
/// Entry point (replaces the XAML-generated one, see DISABLE_XAML_GENERATED_MAIN). Postacı runs as a single instance:
/// a second start hands its activation to the running one, which brings its window forward, and then ends.
/// </summary>
public static class Program
{
    private const string InstanceKey = "main";

    [STAThread]
    private static void Main()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        if (RedirectToRunningInstance()) return;

        Application.Start(p =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App();
        });
    }

    /// <summary>Lets <see cref="App.Restart"/> start a new instance that does not hand itself back to this one.</summary>
    public static void ReleaseInstanceKey() => AppInstance.GetCurrent().UnregisterKey();

    private static bool RedirectToRunningInstance()
    {
        var instance = AppInstance.FindOrRegisterForKey(InstanceKey);
        if (instance.IsCurrent)
        {
            instance.Activated += OnRedirected;
            return false;
        }

        // Only the process the user just started may take the foreground; it passes that right on.
        AllowSetForegroundWindow(instance.ProcessId);
        var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
        // Waiting on the STA thread pumps COM, which the redirection needs.
        Task.Run(() => instance.RedirectActivationToAsync(activation).AsTask()).Wait();
        return true;
    }

    /// <summary>Another start was handed to this instance (raised on a background thread).</summary>
    private static void OnRedirected(object? sender, AppActivationArguments args)
    {
        // Signing in to Windows starts the startup task; an instance that is already running stays as it is.
        if (args.Kind == ExtendedActivationKind.StartupTask) return;
        App.MainWindow?.DispatcherQueue.TryEnqueue(() => App.MainWindow.BringToFront());
    }

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(uint processId);
}

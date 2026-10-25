using ExchangeMail.ViewModels;
using Windows.ApplicationModel;

namespace ExchangeMail.App.Services;

/// <summary>
/// Starts Postacı in the tray when the user signs in to Windows, through the startup task declared in
/// Package.appxmanifest. Windows lists it under Settings → Apps → Startup, where the user has the last word.
/// </summary>
public sealed class StartupTaskService : IStartupService
{
    /// <summary>TaskId of the windows.startupTask extension in Package.appxmanifest.</summary>
    public const string TaskId = "PostaciStartup";

    public async Task<StartupState> GetStateAsync() => Map((await StartupTask.GetAsync(TaskId)).State);

    public async Task<StartupState> SetEnabledAsync(bool enabled)
    {
        var task = await StartupTask.GetAsync(TaskId);
        if (enabled) return Map(await task.RequestEnableAsync());
        task.Disable();
        return Map(task.State);
    }

    private static StartupState Map(StartupTaskState state) => state switch
    {
        StartupTaskState.Enabled => StartupState.Enabled,
        StartupTaskState.EnabledByPolicy => StartupState.EnabledByPolicy,
        StartupTaskState.DisabledByUser => StartupState.DisabledByUser,
        StartupTaskState.DisabledByPolicy => StartupState.DisabledByPolicy,
        _ => StartupState.Disabled,
    };
}

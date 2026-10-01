using Dalamud.Plugin.Services;
using Ocelot.Config;
using Ocelot.Lifecycle;
using BOCCHI.Config;

namespace BOCCHI.Services.Changelog;

public sealed class ChangelogPopupService
(
    Configuration config,
    IConfigSaver saver,
    IChangelogWindow changelogWindow,
    IFramework framework
) : IOnStart
{
    private bool handled;

    public void OnStart()
    {
        // Defer off StartHost — same rationale as MOTD (avoid load-time deadlock).
        framework.RunOnTick(TryShowOnce);
    }

    private void TryShowOnce()
    {
        if (handled)
        {
            return;
        }

        handled = true;

        string current = ChangelogText.CurrentPluginVersion;
        string lastSeen = config.LastSeenPluginVersion ?? string.Empty;

        if (string.IsNullOrWhiteSpace(lastSeen))
        {
            Remember(current);
            return;
        }

        if (string.Equals(lastSeen, current, StringComparison.Ordinal))
        {
            return;
        }

        if (!ChangelogText.TryGetSectionForVersion(current, out _))
        {
            Remember(current);
            return;
        }

        changelogWindow.ShowForCurrentVersion();
    }

    private void Remember(string version)
    {
        if (config.LastSeenPluginVersion == version)
        {
            return;
        }

        config.LastSeenPluginVersion = version;
        saver.Save();
    }
}

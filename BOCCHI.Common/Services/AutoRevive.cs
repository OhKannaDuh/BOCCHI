using BOCCHI.Common.Config;
using BOCCHI.Common.Data.Aethernet;
using BOCCHI.Common.Data.Zones;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using ECommons.Throttlers;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.UI;
using Ocelot.Lifecycle;
using Ocelot.Services.Logger;

namespace BOCCHI.Common.Services;

public sealed class AutoRevive(
    AutomatorConfig config,
    ICondition conditions,
    IObjectTable objects,
    IZoneProvider zones,
    ICriticalEncounterContext criticalEncounters,
    ILogger<AutoRevive> logger
) : IOnUpdate
{
    private const uint RaisePendingStatusId = 148; // Raise

    private DateTime? deadSinceUtc;

    private bool protectedContent;

    private bool sawAlive;

    public unsafe void Update()
    {
        if (!zones.GetZone().IsOccultCrescentZone())
        {
            deadSinceUtc = null;
            sawAlive = false;
            return;
        }

        bool inProtectedContent = zones.GetZone().IsInForkedTower() || criticalEncounters.IsRegisteredOrInCriticalEncounter();
        if (!conditions[ConditionFlag.Unconscious])
        {
            deadSinceUtc = null;
            sawAlive = true;
            protectedContent = inProtectedContent;
            return;
        }

        // Event state may not survive death, so keep what was true while alive; a wrong release out of Forked Tower can't be undone.
        protectedContent |= inProtectedContent;
        deadSinceUtc ??= DateTime.UtcNow;

        if (!config.AutoAcceptRaise && !config.ReturnToBaseCampAfterDeath)
        {
            return;
        }

        if (!AddonHelpers.TryGetSelectYesno(out AddonSelectYesno* yesno) || ReturnYesNo.IsPartyInvite(&yesno->AtkUnitBase))
        {
            return;
        }

        bool raiseOffered = objects.LocalPlayer?.StatusList.Any(s => s.StatusId == RaisePendingStatusId) == true
                            && !ReturnYesNo.IsReturnConfirmation(&yesno->AtkUnitBase);
        string? reason = raiseOffered ? AcceptRaiseReason() : ReturnReason();
        if (reason == null || !EzThrottler.Throttle("AutoRevive::Yes", 1000))
        {
            return;
        }

        logger.Info("Auto revive: {Reason}", reason);
        new AddonMaster.SelectYesno((nint)yesno).Yes();
    }

    private string? AcceptRaiseReason() => config.AutoAcceptRaise ? "accepting raise" : null;

    private string? ReturnReason()
    {
        if (!config.ReturnToBaseCampAfterDeath
            || DateTime.UtcNow - deadSinceUtc < TimeSpan.FromSeconds(config.ReturnAfterDeathDelaySeconds)
            || protectedContent
            || !sawAlive)
        {
            return null;
        }

        return $"no raise after {config.ReturnAfterDeathDelaySeconds}s — returning to base camp";
    }
}

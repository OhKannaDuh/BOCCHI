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
    YesAlreadyPause yesAlready,
    ILogger<AutoRevive> logger
) : IOnUpdate, IOnStop
{
    private const uint RaisePendingStatusId = 148; // Raise

    private DateTime? deadSinceUtc;

    private bool protectedContent;

    private bool sawAlive;

    private string? lastNote;

    public unsafe void Update()
    {
        if (!zones.GetZone().IsOccultCrescentZone())
        {
            deadSinceUtc = null;
            sawAlive = false;
            lastNote = null;
            yesAlready.Release();
            return;
        }

        bool inProtectedContent = zones.GetZone().IsInForkedTower() || criticalEncounters.IsRegisteredOrInCriticalEncounter();
        if (!conditions[ConditionFlag.Unconscious])
        {
            deadSinceUtc = null;
            sawAlive = true;
            lastNote = null;
            protectedContent = inProtectedContent;
            yesAlready.Release();
            return;
        }

        // Event state may not survive death, so keep what was true while alive; a wrong release out of Forked Tower can't be undone.
        protectedContent |= inProtectedContent;
        deadSinceUtc ??= DateTime.UtcNow;

        if (!config.AutoAcceptRaise && !config.ReturnToBaseCampAfterDeath)
        {
            yesAlready.Release();
            return;
        }

        // A YesAlready rule would click the revive prompt first, also inside Forked Tower.
        yesAlready.Hold();

        if (!AddonHelpers.TryGetSelectYesno(out AddonSelectYesno* yesno) || ReturnYesNo.IsPartyInvite(&yesno->AtkUnitBase))
        {
            Note("you are down; no revive prompt yet");
            return;
        }

        bool raisePending = objects.LocalPlayer?.StatusList.Any(s => s.StatusId == RaisePendingStatusId) == true;
        bool raiseOffered = raisePending && !ReturnYesNo.IsReturnConfirmation(&yesno->AtkUnitBase);
        string? reason = raiseOffered ? AcceptRaiseReason() : ReturnReason();
        if (reason == null)
        {
            Note(raiseOffered ? "a raise is offered, but Accept raises is off" : WhyNotReturning(raisePending));
            return;
        }

        if (!EzThrottler.Throttle("AutoRevive::Yes", 1000))
        {
            return;
        }

        lastNote = null;
        logger.Info("Auto revive: {Reason}", reason);
        new AddonMaster.SelectYesno((nint)yesno).Yes();
    }

    public void OnStop() => yesAlready.Release();

    private void Note(string note)
    {
        if (note == lastNote)
        {
            return;
        }

        lastNote = note;
        logger.Info("Auto revive: {Note}", note);
    }

    private string WhyNotReturning(bool raisePending)
    {
        string prompt = raisePending ? "a raise is pending but the prompt looks like a return prompt" : "no raise pending";
        if (!config.ReturnToBaseCampAfterDeath)
        {
            return $"{prompt}; Return to camp is off";
        }

        if (protectedContent)
        {
            return $"{prompt}; inside Forked Tower or a critical encounter, so not returning";
        }

        if (!sawAlive)
        {
            return $"{prompt}; BOCCHI did not see you alive first, so not returning";
        }

        return $"{prompt}; returning to camp after {config.ReturnAfterDeathDelaySeconds}s without a raise";
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

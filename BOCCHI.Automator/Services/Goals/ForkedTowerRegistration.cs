using BOCCHI.Automator.Data;
using BOCCHI.Common.Config;
using BOCCHI.Common.Data.CriticalEncounters;
using BOCCHI.Common.Data.OccultCrescent;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Services;
using BOCCHI.Treasure.Services;
using Dalamud.Plugin.Services;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using Ocelot.Extensions;
using Ocelot.Services.Logger;

namespace BOCCHI.Automator.Services.Goals;

public interface IForkedTowerRegistration
{
    bool IsEnabled { get; }

    /// <summary>The tower Illegal Mode should walk to and wait on, or null to keep farming.</summary>
    CriticalEncounter? FindRegistrable();

    /// <summary>Whether this tower can still be entered from the pad in its current state.</summary>
    bool CanStillEnter(CriticalEncounter tower);

    /// <summary>The player has been teleported into the tower, not just registered on the pad.</summary>
    bool IsInsideTower();
}

public sealed class ForkedTowerRegistration(
    ForkedTowerConfig config,
    IAutomatorContext context,
    ICriticalEncounterRepository repo,
    IZoneProvider zones,
    IAutomatorMemory memory,
    IObjectTable objects,
    ILogger<ForkedTowerRegistration> logger
) : IForkedTowerRegistration
{
    public bool IsEnabled => config.AutoRegisterInIllegalMode && context.IsIllegalMode;

    private DynamicEventState? lastSeenState;

    public CriticalEncounter? FindRegistrable()
    {
        LogStateChange();

        if (!IsEnabled)
        {
            return null;
        }

        IZone zone = zones.GetZone();
        if (zone.ForkedTowerEntrance == null || IsInsideTower())
        {
            return null;
        }

        if (repo.TryGetForkedTower() is not { } tower)
        {
            LogSkip(zone, "tower event not active");
            return null;
        }

        if (!tower.IsPreparing())
        {
            LogSkip(zone, $"tower state is {tower.State}");
            return null;
        }

        if (!MeetsKnowledgeRequirement())
        {
            LogSkip(zone, $"knowledge level below {RequiredKnowledgeLevel(zone)}");
            return null;
        }

        if (IllegalModeActivityWork.TakeActiveUnreachable(memory)?.MatchesForkedTower(tower.Id) == true)
        {
            LogSkip(zone, "tower pad marked unreachable");
            return null;
        }

        return tower;
    }

    private void LogStateChange()
    {
        DynamicEventState? state = repo.TryGetForkedTower()?.State;
        if (state == lastSeenState)
        {
            return;
        }

        logger.Info(
            "Forked Tower state {Prev} -> {Next} (auto register: {Config}, illegal mode: {Illegal}, inside: {Inside})",
            lastSeenState?.ToString() ?? "Inactive",
            state?.ToString() ?? "Inactive",
            config.AutoRegisterInIllegalMode,
            context.IsIllegalMode,
            IsInsideTower());
        lastSeenState = state;
    }

    private void LogSkip(IZone zone, string reason)
    {
        if (!EzThrottler.Throttle("ForkedTowerRegistration::Skip", 15000))
        {
            return;
        }

        string live = string.Join(", ", repo.Snapshot().Select(e => $"{e.Id.Value}:{e.State}"));
        logger.Info("Forked Tower {Id} not registrable: {Reason} (live events: {Live})", zone.ForkedTowerEventId, reason, live);
    }

    // Boss arenas sit 500+ yalms from the pad. The tower event id may already be current while
    // registered on the pad, and can linger after a failed entry, so it is not enough on its own.
    private const float InsideTowerMinDistanceFromPad = 150f;

    public bool IsInsideTower()
    {
        IZone zone = zones.GetZone();
        return zone.ForkedTowerEntrance is { } pad
               && zone.IsInForkedTower()
               && objects.LocalPlayer is { } player
               && player.Position.Distance2D(pad) > InsideTowerMinDistanceFromPad;
    }

    // Magic turns away anyone below Knowledge 40, and dying can drop the player under it.
    private const int NorthHornRequiredKnowledgeLevel = 40;

    private static int RequiredKnowledgeLevel(IZone zone) =>
        zone.ZoneId == ZoneId.NorthHorn ? NorthHornRequiredKnowledgeLevel : 0;

    private bool MeetsKnowledgeRequirement()
    {
        int required = RequiredKnowledgeLevel(zones.GetZone());
        // Unknown level is not treated as too low, so a missing read never blocks registration.
        return required == 0
               || KnowledgeThreat.TryGetPlayerForayLevel(objects) is not { } level
               || level >= required;
    }

    // Blood's ciphers are offered by hand, so only the knowledge gate is checked here.
    public bool CanStillEnter(CriticalEncounter tower) => MeetsKnowledgeRequirement();
}

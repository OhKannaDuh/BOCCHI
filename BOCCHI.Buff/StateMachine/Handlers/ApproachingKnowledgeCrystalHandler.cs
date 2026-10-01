using BOCCHI.Buff.Data;
using BOCCHI.Common.Config;
using BOCCHI.Common.Data.Aethernet;
using BOCCHI.Common.Data.KnowledgeCrystals;
using BOCCHI.Common.Data.StateMemory;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Services;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using Ocelot.Extensions;
using Ocelot.Services.Pathfinding;
using Ocelot.Services.PlayerState;
using Ocelot.States.Flow;
using System.Numerics;

namespace BOCCHI.Buff.StateMachine.Handlers;

public class ApproachingKnowledgeCrystalHandler
(
    IZoneProvider zones,
    IPlayer player,
    IPathfinder pathfinder,
    ICondition conditions,
    IObjectTable objects,
    IAutomatorMemory memory,
    MovementConfig movement
) : FlowStateHandler<BuffState>(BuffState.ApproachingKnowledgeCrystal)
{
    private const float CrystalInteractionRange = 5f;

    private const float CrystalApproachRange = CrystalInteractionRange - 1.25f;

    private const float ArrivalRadius = AethernetNavigation.PathfindArrivalRadius;

    private const float ArrivalSlack = AethernetNavigation.PathfindArrivalSlack;

    public override BuffState? Handle()
    {
        IZone zone = zones.GetZone();
        if (!zone.IsOccultCrescentZone())
        {
            return null;
        }

        List<KnowledgeCrystalData> crystals = zone.GetNearbyKnowledgeCrystals().ToList();
        if (crystals.Count == 0)
        {
            return BuffState.NoCrystalsFound;
        }

        bool manual = memory.TryRemember<ManualBuffRunMemory>(out ManualBuffRunMemory _);
        bool inRange = zone.IsInBuffCastRange(player.Position);

        if (inRange)
        {
            pathfinder.Stop();

            if (conditions[ConditionFlag.BetweenAreas]
                || conditions[ConditionFlag.BetweenAreas51]
                || DismountAssist.TryDismount(conditions))
            {
                return null;
            }

            return BuffState.ChoosingBuffToApply;
        }

        if (manual)
        {
            pathfinder.Stop();
            memory.Forget<ApplyingBuffsMemory>();
            memory.Forget<ManualBuffRunMemory>();
            memory.Forget<InquiringMindAttemptedMemory>();
            return null;
        }

        BuffZone? buffZone = zone.GetBuffZone();
        KnowledgeCrystalData closest = crystals[0];
        Vector3 destination = buffZone is { } bz
            && Vector3.DistanceSquared(closest.Position, bz.Center) <= 900f
                ? bz.GetApproachPoint(player.Position)
                : closest.Position.GetApproachPosition(player.Position, CrystalApproachRange);

        float distToDest = player.Position.Distance2D(destination);

        if (pathfinder.GetState() == PathfindingState.Idle && distToDest > ArrivalRadius + ArrivalSlack)
        {
            pathfinder.PathfindAndMoveTo(new(destination)
            {
                DistanceThreshold = ArrivalRadius,
                // Crystal pads sit on aetheryte mesh — floor snap jumps to the wrong side.
                ShouldSnapToFloor = false,
            });
        }

        MountWait.TryCastIfNeeded(
            conditions,
            objects,
            destination,
            movement.ShouldAutoMount,
            movement.PreferredMountId,
            inBaseCamp: zone.IsInBasecamp(),
            zone: zone);

        return null;
    }
}

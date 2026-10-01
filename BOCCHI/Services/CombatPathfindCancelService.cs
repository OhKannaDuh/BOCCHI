using BOCCHI.Automator.Services;
using BOCCHI.Common.Data.OccultCrescent;
using BOCCHI.Common.Data.Paths;
using BOCCHI.Common.Data.StateMemory;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Services;
using BOCCHI.Common.Services.Paths;
using BOCCHI.MobFarmer.Services;
using BOCCHI.Treasure.Services;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Ocelot.Actions;
using Ocelot.Chain;
using Ocelot.Ipc.VNavmesh;
using Ocelot.Lifecycle;
using Ocelot.Services.Logger;
using Ocelot.Services.Pathfinding;

namespace BOCCHI.Services;

public sealed unsafe class CombatPathfindCancelService
(
    IGameInteropProvider interop,
    IClientState client,
    IPathfinder pathfinder,
    IVNavmeshIpc vnav,
    IChainManager chains,
    IAutomatorMemory memory,
    IAutomator automator,
    IZoneProvider zones,
    IFateContext fates,
    ICriticalEncounterContext criticalEncounters,
    Func<IMobFarmer> farmerFactory,
    Func<ITreasureHunter> hunterFactory,
    Func<ICarrotHunter> carrotHunterFactory,
    ILogger<CombatPathfindCancelService> logger
) : IOnStart, IOnStop, IDisposable
{
    private delegate bool UseActionDelegate(
        ActionManager* manager,
        ActionType actionType,
        uint actionId,
        ulong targetId,
        uint extraParam,
        ActionManager.UseActionMode mode,
        uint comboRouteId,
        bool* outOptAreaTargeted);

    private Hook<UseActionDelegate>? useActionHook;

    public void OnStart()
    {
        try
        {
            useActionHook = interop.HookFromAddress<UseActionDelegate>(
                (nint)ActionManager.MemberFunctionPointers.UseAction,
                UseActionDetour);
            useActionHook.Enable();
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to hook UseAction for combat pathfind cancel");
        }
    }

    public void OnStop() => Dispose();

    public void Dispose()
    {
        useActionHook?.Disable();
        useActionHook?.Dispose();
        useActionHook = null;
    }

    private bool UseActionDetour(
        ActionManager* manager,
        ActionType actionType,
        uint actionId,
        ulong targetId,
        uint extraParam,
        ActionManager.UseActionMode mode,
        uint comboRouteId,
        bool* outOptAreaTargeted)
    {
        try
        {
            if (!ActionCastScope.IsSuppressingPathfindCancel
                && ShouldCancelPathfinding(actionType, actionId))
            {
                CancelPathfinding();
            }
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Combat pathfind cancel failed");
        }

        return useActionHook!.Original(
            manager, actionType, actionId, targetId, extraParam, mode, comboRouteId, outOptAreaTargeted);
    }

    private bool ShouldCancelPathfinding(ActionType actionType, uint actionId)
    {
        if (!IsInOccultCrescentTerritory())
        {
            return false;
        }

        if (actionType != ActionType.Action || actionId == 0)
        {
            return false;
        }

        if (actionId == PhantomActions.OccultSprint)
        {
            return false;
        }

        if (ActivityOwnsMovement())
        {
            return false;
        }

        PathfindingState state = pathfinder.GetState();
        return state is PathfindingState.Moving or PathfindingState.Pathfinding
               || vnav.IsRunning()
               || vnav.IsPathfinding();
    }

    private bool IsInOccultCrescentTerritory()
    {
        ushort territory = (ushort)client.TerritoryType;
        if (territory is not ((ushort)ZoneId.SouthHorn or (ushort)ZoneId.NorthHorn))
        {
            return false;
        }

        return zones.GetZone().IsOccultCrescentZone();
    }

    private bool ActivityOwnsMovement() =>
        memory.TryRemember<SuspendTravelForActivityMemory>(out SuspendTravelForActivityMemory _)
        || memory.TryRemember<CommittedFateMemory>(out CommittedFateMemory _)
        || fates.IsInFate()
        || criticalEncounters.IsInCriticalEncounter()
        || farmerFactory().Running
        || hunterFactory().Running
        || carrotHunterFactory().Running;

    private void CancelPathfinding()
    {
        // Don't abort aethernet Teleport mid-hop, or the Pathfind onto the pad before it —
        // combat while walking to the camp pad was dropping Return→TP and remounting (#174).
        if (memory.TryRemember<GoalPathStepMemory>(out GoalPathStepMemory path)
            && path.IsApproachingAethernetTeleport())
        {
            return;
        }

        logger.Debug("Combat action used — canceling pathfinding");
        pathfinder.Stop();
        vnav.Stop();
        chains.CancelWhere(name =>
            name.StartsWith("ActivityGoto::", StringComparison.Ordinal)
            || (PathStepSoftStop.IsPathStepChain(name)
                && !name.StartsWith($"{PathStepSoftStop.Prefix}Teleport", StringComparison.Ordinal)));

        memory.Forget<GoalPathStepMemory>();
        memory.Forget<BaseTeleportDelayMemory>();

        if (automator.IsActive)
        {
            return;
        }

        if (memory.TryRemember<GoalMemory>(out _))
        {
            memory.Forget<GoalMemory>();
            memory.TryAdd<NavigationInterruptedMemory>();
        }
    }
}

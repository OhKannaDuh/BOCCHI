using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Ocelot.Chain;
using Ocelot.Chain.Extensions;
using Ocelot.Chain.Middleware.Chain;
using Ocelot.Chain.Middleware.Step;
using Ocelot.Extensions;
using Ocelot.Ipc.VNavmesh;
using Ocelot.Services.PlayerState;
using BOCCHI.Treasure.Services;
using System.Numerics;
using DalamudObjectKind = Dalamud.Game.ClientState.Objects.Enums.ObjectKind;
using TreasureFlags = FFXIVClientStructs.FFXIV.Client.Game.Object.Treasure.TreasureFlags;
using TreasureState = FFXIVClientStructs.FFXIV.Client.Game.Object.Treasure.TreasureState;
using CsObjectKind = FFXIVClientStructs.FFXIV.Client.Game.Object.ObjectKind;

namespace BOCCHI.Treasure.ChainRecipes;

public readonly record struct TreasureOpenTarget(Vector3 Position, IReadOnlyList<uint>? PreferredBaseIds = null)
{
    public static implicit operator TreasureOpenTarget(Vector3 position) => new(position);
}

public class OpenTreasureCofferChain
(
    IChainFactory chains,
    IObjectTable objects,
    IPlayer player,
    ICondition conditions,
    IVNavmeshIpc vnav
) : ChainRecipe<TreasureOpenTarget>(chains)
{
    public const float PathArrivalRange = 1.0f;

    public const float PreferredOpenDistance = 2.0f;

    public const float OpenAttemptSlack = 1.5f;

    public static float MaxOpenAttemptDistance => PreferredOpenDistance + OpenAttemptSlack;

    public const float InteractDistance = PreferredOpenDistance;

    public override string Name => "Open Treasure Coffer";

    protected override IChain Compose(IChain chain, TreasureOpenTarget target)
    {
        var pathState = new PathState();

        return chain
            .UseMiddleware<LogChainMiddleware>()
            .UseStepMiddleware<LogStepMiddleware>()
            .UseStepMiddleware<RunOnMainThreadMiddleware>()
            .WaitUntil(
                _ => ValueTask.FromResult(TryInteract(target, pathState)),
                TimeSpan.FromSeconds(45),
                TimeSpan.FromMilliseconds(200),
                "OpenTreasureCofferChain::Interact"
            );
    }

    private bool TryInteract(TreasureOpenTarget target, PathState pathState)
    {
        if (conditions[ConditionFlag.BetweenAreas]
            || conditions[ConditionFlag.Unconscious])
        {
            return false;
        }

        IGameObject? nearby = FindMatchingTreasureNear(target, searchRadius: 6f);
        if (nearby != null)
        {
            pathState.SawChest = true;

            unsafe
            {
                GameObject* gameObject = (GameObject*)(void*)nearby.Address;
                var tr = (FFXIVClientStructs.FFXIV.Client.Game.Object.Treasure*)gameObject;

                if (IsOpenedOrLooted(nearby, tr))
                {
                    StopNav();
                    return true;
                }

                // Pot reveals often report Y ≈ -500 — use 2D so we still walk up and open (#170).
                float dist2d = player.Position.Distance2D(nearby.Position);
                if (dist2d > PreferredOpenDistance)
                {
                    EnsurePathing(nearby.Position, pathState);
                    if (dist2d > MaxOpenAttemptDistance)
                    {
                        return false;
                    }
                }
                else
                {
                    StopNav();
                }

                // Pot reveals open on a cast; re-issuing Interact every 200ms restarts it.
                if (player.IsCasting()
                    || conditions[ConditionFlag.Casting]
                    || conditions[ConditionFlag.Casting87])
                {
                    return false;
                }

                if (!gameObject->GetIsTargetable())
                {
                    return false;
                }

                if (!EzThrottler.Throttle("ChestThrottle", 500))
                {
                    return false;
                }

                pathState.InteractAttempted = true;
                TargetSystem.Instance()->InteractWithObject(gameObject, false);
                return IsOpenedOrLooted(nearby, tr);
            }
        }

        if (pathState.SawChest || pathState.InteractAttempted)
        {
            StopNav();
            return true;
        }

        EnsurePathing(target.Position, pathState);
        return false;
    }

    private void StopNav()
    {
        if (vnav.IsRunning() || vnav.IsPathfinding())
        {
            vnav.Stop();
        }
    }

    private void EnsurePathing(Vector3 destination, PathState pathState)
    {
        Vector3 moveTarget = PathableWhenFar(destination);

        // Stop only once within Pandora's ≤2y interact gate. Stopping earlier left us
        // Interact-spamming outside game range until the 45s WaitUntil timed out.
        if (player.Position.Distance2D(destination) <= PreferredOpenDistance)
        {
            StopNav();
            return;
        }

        const float RepathDrift = 1.5f;
        bool drifted = pathState.LastTarget is not { } last
                       || last.Distance2D(moveTarget) > RepathDrift;

        bool cooling = !drifted
                       && pathState.LastIssuedUtc != DateTime.MinValue
                       && DateTime.UtcNow - pathState.LastIssuedUtc < TimeSpan.FromSeconds(2.5);

        if (cooling)
        {
            return;
        }

        if ((!vnav.IsRunning() && !vnav.IsPathfinding()) || drifted)
        {
            pathState.LastTarget = moveTarget;
            pathState.LastIssuedUtc = DateTime.UtcNow;
            vnav.PathfindAndMoveCloseTo(moveTarget, false, PreferredOpenDistance);
        }
    }

    private Vector3 PathableWhenFar(Vector3 position)
    {
        if (player.Position.Distance2D(position) <= MaxOpenAttemptDistance + 4f)
        {
            return TreasurePathing.PathablePosition(position, player.Position.Y);
        }

        _ = TreasurePathing.TrySnapToNavmesh(position, player.Position.Y, vnav, out Vector3 pathable);
        return pathable;
    }

    public static unsafe bool IsOpenedOrLooted(IGameObject chest)
    {
        GameObject* gameObject = (GameObject*)(void*)chest.Address;
        var tr = (FFXIVClientStructs.FFXIV.Client.Game.Object.Treasure*)gameObject;
        return IsOpenedOrLooted(chest, tr);
    }

    private static unsafe bool IsOpenedOrLooted(
        IGameObject chest,
        FFXIVClientStructs.FFXIV.Client.Game.Object.Treasure* tr
    )
    {
        // Only a real Treasure has the Treasure struct behind it. Pot reveals are EventObj, and
        // reading Flags/State through that cast is reading whatever happens to sit at those
        // offsets — a junk "already opened" would silently retire an unopened coffer. For those,
        // the Loot window is the only trustworthy signal.
        if (chest.ObjectKind == DalamudObjectKind.Treasure
            && (tr->Flags.HasFlag(TreasureFlags.Opened)
                || tr->Flags.HasFlag(TreasureFlags.FadedOut)
                || tr->State is TreasureState.Opened or TreasureState.FadingOut or TreasureState.FadedOut))
        {
            return true;
        }

        Loot* loot = Loot.Instance();
        if (loot == null)
        {
            return false;
        }

        foreach (LootItem item in loot->Items)
        {
            if (item.ChestObjectId == chest.GameObjectId)
            {
                return true;
            }
        }

        return false;
    }

    private IGameObject? FindMatchingTreasureNear(TreasureOpenTarget target, float searchRadius)
    {
        Vector3 position = target.Position;
        IReadOnlyList<uint>? preferred = target.PreferredBaseIds;

        return objects
            .Where(o =>
            {
                if (!o.IsValid() || o.IsDead)
                {
                    return false;
                }

                if (position.Distance2D(o.Position) > searchRadius)
                {
                    return false;
                }

                if (preferred is { Count: > 0 })
                {
                    return MatchesOpenFilter(o, preferred);
                }

                if (!MatchesOpenFilter(o, preferred))
                {
                    return false;
                }

                unsafe
                {
                    var obj = (GameObject*)(void*)o.Address;
                    return (CsObjectKind)obj->ObjectKind == CsObjectKind.Treasure;
                }
            })
            .OrderBy(o => player.Position.Distance2D(o.Position))
            .FirstOrDefault();
    }

    private static bool MatchesOpenFilter(IGameObject obj, IReadOnlyList<uint>? preferredBaseIds)
    {
        if (preferredBaseIds is { Count: > 0 })
        {
            for (int i = 0; i < preferredBaseIds.Count; i++)
            {
                if (obj.BaseId == preferredBaseIds[i])
                {
                    return true;
                }
            }

            return false;
        }

        return obj.ObjectKind == DalamudObjectKind.Treasure;
    }

    private sealed class PathState
    {
        public Vector3? LastTarget;

        public DateTime LastIssuedUtc;

        public bool SawChest;

        public bool InteractAttempted;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using BOCCHI.ActionHelpers;
using BOCCHI.Data;
using BOCCHI.Enums;
using BOCCHI.ItemHelpers;
using BOCCHI.Pathfinding;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Ocelot.Chain;
using Ocelot.Chain.ChainEx;
using ObjectKind = Dalamud.Game.ClientState.Objects.Enums.ObjectKind;

namespace BOCCHI.Modules.Carrots;

public class CarrotHunt(CarrotsModule module) : Hunter(module)
{
    protected override IEnumerable<IGameObject> GetValidObjects()
    {
        return Svc.Objects
            .Where(o => o is
            {
                ObjectKind: ObjectKind.EventObj,
                BaseId: (uint)OccultObjectType.Carrot,
                IsDead: false,
            } && o.IsValid());
    }

    protected override Vector3 GetDestinationForCurrentStep()
    {
        return CarrotData.Data.First(c => c.Id == CurrentStep.NodeId).Position;
    }

    protected override IPathfinder CreatePathfinder()
    {
        return new Pathfinder(module.PluginConfig.PathfinderConfig.ReturnCost, module.PluginConfig.PathfinderConfig.TeleportCost);
    }

    protected override unsafe Func<Chain> GetInteractionChain(IGameObject obj)
    {
        return () => Chain.Create()
            .BreakIf(() => !GetValidObjects().Any(o => Vector3.Distance(o.Position, obj.Position) <= DISTANCE_TO_NODE_TO_USE))
            .ConditionalThen(_ => Player.Mounted, _ => Actions.Unmount.Cast())
            .Wait(500)
            .BreakIf(() => Items.FortuneCarrot.Count() <= 0)
            .Then(_ => Items.FortuneCarrot.Use())
            .WaitToCast()
            .Then(_ => GetBunnyChests().Any())
            .Then(_ =>
            {
                var chest = GetBunnyChests().FirstOrDefault();
                if (chest == null)
                {
                    return true;
                }

                Svc.Targets.Target = chest;

                var gameObject = (GameObject*)(void*)chest.Address;
                TargetSystem.Instance()->InteractWithObject(gameObject);
                return Svc.ClientState.LocalPlayer?.IsCasting == true;
            })
            .WaitToCast();
    }

    protected override List<uint> GetValidNodes(int max)
    {
        return CarrotData.Data.Where(node => node.Level <= max).Select(node => node.Id).ToList();
    }

    protected override void Teardown()
    {
        if (!module.Config.RepeatCarrotHunt)
        {
            stopwatch.Stop();
            running = false;
        }

        stepIndex = 0;
        Steps.Clear();
        vnav.Stop();
        Plugin.Chain.Abort();
        StepProcessor.Abort();
        pathfinder = null;
    }

    private IEnumerable<IEventObj> GetBunnyChests()
    {
        return Svc.Objects.OfType<IEventObj>().Where(o => o.BaseId == (uint)OccultObjectType.BunnyChest);
    }
}

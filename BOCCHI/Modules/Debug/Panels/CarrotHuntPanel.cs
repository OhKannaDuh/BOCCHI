using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Threading.Tasks;
using BOCCHI.ActionHelpers;
using BOCCHI.Data;
using BOCCHI.Enums;
using BOCCHI.ItemHelpers;
using BOCCHI.Modules.Data;
using BOCCHI.Pathfinding;
using Dalamud.Game.ClientState.Objects.SubKinds;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Dalamud.Bindings.ImGui;
using Ocelot.Ui;
using Ocelot.Chain;
using Ocelot.Chain.ChainEx;
using Ocelot.IPC;

namespace BOCCHI.Modules.Debug.Panels;

public class CarrotHuntPanel : Panel
{
    private bool HasRun = false;

    private bool ShouldRun = false;

    private Stopwatch stopwatch = new();

    private Task? task = null;

    private uint Progress = 0;

    private readonly uint MaxProgress = 0;

    private ChainQueue ChainQueue
    {
        get => ChainManager.Get("CarrotHuntPanelChain");
    }

    public CarrotHuntPanel()
    {
        var carrotCount = CarrotData.Data.Count;
        var aethernetCount = Enum.GetNames(typeof(Aethernet)).Length;

        MaxProgress = (uint)(carrotCount * (carrotCount - 1));
        MaxProgress += (uint)(aethernetCount * carrotCount);
        MaxProgress += (uint)(carrotCount * aethernetCount);
    }

    public override string GetName()
    {
        return "Carrot Hunt Helper";
    }

    public override unsafe void Render(DebugModule module)
    {
        var vnav = module.GetIPCSubscriber<VNavmesh>();
        OcelotUi.LabelledValue("Carrots", CarrotData.Data.Count); // 25

        OcelotUi.Indent(() =>
        {
            if (ImGui.Button("Test carrot usage chain"))
            {
                Plugin.Chain.Submit(() => Chain.Create()
                    .ConditionalThen(_ => Player.Mounted, _ => Actions.Unmount.Cast())
                    .Wait(500)
                    .BreakIf(() => Items.FortuneCarrot.Count() <= 0)
                    .Then(_ => Items.FortuneCarrot.Use())
                    .WaitToCast()
                    .Then(_ => GetBunnyChests().Any())
                    .Then(_ =>
                    {
                        var target = GetBunnyChests().FirstOrDefault();
                        if (target == null)
                        {
                            return true;
                        }

                        Svc.Targets.Target = target;

                        if (!vnav.IsRunning())
                        {
                            vnav.PathfindAndMoveTo(target.Position, false);
                        }

                        if (Player.DistanceTo(target) <= 2f)
                        {
                            var gameObject = (GameObject*)(void*)target.Address;
                            TargetSystem.Instance()->InteractWithObject(gameObject);
                            return true;
                        }

                        return false;
                    })
                    .WaitToCast()
                );
            }

            var obj = Svc.Objects.OfType<IEventObj>();
            foreach (var o in obj)
            {
                ImGui.TextUnformatted(o.Name + " " + o.ObjectKind + " " + o.BaseId);
            }

            if (!HasRun)
            {
                if (ImGui.Button("Run"))
                {
                    ShouldRun = true;
                }

                return;
            }

            var Completion = (float)Progress / (float)MaxProgress * 100;

            OcelotUi.LabelledValue("Progress: ", $"{Completion:f2}%");
            OcelotUi.Indent(() => OcelotUi.LabelledValue("Calculations: ", $"{Progress}/{MaxProgress}"));
            OcelotUi.LabelledValue("Elapsed: ", stopwatch.Elapsed.ToString("mm\\:ss"));
        });
    }

    public override void Update(DebugModule module)
    {
        if (!ShouldRun || HasRun || task != null)
        {
            return;
        }

        ShouldRun = true;
        HasRun = true;

        task = PrecomputeCarrotPathDistances(module);
    }

    private Task PrecomputeCarrotPathDistances(DebugModule module)
    {
        stopwatch.Restart();
        var outputFile = Path.Join(ZoneData.GetCurrentZoneDataDirectory(), "precomputed_carrot_hunt_data.json");

        var vnav = module.GetIPCSubscriber<VNavmesh>();

        NodeDataSchema data = new();
        foreach (var datum in AethernetData.All())
        {
            data.AethernetToNodeDistances[datum.Aethernet] = [];
        }

        foreach (var carrot in CarrotData.Data)
        {
            data.NodeToNodeDistances[carrot.Id] = [];
            data.NodeToAethernetDistances[carrot.Id] = [];

            foreach (var other in CarrotData.Data.Where(c => c.Id != carrot.Id))
            {
                ChainQueue.Submit(() =>
                    Chain.Create()
                        .Then(async void (_) =>
                        {
                            var path = await vnav.Pathfind(carrot.Position, other.Position, false);
                            var distance = CalculatePathLength(path);

                            var nodes = path.Select(Position.Create).ToList();

                            data.NodeToNodeDistances[carrot.Id].Add(new ToNode(other.Id, distance, nodes));

                            Progress++;
                        })
                        .Then(_ => !vnav.IsRunning())
                );
            }

            foreach (var datum in AethernetData.All())
            {
                ChainQueue.Submit(() =>
                    Chain.Create()
                        .Then(async void (_) =>
                        {
                            var path = await vnav.Pathfind(datum.Destination, carrot.Position, false);
                            var distance = CalculatePathLength(path);

                            var nodes = path.Select(Position.Create).ToList();

                            data.AethernetToNodeDistances[datum.Aethernet].Add(new ToNode(carrot.Id, distance, nodes));

                            Progress++;
                        })
                        .Then(_ => !vnav.IsRunning())
                );

                ChainQueue.Submit(() =>
                    Chain.Create()
                        .Then(async void (_) =>
                        {
                            var path = await vnav.Pathfind(datum.Destination, carrot.Position, false);
                            var distance = CalculatePathLength(path);

                            var nodes = path.Select(Position.Create).ToList();

                            data.NodeToAethernetDistances[carrot.Id].Add(new ToAethernet(datum.Aethernet, distance, nodes));

                            Progress++;
                        })
                        .Then(_ => !vnav.IsRunning())
                );
            }
        }

        ChainQueue.Submit(() =>
            Chain.Create()
                .Then(_ =>
                {
                    stopwatch.Stop();

                    var options = new JsonSerializerOptions
                    {
                        WriteIndented = false,
                        IncludeFields = false,
                    };

                    Svc.Log.Info("Saving file to " + outputFile);
                    var json = JsonSerializer.Serialize(data, options);
                    File.WriteAllTextAsync(outputFile, json);
                })
        );
        return Task.CompletedTask;
    }

    private float CalculatePathLength(List<Vector3> path)
    {
        var length = 0f;

        for (var i = 1; i < path.Count; i++)
        {
            length += Vector3.Distance(path[i - 1], path[i]);
        }

        return length;
    }

    private IEnumerable<IEventObj> GetBunnyChests()
    {
        return Svc.Objects.OfType<IEventObj>().Where(o => o.BaseId == (uint)OccultObjectType.BunnyChest);
    }
}

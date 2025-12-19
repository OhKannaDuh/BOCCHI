using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using ECommons.GameFunctions;
using ECommons.GameHelpers;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using Dalamud.Bindings.ImGui;
using Ocelot.Ui;

namespace BOCCHI.Modules.Debug.Panels;

public class EnemyPanel : Panel
{
    public override string GetName()
    {
        return "Nearby Enemies";
    }

    private List<IGameObject> enemies = [];

    public override unsafe void Render(DebugModule module)
    {
        OcelotUi.Indent(() =>
        {
            foreach (var enemy in enemies)
            {
                if (ImGui.CollapsingHeader($"{enemy.Name} - {enemy.BaseId}##{enemy.ObjectIndex}"))
                {
                    OcelotUi.Indent(() =>
                    {
                        ImGui.Text($"Name: {enemy.Name.TextValue}");
                        ImGui.Text($"GameObjectId: {enemy.GameObjectId:X}");
                        ImGui.Text($"EntityId: {enemy.EntityId:X}");
                        ImGui.Text($"BaseId: {enemy.BaseId}");
                        ImGui.Text($"OwnerId: {enemy.OwnerId}");
                        ImGui.Text($"ObjectIndex: {enemy.ObjectIndex}");
                        ImGui.Text($"ObjectKind: {enemy.ObjectKind}");
                        ImGui.Text($"SubKind: {enemy.SubKind}");
                        ImGui.Text($"Position: {enemy.Position}");
                        ImGui.Text($"Rotation: {enemy.Rotation}");
                        ImGui.Text($"HitboxRadius: {enemy.HitboxRadius}");
                        ImGui.Text($"YalmDistanceX: {enemy.YalmDistanceX}");
                        ImGui.Text($"YalmDistanceZ: {enemy.YalmDistanceZ}");
                        ImGui.Text($"IsDead: {enemy.IsDead}");
                        ImGui.Text($"IsTargetable: {enemy.IsTargetable}");
                        ImGui.Text($"TargetObjectId: {enemy.TargetObjectId:X}");

                        if (enemy.TargetObject is { } target)
                        {
                            ImGui.Text($"TargetObject: {target.Name.TextValue} ({target.GameObjectId:X})");
                        }
                        else
                        {
                            ImGui.Text($"TargetObject: None");
                        }

                        ImGui.Text($"IsValid(): {enemy.IsValid()}");
                        ImGui.Text($"Address: 0x{enemy.Address.ToInt64():X}");


                        var battleChara = (BattleChara*)enemy.Address;


                        ImGui.Text($"LayoutId: {battleChara->LayoutId}");
                        ImGui.Text($"Level: {battleChara->ForayInfo.Level}");

                        var distance = Player.DistanceTo(enemy.Position);
                        if (distance <= 30f)
                        {
                            if (ImGui.Button("Target"))
                            {
                                Svc.Targets.Target = enemy;
                            }
                        }
                    });
                }
            }
        });
    }

    public override void Update(DebugModule module)
    {
        if (EzThrottler.Throttle("enemies", 2000))
        {
            // DoThing();
            enemies = Svc.Objects
                .Where(o =>
                    o != null &&
                    o.IsHostile() &&
                    o.IsTargetable &&
                    o.Name.TextValue.Length > 0
                )
                .OrderBy(o => Vector3.Distance(o.Position, Player.Position))
                .ToList();
        }
    }
}

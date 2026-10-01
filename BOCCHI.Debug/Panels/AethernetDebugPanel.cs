using BOCCHI.Common.Data.Aethernet;
using BOCCHI.Common.Data.Zones;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;
using Ocelot.Ipc.Lifestream;
using Ocelot.Services.UI;
using System.Numerics;
using System.Text;

namespace BOCCHI.Debug.Panels;

public sealed class AethernetDebugPanel
(
    ITargetManager targets,
    IObjectTable objects,
    IDataManager data,
    IZoneProvider zones,
    ILifestreamIpc lifestream,
    IPluginLog log,
    IBrandingService branding,
    IUIService ui
) : IDebugPanel
{
    private readonly List<Capture> captures = [];

    private ulong lastTargetId;

    private Capture? pending;

    public string Name => "Aethernet";

    public void Render()
    {
        ui.Text("Target / click an aetheryte or aethernet shard.", branding.DalamudGrey);
        ui.Text("Stand where you want Destination, then Capture (or auto-fills if already close).", branding.DalamudGrey);

        unsafe
        {
            TerritoryInfo* info = TerritoryInfo.Instance();
            uint subArea = info == null ? 0u : info->SubAreaPlaceNameId;
            ui.LabelledValue("SubArea PlaceName Id", FormatPlaceName(subArea));
        }

        try
        {
            ui.LabelledValue("Lifestream active custom", lifestream.GetActiveCustomAetheryte());
        }
        catch
        {
            ui.LabelledValue("Lifestream active custom", "(IPC unavailable)");
        }

        IGameObject? target = targets.Target ?? targets.SoftTarget;
        WatchTarget(target);

        ImGui.Separator();
        ui.Text("Current target", branding.DalamudYellow);

        if (target == null || !IsAethernetCandidate(target))
        {
            ui.Text(target == null ? "Nothing targeted." : $"Not an aetheryte candidate ({target.ObjectKind} / BaseId {target.BaseId}).");
        }
        else
        {
            RenderTarget(target);
        }

            if (pending != null)
        {
            Capture pendingCapture = pending.Value;
            ImGui.Separator();
            ui.Text("Pending capture", branding.DalamudYellow);
            RenderCapture(pendingCapture);

            if (ImGui.Button("Set Destination from player"))
            {
                pending = WithPlayerDestination(pendingCapture);
            }

            ImGui.SameLine();
            if (ImGui.Button("Capture"))
            {
                Capture ready = pendingCapture.Destination == Vector3.Zero
                    ? WithPlayerDestination(pendingCapture)
                    : pendingCapture;
                AddCapture(ready);
                pending = null;
            }

            ImGui.SameLine();
            if (ImGui.Button("Copy snippet"))
            {
                Capture ready = pendingCapture.Destination == Vector3.Zero
                    ? WithPlayerDestination(pendingCapture)
                    : pendingCapture;
                CopySnippet(ready);
            }
        }

        if (captures.Count > 0)
        {
            ImGui.Separator();
            ui.Text($"Captured ({captures.Count})", branding.DalamudYellow);

            if (ImGui.Button("Copy all snippets"))
            {
                CopySnippet(captures.ToArray());
            }

            ImGui.SameLine();
            if (ImGui.Button("Clear captures"))
            {
                captures.Clear();
            }

            for(int i = 0; i < captures.Count; i++)
            {
                Capture capture = captures[i];
                if (ImGui.TreeNode($"{capture.Label}  BaseId={capture.BaseId}  Id={capture.PlaceNameId}##cap{i}"))
                {
                    RenderCapture(capture);
                    if (ImGui.Button($"Copy##{i}"))
                    {
                        CopySnippet(capture);
                    }

                    ImGui.TreePop();
                }
            }
        }

        ImGui.Separator();
        ui.Text("Nearby live aetheryte objects", branding.DalamudYellow);
        ui.Text("These BaseIds come from the game — use Target + Capture to author them.", branding.DalamudGrey);
        if (objects.LocalPlayer is { } player)
        {
            foreach(IGameObject obj in objects
                         .Where(IsAethernetCandidate)
                         .OrderBy(o => Vector3.DistanceSquared(o.Position, player.Position))
                         .Take(12))
            {
                float dist = Vector3.Distance(obj.Position, player.Position);
                ui.LabelledValue(
                    $"{obj.Name.TextValue} ({obj.ObjectKind})",
                    $"BaseId={obj.BaseId} Pos={Fmt(obj.Position)} Dist={dist:0.#}");
            }
        }

        ImGui.Separator();
        ui.Text("Zone authored (in code — /xlreload after edits)", branding.DalamudYellow);
        foreach(AethernetData authored in zones.GetZone().GetAetherytes())
        {
            string stub = authored.BaseId == 0 ? " STUB" : "";
            string lockState = zones.GetZone().IsUsableAethernetDestination(authored.Id) ? "unlocked" : "LOCKED";
            ui.LabelledValue(
                FormatPlaceName(authored.Id) + stub,
                $"{lockState}  Id={authored.Id} BaseId={authored.BaseId} Pos={Fmt(authored.Position)} Dest={Fmt(authored.Destination)} Dead={authored.DeadRadius:0.##}");
        }
    }

    private void WatchTarget(IGameObject? target)
    {
        if (target == null || !IsAethernetCandidate(target))
        {
            return;
        }

        if (target.GameObjectId == lastTargetId)
        {
            return;
        }

        lastTargetId = target.GameObjectId;
        pending = CreateCapture(target);
        log.Information("[AethernetDebug] Targeted {Name} BaseId={BaseId} Pos={Pos}", target.Name.TextValue, target.BaseId, Fmt(target.Position));
    }

    private Capture CreateCapture(IGameObject target)
    {
        uint placeNameId = 0;
        unsafe
        {
            TerritoryInfo* info = TerritoryInfo.Instance();
            if (info != null)
            {
                placeNameId = info->SubAreaPlaceNameId;
            }
        }

        try
        {
            uint active = lifestream.GetActiveCustomAetheryte();
            if (active != 0 && active < 100_000)
            {
                placeNameId = active;
            }
        }
        catch
        {
        }

        if (placeNameId == 0)
        {
            AethernetData? nearest = zones.GetZone().GetAetherytes()
                .OrderBy(a => Vector3.DistanceSquared(a.Position, target.Position))
                .FirstOrDefault();
            if (nearest != null && Vector3.Distance(nearest.Position, target.Position) < 50f && nearest.Id != 0)
            {
                placeNameId = nearest.Id;
            }
        }

        Capture capture = new(
            SanitizeLabel(target.Name.TextValue),
            target.Name.TextValue,
            target.BaseId,
            placeNameId,
            target.Position,
            Vector3.Zero,
            3.2f);

        if (objects.LocalPlayer is { } player
            && Vector3.Distance(player.Position, target.Position) <= AethernetData.InteractRadius + 2f)
        {
            capture = WithPlayerDestination(capture);
        }

        return capture;
    }

    private Capture WithPlayerDestination(Capture capture)
    {
        if (objects.LocalPlayer is not { } player)
        {
            return capture;
        }

        float dead = MathF.Max(3f, Vector3.Distance(capture.Position, player.Position));
        return capture with
        {
            Destination = player.Position,
            DeadRadius = MathF.Round(dead, 2)
        };
    }

    private void AddCapture(Capture capture)
    {
        captures.RemoveAll(c => c.BaseId == capture.BaseId);
        captures.Add(capture);
        log.Information(
            "[AethernetDebug] Captured Id={Id} BaseId={BaseId} Pos={Pos} Dest={Dest} Dead={Dead}",
            capture.PlaceNameId,
            capture.BaseId,
            Fmt(capture.Position),
            Fmt(capture.Destination),
            capture.DeadRadius);
        CopySnippet(capture);
    }

    private void RenderTarget(IGameObject target)
    {
        ui.LabelledValue("Name", target.Name.TextValue);
        ui.LabelledValue("ObjectKind", target.ObjectKind);
        ui.LabelledValue("BaseId", target.BaseId);
        ui.LabelledValue("Position", Fmt(target.Position));
        if (objects.LocalPlayer is { } player)
        {
            ui.LabelledValue("Player (suggested Destination)", Fmt(player.Position));
            ui.LabelledValue("Distance", $"{Vector3.Distance(player.Position, target.Position):0.##}");
        }
    }

    private void RenderCapture(Capture capture)
    {
        ui.LabelledValue("Label", capture.Label);
        ui.LabelledValue("Name", capture.Name);
        ui.LabelledValue("Id (PlaceName)", FormatPlaceName(capture.PlaceNameId));
        ui.LabelledValue("BaseId", capture.BaseId);
        ui.LabelledValue("Position", Fmt(capture.Position));
        ui.LabelledValue("Destination", capture.Destination == Vector3.Zero ? "(unset)" : Fmt(capture.Destination));
        ui.LabelledValue("DeadRadius", capture.DeadRadius);
    }

    private void CopySnippet(params Capture[] items)
    {
        string text = string.Join("\n\n", items.Select(ToSnippet));
        ImGui.SetClipboardText(text);
        log.Information("[AethernetDebug] Copied {Count} snippet(s) to clipboard", items.Length);
    }

    private static string ToSnippet(Capture capture)
    {
        StringBuilder sb = new();
        sb.AppendLine($"private static readonly AethernetData {capture.Label} = new()");
        sb.AppendLine("{");
        sb.AppendLine($"    Id = {capture.PlaceNameId},");
        sb.AppendLine($"    BaseId = {capture.BaseId},");
        sb.AppendLine($"    Position = new({FmtCode(capture.Position)}),");
        Vector3 destination = capture.Destination == Vector3.Zero ? capture.Position : capture.Destination;
        sb.AppendLine($"    Destination = new({FmtCode(destination)}),");
        sb.AppendLine($"    DeadRadius = {capture.DeadRadius.ToString(System.Globalization.CultureInfo.InvariantCulture)}f");
        sb.Append("};");
        return sb.ToString();
    }

    private string FormatPlaceName(uint id)
    {
        if (id == 0)
        {
            return "0";
        }

        string name = "?";
        if (data.GetExcelSheet<PlaceName>().TryGetRow(id, out PlaceName row))
        {
            name = row.Name.ToString();
        }

        return $"{id} ({name})";
    }

    private static bool IsAethernetCandidate(IGameObject obj) =>
        obj.ObjectKind is ObjectKind.Aetheryte or ObjectKind.EventObj;

    private static string SanitizeLabel(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Aethernet";
        }

        StringBuilder sb = new();
        bool upperNext = true;
        foreach(char c in name)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(upperNext ? char.ToUpperInvariant(c) : c);
                upperNext = false;
            }
            else
            {
                upperNext = true;
            }
        }

        string label = sb.ToString();
        return string.IsNullOrEmpty(label) ? "Aethernet" : label;
    }

    private static string Fmt(Vector3 v) => $"({v.X:0.###}, {v.Y:0.###}, {v.Z:0.###})";

    private static string FmtCode(Vector3 v)
    {
        System.Globalization.CultureInfo inv = System.Globalization.CultureInfo.InvariantCulture;
        return $"{v.X.ToString("0.###", inv)}f, {v.Y.ToString("0.###", inv)}f, {v.Z.ToString("0.###", inv)}f";
    }

    private readonly record struct Capture(
        string Label,
        string Name,
        uint BaseId,
        uint PlaceNameId,
        Vector3 Position,
        Vector3 Destination,
        float DeadRadius);
}

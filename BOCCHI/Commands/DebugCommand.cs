using System.Globalization;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;
using BOCCHI.Automator.Services.PotTreasure;
using BOCCHI.Common;
using BOCCHI.Common.Config;
using BOCCHI.Common.Data.CriticalEncounters;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Data.Zones.Graph;
using BOCCHI.Common.Services;
using BOCCHI.Debug;
using BOCCHI.Services.Shopping;
using Dalamud.Plugin.Services;
using Ocelot.Rotation.Services;
using Ocelot.Rotation.Services.BossMod;
using Ocelot.Services.Commands;
using Ocelot.Services.PlayerState;
using Ocelot.Services.Translation;
using DalamudObjectKind = Dalamud.Game.ClientState.Objects.Enums.ObjectKind;
using GameDynamicEvent = FFXIVClientStructs.FFXIV.Client.Game.InstanceContent.DynamicEvent;

namespace BOCCHI.Commands;

public unsafe class DebugCommand
(
    IDebugWindow debugWindow,
    BossModMiscAiBackend bossModMiscAi,
    CombatAiPresetNaming presetNaming,
    IPlayer player,
    IObjectTable objects,
    IZoneProvider zones,
    IDataManager data,
    CriticalEncounterGeometry geometry,
    IShoppingService shopping,
    IChatGui chat,
    UIConfig uiConfig,
    ITranslator<DebugCommand> translator
) : OcelotCommand(translator)
{
    public override string Command => "debug";

    public override List<string> Aliases => [];

    public override void Execute(CommandContext context)
    {
        if (context.Args.Length == 0)
        {
            debugWindow.Toggle();
            return;
        }

        switch (context.Args[0].ToLowerInvariant())
        {
            case "ai-preset":
            case "make-ai-preset":
                MakeAiPreset();
                break;
            case "open":
                debugWindow.IsOpen = true;
                break;
            case "close":
                debugWindow.IsOpen = false;
                break;
            case "toggle":
                debugWindow.Toggle();
                break;
            case "pos":
            case "position":
                PrintPosition();
                break;
            case "chests":
                PrintNearbyChests();
                break;
            case "instance":
                PrintInstance();
                break;
            case "currency":
                PrintCurrency();
                break;
            case "ce":
                PrintCriticalEncounterMeasurement(context.Args.Length > 1 ? context.Args[1] : null);
                break;
            case "shop":
            case "shopping":
            case "knightshopper":
            case "ks":
                DebugShopping(context.Args.Length > 1 ? context.Args[1] : null);
                break;
            default:
                chat.PrintError(
                    "Usage: /bocchi debug [open|close|toggle|ai-preset|pos|chests|instance|currency|ce [id]|shop [status|cancel]]");
                break;
        }
    }

    /// <summary>
    ///     Return to base camp if needed, then start Knightshopper’s Occult Crescent list
    ///     (no currency threshold; works with auto-shop off).
    ///     <c>/bocchi debug shop</c> · <c>shop status</c> · <c>shop cancel</c>
    /// </summary>
    private void DebugShopping(string? sub)
    {
        string action = (sub ?? "start").ToLowerInvariant();
        if (action is "help" or "?")
        {
            BocchiChat.Print(
                chat,
                uiConfig,
                "Usage: /bocchi debug shop [status|cancel] — Return to base camp, then Knightshopper Occult Crescent list.");
            return;
        }

        if (action is "status" or "st")
        {
            BocchiChat.Print(chat, uiConfig, shopping.DescribeStatus());
            return;
        }

        if (action is "cancel" or "stop")
        {
            if (!shopping.IsActive)
            {
                BocchiChat.PrintError(chat, uiConfig, "Shopping is not active.");
                return;
            }

            shopping.ForceStop();
            BocchiChat.Print(chat, uiConfig, "Shopping cancelled (Return / Knightshopper stopped).");
            return;
        }

        if (action is not ("start" or "run" or "go"))
        {
            BocchiChat.PrintError(
                chat,
                uiConfig,
                "Usage: /bocchi debug shop [status|cancel]");
            return;
        }

        if (!shopping.TryForceStart(out string detail))
        {
            BocchiChat.PrintError(chat, uiConfig, detail);
            return;
        }

        BocchiChat.Print(chat, uiConfig, detail);
    }

    /// <summary>
    ///     Dump nearby objects with their BaseId. Pot reveal detection only matches the BaseIds in
    ///     PotTreasureIds.RevealCofferBaseIds, so stand next to a revealed chest and run this to see
    ///     what it actually is.
    /// </summary>
    private unsafe void PrintInstance()
    {
        UIState* ui = UIState.Instance();
        BocchiChat.Print(
            chat,
            uiConfig,
            ui == null
                ? "UIState unavailable."
                : $"IsInstancedArea={ui->PublicInstance.IsInstancedArea()} InstanceId={ui->PublicInstance.InstanceId}");
    }

    /// <summary>
    ///     Measure a CE's registration area vs what BOCCHI uses for travel and waiting.
    ///     Stand where travel stops (or on the blue rim) and run <c>/bocchi debug ce</c> or
    ///     <c>/bocchi debug ce 46</c> for a specific encounter.
    /// </summary>
    private void PrintCriticalEncounterMeasurement(string? ceIdArg)
    {
        List<ActivityData> encounters = zones.GetZone().GetCriticalEncounterData();
        if (encounters.Count == 0)
        {
            BocchiChat.Print(chat, uiConfig, "No authored Critical Encounters in this zone.");
            return;
        }

        if (!TryResolveDebugCriticalEncounter(encounters, ceIdArg, out ActivityData authored))
        {
            return;
        }

        Vector3 me = player.Position;
        CriticalEncounterArea? lgbMaybe = geometry.TryResolveForAuthored(
            (ushort)authored.Id,
            authored.Position,
            out string lgbDetail);
        bool haveLgb = lgbMaybe is { Radius: > 0 };
        CriticalEncounterArea lgbArea = haveLgb ? lgbMaybe!.Value : default;
        ActivityAreaShape shape = haveLgb
            ? NavigationConstants.ResolveCriticalEncounterShape(authored, lgbArea.IsSquare)
            : authored.AreaShape;

        CriticalEncounter.SanitizeRegistration(
            authored.Position,
            haveLgb ? lgbArea.Center : Vector3.Zero,
            haveLgb ? lgbArea.Radius : 0f,
            out Vector3 waitCenter,
            out float combatRadius,
            out bool sanitized,
            authored.CombatRadius);

        float padded = combatRadius > 0f
            ? NavigationConstants.CriticalEncounterPaddedRadius(combatRadius, shape)
            : 0f;
        float red = padded > 0f ? NavigationConstants.CriticalEncounterRedRadius(padded, shape) : 0f;
        float waitBoundary = red > 0f
            ? MathF.Max(NavigationConstants.EventArrivalRadius, red - NavigationConstants.CriticalEncounterWaitInset)
            : 0f;

        float distStaging = Flat(me, authored.Position);
        float distWait = red > 0f ? Flat(me, waitCenter) : distStaging;
        float stagingWaitSkew = haveLgb ? Flat(authored.Position, waitCenter) : 0f;
        bool insideWait = red > 0f
                          && NavigationConstants.IsInsideCriticalEncounterWaitArea(
                              waitCenter, red, shape, me);
        bool insideReg = red > 0f
                         && NavigationConstants.IsInsideCriticalEncounterRegistrationArea(
                             waitCenter, red, shape, me);

        Vector3 pathTarget = red > 0f
            ? NavigationApproach.GetCriticalEncounterApproachPosition(
                waitCenter, red, shape, authored.StandRadius ?? 0f)
            : authored.Position;

        BocchiChat.Print(
            chat,
            uiConfig,
            string.Format(
                CultureInfo.InvariantCulture,
                "CE {0} ({1}) — stand here when reporting travel/wait issues",
                authored.Id,
                shape));
        BocchiChat.Print(
            chat,
            uiConfig,
            string.Format(
                CultureInfo.InvariantCulture,
                "  Authored staging <{0:0.#}, {1:0.#}> Y={2:0.#}  you are {3:0.#}y away (XZ), ΔY={4:0.#}",
                authored.Position.X,
                authored.Position.Z,
                authored.Position.Y,
                distStaging,
                me.Y - authored.Position.Y));
        BocchiChat.Print(
            chat,
            uiConfig,
            haveLgb
                ? string.Format(
                    CultureInfo.InvariantCulture,
                    "  LGB <{0:0.#}, {1:0.#}> r={2:0.#}y ({3})",
                    lgbArea.Center.X,
                    lgbArea.Center.Z,
                    lgbArea.Radius,
                    lgbDetail)
                : "  LGB unresolved (" + lgbDetail + ")");
        BocchiChat.Print(
            chat,
            uiConfig,
            string.Format(
                CultureInfo.InvariantCulture,
                "  BOCCHI wait centre <{0:0.#}, {1:0.#}>  combat {2:0.#}y  wait boundary {3:0.#}y{4}",
                waitCenter.X,
                waitCenter.Z,
                red,
                waitBoundary,
                sanitized
                    ? lgbDetail.StartsWith("alternate", StringComparison.Ordinal)
                        ? " (sanitized; ground alternate MapRange)"
                        : " (sanitized LGB)"
                    : lgbDetail.StartsWith("alternate", StringComparison.Ordinal)
                        ? " (ground alternate MapRange)"
                        : ""));
        BocchiChat.Print(
            chat,
            uiConfig,
            string.Format(
                CultureInfo.InvariantCulture,
                "  You are {0:0.#}y from wait centre → {1}",
                distWait,
                red <= 0f
                    ? "no combat radius resolved"
                    : insideWait
                        ? "INSIDE wait (BOCCHI should stop pathing)"
                        : insideReg
                            ? "inside registration rim but OUTSIDE wait inset"
                            : "OUTSIDE registration"));
        BocchiChat.Print(
            chat,
            uiConfig,
            string.Format(
                CultureInfo.InvariantCulture,
                "  Path target sample <{0:0.#}, {1:0.#}>  ({2:0.#}y from you)",
                pathTarget.X,
                pathTarget.Z,
                Flat(me, pathTarget)));

        if (stagingWaitSkew > 15f)
        {
            BocchiChat.Print(
                chat,
                uiConfig,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "  Note: staging and wait centre are {0:0.#}y apart — old builds path to staging while waiting uses LGB centre.",
                    stagingWaitSkew));
        }

        PrintLiveEventGeometry();
    }

    private bool TryResolveDebugCriticalEncounter(
        List<ActivityData> encounters,
        string? ceIdArg,
        out ActivityData authored)
    {
        if (!string.IsNullOrWhiteSpace(ceIdArg))
        {
            if (!int.TryParse(ceIdArg, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
            {
                BocchiChat.PrintError(chat, uiConfig, "CE id must be a number (e.g. /bocchi debug ce 46).");
                authored = encounters[0];
                return false;
            }

            ActivityData? match = encounters.FirstOrDefault(ce => ce.Id == id);
            if (match is null)
            {
                BocchiChat.PrintError(chat, uiConfig, $"No authored CE {id} in this zone.");
                authored = encounters[0];
                return false;
            }

            authored = match;
            return true;
        }

        Vector3 me = player.Position;
        authored = encounters.MinBy(ce => Flat(me, ce.Position))!;
        return true;
    }

    /// <summary>
    ///     Report live DynamicEvent marker centre/radius. Occult CE MapMarker.Radius is 0;
    ///     registration size comes from LGB MapRange via <c>/bocchi debug ce</c>.
    /// </summary>
    private void PrintLiveEventGeometry()
    {
        PublicContentOccultCrescent* content = PublicContentOccultCrescent.GetInstance();
        if (content == null)
        {
            BocchiChat.Print(chat, uiConfig, "  (Occult content director unavailable — no live event data)");
            return;
        }

        BocchiChat.Print(chat, uiConfig, "Live DynamicEvent markers (id / state / centre / radius):");
        ref DynamicEventContainer container = ref content->DynamicEventContainer;

        var any = false;
        for (var i = 0; i < container.Events.Length; i++)
        {
            GameDynamicEvent evt = container.Events[i];
            if (evt.DynamicEventId == 0)
            {
                continue;
            }

            any = true;

            // MapMarker.Radius reads 0 for Occult CEs, so the usable size comes from the LGB
            // MapRange the event points at.
            CriticalEncounterArea? area = geometry.TryGet(evt.DynamicEventId, out string lgbDetail);
            string lgb = area is { } a
                ? string.Format(
                    CultureInfo.InvariantCulture,
                    "  LGB <{0:0.#}, {1:0.#}> r={2:0.#}y {3} ({4})",
                    a.Center.X,
                    a.Center.Z,
                    a.Radius,
                    a.IsSquare ? "square" : "circle",
                    lgbDetail)
                : "  LGB unresolved (" + lgbDetail + ")";

            BocchiChat.Print(
                chat,
                uiConfig,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "  {0}  {1}  marker <{2:0.#}, {3:0.#}> r={4:0.#}y{5}",
                    evt.DynamicEventId,
                    evt.State,
                    evt.MapMarker.Position.X,
                    evt.MapMarker.Position.Z,
                    evt.MapMarker.Radius,
                    lgb));
        }

        if (!any)
        {
            BocchiChat.Print(chat, uiConfig, "  (no events populated right now)");
        }
    }

    /// <summary>
    ///     Prints the raw currency source the per-hour trackers read. Compare against the in-game
    ///     Enlightenment counters: matching numbers mean the source is fine and any wrong rate is in
    ///     the rate logic; zeroes or nonsense mean the tracker is reading the wrong field.
    /// </summary>
    private void PrintCurrency()
    {
        BocchiChat.Print(
            chat,
            uiConfig,
            $"IsStateAvailable={OccultCrescentHelper.IsStateAvailable()} "
            + $"GoldTotal={OccultCrescentHelper.GetGoldTotal()} SilverTotal={OccultCrescentHelper.GetSilverTotal()} "
            + $"(pieces {OccultCrescentHelper.GetGoldPieces()}/{OccultCrescentHelper.GetSilverPieces()})");

        // Drops mention currencies we have never heard of ("Enlightenment silver obols"), and they
        // do not live in InventoryType.Currency. Ask the game's own item sheet which Enlightenment
        // items exist and how many we hold, so the ids come from the game rather than a guess.
        InventoryManager* inventory = InventoryManager.Instance();
        BocchiChat.Print(chat, uiConfig, "Enlightenment items (itemId / name / held):");

        foreach (Item row in data.GetExcelSheet<Item>())
        {
            string name = row.Name.ExtractText();
            if (!name.Contains("Enlightenment", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            int held = inventory == null ? -1 : inventory->GetInventoryItemCount(row.RowId);
            BocchiChat.Print(chat, uiConfig, $"  {row.RowId}  \"{name}\"  {held}");
        }
    }

    /// <summary>
    ///     Lists nearby objects and, for treasure ones, says whether the pot-reveal filter would
    ///     accept them and why not. Standing on a revealed pot chest and running this answers the
    ///     question the logs cannot: is the coffer being rejected, or never seen at all?
    /// </summary>
    private void PrintNearbyChests()
    {
        Vector3 me = player.Position;
        var near = objects
            .Where(o => o.IsValid() && !o.IsDead)
            .Select(o => (Obj: o, Dist: Flat(me, o.Position)))
            .Where(x => x.Dist <= 30f)
            .OrderBy(x => x.Dist)
            .Take(15)
            .ToList();

        if (near.Count == 0)
        {
            BocchiChat.Print(chat, uiConfig, "No objects within 30y.");
            return;
        }

        IZone zone = zones.GetZone();
        List<Vector3> potSpots = zone.GetPotChestData().Values
            .SelectMany(chests => chests.Select(c => c.Position))
            .Concat(zone.GetRerollPotChestData().Select(c => c.Position))
            .ToList();
        List<Vector3> huntSpots = zone.GetTreasureData()
            .Where(t => t.Position.HasValue)
            .Select(t => t.Position!.Value)
            .ToList();

        BocchiChat.Print(chat, uiConfig, "Objects within 30y (BaseId / kind / name / distance):");
        foreach ((IGameObject obj, float dist) in near)
        {
            BocchiChat.Print(
                chat,
                uiConfig,
                $"  {obj.BaseId}  {obj.ObjectKind}  \"{obj.Name.TextValue}\"  {dist:0.#}y"
                + (obj.ObjectKind == DalamudObjectKind.Treasure
                    ? $"  targetable={obj.IsTargetable}  {ClassifyReveal(obj, potSpots, huntSpots)}"
                    : string.Empty));
        }
    }

    /// <summary>Mirrors FarmingPotChestsHandler's reveal gate so the verdict here matches the farm.</summary>
    private static string ClassifyReveal(IGameObject obj, List<Vector3> potSpots, List<Vector3> huntSpots)
    {
        float pot = potSpots.Count == 0 ? float.MaxValue : potSpots.Min(p => Flat(obj.Position, p));
        float hunt = huntSpots.Count == 0 ? float.MaxValue : huntSpots.Min(p => Flat(obj.Position, p));
        string distances = $"pot={pot:0.#}y hunt={hunt:0.#}y";

        return PotTreasureFilter.IsOnAuthoredPotSpot(obj.Position, potSpots, huntSpots)
            ? $"ACCEPT as pot reveal ({distances})"
            : pot > PotTreasureFilter.RevealSpotTolerance
                ? $"REJECT (not on a pot spot; {distances})"
                : $"REJECT (nearer a hunt coffer; {distances})";
    }

    private static float Flat(Vector3 a, Vector3 b) =>
        Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));

    /// <summary>
    ///     Print the player position as a TreasureHuntPathOverrides via-point literal. Stand on the
    ///     safe line, run the command, paste the line.
    /// </summary>
    private void PrintPosition()
    {
        Vector3 p = player.Position;
        BocchiChat.Print(
            chat,
            uiConfig,
            $"new({p.X.ToString("0.###", CultureInfo.InvariantCulture)}f, "
            + $"{p.Y.ToString("0.###", CultureInfo.InvariantCulture)}f, "
            + $"{p.Z.ToString("0.###", CultureInfo.InvariantCulture)}f),");
    }

    private void MakeAiPreset()
    {
        var job = player.GetClassJob();
        BocchiChat.Print(
            chat,
            uiConfig,
            $"Base job={job?.Abbreviation.ToString() ?? "?"} Role={job?.Role.ToString() ?? "?"} "
            + $"IsMelee={player.IsMelee()} IsMeleeDps={player.IsMeleeDps()}");

        if (!bossModMiscAi.TryEnsurePresets(out string? storedJson))
        {
            BocchiChat.PrintError(chat, uiConfig, "Failed to create BOCCHI AI preset (is BossMod / BMR loaded?)");
            return;
        }

        BocchiChat.Print(
            chat,
            uiConfig,
            $"Using presets '{presetNaming.FateMiscAi}', '{presetNaming.CeMiscAi}', and '{presetNaming.MobFarmMiscAi}' (created only if missing).");
        if (string.IsNullOrWhiteSpace(storedJson))
        {
            BocchiChat.PrintError(
                chat,
                uiConfig,
                "Preset Create succeeded but Get returned empty — check BossMod Presets IPC.");
            return;
        }

        BocchiChat.Print(chat, uiConfig, $"Stored JSON:\n{storedJson}");
    }
}

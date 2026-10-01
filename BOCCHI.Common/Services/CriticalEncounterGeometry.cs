using BOCCHI.Common.Data.CriticalEncounters;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using Lumina.Data.Files;
using Lumina.Data.Parsing.Layer;
using Lumina.Excel.Sheets;
using Ocelot.Extensions;
using Ocelot.Services.Logger;
using System.Numerics;
using GameDynamicEvent = FFXIVClientStructs.FFXIV.Client.Game.InstanceContent.DynamicEvent;
using ExcelDynamicEvent = Lumina.Excel.Sheets.DynamicEvent;

namespace BOCCHI.Common.Services;

public readonly record struct CriticalEncounterArea(Vector3 Center, float Radius, bool IsSquare);

public sealed class CriticalEncounterGeometry(
    IDataManager data,
    IClientState clientState,
    ILogger<CriticalEncounterGeometry> logger)
{
    private static readonly string[] ExtraLayerFiles =
        ["planevent", "planmap", "planner", "planlive", "planobject", "bg"];

    private readonly Dictionary<uint, CriticalEncounterArea> cache = [];

    private readonly Dictionary<uint, (CriticalEncounterArea? Area, string Detail)> authoredResolveCache = [];

    private ushort cachedTerritory;

    public unsafe CriticalEncounterArea? TryGet(ushort dynamicEventId, out string detail)
    {
        if (cachedTerritory != (ushort)clientState.TerritoryType)
        {
            cache.Clear();
            authoredResolveCache.Clear();
            cachedTerritory = (ushort)clientState.TerritoryType;
        }

        if (cache.TryGetValue(dynamicEventId, out CriticalEncounterArea cached))
        {
            detail = "cached";
            return cached;
        }

        PublicContentOccultCrescent* content = PublicContentOccultCrescent.GetInstance();
        if (content == null)
        {
            detail = "no occult director";
            return null;
        }

        uint liveRangeId = 0;
        Vector3 marker = Vector3.Zero;
        bool haveMarker = false;
        ref DynamicEventContainer container = ref content->DynamicEventContainer;
        for (int i = 0; i < container.Events.Length; i++)
        {
            GameDynamicEvent evt = container.Events[i];
            if (evt.DynamicEventId != dynamicEventId)
            {
                continue;
            }

            liveRangeId = evt.LGBMapRange;
            marker = evt.MapMarker.Position;
            haveMarker = marker.X != 0f || marker.Z != 0f;
            break;
        }

        uint excelRangeId = 0;
        if (data.GetExcelSheet<ExcelDynamicEvent>().TryGetRow(dynamicEventId, out ExcelDynamicEvent row))
        {
            excelRangeId = row.LGBMapRange;
        }

        uint mapRangeId = liveRangeId != 0 ? liveRangeId : excelRangeId;
        List<(string Path, LayerCommon.InstanceObject Instance, LayerCommon.MapRangeInstanceObject Range)> ranges =
            LoadMapRanges();

        if (mapRangeId != 0)
        {
            foreach ((string path, LayerCommon.InstanceObject instance, LayerCommon.MapRangeInstanceObject range) in ranges)
            {
                if (instance.InstanceId != mapRangeId)
                {
                    continue;
                }

                CriticalEncounterArea area = Build(instance, range);
                cache[dynamicEventId] = area;
                detail = $"id {mapRangeId} in {path} (live={liveRangeId} excel={excelRangeId})";
                logger.Debug(
                    "CE {Id}: LGB MapRange {RangeId} centre {Center:F0} radius {Radius:F1}y ({Shape})",
                    dynamicEventId,
                    mapRangeId,
                    area.Center,
                    area.Radius,
                    area.IsSquare ? "square" : "circle");
                return area;
            }
        }

        if (haveMarker && ranges.Count > 0)
        {
            float bestDist = float.MaxValue;
            CriticalEncounterArea? best = null;
            string bestPath = "";
            uint bestId = 0;
            foreach ((string path, LayerCommon.InstanceObject instance, LayerCommon.MapRangeInstanceObject range) in ranges)
            {
                CriticalEncounterArea candidate = Build(instance, range);
                float dist = candidate.Center.Distance2D(marker);
                if (dist >= bestDist || dist > 40f)
                {
                    continue;
                }

                bestDist = dist;
                best = candidate;
                bestPath = path;
                bestId = instance.InstanceId;
            }

            if (best is { } near)
            {
                cache[dynamicEventId] = near;
                detail = $"nearest MapRange {bestId} at {bestDist:0.#}y in {bestPath} (live={liveRangeId} excel={excelRangeId})";
                logger.Debug(
                    "CE {Id}: no MapRange id {Wanted}; using nearest {RangeId} ({Dist:F1}y) centre {Center:F0} radius {Radius:F1}y",
                    dynamicEventId,
                    mapRangeId,
                    bestId,
                    bestDist,
                    near.Center,
                    near.Radius);
                return near;
            }
        }

        detail = ranges.Count == 0
            ? $"no LGB MapRange files (live={liveRangeId} excel={excelRangeId})"
            : $"id live={liveRangeId} excel={excelRangeId} not in {ranges.Count} MapRange(s)";
        logger.Warning("CE {Id}: {Detail}", dynamicEventId, detail);
        return null;
    }

    private const float AlternateMapRangeSearchRadius = 80f;

    public CriticalEncounterArea? TryResolveForAuthored(
        ushort dynamicEventId,
        Vector3 authoredStaging,
        out string detail)
    {
        if (cachedTerritory != (ushort)clientState.TerritoryType)
        {
            cache.Clear();
            authoredResolveCache.Clear();
            cachedTerritory = (ushort)clientState.TerritoryType;
        }

        if (authoredResolveCache.TryGetValue(dynamicEventId, out (CriticalEncounterArea? Area, string Detail) hit))
        {
            detail = hit.Detail;
            return hit.Area;
        }

        CriticalEncounterArea? raw = TryGet(dynamicEventId, out string rawDetail);
        if (raw is not { Radius: > 0 } area)
        {
            detail = rawDetail;
            authoredResolveCache[dynamicEventId] = (null, detail);
            return null;
        }

        CriticalEncounter.SanitizeRegistration(
            authoredStaging,
            area.Center,
            area.Radius,
            out _,
            out _,
            out bool rejected);

        if (!rejected || float.IsNaN(authoredStaging.X))
        {
            detail = rawDetail;
            authoredResolveCache[dynamicEventId] = (area, detail);
            return area;
        }

        float bestDist = float.MaxValue;
        CriticalEncounterArea? best = null;
        string bestLabel = "";
        foreach ((string path, LayerCommon.InstanceObject instance, LayerCommon.MapRangeInstanceObject range) in LoadMapRanges())
        {
            CriticalEncounterArea candidate = Build(instance, range);
            CriticalEncounter.SanitizeRegistration(
                authoredStaging,
                candidate.Center,
                candidate.Radius,
                out _,
                out _,
                out bool altRejected);
            if (altRejected)
            {
                continue;
            }

            float dist = candidate.Center.Distance2D(authoredStaging);
            if (dist >= bestDist || dist > AlternateMapRangeSearchRadius)
            {
                continue;
            }

            bestDist = dist;
            best = candidate;
            bestLabel = $"MapRange {instance.InstanceId} at {dist:0.#}y in {path}";
        }

        if (best is { } alt)
        {
            detail = $"alternate {bestLabel} (rejected {rawDetail})";
            logger.Debug(
                "CE {Id}: rejected MapRange ({Raw}); using ground alternate centre {Center:F0} radius {Radius:F1}y",
                dynamicEventId,
                rawDetail,
                alt.Center,
                alt.Radius);
            authoredResolveCache[dynamicEventId] = (alt, detail);
            return alt;
        }

        detail = $"rejected {rawDetail}; no ground alternate within {AlternateMapRangeSearchRadius:0}y";
        authoredResolveCache[dynamicEventId] = (area, detail);
        return area;
    }

    private List<(string Path, LayerCommon.InstanceObject Instance, LayerCommon.MapRangeInstanceObject Range)> LoadMapRanges()
    {
        List<(string, LayerCommon.InstanceObject, LayerCommon.MapRangeInstanceObject)> found = [];
        foreach (string path in EnumerateLayerPaths())
        {
            LgbFile? lgb = data.GetFile<LgbFile>(path);
            if (lgb == null)
            {
                continue;
            }

            foreach (LayerCommon.Layer layer in lgb.Layers)
            {
                foreach (LayerCommon.InstanceObject instance in layer.InstanceObjects)
                {
                    if (instance.Object is not LayerCommon.MapRangeInstanceObject range)
                    {
                        continue;
                    }

                    found.Add((path, instance, range));
                }
            }
        }

        return found;
    }

    private IEnumerable<string> EnumerateLayerPaths()
    {
        if (!data.GetExcelSheet<TerritoryType>().TryGetRow(clientState.TerritoryType, out TerritoryType territory))
        {
            yield break;
        }

        string bg = territory.Bg.ExtractText();
        if (string.IsNullOrEmpty(bg))
        {
            yield break;
        }

        int levelIndex = bg.LastIndexOf("/level/", StringComparison.Ordinal);
        string levelDirectory = levelIndex >= 0 ? bg[..(levelIndex + "/level/".Length)] : bg + "/level/";
        string stem = levelIndex >= 0 ? bg[(levelIndex + "/level/".Length)..] : "";

        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(stem))
        {
            names.Add(stem);
        }

        foreach (string extra in ExtraLayerFiles)
        {
            names.Add(extra);
        }

        foreach (string name in names)
        {
            yield return $"bg/{levelDirectory}{name}.lgb";
        }
    }

    private static CriticalEncounterArea Build(
        LayerCommon.InstanceObject instance,
        LayerCommon.MapRangeInstanceObject range)
    {
        Vector3 center = new(instance.Transform.Translation.X, instance.Transform.Translation.Y, instance.Transform.Translation.Z);
        Vector3 scale = new(instance.Transform.Scale.X, instance.Transform.Scale.Y, instance.Transform.Scale.Z);

        bool square = range.ParentData.TriggerBoxShape == TriggerBoxShape.TriggerBoxShapeBox
                      || range.ParentData.TriggerBoxShape == TriggerBoxShape.TriggerBoxShapeBoard;

        float radius = MathF.Max(scale.X, scale.Z);
        return new CriticalEncounterArea(center, radius, square);
    }
}

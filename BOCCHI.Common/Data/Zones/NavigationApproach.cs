using BOCCHI.Common.Data.CriticalEncounters;
using BOCCHI.Common.Data.Zones.Graph;
using BOCCHI.Common.Services;
using Ocelot.Extensions;
using System.Numerics;

namespace BOCCHI.Common.Data.Zones;

public static class NavigationConstants
{
    public const float MaxDirectWalkDistance = 80f;

    public const float ReturnCost = 40f;

    public const float AethernetHopCost = 50f;

    public const float CampRadius = 80f;

    public const float EventApproachMinRadius = 0f;

    public const float EventApproachMaxRadius = 5f;

    public const float EventApproachJitter = 50f;

    public const float EventArrivalRadius = 5f;

    public const float FateAiHandoffRange = 25f;

    public const float FateAiHandoffFromCenter = 25f;

    public const float FateCommittedLeaveYalms = 40f;

    public static bool IsWithinFateAiHandoff(float distanceToCenter, float nearestTargetPastHitbox) =>
        nearestTargetPastHitbox <= FateAiHandoffRange
        || distanceToCenter <= FateAiHandoffFromCenter;

    public static bool IsWithinFateCommitment(
        float distanceToCenter,
        float fateRadius,
        float nearestTargetPastHitbox)
    {
        float leash = MathF.Max(fateRadius, FateAiHandoffFromCenter) + FateCommittedLeaveYalms;
        return nearestTargetPastHitbox <= FateAiHandoffRange
               || distanceToCenter <= leash;
    }

    public const float CriticalEncounterRadiusPadding = 7f;

    public const float CriticalEncounterYellowInset = 2f;

    public const float CriticalEncounterSquareStandRatio = 0.7f;

    public const float CriticalEncounterCircleStandRatio = 0.45f;

    public const float CriticalEncounterSquareRadiusPadding = 7f;

    public const float CriticalEncounterWaitInset = 8f;

    public const float CriticalEncounterApproachMinRatio = 0.25f;

    public const float CriticalEncounterApproachMaxRatio = 0.4f;

    public const float CriticalEncounterSquareApproachMaxRatio = 0.25f;

    public static ActivityAreaShape ResolveCriticalEncounterShape(ActivityData? authored, bool lgbIsSquare) =>
        authored is not null
            ? authored.AreaShape
            : lgbIsSquare
                ? ActivityAreaShape.Square
                : ActivityAreaShape.Circle;

    public static ActivityAreaShape ResolveCriticalEncounterShape(IZone zone, int eventId, bool lgbIsSquare)
    {
        ActivityData? authored = zone.GetCriticalEncounterData().FirstOrDefault(a => a.Id == eventId);
        return ResolveCriticalEncounterShape(authored, lgbIsSquare);
    }

    public const float PotPrepositionMinRadius = 12f;

    public const float PotPrepositionMaxRadius = 32f;

    public const float MountMinDistance = 20f;

    public static float CriticalEncounterRedRadius(
        float paddedRadius,
        ActivityAreaShape shape = ActivityAreaShape.Circle)
    {
        float pad = shape == ActivityAreaShape.Square
            ? CriticalEncounterSquareRadiusPadding
            : CriticalEncounterRadiusPadding;
        return MathF.Max(0f, paddedRadius - pad);
    }

    public static float CriticalEncounterYellowRadius(float paddedRadius) =>
        MathF.Max(0f, paddedRadius - CriticalEncounterYellowInset);

    public static float CriticalEncounterStandRadius(float combatRadius, ActivityAreaShape shape)
    {
        if (combatRadius <= 0f)
        {
            return EventArrivalRadius;
        }

        float ratio = shape == ActivityAreaShape.Square
            ? CriticalEncounterSquareStandRatio
            : CriticalEncounterCircleStandRatio;
        return combatRadius * ratio;
    }

    public static float CriticalEncounterPaddedRadius(float combatRadius, ActivityAreaShape shape)
    {
        float pad = shape == ActivityAreaShape.Square
            ? CriticalEncounterSquareRadiusPadding
            : CriticalEncounterRadiusPadding;
        return combatRadius + pad;
    }

    public static bool IsInsideCriticalEncounterWaitArea(
        Vector3 center,
        float combatRadius,
        ActivityAreaShape shape,
        Vector3 point)
    {
        if (combatRadius <= 0f)
        {
            return false;
        }

        float wait = MathF.Max(EventArrivalRadius, combatRadius - CriticalEncounterWaitInset);
        if (wait > combatRadius)
        {
            wait = combatRadius;
        }

        return IsInsideCriticalEncounterArea(center, wait, shape, point);
    }

    public static bool IsInsideCriticalEncounterRegistrationArea(
        Vector3 center,
        float combatRadius,
        ActivityAreaShape shape,
        Vector3 point) =>
        combatRadius > 0f && IsInsideCriticalEncounterArea(center, combatRadius, shape, point);

    private static bool IsInsideCriticalEncounterArea(
        Vector3 center,
        float radiusOrHalfExtent,
        ActivityAreaShape shape,
        Vector3 point)
    {
        if (shape == ActivityAreaShape.Square)
        {
            float dx = MathF.Abs(point.X - center.X);
            float dz = MathF.Abs(point.Z - center.Z);
            return MathF.Max(dx, dz) <= radiusOrHalfExtent;
        }

        return point.Distance2D(center) <= radiusOrHalfExtent;
    }
}

public static class NavigationApproach
{
    public static Vector3 GetEventPosition(Vector3 destination, Vector3 from)
    {
        float range = NavigationConstants.EventApproachMinRadius
                      + Random.Shared.NextSingle() * (NavigationConstants.EventApproachMaxRadius - NavigationConstants.EventApproachMinRadius);

        return destination.GetApproachPosition(from, range, NavigationConstants.EventApproachJitter);
    }

    public static Vector3 GetCriticalEncounterApproachPosition(
        Vector3 center,
        float combatRadius,
        ActivityAreaShape shape = ActivityAreaShape.Circle,
        float standRadius = 0f,
        int? stableSeed = null)
    {
        Random rng = stableSeed is int seed
            ? new Random(HashCode.Combine(seed, 0xCE))
            : Random.Shared;

        float red = MathF.Max(1f, standRadius > 0f ? standRadius : combatRadius);
        if (shape == ActivityAreaShape.Square)
        {
            float maxFromCenter = MathF.Min(
                red * NavigationConstants.CriticalEncounterSquareApproachMaxRatio,
                NavigationConstants.EventApproachMaxRadius);
            if (maxFromCenter < 0.5f)
            {
                maxFromCenter = 0.5f;
            }

            float x = (rng.NextSingle() * 2f - 1f) * maxFromCenter;
            float z = (rng.NextSingle() * 2f - 1f) * maxFromCenter;
            return center + new Vector3(x, 0f, z);
        }

        float min = red * NavigationConstants.CriticalEncounterApproachMinRatio;
        float max = red * NavigationConstants.CriticalEncounterApproachMaxRatio;
        if (max < min)
        {
            max = min;
        }

        float dist = min + rng.NextSingle() * (max - min);
        float angle = rng.NextSingle() * MathF.PI * 2f;
        return center + new Vector3(MathF.Cos(angle) * dist, 0f, MathF.Sin(angle) * dist);
    }

    public static Vector3 ResolveActivityApproach(Node goal, Vector3 from)
    {
        if (goal.Type == NodeType.CriticalEncounter
            && goal.Metadata is ActivityNodeMetadata { CombatRadius: > 0 } meta)
        {
            float radius = meta.CombatRadius > CriticalEncounter.MaxRegistrationRadius
                ? CriticalEncounter.FallbackRegistrationRadius
                : meta.CombatRadius;
            return GetCriticalEncounterApproachPosition(
                goal.Position,
                radius,
                meta.AreaShape,
                meta.StandRadius);
        }

        return GetEventPosition(goal.Position, from);
    }

    public static bool TryResolveCriticalEncounterApproach(
        IZone zone,
        CriticalEncounterGeometry? geometry,
        Vector3 destination,
        Vector3 from,
        out Vector3 approach,
        out ActivityData? activity,
        out bool alreadyInside)
    {
        alreadyInside = false;
        const float matchRadius = 80f;
        foreach (ActivityData candidate in zone.GetCriticalEncounterData())
        {
            if (destination.Distance2D(candidate.Position) > matchRadius)
            {
                continue;
            }

            if (geometry?.TryResolveForAuthored(
                    (ushort)candidate.Id,
                    candidate.Position,
                    out _) is not { Radius: > 0 } area)
            {
                continue;
            }

            activity = candidate;
            ActivityAreaShape shape = NavigationConstants.ResolveCriticalEncounterShape(
                candidate,
                area.IsSquare);
            CriticalEncounter.SanitizeRegistration(
                candidate.Position,
                area.Center,
                area.Radius,
                out Vector3 center,
                out float radius,
                out _,
                candidate.CombatRadius);

            if (NavigationConstants.IsInsideCriticalEncounterWaitArea(
                    center, radius, shape, from))
            {
                approach = from;
                alreadyInside = true;
                return true;
            }

            approach = GetCriticalEncounterApproachPosition(
                center, radius, shape, candidate.StandRadius ?? 0f);
            return true;
        }

        activity = null;
        approach = default;
        return false;
    }

    public static Vector3 GetPotPrepositionPosition(Vector3 potCenter, Vector3 from)
    {
        float dist = from.Distance2D(potCenter);
        if (dist >= NavigationConstants.PotPrepositionMinRadius
            && dist <= NavigationConstants.PotPrepositionMaxRadius)
        {
            return from;
        }

        float range = NavigationConstants.PotPrepositionMinRadius
                      + Random.Shared.NextSingle()
                      * (NavigationConstants.PotPrepositionMaxRadius - NavigationConstants.PotPrepositionMinRadius);
        float angle = Random.Shared.NextSingle() * MathF.PI * 2f;

        return potCenter + new Vector3(MathF.Cos(angle) * range, 0f, MathF.Sin(angle) * range);
    }
}

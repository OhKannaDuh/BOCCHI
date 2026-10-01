using BOCCHI.Common.Data.Zones;
using Ocelot.Extensions;
using Ocelot.Services.Pathfinding;
using System.Numerics;

namespace BOCCHI.MobFarmer.Services;

public sealed class FarmerWalkStuckAssist
{
    private static readonly TimeSpan NudgeTimeout = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan RepathAfterNudgeTimeout = TimeSpan.FromSeconds(8);

    private const float ProgressThreshold = 1.5f;

    public const int MaxFailures = 2;

    private ulong? key;

    private float bestDistance = float.MaxValue;

    private DateTime startedUtc = DateTime.MinValue;

    private bool nudgeIssued;

    private int failureCount;

    private Vector3 lastGoal;

    public enum Recovery
    {
        None,
        Nudge,
        RepathGoal,
        GiveUp,
    }

    public void Reset()
    {
        key = null;
        bestDistance = float.MaxValue;
        startedUtc = DateTime.MinValue;
        nudgeIssued = false;
        failureCount = 0;
        lastGoal = default;
    }

    public Recovery Tick(ulong watchKey, float distance, Vector3 goal, PathfindingState state)
    {
        DateTime now = DateTime.UtcNow;

        if (key != watchKey)
        {
            key = watchKey;
            lastGoal = goal;
            bestDistance = distance;
            startedUtc = now;
            nudgeIssued = false;
            failureCount = 0;
            return Recovery.None;
        }

        if (lastGoal.Distance2D(goal) > 5f)
        {
            lastGoal = goal;
            bestDistance = distance;
            startedUtc = now;
            nudgeIssued = false;
            return Recovery.None;
        }

        lastGoal = goal;

        if (state == PathfindingState.Pathfinding)
        {
            return Recovery.None;
        }

        if (distance < bestDistance - ProgressThreshold)
        {
            bestDistance = distance;
            startedUtc = now;
            nudgeIssued = false;
            return Recovery.None;
        }

        if (!nudgeIssued && now - startedUtc >= NudgeTimeout)
        {
            nudgeIssued = true;
            return Recovery.Nudge;
        }

        if (nudgeIssued && now - startedUtc >= NudgeTimeout + RepathAfterNudgeTimeout)
        {
            failureCount++;
            startedUtc = now;
            nudgeIssued = false;

            if (failureCount > MaxFailures)
            {
                Reset();
                return Recovery.GiveUp;
            }

            return Recovery.RepathGoal;
        }

        return Recovery.None;
    }

    public static Vector3 LateralNudge(Vector3 from, Vector3 goal) =>
        PathfindingNudge.LateralFrom(from, goal);
}

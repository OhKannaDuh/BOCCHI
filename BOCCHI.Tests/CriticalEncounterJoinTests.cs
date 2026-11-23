using System.Numerics;
using BOCCHI.Common.Data.CriticalEncounters;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Data.Zones.Graph;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using Xunit;

namespace BOCCHI.Tests;

public class CriticalEncounterJoinTests
{
    // Audit: only registration and warmup states allow a preparing encounter to be selected.
    [Theory]
    [InlineData(DynamicEventState.Register, true)]
    [InlineData(DynamicEventState.Warmup, true)]
    [InlineData(DynamicEventState.Battle, false)]
    [InlineData((DynamicEventState)0, false)]
    public void PreparationStateAcceptsRegistrationAndWarmup(DynamicEventState state, bool expected)
    {
        CriticalEncounter encounter = new(new(1), new DynamicEvent { State = state }, 40, Vector3.Zero);
        Assert.Equal(expected, encounter.IsPreparing());
    }

    // Audit: unknown start times remain unknown and expired deadlines stay negative.
    [Theory]
    [InlineData(0, null)]
    [InlineData(1060, 60)]
    [InlineData(1000, 0)]
    [InlineData(999, -1)]
    public void TimeUntilStartPreservesUnknownAndExpiredDeadlines(int timestamp, int? seconds)
    {
        CriticalEncounter encounter = new(new(1), new DynamicEvent { StartTimestamp = timestamp }, 40, Vector3.Zero);
        TimeSpan? remaining = encounter.GetTimeUntilStart(() => DateTimeOffset.FromUnixTimeSeconds(1000));
        Assert.Equal(seconds.HasValue ? TimeSpan.FromSeconds(seconds.Value) : (TimeSpan?)null, remaining);
    }

    // Audit: registration includes the boundary and respects circular versus square arenas.
    [Theory]
    [InlineData(ActivityAreaShape.Circle, 40, 0, true)]
    [InlineData(ActivityAreaShape.Circle, 40.01f, 0, false)]
    [InlineData(ActivityAreaShape.Circle, 40, 40, false)]
    [InlineData(ActivityAreaShape.Square, 40, 40, true)]
    [InlineData(ActivityAreaShape.Square, 40.01f, 0, false)]
    public void RegistrationBoundaryRespectsArenaShape(ActivityAreaShape shape, float x, float z, bool expected)
    {
        Vector3 center = new(100, 5, 200);
        Assert.Equal(expected, NavigationConstants.IsInsideCriticalEncounterRegistrationArea(center, 40, shape, center + new Vector3(x, 0, z)));
    }

    // Audit: an invalid registration radius must not mark the center as inside.
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveRegistrationRadiusRejectsCenter(float radius)
    {
        Assert.False(NavigationConstants.IsInsideCriticalEncounterRegistrationArea(Vector3.Zero, radius, ActivityAreaShape.Circle, Vector3.Zero));
    }
}

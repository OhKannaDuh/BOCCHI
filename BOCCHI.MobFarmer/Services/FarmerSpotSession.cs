using BOCCHI.Common.Config;
using BOCCHI.Common.Data.MobFarmer;
using BOCCHI.Common.Extensions;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Services;
using Ocelot.Extensions;
using Ocelot.Services.PlayerState;
using System.Numerics;

namespace BOCCHI.MobFarmer.Services;

/// <summary>Picks the active farm spot, rotates when a camp is claimed this session.</summary>
public sealed class FarmerSpotSession(MobFarmerConfig config, IPlayer player, IObjectTable objects)
{
    private readonly HashSet<int> claimedIndices = [];

    private DateTimeOffset? contestedSinceUtc;

    public FarmSpot? Current { get; private set; }

    public Vector3 Origin { get; private set; }

    public Vector3? StackPoint => Current?.StackPoint;

    public string? Name => Current?.Name;

    public bool NeedsApproach { get; private set; }

    public int EffectiveMinimumMobsToStartFight =>
        Current is { MinimumMobsToStartFight: > 0 } spot
            ? spot.MinimumMobsToStartFight
            : config.MinimumMobsToStartFight;

    public void Begin()
    {
        claimedIndices.Clear();
        contestedSinceUtc = null;
        Current = SelectBest(player.Position);
        Origin = Current?.Origin ?? player.Position;
        NeedsApproach = Current != null && player.Position.Distance2D(Origin) > 8f;
    }

    public void Reset()
    {
        claimedIndices.Clear();
        contestedSinceUtc = null;
        Current = null;
        Origin = Vector3.Zero;
        NeedsApproach = false;
    }

    public void MarkArrived() => NeedsApproach = false;

    /// <summary>After a yield (crystal buffs, pots, hunt) took us away, walk back before farming again.</summary>
    public void RequireApproachIfAway()
    {
        if (Origin != Vector3.Zero && player.Position.Distance2D(Origin) > 8f)
        {
            NeedsApproach = true;
        }
    }

    /// <summary>Returns true when the session moved to a different spot.</summary>
    public bool TickClaimed(IMobScanner scanner)
    {
        if (Current == null || config.Spots.Count == 0)
        {
            contestedSinceUtc = null;
            return false;
        }

        if (!IsClaimedNow(scanner))
        {
            contestedSinceUtc = null;
            return false;
        }

        contestedSinceUtc ??= DateTimeOffset.UtcNow;
        if (DateTimeOffset.UtcNow - contestedSinceUtc < TimeSpan.FromSeconds(config.ClaimedSpotSeconds))
        {
            return false;
        }

        int index = config.Spots.IndexOf(Current);
        if (index >= 0)
        {
            claimedIndices.Add(index);
        }

        contestedSinceUtc = null;
        FarmSpot? next = SelectBest(player.Position);
        if (next == null || ReferenceEquals(next, Current))
        {
            return false;
        }

        Current = next;
        Origin = next.Origin;
        NeedsApproach = true;
        return true;
    }

    private bool IsClaimedNow(IMobScanner scanner)
    {
        bool contested = scanner.Contested.Any();
        bool noFree = !scanner.NotInCombat.Any();
        if (contested && noFree)
        {
            return true;
        }

        Vector3 watch = StackPoint ?? Origin;
        float radius = config.ClaimedPlayerRadius;
        ulong localId = objects.LocalPlayer?.GameObjectId ?? 0;
        return objects.OfType<IPlayerCharacter>()
            .Any(p => p.GameObjectId != localId
                      && p.Position.Distance2D(watch) <= radius);
    }

    private FarmSpot? SelectBest(Vector3 from)
    {
        List<(int Index, FarmSpot Spot)> enabled = [];
        for (int i = 0; i < config.Spots.Count; i++)
        {
            FarmSpot spot = config.Spots[i];
            if (spot.Enabled && !claimedIndices.Contains(i))
            {
                enabled.Add((i, spot));
            }
        }

        if (enabled.Count == 0)
        {
            return null;
        }

        return enabled
            .OrderByDescending(e => e.Spot.Priority)
            .ThenBy(e => from.Distance2D(e.Spot.Origin))
            .Select(e => e.Spot)
            .First();
    }
}

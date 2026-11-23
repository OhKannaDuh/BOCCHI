using BOCCHI.Common.Config;
using BOCCHI.Common.Data.MobFarmer;
using BOCCHI.Common.Extensions;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Services;
using Ocelot.Extensions;
using Ocelot.Services.PlayerState;
using System.Numerics;

namespace BOCCHI.MobFarmer.Services;

public sealed class FarmerSpotSession(
    MobFarmerConfig config,
    IPlayer player,
    IObjectTable objects,
    IPartyList party,
    IClientState client)
{
    private static readonly TimeSpan ClaimExpiry = TimeSpan.FromMinutes(5);

    private readonly Dictionary<FarmSpot, DateTimeOffset> claimedUntil = [];

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
        claimedUntil.Clear();
        contestedSinceUtc = null;
        Current = SelectBest(player.Position);
        Origin = Current?.Origin ?? player.Position;
        NeedsApproach = Current != null && player.Position.Distance2D(Origin) > 8f;
    }

    public void Reset()
    {
        claimedUntil.Clear();
        contestedSinceUtc = null;
        Current = null;
        Origin = Vector3.Zero;
        NeedsApproach = false;
    }

    public void MarkArrived() => NeedsApproach = false;

    public void RequireApproachIfAway()
    {
        if (Origin != Vector3.Zero && player.Position.Distance2D(Origin) > 8f)
        {
            NeedsApproach = true;
        }
    }

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

        claimedUntil[Current] = DateTimeOffset.UtcNow + ClaimExpiry;

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
        HashSet<uint> partyIds = party.Select(m => m.EntityId).ToHashSet();
        return objects.OfType<IPlayerCharacter>()
            .Any(p => p.GameObjectId != localId
                      && !partyIds.Contains(p.EntityId)
                      && p.Position.Distance2D(watch) <= radius);
    }

    private FarmSpot? SelectBest(Vector3 from)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        uint territory = client.TerritoryType;
        return config.Spots
            .Where(spot => spot.Enabled
                           && (spot.Territory == 0 || spot.Territory == territory)
                           && !(claimedUntil.TryGetValue(spot, out DateTimeOffset until) && until > now))
            .OrderByDescending(spot => spot.Priority)
            .ThenBy(spot => from.Distance2D(spot.Origin))
            .FirstOrDefault();
    }
}

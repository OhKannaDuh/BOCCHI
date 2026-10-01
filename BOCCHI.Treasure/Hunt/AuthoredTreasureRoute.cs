namespace BOCCHI.Treasure.Hunt;

public sealed class AuthoredTreasureRoute
{
    public int SchemaVersion { get; set; }

    public string Zone { get; set; } = "";

    public List<AuthoredTreasureSegment> Segments { get; set; } = [];
}

public sealed class AuthoredTreasureSegment
{
    public string Id { get; set; } = "";

    public List<uint> Nodes { get; set; } = [];

    public AuthoredTreasureTransition? TransitionAfter { get; set; }
}

public sealed class AuthoredTreasureTransition
{
    public string Type { get; set; } = "auto";

    public string? To { get; set; }
}

public readonly record struct AuthoredRouteEntry(uint NodeId, int SegmentIndex);

public readonly record struct AuthoredRouteSegment(string Id, AuthoredTreasureTransition? TransitionAfter);

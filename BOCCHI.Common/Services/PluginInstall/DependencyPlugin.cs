namespace BOCCHI.Common.Services.PluginInstall;

/// <summary>A plugin the Dependencies tab can add a repository for and install.</summary>
/// <param name="DisplayName">Name shown in the UI and in chat.</param>
/// <param name="InstallName">InternalName looked up in the repository manifest when installing.</param>
/// <param name="RepositoryUrls">Primary repository first, then mirrors that count as "already added".</param>
/// <param name="KnownInternalNames">Every InternalName that counts as this plugin (forks included).</param>
public sealed record DependencyPlugin(
    string DisplayName,
    string InstallName,
    string[] RepositoryUrls,
    string[]? KnownInternalNames = null)
{
    public string PrimaryRepositoryUrl => RepositoryUrls[0];

    public IReadOnlyList<string> InternalNames => KnownInternalNames ?? [InstallName];
}

public static class DependencyPlugins
{
    private const string VeynRepository = "https://puni.sh/api/repository/veyn";

    private static readonly string[] NightmareXivRepositories =
    [
        "https://github.com/NightmareXIV/MyDalamudPlugins/raw/main/pluginmaster.json",
        "https://raw.githubusercontent.com/NightmareXIV/MyDalamudPlugins/main/pluginmaster.json",
    ];

    private static readonly string[] PunishRepositories =
    [
        "https://love.puni.sh/ment.json",
        "https://puni.sh/api/plugins",
    ];

    private const string CombatRebornRepository =
        "https://raw.githubusercontent.com/FFXIV-CombatReborn/CombatRebornRepo/main/pluginmaster.json";

    /// <summary>blackappleD's repository, which ships the GatherBuddyReborn-bld fork BOCCHI's GBR backend targets.</summary>
    private const string BlackappleRepository = "https://raw.githubusercontent.com/blackappleD/DalamudPlugins/main/repo.json";

    public static readonly DependencyPlugin VNavmesh = new("vnavmesh", "vnavmesh", [VeynRepository]);

    public static readonly DependencyPlugin Lifestream = new("Lifestream", "Lifestream", NightmareXivRepositories);

    public static readonly DependencyPlugin GatherBuddyReborn = new(
        "GatherBuddy Reborn",
        "GatherBuddyReborn-bld",
        [BlackappleRepository],
        ["GatherBuddyReborn", "GatherBuddyReborn-bld"]);

    public static readonly DependencyPlugin Knightshopper =
        new("Knightshopper", "Knightshopper", ["https://puni.sh/api/repository/knightmore"]);

    public static readonly DependencyPlugin WrathCombo = new("Wrath Combo", "WrathCombo", PunishRepositories);

    public static readonly DependencyPlugin RotationSolverReborn =
        new("Rotation Solver Reborn", "RotationSolver", [CombatRebornRepository]);

    public static readonly DependencyPlugin BossMod = new("BossMod", "BossMod", [VeynRepository]);

    public static readonly DependencyPlugin BossModReborn = new("BossMod Reborn", "BossModReborn", [CombatRebornRepository]);
}

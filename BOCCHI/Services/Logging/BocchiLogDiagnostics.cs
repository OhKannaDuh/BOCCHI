using System.Text;
using BOCCHI.Automator.Data;
using BOCCHI.Automator.Services;
using BOCCHI.Common.Config;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Services;
using BOCCHI.MobFarmer.Data;
using BOCCHI.MobFarmer.Services;
using BOCCHI.Services.Changelog;
using BOCCHI.Treasure.Services;
using Ocelot.Services.PluginStatus;

namespace BOCCHI.Services.Logging;

public interface IBocchiLogDiagnostics
{
    string BuildHeader();
}

public sealed class BocchiLogDiagnostics
(
    AutomatorConfig automatorConfig,
    Func<IAutomator> automatorFactory,
    Func<IMobFarmer> farmerFactory,
    ITreasureHunter hunter,
    ICarrotHunter carrotHunter,
    IZoneProvider zones,
    IPluginStatus pluginStatus
) : IBocchiLogDiagnostics
{
    private static readonly CombatAutorotationDisplay CombatDisplay = new();

    private IAutomator? automator;

    private IMobFarmer? farmer;

    private IAutomator Automator => automator ??= automatorFactory();

    private IMobFarmer Farmer => farmer ??= farmerFactory();

    public string BuildHeader()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"BOCCHI {ChangelogText.CurrentPluginVersion}");
        sb.AppendLine($"Rotation: {CombatDisplay.Display(automatorConfig.CombatAutorotation)}");

        string wrath = Status("WrathCombo");
        string bossMod = Status("BossMod");
        string bossModReborn = Status("BossModReborn");
        string rsr = Status(CombatPluginPresence.RotationSolver);
        sb.AppendLine($"Plugins: WrathCombo={wrath} BossMod={bossMod} BossModReborn={bossModReborn} RSR={rsr}");

        bool shopping =
            Automator.SuspendedForShopping
            || (Farmer.Running && Farmer.Suspended && Farmer.YieldReason == FarmerYieldReason.Shopping);

        sb.AppendLine(
            "Modes: "
            + $"Illegal={OnOff(Automator.Enabled)} "
            + $"MobFarmer={OnOff(Farmer.Running)} "
            + $"TreasureHunt={OnOff(hunter.Running)}"
            + (hunter.Running ? $"(paused={OnOff(hunter.Paused)})" : string.Empty)
            + " "
            + $"CarrotHunt={OnOff(carrotHunter.Running)} "
            + $"Shopping={OnOff(shopping)}");

        string illegalState = Automator.CurrentState?.ToString()
            ?? (Automator.SuspendedForTreasure
                ? "Suspended(Treasure)"
                : Automator.SuspendedForShopping
                    ? "Suspended(Shopping)"
                    : Automator.Enabled
                        ? "off-tick"
                        : "off");
        sb.AppendLine($"IllegalState: {illegalState}");

        IZone zone = zones.GetZone();
        string zoneName = zone.ZoneId switch
        {
            ZoneId.SouthHorn => "South Horn",
            ZoneId.NorthHorn => "North Horn",
            _ => zone.ZoneId.ToString(),
        };
        sb.AppendLine($"Zone: {zoneName} ({zone.TerritoryType})");
        return sb.ToString();
    }

    private string Status(string internalName) =>
        pluginStatus.IsLoaded(internalName) ? "loaded" : "missing";

    private static string OnOff(bool value) => value ? "on" : "off";
}

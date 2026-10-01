using Ocelot.Config.Renderers.Enum;

namespace BOCCHI.Common.Config;

public enum CombatAutorotation
{
    None = 0,
    WrathCombo = 1,
    RotationSolverReborn = 2,
    BossMod = 3,
    BossModReborn = 4,
}

public class CombatAutorotationDisplay : IEnumDisplay<CombatAutorotation>
{
    public string Display(CombatAutorotation value) => value switch
    {
        CombatAutorotation.WrathCombo => "Wrath Combo + BOCCHI AI",
        CombatAutorotation.RotationSolverReborn => "Rotation Solver Reborn + BOCCHI AI",
        CombatAutorotation.BossMod => "BossMod rotation",
        CombatAutorotation.BossModReborn => "BossMod Reborn rotation",
        _ => "None",
    };
}

public static class CombatAutorotationExtensions
{
    public static bool UsesCombatAutomation(this CombatAutorotation value) =>
        value != CombatAutorotation.None;
}

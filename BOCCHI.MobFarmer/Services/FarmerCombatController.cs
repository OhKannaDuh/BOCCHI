using BOCCHI.Common.Config;
using BOCCHI.Common.Data.SupportJobs;
using BOCCHI.Common.Services;
using Dalamud.Plugin.Services;
using Ocelot.Ipc.RotationSolverReborn;
using Ocelot.Rotation.Services;
using Ocelot.Services.PlayerState;
using Ocelot.Services.PluginStatus;

namespace BOCCHI.MobFarmer.Services;

public sealed class FarmerCombatController(
    ICombatRotationSession session,
    AutomatorConfig automatorConfig,
    UIConfig uiConfig,
    IPlayer player,
    IChatGui chat,
    IPluginStatus pluginStatus,
    IRotationSolverRebornIpc rsr,
    ISupportJobFactory supportJobs
) : IFarmerCombatController
{
    public void Prepare()
    {
        session.OverwriteBossModPresets = automatorConfig.UpdateBossModPresetsAutomatically;
        session.MovementSettings = BossModMovement.From(automatorConfig, player.IsMelee(), player.GetClassJob()?.RowId);
        if (!automatorConfig.CombatAutorotation.UsesCombatAutomation()
            || !CombatAutorotationSetup.ValidatePlugins(
                automatorConfig.CombatAutorotation,
                pluginStatus,
                rsr,
                message => CombatAutorotationSetup.PrintError(chat, uiConfig, message)))
        {
            return;
        }

        session.Prepare(CombatAutorotationSetup.ToRecipe(automatorConfig));
        session.Disable();
    }

    public void EnableFighting() => session.Enable(CombatActivity.MobFarm);

    public void Disable() => session.Disable();

    public void Tick()
    {
        session.OverwriteBossModPresets = automatorConfig.UpdateBossModPresetsAutomatically;
        session.MovementSettings = BossModMovement.From(automatorConfig, player.IsMelee(), player.GetClassJob()?.RowId);
        session.Tick(supportJobs.TryGetCurrent(out SupportJob current) ? current.Id.RowId() : null);
    }

    public void Teardown() => session.Teardown();
}

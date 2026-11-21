using BOCCHI.Common.Config;
using BOCCHI.Common.Services;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Treasure.Services;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using Ocelot.Lifecycle;
using Ocelot.Services.Logger;
using Ocelot.Services.Pathfinding;

namespace BOCCHI.Automator.Services;

public sealed class HuntSelfDefenceService
(
    ITreasureHunter hunter,
    ICarrotHunter carrots,
    ICondition conditions,
    IObjectTable objects,
    AutomatorConfig config,
    AutoRotationController rotation,
    IPathfinder pathfinder,
    ILogger<HuntSelfDefenceService> logger
) : IOnUpdate
{
    private static readonly TimeSpan OutrunGrace = TimeSpan.FromSeconds(10);

    private DateTimeOffset? combatSince;

    private bool defending;

    private bool pausedTreasureHunt;

    public void Update()
    {
        bool treasure = hunter.Running
                        && !hunter.ManagedByPotsTreasure
                        && !hunter.ManagedByIllegalModeFiller
                        && !hunter.ManagedByMobFarmer;
        bool carrot = carrots.Running;
        if (!treasure && !carrot)
        {
            combatSince = null;
            Stop(resume: false);
            return;
        }

        bool inCombat = conditions[ConditionFlag.InCombat] && !conditions[ConditionFlag.Unconscious];
        combatSince = inCombat ? combatSince ?? DateTimeOffset.UtcNow : null;

        if (defending)
        {
            if (!inCombat)
            {
                Stop(resume: true);
                return;
            }

            pathfinder.Stop();
            if (!DismountAssist.TryDismount(conditions))
            {
                rotation.EnableForSelfDefence();
                rotation.Tick();
            }

            return;
        }

        if (!inCombat
            || !config.CombatAutorotation.UsesCombatAutomation()
            || DateTimeOffset.UtcNow - combatSince < OutrunGrace
            || (treasure ? hunter.Paused : carrots.Paused))
        {
            return;
        }

        defending = true;
        pausedTreasureHunt = treasure;
        if (treasure)
        {
            hunter.Pause();
        }
        else
        {
            carrots.Pause();
        }

        rotation.PrepareForIllegalMode();
        logger.Info(
            "{Hunt}: pausing to fight — still attacked after {Seconds}s (attacker level {Enemy}, your Knowledge Level {Mine})",
            treasure ? "Treasure hunt" : "Carrot hunt",
            (int)OutrunGrace.TotalSeconds,
            KnowledgeThreat.HighestAttackerLevel(objects)?.ToString() ?? "?",
            KnowledgeThreat.TryGetPlayerForayLevel(objects)?.ToString() ?? "?");
    }

    private void Stop(bool resume)
    {
        if (!defending)
        {
            return;
        }

        defending = false;
        rotation.DisableAi();
        rotation.TeardownForIllegalMode();
        if (!resume)
        {
            return;
        }

        if (pausedTreasureHunt && hunter.Running && hunter.Paused)
        {
            hunter.ResumeNearPlayer();
        }
        else if (!pausedTreasureHunt && carrots.Running && carrots.Paused)
        {
            carrots.Resume();
        }

        logger.Info("{Hunt}: fight over — continuing", pausedTreasureHunt ? "Treasure hunt" : "Carrot hunt");
    }
}

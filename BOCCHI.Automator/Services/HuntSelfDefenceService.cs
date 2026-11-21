using BOCCHI.Common.Config;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Services;
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
    private readonly OutrunTimer outrun = new();

    private bool pausedTreasureHunt;

    private bool ownsRotation;

    public bool Defending { get; private set; }

    public void Update()
    {
        // Illegal Mode's own hunt hands the fight to Illegal Mode instead (IllegalModeTreasureFillerService).
        bool treasure = hunter.Running && !hunter.ManagedByIllegalModeFiller;
        bool carrot = carrots.Running;
        if (!treasure && !carrot)
        {
            outrun.Reset();
            Stop(resume: false);
            return;
        }

        bool inCombat = conditions[ConditionFlag.InCombat] && !conditions[ConditionFlag.Unconscious];
        bool chased = outrun.Update(inCombat, DateTimeOffset.UtcNow);

        if (Defending)
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

        if (!chased
            || !config.CombatAutorotation.UsesCombatAutomation()
            || (treasure ? hunter.Paused : carrots.Paused))
        {
            return;
        }

        Defending = true;
        pausedTreasureHunt = treasure;
        if (treasure)
        {
            hunter.Pause();
        }
        else
        {
            carrots.Pause();
        }

        // Pots & Treasure keeps its own rotation session prepared; tearing that down would leave its FATEs without one.
        ownsRotation = !rotation.IsPrepared;
        if (ownsRotation)
        {
            rotation.PrepareForIllegalMode();
        }

        logger.Info(
            "{Hunt}: pausing to fight — still attacked after {Seconds}s (attacker level {Enemy}, your Knowledge Level {Mine})",
            treasure ? "Treasure hunt" : "Carrot hunt",
            (int)CombatRules.OutrunGrace.TotalSeconds,
            KnowledgeThreat.HighestAttackerLevel(objects)?.ToString() ?? "?",
            KnowledgeThreat.TryGetPlayerForayLevel(objects)?.ToString() ?? "?");
    }

    private void Stop(bool resume)
    {
        if (!Defending)
        {
            return;
        }

        Defending = false;
        rotation.DisableAi();
        if (ownsRotation)
        {
            rotation.TeardownForIllegalMode();
        }

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

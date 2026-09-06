using BOCCHI.Automator.Data;
using BOCCHI.Automator.Services;
using BOCCHI.Automator.Services.PotTreasure;
using BOCCHI.Common.Config;
using BOCCHI.Common.Data.StateMemory;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Data.Zones.Graph;
using BOCCHI.Common.Data.Paths;
using BOCCHI.Common.Services;
using BOCCHI.Common.Services.Paths;
using BOCCHI.Common.Targeting;
using BOCCHI.Treasure.ChainRecipes;
using BOCCHI.Treasure.Services;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using ECommons.Throttlers;
using Ocelot.Chain;
using Ocelot.Extensions;
using Ocelot.Ipc.VNavmesh;
using Ocelot.Pathfinding.Extensions;
using Ocelot.Services.Logger;
using Ocelot.Services.Pathfinding;
using Ocelot.Services.PlayerState;
using Ocelot.States.Score;
using System.Numerics;
using ECommonsPlayer = ECommons.GameHelpers.Player;

namespace BOCCHI.Automator.StateMachine.Handlers;

public class FarmingPotChestsHandler
(
    IAutomatorMemory memory,
    IChainFactory chains,
    IChainManager chainManager,
    IPathfinder pathfinder,
    IPathCalculator pathCalculator,
    IPathStepExecutor pathStepExecutor,
    IObjectTable objects,
    ICondition conditions,
    IPlayer player,
    IZoneProvider zones,
    PotTreasureHintTracker hints,
    IPluginLog pluginLog,
    AutoRotationController autoRotation,
    MovementConfig movement,
    PotsConfig potsConfig,
    TreasureConfig treasureConfig,
    NinjaHideAssist ninjaHide,
    IAutomatorContext context,
    PandoraAutoOpenHold pandoraAutoOpen,
    IVNavmeshIpc vnav,
    ILogger<FarmingPotChestsHandler> logger
) : ScoreStateHandler<AutomatorState, StatePriority>(AutomatorState.FarmingPotChests)
{
    private const float ChestSearchRadius = 18f;

    private const float RevealSearchRadius = 28f;

    /// <summary>
    ///     Divert to any streamed pot coffer within this range even if it is not on an authored pad
    ///     (authored list can miss a spawn while neighbors were already swept).
    /// </summary>
    private const float LiveCofferDivertRadius = 80f;

    /// <summary>
    ///     Extra radius around the pot / 2nd-chance pad when deciding to Hide before walking in
    ///     (on-player enter alone is too late once the pack has already aggroed).
    /// </summary>
    private const float PotChestHideApproachLead = 30f;

    /// <summary>On-pad distance for the elixir probe (not coffer interact range).</summary>
    private const float CandidateProbeRadius = 5f;

    private static readonly TimeSpan ChestSpawnWait = TimeSpan.FromSeconds(45);

    /// <summary>
    ///     Cache Me If You Can / Magical Elixir can appear shortly after the pot FATE despawns.
    ///     Keep this longer than a frame or two so we do not abandon a real reward.
    /// </summary>
    private static readonly TimeSpan BuffWaitTimeout = TimeSpan.FromSeconds(25);

    private static readonly TimeSpan HintWaitTimeout = TimeSpan.FromSeconds(4);

    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(300);

    /// <summary>
    ///     Skip a pot treasure target when vnav sits idle this long without reaching it — it has no
    ///     route to the pad (off-mesh pads, #176/#177).
    /// </summary>
    private static readonly TimeSpan ApproachIdleTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    ///     Backstop for vnav following a path but never arriving. Deliberately long: while it is
    ///     moving it is presumed to be making real progress, however the straight-line distance looks.
    /// </summary>
    private static readonly TimeSpan ApproachHardTimeout = TimeSpan.FromSeconds(90);

    private const float ApproachProgressThreshold = 1.5f;

    /// <summary>Destination move that forces a fresh path even if one is already running.</summary>
    private const float RepathDrift = 2f;

    /// <summary>
    ///     Do not re-issue the same PathfindAndMoveTo while vnav is idle near the last pad
    ///     (TreasureHunterService SameDestRepathCooldown). Without this, Idle + 750ms throttle
    ///     spam-queues move-to on an already-reached pad and never opens.
    /// </summary>
    private static readonly TimeSpan SameDestRepathCooldown = TimeSpan.FromSeconds(2.5);

    /// <summary>Hard cap on the whole tail after Cache Me drops, including walking to the coffer.</summary>
    private static readonly TimeSpan PostBuffGrace = TimeSpan.FromSeconds(30);

    /// <summary>How long to wait for the coffer object to appear before accepting there is none.</summary>
    private static readonly TimeSpan RevealSpawnGrace = TimeSpan.FromSeconds(5);

    /// <summary>Once a coffer has been handled, how long to stay latched for a reroll.</summary>
    private static readonly TimeSpan RerollWait = TimeSpan.FromSeconds(12);

    private const int MaxElixirAttempts = 3;

    private Task<ChainResult>? activeChain;

    /// <summary>Every treasure object this tick (see <see cref="RefreshTickChests"/>).</summary>
    private readonly List<IGameObject> tickChests = [];

    /// <summary>Pot reveal coffers this tick — matched by BaseId, any ObjectKind.</summary>
    private readonly List<IGameObject> tickReveals = [];

    private readonly List<Vector3> authoredSpots = [];

    /// <summary>Hunt coffer positions — objects nearer one of these are not pot reveals.</summary>
    private readonly List<Vector3> foreignSpots = [];

    private int authoredSpotsFate = -1;

    /// <summary>Last destination handed to the pathfinder, for drift detection.</summary>
    private Vector3? lastPathDestination;

    /// <summary>When <see cref="lastPathDestination"/> was last issued (UTC).</summary>
    private DateTimeOffset lastPathIssueAt = DateTimeOffset.MinValue;

    /// <summary>
    ///     After a mid-route PathStep cancel, prefer plain PathfindAndMoveTo until we arrive or
    ///     the destination drifts — avoids PathStep cancel ↔ recalculate thrash (~265ms loops).
    /// </summary>
    private bool preferDirectApproach;

    /// <summary>In-flight aethernet route plan for the current long hop.</summary>
    private Task<PathCalculationResult>? travelPlanTask;

    /// <summary>Destination the current plan was built for.</summary>
    private Vector3? travelPlanTarget;

    /// <summary>Remaining steps of the planned route; null when travelling on plain vnav.</summary>
    private Queue<IPathStep>? travelSteps;

    private Vector3? approachTarget;

    private DateTimeOffset approachSince = DateTimeOffset.MinValue;

    /// <summary>When vnav went idle short of the target; MinValue while it is working.</summary>
    private DateTimeOffset approachIdleSince = DateTimeOffset.MinValue;

    private float approachBestDist = float.MaxValue;

    /// <summary>True while the AI holds movement for a fight (see the combat branch in Handle).</summary>
    private bool defendingInCombat;

    private readonly NinjaHideRouteGate ninjaHideRouteGate = new();

    private bool ninjaHideRequired;

    public override StatePriority GetScore()
    {
        if (conditions[ConditionFlag.Unconscious])
        {
            return StatePriority.Never;
        }

        // High priority vs Return/next-goal handoff race.
        return memory.TryRemember<PotChestFarmMemory>(out PotChestFarmMemory _)
            ? StatePriority.High
            : StatePriority.Never;
    }

    public override void Enter()
    {
        base.Enter();
        // BossMod AI from the pot FATE otherwise keeps AutoTarget / movement during chest pathing.
        autoRotation.DisableAi();
        chainManager.CancelAll();
        pathfinder.Stop();
        activeChain = null;
        ClearTravelPlan();
        preferDirectApproach = false;
        lastPathIssueAt = DateTimeOffset.MinValue;
        lastPathDestination = null;
        pandoraAutoOpen.Hold();
        ClearNinjaHideRequirement();

        bool hasFarm = memory.TryRemember<PotChestFarmMemory>(out PotChestFarmMemory farm);
        logger.Debug(
            "Enter pot chest farm fate={Fate} mode={Mode} phase={Phase} remaining={Remaining}/{Total}",
            hasFarm ? farm.FateId.Value.ToString() : "?",
            hasFarm ? farm.Mode.ToString() : "?",
            hasFarm ? farm.Phase.ToString() : "?",
            hasFarm ? farm.RemainingChests : 0,
            hasFarm ? farm.TotalChests : 0);
    }

    public override void Exit(AutomatorState next)
    {
        base.Exit(next);
        ResetApproachWatch();
        chainManager.CancelAll();
        pathfinder.Stop();
        activeChain = null;
        ClearTravelPlan();
        preferDirectApproach = false;
        lastPathIssueAt = DateTimeOffset.MinValue;
        lastPathDestination = null;
        tickChests.Clear();
        tickReveals.Clear();
        defendingInCombat = false;
        ClearNinjaHideRequirement();
        ninjaHide.RestorePreviousGearsetIfNeeded();
        hints.Disarm();
        pandoraAutoOpen.Release();
    }

    public override void Handle()
    {
        if (!memory.TryRemember<PotChestFarmMemory>(out PotChestFarmMemory farm))
        {
            return;
        }

        if (activeChain is { IsCompleted: false })
        {
            // Travel chains block the handler for a long time — a compass hint that lands mid-walk
            // would otherwise be applied from the arrival pad, not from where Magical Elixir was used.
            // Open chains: re-Hide if interact dropped stealth while threats remain.
            if (farm.Phase == PotChestFarmPhase.OpeningReveal
                || farm.Phase == PotChestFarmPhase.BlindSweep)
            {
                MaintainNinjaHideDuringInteract();
            }

            bool interrupt = false;
            if (farm.Mode == PotChestFarmMode.Smart
                && farm.Phase is (PotChestFarmPhase.SearchingCandidates
                    or PotChestFarmPhase.ElixirAtCenter
                    or PotChestFarmPhase.OpeningReveal)
                && hints.TryGetEventSince(farm.HintRevisionBaseline, out PotTreasureHintEvent travelHint)
                && travelHint.Kind == PotTreasureHintKind.Hint)
            {
                farm.HintRevisionBaseline = travelHint.Revision;
                if (TryNarrowByHint(farm, travelHint)
                    && farm.Phase == PotChestFarmPhase.OpeningReveal)
                {
                    farm.Phase = PotChestFarmPhase.SearchingCandidates;
                    farm.PhaseStartedUtc = DateTimeOffset.UtcNow;
                    farm.SettledAtUtc = DateTimeOffset.MinValue;
                }

                logger.Debug("Pot treasure: compass hint during travel — cancelling path to re-route");
                interrupt = true;
            }

            if (!interrupt)
            {
                return;
            }

            chainManager.CancelAll();
            pathfinder.Stop();
            activeChain = null;
            ClearTravelPlan();
            preferDirectApproach = false;
            lastPathIssueAt = DateTimeOffset.MinValue;
            lastPathDestination = null;
            ResetApproachWatch();
        }

        // PathStep hops often complete as canceled when vnav stops short of the issued point
        // (PathfindToChain). Re-issuing a fresh aethernet plan every tick thrashes — finish on foot.
        if (activeChain is { IsCompleted: true } finishedChain)
        {
            ChainResult? finished = null;
            try
            {
                if (finishedChain.IsCompletedSuccessfully)
                {
                    finished = finishedChain.Result;
                }
            }
            catch
            {
                // Ignore faulted tasks — treat as a normal completion clear below.
            }

            if (finished is { IsCanceled: true })
            {
                ClearTravelPlan();
                preferDirectApproach = true;
                lastPathIssueAt = DateTimeOffset.MinValue;
                lastPathDestination = null;
            }
        }

        activeChain = null;

        // Combat is the one window where we do not drive movement — AI fights and dodges (#188).
        if (conditions[ConditionFlag.InCombat])
        {
            pathfinder.Stop();
            ClearTravelPlan();
            preferDirectApproach = false;
            lastPathIssueAt = DateTimeOffset.MinValue;
            lastPathDestination = null;

            if (!defendingInCombat)
            {
                defendingInCombat = true;
                autoRotation.EnableForSelfDefence();
                logger.Debug("Pot treasure: in combat — AI is fighting and dodging until it clears");
            }

            return;
        }

        if (defendingInCombat)
        {
            defendingInCombat = false;
            autoRotation.DisableAi();
            ResetApproachWatch();
            logger.Debug("Pot treasure: combat over — taking movement back for the chest search");
        }

        RefreshTickChests(farm);

        // Cache Me clears when the coffer is found, when the chests are done, or when the pot dies.
        // Finding it is the common case, so check for a coffer to open before treating this as the end.
        if (farm.Phase != PotChestFarmPhase.WaitingForBuff && !HasTreasureBuff())
        {
            if (TryFinishRevealAfterBuff(farm))
            {
                return;
            }

            logger.Info("Pot treasure: Cache Me If You Can gone — ending farm");
            FinishFarm();
            return;
        }

        farm.BuffLostUtc = DateTimeOffset.MinValue;

        // Buff is back (reroll) — drop the grace latch and pick the search straight back up. Leaving
        // the phase on OpeningReveal would idle 15s waiting on a coffer that is already looted.
        if (farm.HoldingAfterBuffLoss)
        {
            farm.HoldingAfterBuffLoss = false;
            farm.RerollWaitStarted = false;
            logger.Info("Pot treasure: Cache Me back after the coffer — continuing (reroll)");
            ResumeSearchOrBlind(farm);
            return;
        }

        if (farm.Mode == PotChestFarmMode.Blind || farm.Phase == PotChestFarmPhase.BlindSweep)
        {
            HandleBlindSweep(farm);
            return;
        }

        switch (farm.Phase)
        {
            case PotChestFarmPhase.WaitingForBuff:
                HandleWaitingForBuff(farm);
                break;
            case PotChestFarmPhase.ElixirAtCenter:
                HandleElixirAtCenter(farm);
                break;
            case PotChestFarmPhase.SearchingCandidates:
                HandleSearchingCandidates(farm);
                break;
            case PotChestFarmPhase.OpeningReveal:
                HandleOpeningReveal(farm);
                break;
            default:
                FallBackToBlind(farm);
                break;
        }
    }

    /// <summary>Keep farming after Cache Me drops while a revealed coffer is still in front of us.</summary>
    private bool TryFinishRevealAfterBuff(PotChestFarmMemory farm)
    {
        if (farm.BuffLostUtc == DateTimeOffset.MinValue)
        {
            farm.BuffLostUtc = DateTimeOffset.UtcNow;
        }

        TimeSpan since = DateTimeOffset.UtcNow - farm.BuffLostUtc;
        if (since >= PostBuffGrace)
        {
            return false;
        }

        // Reveal log can land on the same tick Cache Me drops — read it before the phase handlers.
        if (hints.TryGetEventSince(farm.HintRevisionBaseline, out PotTreasureHintEvent evt))
        {
            farm.HintRevisionBaseline = evt.Revision;
            if (evt.Kind == PotTreasureHintKind.CofferReveal)
            {
                farm.HoldingAfterBuffLoss = true;
                farm.HasOpenedChest = true;
            }
        }

        // Match the reveal even if it is nearer the candidate pad than the player.
        if (!TryAcquireReveal(farm, out IGameObject? reveal) || reveal == null)
        {
            if (!farm.HoldingAfterBuffLoss)
            {
                // Coffer object trails the buff drop — wait briefly before giving up.
                return since < RevealSpawnGrace;
            }

            // Coffer is gone (opened) — start the reroll wait from now, not from the buff drop.
            if (!farm.RerollWaitStarted)
            {
                farm.RerollWaitStarted = true;
                farm.BuffLostUtc = DateTimeOffset.UtcNow;
                return true;
            }

            return since < RerollWait;
        }

        if (farm.Phase != PotChestFarmPhase.OpeningReveal)
        {
            farm.Phase = PotChestFarmPhase.OpeningReveal;
            logger.Debug("Pot treasure: Cache Me gone but a coffer is revealed — opening it before ending");
        }

        farm.HoldingAfterBuffLoss = true;
        farm.HasOpenedChest = true;

        if (DismountAssist.TryDismount(conditions, ReportDismount))
        {
            return true;
        }

        float distance = player.Position.Distance2D(reveal.Position);
        // MaxOpenAttemptDistance (not InteractDistance): vnav often parks in the 2–3.5y band
        // and never reaches the tighter interact gate — open chain can still succeed there.
        if (distance > OpenTreasureCofferChain.MaxOpenAttemptDistance)
        {
            if (!EnsurePathing(reveal.Position, allowRemount: false))
            {
                logger.Warning(
                    "Pot treasure: no navmesh at revealed coffer {Pos:F0} — giving up on it",
                    reveal.Position);
                return false;
            }

            return true;
        }

        preferDirectApproach = false;
        pathfinder.Stop();
        TryOpenChest(reveal);
        return true;
    }

    private void ReportDismount(string detail) =>
        logger.Debug("Pot treasure: dismount {Detail}", detail);

    private void HandleWaitingForBuff(PotChestFarmMemory farm)
    {
        // Require Cache Me (1531); not Magical Elixir alone.
        if (HasTreasureBuff())
        {
            hints.Arm();
            pathfinder.Stop();
            farm.Phase = PotChestFarmPhase.ElixirAtCenter;
            farm.PhaseStartedUtc = DateTimeOffset.UtcNow;
            farm.SettledAtUtc = DateTimeOffset.MinValue;
            farm.ElixirAttempts = 0;
            return;
        }

        if (DateTimeOffset.UtcNow - farm.PhaseStartedUtc >= BuffWaitTimeout)
        {
            logger.Info(
                "Pot treasure: no Cache Me If You Can after wait — ending farm (not selected or pot failed)");
            FinishFarm();
        }
    }

    private void HandleElixirAtCenter(PotChestFarmMemory farm)
    {
        if (hints.TryGetEventSince(farm.HintRevisionBaseline, out PotTreasureHintEvent evt))
        {
            if (evt.Kind == PotTreasureHintKind.BonusOffer)
            {
                farm.HintRevisionBaseline = evt.Revision;
                SwitchToRerollPool(farm);
                return;
            }

            if (evt.Kind == PotTreasureHintKind.Hint)
            {
                farm.SeedPool(BuildActivePool(farm));
                if (farm.Pool.Count == 0)
                {
                    logger.Warning("Pot treasure: no authored chest spots for this pot — blind fallback");
                    FallBackToBlind(farm);
                    return;
                }

                if (!TryNarrowByHint(farm, evt))
                {
                    return;
                }

                farm.HintRevisionBaseline = hints.Revision;
                return;
            }

            // ElixirPrompt / Reveal without initial hint — keep waiting, bump baseline.
            farm.HintRevisionBaseline = evt.Revision;
        }

        if (farm.ElixirAttempts >= MaxElixirAttempts
            && DateTimeOffset.UtcNow - farm.PhaseStartedUtc >= HintWaitTimeout)
        {
            logger.Info("Pot treasure: no compass hint after elixir — blind fallback");
            FallBackToBlind(farm);
            return;
        }

        if (farm.ElixirAttempts < MaxElixirAttempts
            && (farm.ElixirAttempts == 0
                || DateTimeOffset.UtcNow - farm.PhaseStartedUtc >= HintWaitTimeout))
        {
            if (!InventoryItemAssist.Has(PotTreasureIds.MagicalElixirItemId, includeKeyItems: true))
            {
                logger.Info("Pot treasure: no Magical Elixir — blind fallback");
                FallBackToBlind(farm);
                return;
            }

            if (TryUseElixir(farm))
            {
                return;
            }
        }
    }

    /// <summary>
    ///     Magical Elixir is a key item, so UseItem takes the KeyItems inventory path and works while
    ///     mounted — dismounting here only cost the dismount and its landing beat. Reveals still need
    ///     feet, but TryOpenChest dismounts itself once one actually appears (#175).
    /// </summary>
    private bool TryUseElixir(PotChestFarmMemory farm)
    {
        // Game recast is ~5s — keep throttle slightly above so UseItem is not spammed on CD.
        if (!InventoryItemAssist.TryUse(
                PotTreasureIds.MagicalElixirItemId,
                "PotTreasure::MagicalElixir",
                5500,
                pluginLog,
                "Pot treasure",
                tryKeyItems: true))
        {
            return false;
        }

        farm.ElixirAttempts++;
        farm.PhaseStartedUtc = DateTimeOffset.UtcNow;
        farm.HintRevisionBaseline = hints.Revision;
        farm.ElixirHintOrigin = player.Position;

        // Start the "did anything happen" wait from the probe rather than from arrival. The elixir
        // has a ~5s recast, so a candidate reached shortly after the previous probe could time out
        // and be skipped before its own probe had even fired.
        farm.SettledAtUtc = DateTimeOffset.UtcNow;
        return true;
    }

    private void HandleSearchingCandidates(PotChestFarmMemory farm)
    {
        if (TryAcquireReveal(farm, out IGameObject? reveal) && reveal != null)
        {
            farm.Phase = PotChestFarmPhase.OpeningReveal;
            farm.PhaseStartedUtc = DateTimeOffset.UtcNow;
            TryOpenChest(reveal);
            return;
        }

        // Authored list can miss a spawn — if a pot coffer is already streamed nearby, walk to it.
        if (FindUnopenedRevealNearPlayer(LiveCofferDivertRadius) is { } liveDivert)
        {
            if (EzThrottler.Throttle("PotChestFarm::LiveDivert", 5000))
            {
                logger.Info(
                    "Pot treasure: live coffer at {Pos:F0} ({Dist:F0}y) — diverting off authored route",
                    liveDivert.Position,
                    player.Position.Distance2D(liveDivert.Position));
            }

            farm.Phase = PotChestFarmPhase.OpeningReveal;
            farm.PhaseStartedUtc = DateTimeOffset.UtcNow;
            if (!EnsurePathing(liveDivert.Position, allowRemount: false, skipIfOffMesh: false))
            {
                TryOpenChest(liveDivert);
            }

            return;
        }

        if (hints.TryGetEventSince(farm.HintRevisionBaseline, out PotTreasureHintEvent evt))
        {
            farm.HintRevisionBaseline = evt.Revision;

            if (evt.Kind == PotTreasureHintKind.BonusOffer)
            {
                SwitchToRerollPool(farm);
                return;
            }

            if (evt.Kind == PotTreasureHintKind.CofferReveal)
            {
                farm.Phase = PotChestFarmPhase.OpeningReveal;
                farm.PhaseStartedUtc = DateTimeOffset.UtcNow;
                return;
            }

            if (evt.Kind == PotTreasureHintKind.Hint)
            {
                if (!TryNarrowByHint(farm, evt))
                {
                    return;
                }
            }
        }

        while (farm.Candidates.Count > 0)
        {
            PotTreasureCandidate peek = farm.Candidates.Peek();
            if (IsChestOpened(peek.Position))
            {
                farm.Candidates.Dequeue();
                farm.WaitingForSpawnSince = DateTimeOffset.MinValue;
                farm.SettledAtUtc = DateTimeOffset.MinValue;
                farm.ElixirAttempts = 0;
                continue;
            }

            break;
        }

        if (farm.Candidates.Count == 0)
        {
            // Exhausted candidates → re-read, not a 50-spot sweep.
            ResumeSearchOrBlind(farm);
            return;
        }

        Vector3 target = farm.Candidates.Peek().Position;
        // Prefer a live coffer near the pad when the authored point is a bit off.
        IGameObject? live = FindUnopenedRevealNear(target) ?? FindUnopenedChestNear(target);
        Vector3 pathTarget = live?.Position ?? target;
        // Arrive at the snapped mesh point, not the authored pad — a 6–12y snap used to leave us
        // forever short of CandidateProbeRadius and re-path in place (#201).
        if (!TreasurePathing.TryResolvePathable(pathTarget, player.Position.Y, vnav, skipIfOffMesh: live == null, out Vector3 pathable))
        {
            logger.Warning(
                "Pot treasure: no navmesh at {Label} {Pos:F0} — skipping candidate ({Remaining} left)",
                farm.Candidates.Peek().Label,
                pathTarget,
                farm.Candidates.Count - 1);
            SkipCurrentCandidate(farm);
            return;
        }

        float distance = player.Position.Distance2D(pathable);

        if (distance > CandidateProbeRadius)
        {
            farm.SettledAtUtc = DateTimeOffset.MinValue;
            if (IsApproachStuck(pathable, distance))
            {
                logger.Warning(
                    "Pot treasure: stuck approaching {Label} at {Pos:F0} — skipping candidate ({Remaining} left)",
                    farm.Candidates.Peek().Label,
                    pathable,
                    farm.Candidates.Count - 1);
                SkipCurrentCandidate(farm);
                return;
            }

            if (!EnsurePathing(pathTarget))
            {
                logger.Warning(
                    "Pot treasure: no navmesh at {Label} {Pos:F0} — skipping candidate ({Remaining} left)",
                    farm.Candidates.Peek().Label,
                    pathTarget,
                    farm.Candidates.Count - 1);
                SkipCurrentCandidate(farm);
            }

            return;
        }

        ResetApproachWatch();
        pathfinder.Stop();
        if (farm.SettledAtUtc == DateTimeOffset.MinValue)
        {
            farm.SettledAtUtc = DateTimeOffset.UtcNow;
            return;
        }

        if (DateTimeOffset.UtcNow - farm.SettledAtUtc < SettleDelay)
        {
            return;
        }

        IGameObject? settledChest = FindChestNear(target) ?? FindRevealNear(player.Position);
        if (settledChest != null)
        {
            farm.Phase = PotChestFarmPhase.OpeningReveal;
            farm.PhaseStartedUtc = DateTimeOffset.UtcNow;
            TryOpenChest(settledChest);
            return;
        }

        // Probe with elixir at the candidate — mounted is fine, it is a key item.
        if (farm.ElixirAttempts < MaxElixirAttempts)
        {
            TryUseElixir(farm);
        }

        // Wait from when we settled on this pad — don't softlock if UseItem never succeeds,
        // and don't use PhaseStartedUtc (that starts when the whole search begins).
        if (DateTimeOffset.UtcNow - farm.SettledAtUtc < HintWaitTimeout)
        {
            return;
        }

        SkipCurrentCandidate(farm);
    }

    private void SkipCurrentCandidate(PotChestFarmMemory farm)
    {
        if (farm.Candidates.Count > 0)
        {
            farm.Candidates.Dequeue();
        }

        farm.ElixirAttempts = 0;
        farm.SettledAtUtc = DateTimeOffset.MinValue;
        farm.WaitingForSpawnSince = DateTimeOffset.MinValue;
        farm.PhaseStartedUtc = DateTimeOffset.UtcNow;
        ResetApproachWatch();
        pathfinder.Stop();
    }

    /// <summary>
    ///     Stuck means vnav cannot get us there. Long routes may move away from the goal, so only
    ///     <see cref="PathfindingState.Moving"/> counts as a real path — a Pathfinding/idle loop is
    ///     the off-mesh retry from #176/#194.
    /// </summary>
    private bool IsApproachStuck(Vector3 target, float distance)
    {
        Vector3 pathable = PathableTreasurePosition(target);
        if (approachTarget is not { } previous
            || previous.Distance2D(pathable) > 2f)
        {
            approachTarget = pathable;
            approachSince = DateTimeOffset.UtcNow;
            approachBestDist = distance;
            approachIdleSince = DateTimeOffset.MinValue;
            return false;
        }

        // While the route planner owns travel, vnav is legitimately idle between teleport steps —
        // reading that as "no route" would skip the candidate mid-hop.
        if (travelPlanTask != null || travelSteps != null)
        {
            approachIdleSince = DateTimeOffset.MinValue;
            return false;
        }

        // Only Moving means vnav actually has a route. Pathfinding+idle looping is the off-mesh
        // case: EnsurePathing re-issues every 750ms, which used to reset the idle timer forever (#194).
        if (pathfinder.GetState() == PathfindingState.Moving)
        {
            approachIdleSince = DateTimeOffset.MinValue;
            if (distance < approachBestDist - ApproachProgressThreshold)
            {
                approachBestDist = distance;
                approachSince = DateTimeOffset.UtcNow;
            }

            return DateTimeOffset.UtcNow - approachSince >= ApproachHardTimeout;
        }

        if (approachIdleSince == DateTimeOffset.MinValue)
        {
            approachIdleSince = DateTimeOffset.UtcNow;
            return false;
        }

        return DateTimeOffset.UtcNow - approachIdleSince >= ApproachIdleTimeout;
    }

    private void ResetApproachWatch()
    {
        lastPathDestination = null;
        ClearTravelPlan();
        approachTarget = null;
        approachIdleSince = DateTimeOffset.MinValue;
        approachSince = DateTimeOffset.MinValue;
        approachBestDist = float.MaxValue;
    }

    private void HandleOpeningReveal(PotChestFarmMemory farm)
    {
        if (TryAcquireReveal(farm, out IGameObject? reveal) && reveal != null)
        {
            if (OpenTreasureCofferChain.IsOpenedOrLooted(reveal))
            {
                FinishReveal(farm, markOpened: true);
                return;
            }

            // Get on foot before closing the last stretch. Travel and the elixir are fine mounted,
            // but the open needs to be within a few y of the coffer and that is not reliable from a
            // mount — least of all in the air, where Dismount cannot land us on the spot.
            if (DismountAssist.TryDismount(conditions, ReportDismount))
            {
                return;
            }

            // 2D — reveal Y ≈ -500 made 3D distance ~500y and blocked open forever (#170).
            // Match OpenTreasureCofferChain's open-attempt range so we do not keep pathing in the
            // 2–3.5y band where vnav often parks and never starts PotChestFarm::Open.
            float distance = player.Position.Distance2D(reveal.Position);
            if (distance > OpenTreasureCofferChain.MaxOpenAttemptDistance)
            {
                if (IsApproachStuck(reveal.Position, distance))
                {
                    logger.Warning(
                        "Pot treasure: stuck approaching revealed coffer at {Pos:F0} - resuming search",
                        reveal.Position);
                    FinishReveal(farm, markOpened: false);
                    return;
                }

                if (!EnsurePathing(reveal.Position, allowRemount: false))
                {
                    logger.Warning(
                        "Pot treasure: no navmesh at revealed coffer {Pos:F0} - resuming search",
                        reveal.Position);
                    FinishReveal(farm, markOpened: false);
                    return;
                }
                return;
            }

            preferDirectApproach = false;

            ResetApproachWatch();
            pathfinder.Stop();
            TryOpenChest(reveal);
            return;
        }

        // Nothing to open here, and the compass is still talking: a hint arriving means the coffer is
        // elsewhere. This phase never read hints, so one that landed here sat unread until the 15s
        // timeout expired — a fifth of the whole Cache Me window spent standing still. Act on it now.
        if (hints.TryGetEventSince(farm.HintRevisionBaseline, out PotTreasureHintEvent evt)
            && evt.Kind == PotTreasureHintKind.Hint)
        {
            farm.HintRevisionBaseline = evt.Revision;
            if (TryNarrowByHint(farm, evt))
            {
                farm.Phase = PotChestFarmPhase.SearchingCandidates;
                farm.PhaseStartedUtc = DateTimeOffset.UtcNow;
                farm.SettledAtUtc = DateTimeOffset.MinValue;
                ResetApproachWatch();
                pathfinder.Stop();
            }

            return;
        }

        if (DateTimeOffset.UtcNow - farm.PhaseStartedUtc > TimeSpan.FromSeconds(15))
        {
            logger.Debug("Pot treasure: reveal timed out — resume search while Cache Me remains");
            ResumeSearchOrBlind(farm);
        }
    }

    private void FinishReveal(PotChestFarmMemory farm, bool markOpened)
    {
        pathfinder.Stop();
        if (markOpened)
        {
            farm.HasOpenedChest = true;
            logger.Debug(
                "Pot treasure: reveal already open — next candidate ({Remaining} left)",
                farm.Candidates.Count);
        }

        if (farm.Candidates.Count > 0)
        {
            farm.Candidates.Dequeue();
        }

        farm.ElixirAttempts = 0;
        farm.SettledAtUtc = DateTimeOffset.MinValue;
        farm.WaitingForSpawnSince = DateTimeOffset.MinValue;
        ResetApproachWatch();
        ResumeSearchOrBlind(farm);
    }

    /// <summary>Give up on narrowing after this many readings and just sweep.</summary>
    private const int MaxHintReadings = 10;

    private void ResumeSearchOrBlind(PotChestFarmMemory farm)
    {
        if (farm.Candidates.Count > 0)
        {
            farm.Phase = PotChestFarmPhase.SearchingCandidates;
            farm.PhaseStartedUtc = DateTimeOffset.UtcNow;
            farm.SettledAtUtc = DateTimeOffset.MinValue;
            return;
        }

        // Already looted one coffer — only second-chance pads from here on.
        if (farm.HasOpenedChest)
        {
            if (!EnsureSecondChancePool(farm))
            {
                return;
            }

            if (farm.Pool.Count > 0 && farm.HintsApplied < MaxHintReadings && HasTreasureBuff())
            {
                logger.Debug(
                    "Pot treasure: second-chance set spent — re-reading from {Count} reroll pad(s)",
                    farm.Pool.Count);
                farm.NarrowTo(farm.Pool);
                return;
            }

            FallBackToBlind(farm);
            return;
        }

        // Narrowed set spent — re-read from the full pool instead of a 50-spot sweep.
        if (farm.Pool.Count > 0 && farm.HintsApplied < MaxHintReadings && HasTreasureBuff())
        {
            logger.Debug(
                "Pot treasure: narrowed set spent — re-reading from {Count} spots",
                farm.Pool.Count);
            farm.NarrowTo(farm.Pool);
            return;
        }

        FallBackToBlind(farm);
    }

    private void HandleBlindSweep(PotChestFarmMemory farm)
    {
        while (farm.Chests.Count > 0)
        {
            Vector3 target = farm.Chests.Peek();
            if (IsChestOpened(target))
            {
                farm.Chests.Dequeue();
                farm.WaitingForSpawnSince = DateTimeOffset.MinValue;
                continue;
            }

            break;
        }

        if (farm.Chests.Count == 0)
        {
            FinishFarm();
            return;
        }

        Vector3 chestPosition = farm.Chests.Peek();
        IGameObject? liveChest = FindChestNear(chestPosition);
        Vector3 pathTarget = liveChest?.Position ?? chestPosition;
        if (!TreasurePathing.TryResolvePathable(pathTarget, player.Position.Y, vnav, skipIfOffMesh: liveChest == null, out Vector3 pathable))
        {
            SkipCurrentBlindChest(farm, pathTarget, "no navmesh at blind chest");
            return;
        }

        float distance = player.Position.Distance2D(pathable);

        if (liveChest == null)
        {
            if (farm.WaitingForSpawnSince == DateTimeOffset.MinValue)
            {
                farm.WaitingForSpawnSince = DateTimeOffset.UtcNow;
            }

            if (distance > OpenTreasureCofferChain.MaxOpenAttemptDistance)
            {
                if (IsApproachStuck(pathable, distance))
                {
                    SkipCurrentBlindChest(farm, chestPosition, "stuck approaching blind chest");
                    return;
                }

                if (!EnsurePathing(chestPosition))
                {
                    SkipCurrentBlindChest(farm, chestPosition, "no navmesh at blind chest");
                }

                return;
            }

            preferDirectApproach = false;
            ResetApproachWatch();
            pathfinder.Stop();

            if (DateTimeOffset.UtcNow - farm.WaitingForSpawnSince >= ChestSpawnWait)
            {
                farm.Chests.Dequeue();
                farm.WaitingForSpawnSince = DateTimeOffset.MinValue;
            }

            return;
        }

        farm.WaitingForSpawnSince = DateTimeOffset.MinValue;

        if (distance > OpenTreasureCofferChain.MaxOpenAttemptDistance)
        {
            if (IsApproachStuck(pathable, distance))
            {
                SkipCurrentBlindChest(farm, pathTarget, "stuck approaching live blind chest");
                return;
            }

            if (!EnsurePathing(pathTarget))
            {
                SkipCurrentBlindChest(farm, pathTarget, "no navmesh at live blind chest");
            }
            return;
        }

        preferDirectApproach = false;
        ResetApproachWatch();
        pathfinder.Stop();
        TryOpenChest(liveChest);
    }

    private void SkipCurrentBlindChest(PotChestFarmMemory farm, Vector3 target, string reason)
    {
        if (farm.Chests.Count > 0)
        {
            farm.Chests.Dequeue();
        }

        farm.WaitingForSpawnSince = DateTimeOffset.MinValue;
        farm.SettledAtUtc = DateTimeOffset.MinValue;
        ResetApproachWatch();
        pathfinder.Stop();
        logger.Warning(
            "Pot treasure: {Reason} at {Pos:F0} - skipping blind chest ({Remaining} left)",
            reason,
            target,
            farm.Chests.Count);
    }

    /// <param name="allowRemount">
    ///     False once we are closing on a coffer to open. The open path dismounts first, so remounting
    ///     mid-approach just fights it — the two take turns and neither wins.
    /// </param>
    /// <param name="skipIfOffMesh">
    ///     Authored pads: skip when vnav has no polygon. Live coffers still walk with a floor snap.
    /// </param>
    /// <returns>False when the pad is off-mesh and <paramref name="skipIfOffMesh"/> is set.</returns>
    private bool EnsurePathing(Vector3 destination, bool allowRemount = true, bool skipIfOffMesh = true)
    {
        // Same gate as Treasure / Carrot Hunt — prepare Hide before walking into high-Knowledge mobs.
        if (!ApplyNinjaHideGate(destination))
        {
            return true;
        }

        if (!TreasurePathing.TryResolvePathable(destination, player.Position.Y, vnav, skipIfOffMesh, out Vector3 pathable))
        {
            return false;
        }

        float distance = player.Position.Distance2D(pathable);

        // OpenTreasureCofferChain can already interact inside MaxOpenAttemptDistance. Keep issuing
        // PathfindAndMoveTo here and vnav often parks just outside InteractDistance — no Open chain,
        // endless "Queueing move-to" (#nyanoha pot chest).
        if (distance <= OpenTreasureCofferChain.MaxOpenAttemptDistance)
        {
            if (!pathfinder.IsIdle())
            {
                pathfinder.Stop();
            }

            lastPathIssueAt = DateTimeOffset.MinValue;
            return true;
        }

        // Long hops use the FATE/CE aethernet planner; short ones stay on vnav.
        // After a canceled PathStep hop, preferDirectApproach skips re-planning until we arrive.
        if (TryTravelByPlan(pathable))
        {
            return true;
        }

        // Re-path when the destination moves, not only when vnav is idle.
        bool drifted = lastPathDestination is not { } last || last.Distance2D(pathable) > RepathDrift;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        bool sameDestCooldown = !drifted
            && lastPathIssueAt != DateTimeOffset.MinValue
            && now - lastPathIssueAt < SameDestRepathCooldown;

        // Idle + still short of dest: PathfindToChain treats that as cancel; re-issuing every
        // throttle tick thrashes. TreasureHunterService uses the same "parked" skip.
        bool parkedOnIssuedPoint = !drifted
            && pathfinder.IsIdle()
            && lastPathDestination is { } issued
            && player.Position.Distance2D(issued) <= OpenTreasureCofferChain.MaxOpenAttemptDistance + 1.5f;

        if (sameDestCooldown || parkedOnIssuedPoint)
        {
            return true;
        }

        string throttleKey = $"PotChestFarm::Path::{MathF.Round(pathable.X)}::{MathF.Round(pathable.Z)}";
        if ((pathfinder.IsIdle() || drifted) && EzThrottler.Throttle(throttleKey, 750))
        {
            lastPathDestination = pathable;
            lastPathIssueAt = now;
            // Already snapped in TreasurePathing. A second 40y floor snap pulled east Daylight
            // Pottery pads ~30y onto unreachable mesh (#194).
            pathfinder.PathfindAndMoveTo(new(pathable));
        }

        // Remount only for longer walks — not while already on top of a reveal.
        if (allowRemount && distance > 15f)
        {
            IZone zone = zones.GetZone();
            MountWait.TryCastIfNeeded(
                conditions,
                objects,
                pathable,
                movement.ShouldAutoMount,
                movement.PreferredMountId,
                zone.IsInBasecamp(),
                zone);
        }

        return true;
    }

    /// <summary>
    ///     Plan and run an aethernet-assisted route to <paramref name="destination"/>.
    ///     Returns true when travel is being handled here and the caller should not walk.
    /// </summary>
    private bool TryTravelByPlan(Vector3 destination)
    {
        if (preferDirectApproach
            || player.Position.Distance2D(destination) <= NavigationConstants.MaxDirectWalkDistance)
        {
            if (player.Position.Distance2D(destination) <= NavigationConstants.MaxDirectWalkDistance)
            {
                preferDirectApproach = false;
            }

            ClearTravelPlan();
            return false;
        }

        if (travelPlanTarget is { } planned && planned.Distance2D(destination) > RepathDrift)
        {
            ClearTravelPlan();
            preferDirectApproach = false;
        }

        if (travelPlanTask is { IsCompleted: true } finished)
        {
            travelPlanTask = null;
            PathCalculationResult result = finished.IsCompletedSuccessfully
                ? finished.Result
                : PathCalculationResult.Failed();

            // No route (or the planner faulted) — fall back to walking rather than stalling.
            if (result.RoutingFailed || result.Steps.Count == 0)
            {
                travelPlanTarget = null;
                travelSteps = null;
                preferDirectApproach = true;
                return false;
            }

            travelSteps = result.Steps;
            logger.Debug(
                "Pot treasure: routing {Steps} step(s) to {Pos:F0} ({Dist:F0}y)",
                travelSteps.Count,
                destination,
                player.Position.Distance2D(destination));
        }

        if (travelPlanTask != null)
        {
            return true;
        }

        if (travelSteps is { Count: > 0 })
        {
            lastPathDestination = destination;
            lastPathIssueAt = DateTimeOffset.UtcNow;
            activeChain = pathStepExecutor.Execute(travelSteps.Dequeue());
            return true;
        }

        if (travelSteps != null)
        {
            // Plan spent — PathStep may have canceled short of the pad. Prefer direct vnav rather
            // than calculating a fresh multi-hop plan every tick.
            ClearTravelPlan();
            preferDirectApproach = true;
            return false;
        }

        travelPlanTarget = destination;
        travelPlanTask = pathCalculator.CalculateToPosition(destination, CandidateProbeRadius);
        return true;
    }

    private void ClearTravelPlan()
    {
        travelPlanTask = null;
        travelPlanTarget = null;
        travelSteps = null;
    }

    private void TryOpenChest(IGameObject chest)
    {
        // Pot reveals need feet — normal hunt coffers stay mounted (#175).
        if (DismountAssist.TryDismount(conditions, ReportDismount) || ECommonsPlayer.IsJumping)
        {
            return;
        }

        Vector3 position = PathableTreasurePosition(chest.Position);
        // Prefer reveal BaseIds — pot reveals are EventObj, not ObjectKind.Treasure.
        activeChain = chainManager.Manage(
            chains.Create("PotChestFarm::Open")
                .Then<OpenTreasureCofferChain, TreasureOpenTarget>(
                    new TreasureOpenTarget(position, PotTreasureIds.RevealCofferBaseIds))
        );
    }

    private Vector3 PathableTreasurePosition(Vector3 position)
    {
        _ = TreasurePathing.TryResolvePathable(
            position,
            player.Position.Y,
            vnav,
            skipIfOffMesh: false,
            out Vector3 pathable);
        return pathable;
    }

    private bool TryAcquireReveal(PotChestFarmMemory farm, out IGameObject? reveal)
    {
        reveal = FindUnopenedRevealNear(player.Position);
        if (reveal != null)
        {
            return true;
        }

        if (farm.Candidates.Count > 0)
        {
            reveal = FindUnopenedRevealNear(farm.Candidates.Peek().Position)
                     ?? FindUnopenedChestNear(farm.Candidates.Peek().Position);
            return reveal != null;
        }

        return false;
    }

    /// <summary>
    ///     A revealed coffer is in the object table for a beat before it can be interacted with, so
    ///     require targetable before treating one as acquired — latching early means dismounting and
    ///     pathing to something that cannot be opened yet. Not a fallback to the nearest untargetable
    ///     one either: waiting is correct, and the coffer becomes targetable on its own.
    /// </summary>
    private IGameObject? FindUnopenedRevealNear(Vector3 origin)
    {
        IGameObject? reveal = GameObjectNearest.Find2D(
            tickReveals,
            origin,
            RevealSearchRadius,
            o => o.IsTargetable);

        if (reveal == null)
        {
            if (FindRevealNear(origin) != null
                && EzThrottler.Throttle("PotChestFarm::RevealNotTargetable", 2000))
            {
                logger.Debug("Pot treasure: coffer on an authored spot is not targetable yet — waiting");
            }

            return null;
        }

        return OpenTreasureCofferChain.IsOpenedOrLooted(reveal) ? null : reveal;
    }

    private IGameObject? FindUnopenedRevealNearPlayer(float radius)
    {
        IGameObject? reveal = GameObjectNearest.Find2D(
            tickReveals,
            player.Position,
            radius,
            o => o.IsTargetable);

        return reveal != null && !OpenTreasureCofferChain.IsOpenedOrLooted(reveal) ? reveal : null;
    }

    private IGameObject? FindUnopenedChestNear(Vector3 position)
    {
        IGameObject? chest = FindChestNear(position);
        return chest != null && !OpenTreasureCofferChain.IsOpenedOrLooted(chest) ? chest : null;
    }

    /// <returns>False while still preparing Hide (caller should wait via EnsurePathing returning true).</returns>
    private bool ApplyNinjaHideGate(Vector3? approachingDestination = null)
    {
        if (!treasureConfig.UseNinjaHideOnDangerousRoutes)
        {
            ClearNinjaHideRequirement();
            return true;
        }

        UpdateNinjaHideRequired(approachingDestination);

        if (!ninjaHideRequired)
        {
            ninjaHide.RestorePreviousGearsetIfNeeded();
            return true;
        }

        if (conditions[ConditionFlag.InCombat])
        {
            return true;
        }

        if (ninjaHide.EnsureReady(treasureConfig.NinjaGearsetNumber))
        {
            if (treasureConfig.UseOccultSprintWhileHidden)
            {
                ninjaHide.TryOccultSprintWhileHidden();
            }

            return true;
        }

        if (treasureConfig.NinjaGearsetNumber <= 0 && !ninjaHide.IsNinja)
        {
            if (EzThrottler.Throttle("PotChestFarm::NinjaHideNoGearset", 10000))
            {
                logger.Warning(
                    "Ninja Hide is on but gearset is 0 and you are not on Ninja — skipping Hide for this threat");
            }

            ClearNinjaHideRequirement();
            return true;
        }

        pathfinder.Stop();
        vnav.Stop();
        return false;
    }

    private void UpdateNinjaHideRequired(Vector3? approachingDestination = null)
    {
        ninjaHideRequired = ninjaHideRouteGate.UpdateRequired(
            objects,
            player.Position,
            ninjaHideRequired,
            ninjaHide.IsMounted,
            treasureConfig.KnowledgeHideOffset,
            treasureConfig.KnowledgeThreatEnterDistance,
            treasureConfig.KnowledgeThreatExitDistance);

        // Player-radius gate alone Hides too late for pot / 2nd-chance pads in packs — also arm
        // when high-Knowledge mobs sit around the chest we are walking to.
        if (!ninjaHideRequired && approachingDestination is { } dest)
        {
            ninjaHideRequired = ShouldHideForDestination(dest);
        }
    }

    /// <summary>
    ///     True when a Hide-eligible threat is near the pad/coffer (wider than on-player enter).
    /// </summary>
    private bool ShouldHideForDestination(Vector3 destination)
    {
        if (KnowledgeThreat.TryFindIsleblazer(
                objects,
                player.Position,
                KnowledgeThreat.IsleblazerUnhideDistance,
                out _))
        {
            return false;
        }

        if (KnowledgeThreat.TryGetPlayerForayLevel(objects) is not int foray)
        {
            return false;
        }

        int hideAt = KnowledgeThreat.HideAtOrAbove(foray, treasureConfig.KnowledgeHideOffset);
        float lead = treasureConfig.KnowledgeThreatEnterDistance
                     + KnowledgeThreat.MountedThreatEnterBonus
                     + PotChestHideApproachLead;
        return KnowledgeThreat.TryFindThreat(objects, destination, hideAt, lead, out _, out _);
    }

    private void ClearNinjaHideRequirement()
    {
        ninjaHideRequired = false;
        ninjaHideRouteGate.Reset();
    }

    private void MaintainNinjaHideDuringInteract()
    {
        if (!treasureConfig.UseNinjaHideOnDangerousRoutes || !ninjaHideRequired)
        {
            return;
        }

        if (conditions[ConditionFlag.InCombat])
        {
            return;
        }

        // Keep Hide requirement fresh while the open runs next to the pack.
        UpdateNinjaHideRequired();
        if (!ninjaHideRequired)
        {
            return;
        }

        _ = ninjaHide.EnsureReady(treasureConfig.NinjaGearsetNumber);
    }

    /// <summary>
    ///     Apply one hint: keep the spots lying in that direction <b>from where Magical Elixir was
    ///     used</b> (or where the log landed, if we did not record a use). Mid-walk or next-pad
    ///     positions must not re-interpret the bearing.
    ///     Narrows the survivors first so successive readings triangulate; if that leaves nothing the
    ///     reading disagrees with the ones before it, so re-acquire from the full set before giving up.
    /// </summary>
    /// <returns>False when the farm fell back to a blind sweep and the caller should stop.</returns>
    private bool TryNarrowByHint(PotChestFarmMemory farm, PotTreasureHintEvent evt)
    {
        Vector3 from = farm.ElixirHintOrigin ?? evt.Origin ?? player.Position;
        IEnumerable<PotTreasureCandidate> basis = farm.Candidates.Count > 0 ? farm.Candidates : farm.Pool;

        List<PotTreasureCandidate> survivors = PotTreasureFilter.Narrow(
            basis, from, evt.Direction, evt.Distance, PotTreasureFilter.OctantTolerance);

        string source = "narrowed";
        if (survivors.Count == 0)
        {
            survivors = PotTreasureFilter.Narrow(
                farm.Pool, from, evt.Direction, evt.Distance, PotTreasureFilter.OctantTolerance);
            source = "re-acquired";
        }

        if (survivors.Count == 0)
        {
            survivors = PotTreasureFilter.Narrow(
                farm.Pool, from, evt.Direction, evt.Distance, PotTreasureFilter.WideTolerance);
            source = "widened";
        }

        if (survivors.Count == 0)
        {
            // Everything we know says the chest is at one of these pads, so a reading that matches
            // none of them is the odd one out — not grounds to throw away every earlier reading and
            // sweep 50 positions. Keep what we have and ignore it; only sweep with nothing left.
            farm.ElixirHintOrigin = null;
            if (farm.Candidates.Count > 0)
            {
                logger.Warning(
                    "Pot treasure: hint {Direction}/{Distance} matches no authored spot — ignoring it, "
                    + "keeping {Count} candidate(s)",
                    evt.Direction,
                    evt.Distance,
                    farm.Candidates.Count);
                return true;
            }

            logger.Warning(
                "Pot treasure: hint {Direction}/{Distance} matches no authored spot — blind fallback",
                evt.Direction,
                evt.Distance);
            FallBackToBlind(farm);
            return false;
        }

        farm.NarrowTo(survivors);
        logger.Debug(
            "Pot treasure: hint {Hint} {Direction}/{Distance} from {From:F0} — {Count} spot(s) {Source}, nearest {Label}",
            farm.HintsApplied,
            evt.Direction,
            evt.Distance,
            from,
            survivors.Count,
            source,
            survivors[0].Label);
        return true;
    }

    /// <summary>Second-chance chests use reroll pads, not the pot FATE spots (#188).</summary>
    private void SwitchToRerollPool(PotChestFarmMemory farm)
    {
        if (!ShouldIncludeRerolls || farm.OnRerollPool)
        {
            return;
        }

        if (!TryActivateRerollPool(farm, markOpenedChest: true, narrowImmediately: true))
        {
            logger.Warning("Pot treasure: reroll offered but this zone has no authored reroll pads");
            return;
        }

        logger.Info(
            "Pot treasure: second chest offered — switching to {Count} reroll pad(s)",
            farm.Pool.Count);
    }

    /// <summary>
    ///     After the first coffer, search only second-chance pads. Ends the farm when rerolls are
    ///     disabled or missing — walking the pot FATE pads again cannot find that chest.
    /// </summary>
    private bool EnsureSecondChancePool(PotChestFarmMemory farm)
    {
        if (farm.OnRerollPool && farm.Pool.Count > 0)
        {
            return true;
        }

        if (!ShouldIncludeRerolls)
        {
            logger.Info("Pot treasure: coffer opened and second-chance farming is off — ending farm");
            FinishFarm();
            return false;
        }

        if (!TryActivateRerollPool(farm, markOpenedChest: false, narrowImmediately: false))
        {
            logger.Warning("Pot treasure: coffer opened but this zone has no authored reroll pads — ending farm");
            FinishFarm();
            return false;
        }

        logger.Info(
            "Pot treasure: first coffer opened — locking search to {Count} second-chance pad(s)",
            farm.Pool.Count);
        return true;
    }

    private bool TryActivateRerollPool(
        PotChestFarmMemory farm,
        bool markOpenedChest,
        bool narrowImmediately)
    {
        List<PotTreasureCandidate> reroll = PotTreasureFilter.BuildRerollPool(zones.GetZone());
        if (reroll.Count == 0)
        {
            return false;
        }

        farm.OnRerollPool = true;
        if (markOpenedChest)
        {
            farm.HasOpenedChest = true;
        }

        farm.SeedPool(reroll);
        if (narrowImmediately)
        {
            farm.NarrowTo(reroll);
        }

        ResetApproachWatch();
        ClearTravelPlan();
        return true;
    }

    /// <summary>Authored spots for the current search: pot FATE pads, or rerolls after a coffer.</summary>
    private List<PotTreasureCandidate> BuildActivePool(PotChestFarmMemory farm) =>
        farm.HasOpenedChest || farm.OnRerollPool
            ? PotTreasureFilter.BuildRerollPool(zones.GetZone())
            : PotTreasureFilter.BuildPool(zones.GetZone(), farm.FateId.Value);

    /// <summary>Same opt-in the blind sweep uses, so pool and sweep cover the same pads.</summary>
    private bool ShouldIncludeRerolls =>
        context.IsPotsAndTreasure || potsConfig.ShouldFarmRerollPotChests;

    private void FallBackToBlind(PotChestFarmMemory farm)
    {
        hints.Disarm();
        IZone zone = zones.GetZone();
        List<Vector3> positions = [];

        // After opening one coffer, only second-chance pads can host the next — never the pot
        // FATE spots again (those were the first-chest set).
        if (farm.HasOpenedChest || farm.OnRerollPool)
        {
            if (!ShouldIncludeRerolls)
            {
                logger.Info("Pot treasure: second-chance farming is off after a coffer — ending farm");
                FinishFarm();
                return;
            }

            positions.AddRange(zone.GetRerollPotChestData().Select(c => c.Position));
            if (positions.Count == 0)
            {
                logger.Warning("Pot treasure: no second-chance pads left to sweep — ending farm");
                FinishFarm();
                return;
            }

            farm.OnRerollPool = true;
        }
        else
        {
            if (zone.GetPotChestData().TryGetValue(farm.FateId.Value, out List<PotChestData>? chests))
            {
                positions.AddRange(chests.Select(c => c.Position));
            }

            // First-chest blind can still visit reroll pads as a last resort.
            if (ShouldIncludeRerolls)
            {
                positions.AddRange(zone.GetRerollPotChestData().Select(c => c.Position));
            }
        }

        positions = positions
            .OrderBy(p => player.Position.Distance2D(p))
            .ToList();

        if (positions.Count == 0)
        {
            FinishFarm();
            return;
        }

        farm.BeginBlindFallback(positions);
        logger.Debug(
            "Pot treasure: blind sweep with {Count} positions ({Kind})",
            positions.Count,
            farm.HasOpenedChest || farm.OnRerollPool ? "second-chance only" : "pot + second-chance");
    }

    private void FinishFarm()
    {
        hints.Disarm();
        memory.Forget<PotChestFarmMemory>();
    }

    private bool HasTreasureBuff() =>
        player.PlayerCharacter?.StatusList.Has(PotTreasureIds.TreasureBuffStatusId) == true;

    /// <summary>
    ///     Every authored pot chest position for the current FATE, including rerolls. A pot reveal
    ///     only ever appears on one of these, which is what separates it from ordinary field coffers.
    /// </summary>
    private void EnsureAuthoredSpots(PotChestFarmMemory farm)
    {
        if (authoredSpotsFate == farm.FateId.Value)
        {
            return;
        }

        authoredSpotsFate = farm.FateId.Value;
        authoredSpots.Clear();

        IZone zone = zones.GetZone();
        if (zone.GetPotChestData().TryGetValue(farm.FateId.Value, out List<PotChestData>? chests))
        {
            authoredSpots.AddRange(chests.Select(c => c.Position));
        }

        authoredSpots.AddRange(zone.GetRerollPotChestData().Select(c => c.Position));

        foreignSpots.Clear();
        foreignSpots.AddRange(
            zone.GetTreasureData()
                .Where(t => t.Position.HasValue)
                .Select(t => t.Position!.Value));
    }

    /// <summary>Rebuild <see cref="tickChests"/> once per tick for reveal matching.</summary>
    private void RefreshTickChests(PotChestFarmMemory farm)
    {
        EnsureAuthoredSpots(farm);
        tickChests.Clear();
        tickReveals.Clear();

        foreach (IGameObject obj in objects)
        {
            if (!obj.IsValid() || obj.IsDead)
            {
                continue;
            }

            // Pot reveals are EventObj matched by BaseId, not ObjectKind.Treasure.
            if (PotTreasureIds.RevealCofferBaseIds.Contains(obj.BaseId))
            {
                tickReveals.Add(obj);
                continue;
            }

            if (obj.ObjectKind != Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Treasure)
            {
                continue;
            }

            tickChests.Add(obj);

            // Safety net for a reveal id we do not know yet: a coffer sitting on an authored pot
            // spot, and nearer that than any hunt coffer, is a reveal even if its BaseId is new.
            if (PotTreasureFilter.IsOnAuthoredPotSpot(obj.Position, authoredSpots, foreignSpots))
            {
                tickReveals.Add(obj);
                if (EzThrottler.Throttle("PotChestFarm::UnknownRevealId", 5000))
                {
                    logger.Info(
                        "Pot treasure: coffer {BaseId} on a pot spot is not a known reveal id — "
                        + "accepting it, worth adding to RevealCofferBaseIds",
                        obj.BaseId);
                }
            }
        }
    }

    // Distance2D — reveal objects can sit at a bogus Y, so 3D compares miss them (#170).
    private IGameObject? FindChestNear(Vector3 position) =>
        GameObjectNearest.Find2D(tickChests, position, ChestSearchRadius);

    private IGameObject? FindRevealNear(Vector3 origin) =>
        GameObjectNearest.Find2D(tickReveals, origin, RevealSearchRadius);

    /// <summary>
    ///     A spot counts as spent only when there is a coffer there and none of them are still
    ///     closed. Now that any treasure matches, "nearest one is open" would let a leftover layout
    ///     bronze on the same spot retire a candidate whose pot chest has not been touched.
    /// </summary>
    private bool IsChestOpened(Vector3 position) =>
        (FindChestNear(position) ?? FindRevealNear(position)) != null
        && FindUnopenedChestNear(position) == null
        && FindUnopenedRevealNear(position) == null;
}

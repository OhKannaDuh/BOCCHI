using BOCCHI.Automator.Services;
using BOCCHI.Buff.Services;
using BOCCHI.Common;
using BOCCHI.Common.Config;
using BOCCHI.Common.Services;
using BOCCHI.MobFarmer.Data;
using BOCCHI.MobFarmer.Services;
using BOCCHI.Services.Shopping;
using BOCCHI.Treasure.Services;
using Dalamud.Plugin.Services;
using Ocelot.Chain;
using Ocelot.Ipc.VNavmesh;
using Ocelot.Services.Pathfinding;
using Ocelot.Services.Translation;
using Ocelot.Windows;

namespace BOCCHI.Services;

public class AutomationModeGuard
(
    Func<IAutomator> automatorFactory,
    Func<IPotsTreasureMode> potsTreasureFactory,
    Func<IMobFarmer> farmerFactory,
    Func<ITreasureHunter> hunterFactory,
    Func<ICarrotHunter> carrotHunterFactory,
    AutomatorConfig automatorConfig,
    IBuffRunner buffRunner,
    IPathfinder pathfinder,
    IVNavmeshIpc vnav,
    IChainManager chains,
    IChatGui chat,
    UIConfig uiConfig,
    ITranslator<MainWindow> translator,
    Func<IShoppingService> shoppingFactory
) : IAutomationModeGuard
{
    private IAutomator Automator => automatorFactory();

    private IPotsTreasureMode PotsTreasure => potsTreasureFactory();

    private IMobFarmer Farmer => farmerFactory();

    private ITreasureHunter Hunter => hunterFactory();

    private ICarrotHunter CarrotHunter => carrotHunterFactory();

    private bool stopping;

    /// <summary>Treasure / carrot hunt was running when shopping took vnav — resume after, not a hunt already paused for FATE/pots.</summary>
    private bool treasurePausedForShopping;

    private bool carrotPausedForShopping;

    public void EnsureExclusive(AutomationMode mode)
    {
        if (stopping)
        {
            return;
        }

        stopping = true;
        try
        {
            if (mode == AutomationMode.Shopping)
            {
                SoftSuspendForShopping();
            }
            else if (mode == AutomationMode.TreasureHunt
                && (Automator.IsIllegalMode || Automator.IsCompletionist))
            {
                // Auto hunt: soft-pause Illegal Mode and resume when the hunt ends. Manual hunt with
                // auto off: only one primary mode — turn Illegal Mode off instead of running both.
                if (automatorConfig.EnableAutomaticTreasureHuntDuringIllegalMode)
                {
                    Automator.SetSuspendedForTreasure(true);
                }
                else
                {
                    StopIllegalOrCompletionist();
                }
            }
            else if (mode == AutomationMode.TreasureHunt && Farmer.Running)
            {
                Farmer.SetSuspended(true, FarmerYieldReason.TreasureHunt);
            }
            else if (mode is AutomationMode.IllegalMode or AutomationMode.Completionist)
            {
                if (mode == AutomationMode.IllegalMode && Automator.IsCompletionist)
                {
                    Automator.ToggleCompletionist();
                }
                else if (mode == AutomationMode.Completionist && Automator.IsIllegalMode)
                {
                    Automator.Toggle();
                }

                StopStandaloneTreasureHunt();
                if (Hunter.Running && Hunter.ManagedByIllegalModeFiller)
                {
                    Automator.SetSuspendedForTreasure(true);
                }
            }
            else if (mode is not AutomationMode.IllegalMode and not AutomationMode.Completionist)
            {
                StopIllegalOrCompletionist();
            }

            if (mode != AutomationMode.PotsAndTreasure
                && mode != AutomationMode.Shopping
                && PotsTreasure.Running)
            {
                if (PotsTreasure.ManagedByMobFarmer)
                {
                    PotsTreasure.StopManagedFromFarmer();
                }
                else
                {
                    PotsTreasure.Toggle();
                }
            }

            if (mode != AutomationMode.MobFarmer
                && Farmer.Running
                && mode != AutomationMode.TreasureHunt
                && mode != AutomationMode.Shopping)
            {
                Farmer.Toggle();
            }

            // Shopping needs exclusive pathing: pause treasure / carrot (resume after).
            if (mode == AutomationMode.Shopping)
            {
                if (Hunter.Running && !Hunter.Paused)
                {
                    Hunter.Pause();
                    treasurePausedForShopping = true;
                }

                if (CarrotHunter.Running && !CarrotHunter.Paused)
                {
                    CarrotHunter.Pause();
                    carrotPausedForShopping = true;
                }
            }
            else if (mode is not AutomationMode.TreasureHunt
                and not AutomationMode.PotsAndTreasure
                and not AutomationMode.IllegalMode
                and not AutomationMode.Completionist
                && Hunter.Running)
            {
                Hunter.Toggle();
            }

            if (mode != AutomationMode.CarrotHunt
                && mode != AutomationMode.Shopping
                && CarrotHunter.Running)
            {
                CarrotHunter.Toggle();
            }
        }
        finally
        {
            stopping = false;
        }
    }

    public void NotifyTreasureHuntEnded()
    {
        if (stopping)
        {
            return;
        }

        // Resume Illegal Mode / Completionist — Pots & Treasure manages its own suspension.
        if ((Automator.IsIllegalMode || Automator.IsCompletionist) && Automator.SuspendedForTreasure)
        {
            Automator.SetSuspendedForTreasure(false);
        }

        if (Farmer.Running && Farmer.Suspended && Farmer.YieldReason == FarmerYieldReason.TreasureHunt)
        {
            Farmer.SetSuspended(false);
        }
    }

    public void NotifyShoppingEnded()
    {
        if (stopping)
        {
            return;
        }

        if ((Automator.IsIllegalMode || Automator.IsCompletionist || Automator.IsPotsAndTreasure)
            && Automator.SuspendedForShopping)
        {
            Automator.SetSuspendedForShopping(false);
        }

        if (treasurePausedForShopping && Hunter.Running && Hunter.Paused)
        {
            Hunter.Resume();
        }

        treasurePausedForShopping = false;

        if (carrotPausedForShopping && CarrotHunter.Running && CarrotHunter.Paused)
        {
            CarrotHunter.Resume();
        }

        carrotPausedForShopping = false;

        if (Farmer.Running && Farmer.Suspended && Farmer.YieldReason == FarmerYieldReason.Shopping)
        {
            Farmer.SetSuspended(false);
        }
    }

    public void EmergencyStop()
    {
        if (stopping)
        {
            return;
        }

        stopping = true;
        try
        {
            treasurePausedForShopping = false;
            carrotPausedForShopping = false;

            // Shopping owns vnav independently of Illegal Mode — stop it first or pathing continues.
            shoppingFactory().ForceStop();
            if (Automator.SuspendedForShopping)
            {
                Automator.SetSuspendedForShopping(false);
            }

            if (Farmer.Running && Farmer.Suspended && Farmer.YieldReason == FarmerYieldReason.Shopping)
            {
                Farmer.SetSuspended(false);
            }

            StopIllegalOrCompletionist();

            if (PotsTreasure.Running)
            {
                PotsTreasure.Toggle();
            }

            if (Farmer.Running)
            {
                Farmer.Toggle();
            }

            if (Hunter.Running)
            {
                Hunter.Toggle();
            }

            if (CarrotHunter.Running)
            {
                CarrotHunter.Toggle();
            }

            if (buffRunner.IsRunning)
            {
                buffRunner.Stop();
            }

            pathfinder.Stop();
            vnav.Stop();
            chains.CancelAll();
            BocchiChat.Print(chat, uiConfig, translator.T(".status.emergency_stop_done"));
        }
        finally
        {
            stopping = false;
        }
    }

    private void SoftSuspendForShopping()
    {
        // Knowledge-crystal walks (Illegal Mode buff SM or Mob Farmer BuffRunner) sit on the
        // same North camp pad as the antiquarian — they must not keep owning vnav (#203).
        if (buffRunner.IsRunning)
        {
            buffRunner.Stop();
        }

        if (Automator.IsIllegalMode || Automator.IsCompletionist || Automator.IsPotsAndTreasure)
        {
            Automator.SetSuspendedForShopping(true);
        }

        if (Farmer.Running)
        {
            Farmer.SetSuspended(true, FarmerYieldReason.Shopping);
        }
    }

    private void StopIllegalOrCompletionist()
    {
        if (Automator.IsIllegalMode)
        {
            Automator.Toggle();
        }
        else if (Automator.IsCompletionist)
        {
            Automator.ToggleCompletionist();
        }
    }

    /// <summary>
    ///     Manual / Mob Farmer hunts — not the Illegal Mode filler or Pots &amp; Treasure pipeline.
    /// </summary>
    private void StopStandaloneTreasureHunt()
    {
        if (!Hunter.Running || Hunter.ManagedByIllegalModeFiller || Hunter.ManagedByPotsTreasure)
        {
            return;
        }

        Hunter.Toggle();
    }
}

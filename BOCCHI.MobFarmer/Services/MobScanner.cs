using BOCCHI.Common.Config;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Data.Mobs;
using BOCCHI.Common.Extensions;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using ECommons.GameFunctions;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using Ocelot.Extensions;
using Ocelot.Services.PlayerState;

namespace BOCCHI.MobFarmer.Services;

public class MobScanner
(
    MobFarmerConfig config,
    IObjectTable objects,
    IPlayer player,
    IZoneProvider zones,
    Func<IMobFarmer> farmer,
    MobFarmerPanelState panelState
) : IMobScanner
{
    private static readonly TimeSpan IdlePanelScanInterval = TimeSpan.FromMilliseconds(500);

    private DateTime lastIdleScanUtc = DateTime.MinValue;

    public IReadOnlyList<IBattleNpc> Mobs { get; private set; } = [];

    public IReadOnlyList<IBattleNpc> InCombat { get; private set; } = [];

    public IReadOnlyList<IBattleNpc> NotInCombat { get; private set; } = [];

    public IReadOnlyList<IBattleNpc> Contested { get; private set; } = [];

    public unsafe void Update()
    {
        // Occult Crescent only (the farmer panel still previews counts while stopped).
        if (!zones.GetZone().IsOccultCrescentZone())
        {
            ClearScan();
            return;
        }

        IMobFarmer mobFarmer = farmer();
        bool farmerRunning = mobFarmer.Running;
        bool previewCounts = panelState.RecentlyVisible;
        if (!farmerRunning && !previewCounts)
        {
            ClearScan();
            return;
        }

        if (!farmerRunning && previewCounts)
        {
            DateTime now = DateTime.UtcNow;
            if (now - lastIdleScanUtc < IdlePanelScanInterval)
            {
                return;
            }

            lastIdleScanUtc = now;
        }

        if (objects.LocalPlayer is not { } localPlayer)
        {
            ClearScan();
            return;
        }

        List<IBattleNpc> mobs = objects.OfType<IBattleNpc>()
            .Where(o => o is { IsDead: false, IsTargetable: true })
            .Where(o => player.Position.Distance2D(o.Position) <= config.MaxEuclideanDistance)
            .Where(o =>
            {
                // Anything already attacking us must be fought, whatever the mob/level filters say.
                if (o.IsTargetingPlayer(localPlayer))
                {
                    return true;
                }

                BattleChara* battleChara = (BattleChara*)o.Address;
                // Level 0 = foray info unavailable; don't filter those out.
                byte level = battleChara->ForayInfo.Level;
                if (level > 0 && level > config.MaxMobLevel)
                {
                    return false;
                }

                // Selected OC NameIds count even when not flagged hostile yet (common in caves).
                if (MobData.IsSelected(o.NameId, config.Mobs))
                {
                    return true;
                }

                if (!o.IsHostile())
                {
                    return false;
                }

                if (!config.ConsiderSpecialMobs)
                {
                    return false;
                }

                return MobData.TryFromNameId(o.NameId, out Mob mob) && MobData.MobsWithSpawnCondition.Contains(mob);
            })
            .ToList();

        List<IBattleNpc> inCombat = [];
        List<IBattleNpc> notInCombat = [];
        List<IBattleNpc> contested = [];
        foreach (IBattleNpc mob in mobs)
        {
            if (mob.IsTargetingPlayer(localPlayer))
            {
                inCombat.Add(mob);
            }
            else if (!mob.HasTarget())
            {
                notInCombat.Add(mob);
            }
            else
            {
                contested.Add(mob);
            }
        }

        Mobs = mobs;
        InCombat = inCombat;
        NotInCombat = notInCombat;
        Contested = contested;
    }

    private void ClearScan()
    {
        if (Mobs.Count == 0)
        {
            return;
        }

        Mobs = [];
        InCombat = [];
        NotInCombat = [];
        Contested = [];
    }
}

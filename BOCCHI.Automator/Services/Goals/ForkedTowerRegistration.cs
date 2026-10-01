using BOCCHI.Automator.Data;
using BOCCHI.Common.Config;
using BOCCHI.Common.Data.CriticalEncounters;
using BOCCHI.Common.Data.OccultCrescent;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Services;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using Ocelot.Extensions;

namespace BOCCHI.Automator.Services.Goals;

public interface IForkedTowerRegistration
{
    bool IsEnabled { get; }

    /// <summary>The tower Illegal Mode should walk to and wait on, or null to keep farming.</summary>
    CriticalEncounter? FindRegistrable();

    /// <summary>Blood only: the player has already offered ciphers this registration.</summary>
    bool HasRightOfEntry();

    /// <summary>Whether this tower can still be entered from the pad in its current state.</summary>
    bool CanStillEnter(CriticalEncounter tower);
}

public sealed class ForkedTowerRegistration(
    ForkedTowerConfig config,
    IAutomatorContext context,
    ICriticalEncounterRepository repo,
    IZoneProvider zones,
    IAutomatorMemory memory,
    IObjectTable objects
) : IForkedTowerRegistration
{
    public bool IsEnabled => config.AutoRegisterInIllegalMode && context.IsIllegalMode;

    public CriticalEncounter? FindRegistrable()
    {
        if (!IsEnabled)
        {
            return null;
        }

        IZone zone = zones.GetZone();
        if (zone.ForkedTowerEntrance == null || zone.IsInForkedTower())
        {
            return null;
        }

        if (repo.TryGetForkedTower() is not { } tower || !tower.IsPreparing() || !CanStillEnter(tower))
        {
            return null;
        }

        if (IllegalModeActivityWork.TakeActiveUnreachable(memory)?.MatchesForkedTower(tower.Id) == true)
        {
            return null;
        }

        return tower;
    }

    public bool HasRightOfEntry() =>
        objects.LocalPlayer?.StatusList.Has(PlayerStatuses.ForkedTowerRightOfEntry) == true;

    public bool CanStillEnter(CriticalEncounter tower)
    {
        if (zones.GetZone().ZoneId != ZoneId.SouthHorn || HasRightOfEntry())
        {
            return true;
        }

        // Blood needs ciphers offered while registration is open; without them there is no way in.
        return tower.State == DynamicEventState.Register
               && InventoryItemAssist.Count(OccultCurrencies.SouthHornCipherItemId) > 0;
    }
}

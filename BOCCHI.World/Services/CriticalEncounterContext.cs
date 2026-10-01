using BOCCHI.Common.Data.CriticalEncounters;
using BOCCHI.Common.Services;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using ECommons.GameFunctions;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using Ocelot.Extensions;

namespace BOCCHI.CriticalEncounters.Services;

public class CriticalEncounterContext
(
    IObjectTable objects,
    ICriticalEncounterRepository repository
) : ICriticalEncounterContext
{
    public bool IsInCriticalEncounter() => GetCriticalEncounterId() != null;

    public bool IsRegisteredOrInCriticalEncounter() => TryGetParticipatingCriticalEncounterId(out _);

    public unsafe CriticalEncounterId? GetCriticalEncounterId()
    {
        if (!TryGetParticipatingCriticalEncounterId(out CriticalEncounterId id, battleOnly: true))
        {
            return null;
        }

        return id;
    }

    private unsafe bool TryGetParticipatingCriticalEncounterId(
        out CriticalEncounterId id,
        bool battleOnly = false)
    {
        id = default;
        IPlayerCharacter? player = objects.LocalPlayer;
        if (player == null)
        {
            return false;
        }

        ushort entryId = player.BattleChara()->EventId.EntryId;
        if (entryId == 0)
        {
            return false;
        }

        id = new CriticalEncounterId(entryId);
        CriticalEncounterId matched = id;
        CriticalEncounter? ce = repository.SnapshotWithoutForkedTower().FirstOrDefault(c => c.Id == matched);
        if (ce == null)
        {
            return false;
        }

        if (battleOnly)
        {
            return ce.IsActive();
        }

        return ce.IsPreparing() || ce.IsActive();
    }

    public IEnumerable<IBattleNpc> GetTargets()
    {
        CriticalEncounterId? id = GetCriticalEncounterId();
        return id == null ? [] : GetTargetsFor(id.Value);
    }

    public IEnumerable<IBattleNpc> GetTargetsFor(CriticalEncounterId id)
    {
        IPlayerCharacter? player = objects.LocalPlayer;
        if (player == null)
        {
            return [];
        }

        return EnumerateEncounterEnemies(id).OrderBy(o => o.Position.Distance2D(player.Position));
    }

    public bool HasEncounterEnemies(CriticalEncounterId id) => EnumerateEncounterEnemies(id).Any();

    private unsafe IEnumerable<IBattleNpc> EnumerateEncounterEnemies(CriticalEncounterId id)
    {
        ushort ceId = id.Value;

        return objects.OfType<IBattleNpc>()
            .Where(obj => obj is { IsDead: false, IsTargetable: true })
            .Where(o => o.IsHostile())
            .Where(o =>
            {
                BattleChara* battleChara = (BattleChara*)o.Address;

                return o.SubKind == (byte)BattleNpcSubKind.Combatant && battleChara->EventId.EntryId == ceId;
            });
    }
}

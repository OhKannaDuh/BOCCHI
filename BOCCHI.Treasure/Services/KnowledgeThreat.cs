using System.Numerics;
using BOCCHI.Common.Data.Mobs;
using BOCCHI.Common.Services;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using ECommons.GameFunctions;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Ocelot.Extensions;

namespace BOCCHI.Treasure.Services;

/// <summary>Live foray knowledge threats that warrant Ninja Hide.</summary>
public static class KnowledgeThreat
{
    public const uint OccultIsleblazerBaseId = 17900;

    public const float IsleblazerUnhideDistance = 5f;

    /// <summary>Crescent Haunt — sees through Hide; do not arm Hide for it (Mocha / Godfrey, 4.2.0.11).</summary>
    public static readonly uint CrescentHauntNameId = (uint)Mob.Haunt;

    /// <summary>Mounted Hide starts this much earlier so we can dismount first.</summary>
    public const float MountedThreatEnterBonus = 5f;

    /// <summary>
    ///     <see cref="PlayerState.GetContentValue"/> key — Occult Crescent effective (synced) Knowledge.
    /// </summary>
    public const uint ContentValueEffectiveKnowledge = 6;

    /// <summary>
    ///     <see cref="PlayerState.GetContentValue"/> key — Occult Crescent current (actual) Knowledge.
    /// </summary>
    public const uint ContentValueCurrentKnowledge = 7;

    /// <summary>
    ///     Player Knowledge used for Hide thresholds. Prefer actual Knowledge (content value 7), not
    ///     South Horn sync / <c>ForayInfo.Level</c> — enemies ignore sync for aggro (#197).
    /// </summary>
    public static unsafe int? TryGetPlayerForayLevel(IObjectTable objects)
    {
        PlayerState* playerState = PlayerState.Instance();
        if (playerState != null)
        {
            uint current = playerState->GetContentValue(ContentValueCurrentKnowledge);
            if (current > 0)
            {
                return (int)current;
            }
        }

        // Inside Occult Crescent, ForayInfo.Level follows zone sync (South Horn) — using it
        // makes Hide fire on mobs that will not aggro your real Knowledge (#197).
        if (OccultCrescentHelper.IsStateAvailable())
        {
            return null;
        }

        if (objects.LocalPlayer is not { } player)
        {
            return null;
        }

        byte level = ((BattleChara*)player.Address)->ForayInfo.Level;
        return level > 0 ? level : null;
    }

    public static unsafe bool TryFindThreat(
        IObjectTable objects,
        Vector3 origin,
        int hideAtOrAbove,
        float radius,
        out IBattleNpc? threat,
        out float distance)
    {
        threat = null;
        distance = float.MaxValue;

        foreach (IGameObject obj in objects)
        {
            if (obj is not IBattleNpc battle
                || battle is { IsDead: true }
                || !battle.IsTargetable
                || !battle.IsHostile())
            {
                continue;
            }

            // Isleblazer: proximity / unhide handled separately — not a Hide target.
            // Haunt: sees through Hide — walking stealthed into them is worse than running past.
            if (battle.BaseId == OccultIsleblazerBaseId
                || battle.NameId == CrescentHauntNameId)
            {
                continue;
            }

            byte knowledge = ((BattleChara*)battle.Address)->ForayInfo.Level;
            if (knowledge < hideAtOrAbove)
            {
                continue;
            }

            float dist = origin.Distance2D(battle.Position);
            if (dist > radius || dist >= distance)
            {
                continue;
            }

            threat = battle;
            distance = dist;
        }

        return threat != null;
    }

    public static bool TryFindIsleblazer(IObjectTable objects, Vector3 origin, float radius, out float distance)
    {
        distance = float.MaxValue;
        bool found = false;

        foreach (IGameObject obj in objects)
        {
            if (obj is not IBattleNpc battle
                || battle.BaseId != OccultIsleblazerBaseId
                || battle.IsDead
                || !battle.IsTargetable)
            {
                continue;
            }

            float dist = origin.Distance2D(battle.Position);
            if (dist > radius || dist >= distance)
            {
                continue;
            }

            distance = dist;
            found = true;
        }

        return found;
    }

    /// <summary>
    ///     Mob Knowledge must be ≥ player Knowledge + offset (mobs may exceed the player Knowledge
    ///     cap). Do not clamp the sum to the player cap — that made offset 6 at Knowledge 40 still
    ///     hide from every 40+ enemy.
    /// </summary>
    public static int HideAtOrAbove(int playerForayLevel, int hideOffset) =>
        Math.Max(1, playerForayLevel + hideOffset);
}

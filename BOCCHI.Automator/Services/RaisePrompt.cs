using BOCCHI.Common.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace BOCCHI.Automator.Services;

/// <summary>
///     The raise offer shares SelectYesno with the return-to-camp death prompt. AgentRevive only
///     carries a caster while a raise is pending, and the raise prompt names that caster — the
///     camp prompt never does — so both must agree before we press Yes.
/// </summary>
public static unsafe class RaisePrompt
{
    /// <summary>
    ///     AgentRevive has a pending raise. Only reads the agent, so it stays stable while the
    ///     prompt's text node catches up; use it to time the offer, not to decide to press Yes.
    /// </summary>
    public static bool TryGetPending(out string caster, out int timeLeft)
    {
        caster = string.Empty;
        timeLeft = 0;
        AgentRevive* agent = AgentRevive.Instance();
        if (agent == null || agent->ResurrectingPlayerId == 0 || agent->ResurrectionTimeLeft <= 0)
        {
            return false;
        }

        caster = agent->ResurrectingPlayerName.ToString().Trim();
        timeLeft = agent->ResurrectionTimeLeft;
        return caster.Length > 0;
    }

    public static bool TryAccept(out string caster)
    {
        if (!TryGetPending(out caster, out _) || !AddonHelpers.TryGetSelectYesno(out AddonSelectYesno* yesno))
        {
            return false;
        }

        string prompt = yesno->PromptText == null
            ? string.Empty
            : yesno->PromptText->NodeText.ToString();
        if (!prompt.Contains(caster, StringComparison.Ordinal))
        {
            return false;
        }

        ((AtkUnitBase*)yesno)->FireCallbackInt(0);
        return true;
    }
}

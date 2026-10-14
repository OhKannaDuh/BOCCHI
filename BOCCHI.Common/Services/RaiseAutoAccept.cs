using BOCCHI.Common.Config;
using BOCCHI.Common.Data.Zones;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using ECommons.Automation;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.UI;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using Lumina.Text.ReadOnly;
using Ocelot.Lifecycle;
using Ocelot.Services.Logger;

namespace BOCCHI.Common.Services;

/// <summary>
/// Accepts another player's raise while KO'd in Occult Crescent, then optionally thanks them in /say once revived.
/// </summary>
public sealed unsafe class RaiseAutoAccept(
    RaiseConfig config,
    IZoneProvider zones,
    ICondition conditions,
    IDataManager data,
    ILogger<RaiseAutoAccept> logger
) : IOnUpdate
{
    // Addon sheet rows a raise SelectYesno can show, in the client language.
    private static readonly uint[] RaisePromptTextRows =
    [
        3787, // "Accept Raise from <string(lstr1)>?"
        113, // "Accept Rebirth from <string(lstr1)>?"
        112, // "Accept Raise?"
    ];

    private const int AcceptThrottleMs = 1000;

    private static readonly UpdateLimit Limit = new()
    {
        Mode = UpdateLimitMode.Milliseconds,
        Limit = 250,
    };

    private string[]? raisePromptTemplates;

    private bool thankOnRevive;

    public UpdateLimit UpdateLimit => Limit;

    public void Update()
    {
        if (!config.AutoAcceptRaise || !zones.GetZone().IsOccultCrescentZone())
        {
            thankOnRevive = false;
            return;
        }

        if (conditions[ConditionFlag.Unconscious])
        {
            TryAcceptRaise();
            return;
        }

        if (thankOnRevive)
        {
            thankOnRevive = false;
            SayThanks();
        }
    }

    private void TryAcceptRaise()
    {
        if (!AddonHelpers.TryGetSelectYesno(out AddonSelectYesno* yesno) || yesno->PromptText == null)
        {
            return;
        }

        string prompt = new ReadOnlySeStringSpan(yesno->PromptText->NodeText.AsSpan()).ExtractText();
        if (!IsRaisePrompt(prompt) || !EzThrottler.Throttle("RaiseAutoAccept::Accept", AcceptThrottleMs))
        {
            return;
        }

        logger.Info("Raise offered — accepting");
        yesno->AtkUnitBase.FireCallbackInt(0);
        thankOnRevive = config.SayThanksAfterRaise;
    }

    private bool IsRaisePrompt(string prompt)
    {
        raisePromptTemplates ??= LoadRaisePromptTemplates();
        foreach (string template in raisePromptTemplates)
        {
            if (SelectYesnoPrompt.Matches(prompt, template))
            {
                return true;
            }
        }

        return false;
    }

    private string[] LoadRaisePromptTemplates()
    {
        ExcelSheet<Addon> sheet = data.GetExcelSheet<Addon>();
        List<string> templates = [];
        foreach (uint row in RaisePromptTextRows)
        {
            string text = sheet.GetRowOrDefault(row)?.Text.ExtractText().Trim() ?? string.Empty;
            if (text.Length > 0)
            {
                templates.Add(text);
            }
        }

        return templates.ToArray();
    }

    private void SayThanks()
    {
        if (!config.SayThanksAfterRaise)
        {
            return;
        }

        try
        {
            string message = Chat.SanitiseText(config.ThanksMessage ?? string.Empty).Trim();
            if (message.Length == 0)
            {
                return;
            }

            Chat.SendMessage($"/s {message}");
        }
        catch (Exception ex)
        {
            logger.Warn(ex, "Could not send the thank-you message after being raised");
        }
    }
}

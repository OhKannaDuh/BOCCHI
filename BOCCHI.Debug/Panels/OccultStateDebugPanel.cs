using System.Globalization;
using System.Numerics;
using BOCCHI.Common.Data.Aethernet;
using BOCCHI.Common.Data.OccultCrescent;
using BOCCHI.Common.Data.SupportJobs;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Services;
using BOCCHI.Treasure.Services;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.STD;
using Lumina.Excel.Sheets;
using Ocelot.Extensions;
using Ocelot.Services.UI;
using CsCharacter = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;

namespace BOCCHI.Debug.Panels;

public sealed unsafe class OccultStateDebugPanel(
    IObjectTable objects,
    IDataManager data,
    IZoneProvider zones,
    ISupportJobFactory supportJobs,
    IBrandingService branding,
    IUIService ui
) : IDebugPanel
{
    private const int UnkPairsPtrOffset = 0x31E0;

    private const int UnkCountdownOffset = 0x31E8;

    private const int UnkPairCountOffset = 0x31EC;

    private const int StateUnk93Offset = 0x93;

    private const float ChainScanRadius = 80f;

    public string Name => "Occult State";

    public void Render()
    {
        PublicContentOccultCrescent* content = PublicContentOccultCrescent.GetInstance();
        if (content == null)
        {
            ui.Text("PublicContentOccultCrescent unavailable.");
            return;
        }

        OccultCrescentState* state = PublicContentOccultCrescent.GetState();
        ui.LabelledValue("StateLoaded", content->StateLoaded);
        ui.LabelledValue("ContentTimeLeft", content->ContentTimeLeft.ToString("0.0", CultureInfo.InvariantCulture));
        ui.LabelledValue("CurrentEvent", $"{content->DynamicEventContainer.CurrentEventId} idx={content->DynamicEventContainer.CurrentEventIndex}");

        RenderKnowledge(state);
        RenderCurrency(state);
        RenderResolvedExcel();
        RenderMkdDataCs();
        RenderUnkPairs(content);
        RenderStrings(content);
        RenderChainTargets();
        RenderTeleportBits(state);
        RenderAgents();
        RenderExcelSheets();
    }

    private void RenderKnowledge(OccultCrescentState* state)
    {
        if (!ImGui.CollapsingHeader("Knowledge", ImGuiTreeNodeFlags.DefaultOpen))
        {
            return;
        }

        int? foray = KnowledgeThreat.TryGetPlayerForayLevel(objects);
        ui.LabelledValue("Hide uses Knowledge (actual)", foray?.ToString(CultureInfo.InvariantCulture) ?? "(none)");
        PlayerState* playerState = PlayerState.Instance();
        if (playerState != null)
        {
            ui.LabelledValue(
                "ContentValue current (7)",
                playerState->GetContentValue(KnowledgeThreat.ContentValueCurrentKnowledge)
                    .ToString(CultureInfo.InvariantCulture));
            ui.LabelledValue(
                "ContentValue effective/sync (6)",
                playerState->GetContentValue(KnowledgeThreat.ContentValueEffectiveKnowledge)
                    .ToString(CultureInfo.InvariantCulture));
        }

        if (objects.LocalPlayer is { } local)
        {
            byte synced = ((BattleChara*)local.Address)->ForayInfo.Level;
            ui.LabelledValue("ForayInfo.Level (synced)", synced.ToString(CultureInfo.InvariantCulture));
        }

        if (state == null)
        {
            ui.Text("OccultCrescentState null.");
            return;
        }

        ui.LabelledValue("KnowledgeLevelSync", state->KnowledgeLevelSync);
        ui.LabelledValue("CurrentKnowledge (XP)", state->CurrentKnowledge);
        ui.LabelledValue("NeededKnowledge", state->NeededKnowledge);
        ui.LabelledValue("NeededJobExperience", state->NeededJobExperience);
        ui.LabelledValue("CurrentSupportJob", state->CurrentSupportJob);
        if (supportJobs.TryGetCurrent(out SupportJob job))
        {
            ui.LabelledValue("Job name", job.Data.Name.ToString());
        }

        byte* raw = (byte*)state;
        ui.LabelledValue("Unk93–97", $"{raw[StateUnk93Offset]} {raw[StateUnk93Offset + 1]} {raw[StateUnk93Offset + 2]} {raw[StateUnk93Offset + 3]} {raw[StateUnk93Offset + 4]}");
        ui.Text("Unk94/95 are annotated as Sanguine Cipher cur/max.", branding.DalamudGrey);
    }

    private void RenderCurrency(OccultCrescentState* state)
    {
        if (!ImGui.CollapsingHeader("Currency", ImGuiTreeNodeFlags.DefaultOpen))
        {
            return;
        }

        ui.LabelledValue("State Silver/Gold", state == null ? "(null)" : $"{state->Silver} / {state->Gold}");
        ui.LabelledValue(
            "Inv pieces",
            $"{OccultCrescentHelper.GetSilverPieces()} / {OccultCrescentHelper.GetGoldPieces()}");
        ui.LabelledValue(
            "Inv total (pieces+obols)",
            $"{OccultCrescentHelper.GetSilverTotal()} / {OccultCrescentHelper.GetGoldTotal()}");

        InventoryManager* inventory = InventoryManager.Instance();
        int sanguine = inventory == null
            ? -1
            : inventory->GetInventoryItemCount(OccultCurrencies.SouthHornCipherItemId);
        int amulet = inventory == null
            ? -1
            : inventory->GetInventoryItemCount(OccultCurrencies.NorthHornCipherItemId);
        ui.LabelledValue("Inv Sanguine Cipher", $"{sanguine}  id={OccultCurrencies.SouthHornCipherItemId}");
        ui.LabelledValue("Inv Arcane Amulet", $"{amulet}  id={OccultCurrencies.NorthHornCipherItemId}");
        if (state != null)
        {
            byte* raw = (byte*)state;
            ui.LabelledValue("State Unk94/Unk95 (cipher?)", $"{raw[StateUnk93Offset + 1]} / {raw[StateUnk93Offset + 2]}");
        }
    }

    private void RenderResolvedExcel()
    {
        if (!ImGui.CollapsingHeader("Resolved excel (live ids)"))
        {
            return;
        }

        ui.LabelledValue("BattleBell", PhantomActions.BattleBell);
        ui.LabelledValue("RingingRespite", PhantomActions.RingingRespite);
        ui.LabelledValue("Revive", PhantomActions.Revive);
        ui.LabelledValue("OccultRaise", PhantomActions.OccultRaise);
        ui.LabelledValue("OccultSprint", PhantomActions.OccultSprint);
        ui.LabelledValue("OccultTreasuresight", $"{PhantomActions.OccultTreasuresight}  lv{PhantomActions.TreasuresightUnlockLevel}");
        ui.LabelledValue("InquiringMind", $"{PhantomActions.InquiringMind}  lv{PhantomActions.InquiringMindUnlock}");
        ui.LabelledValue("Quickstep", $"{PhantomActions.Quickstep}  lv{PhantomActions.QuickstepUnlock}");
        ui.LabelledValue("BattleBell status", $"{PhantomBuffs.BattleBell} / clangor {PhantomBuffs.BattlesClangor}");
        ui.LabelledValue("QuickerStep status", PhantomBuffs.QuickerStep);
        if (supportJobs.TryGetCurrent(out SupportJob current))
        {
            ui.LabelledValue("Current job status", current.StatusId);
        }
        ui.LabelledValue("Silver/Gold pieces", $"{OccultCurrencies.SilverPieceItemId} / {OccultCurrencies.GoldPieceItemId}");
        ui.LabelledValue("Silver/Gold obols", $"{OccultCurrencies.SilverObolItemId} / {OccultCurrencies.GoldObolItemId}");
        ui.LabelledValue("Ciphers", $"{OccultCurrencies.SouthHornCipherItemId} / {OccultCurrencies.NorthHornCipherItemId}");
    }

    private void RenderMkdDataCs()
    {
        if (!ImGui.CollapsingHeader("CS GetMKDData (struct marked stale)"))
        {
            return;
        }

        OccultCrescentMKDData* mkd = PublicContentOccultCrescent.GetMKDData();
        if (mkd == null)
        {
            ui.Text("GetMKDData null.");
            return;
        }

        ui.LabelledValue("QuestId", mkd->QuestId);
        ui.LabelledValue("ZoneNameId (Addon)", mkd->ZoneNameId);
        for (var i = 0; i < mkd->CurrencyItemIds.Length; i++)
        {
            ui.LabelledValue($"CurrencyItemIds[{i}]", mkd->CurrencyItemIds[i]);
        }

        for (var i = 0; i < mkd->CurrencyNameIds.Length; i++)
        {
            ui.LabelledValue($"CurrencyNameIds[{i}]", mkd->CurrencyNameIds[i]);
        }

        byte* raw = (byte*)mkd;
        ui.LabelledValue("Unknown8/9", $"{raw[0x20]} / {raw[0x21]}");
    }

    private void RenderUnkPairs(PublicContentOccultCrescent* content)
    {
        if (!ImGui.CollapsingHeader("Unk pairs 0x31E0 (quest/collision?)"))
        {
            return;
        }

        byte* raw = (byte*)content;
        float countdown = *(float*)(raw + UnkCountdownOffset);
        byte count = raw[UnkPairCountOffset];
        ui.LabelledValue("Countdown", countdown.ToString("0.00", CultureInfo.InvariantCulture));
        ui.LabelledValue("Count", count);

        StdPair<uint, uint>* pairs = *(StdPair<uint, uint>**)(raw + UnkPairsPtrOffset);
        if (pairs == null)
        {
            ui.Text("Pair pointer null.");
            return;
        }

        int n = Math.Clamp((int)count, 0, 6);
        for (var i = 0; i < n; i++)
        {
            ui.LabelledValue($"[{i}]", $"{pairs[i].Item1} / {pairs[i].Item2}");
        }
    }

    private void RenderStrings(PublicContentOccultCrescent* content)
    {
        if (!ImGui.CollapsingHeader("Director strings"))
        {
            return;
        }

        for (var i = 0; i < content->Strings.Length; i++)
        {
            string text = content->Strings[i].ToString();
            ui.LabelledValue($"[{i}]", string.IsNullOrEmpty(text) ? "(empty)" : text);
        }
    }

    private void RenderChainTargets()
    {
        if (!ImGui.CollapsingHeader("IsChainTarget", ImGuiTreeNodeFlags.DefaultOpen))
        {
            return;
        }

        if (objects.LocalPlayer is not { } player)
        {
            ui.Text("No local player.");
            return;
        }

        Vector3 origin = player.Position;
        var hits = 0;
        foreach (IGameObject obj in objects)
        {
            if (obj is not IBattleNpc battle || !battle.IsValid() || battle.Address == nint.Zero)
            {
                continue;
            }

            float dist = origin.Distance2D(battle.Position);
            if (dist > ChainScanRadius)
            {
                continue;
            }

            var chara = (CsCharacter*)battle.Address;
            if (!PublicContentOccultCrescent.IsChainTarget(chara))
            {
                continue;
            }

            hits++;
            byte foray = ((BattleChara*)battle.Address)->ForayInfo.Level;
            ui.Text(
                $"{battle.Name}  NameId={battle.NameId}  BaseId={battle.BaseId}  foray={foray}  {dist.ToString("0.0", CultureInfo.InvariantCulture)}y",
                branding.DalamudYellow);
        }

        if (hits == 0)
        {
            ui.Text($"None in {ChainScanRadius.ToString("0", CultureInfo.InvariantCulture)}y.", branding.DalamudGrey);
        }
    }

    private void RenderTeleportBits(OccultCrescentState* state)
    {
        if (!ImGui.CollapsingHeader("Teleport bitmask vs helper"))
        {
            return;
        }

        if (state == null)
        {
            ui.Text("State null.");
            return;
        }

        Span<byte> mask = state->UnlockedTeleportBitmask;
        ui.LabelledValue("Bytes", $"{mask[0]:X2} {mask[1]:X2} {mask[2]:X2}");

        var bits = new char[24];
        for (var i = 0; i < 24; i++)
        {
            int byteIndex = i / 8;
            int bit = i % 8;
            bits[i] = (mask[byteIndex] & (1 << bit)) != 0 ? '1' : '0';
        }

        ui.LabelledValue("Bits 0–23", new string(bits));

        IZone zone = zones.GetZone();
        List<AethernetData> pads = zone.GetAetherytes();
        for (var i = 0; i < pads.Count; i++)
        {
            AethernetData pad = pads[i];
            bool helper = OccultCrescentHelper.IsAethernetUnlocked(pad.Id);
            bool bit = i < 24 && bits[i] == '1';
            string name = PlaceName(pad.Id);
            ui.LabelledValue(
                $"[{i}] {pad.Id} {name}",
                $"helper={helper}  bit[i]={bit}");
        }
    }

    private void RenderAgents()
    {
        if (!ImGui.CollapsingHeader("Agents / lore module"))
        {
            return;
        }

        AgentModule* agents = AgentModule.Instance();
        if (agents == null)
        {
            ui.Text("AgentModule null.");
            return;
        }

        var info = (AgentMKDInfo*)agents->GetAgentByInternalId(AgentId.MKDInfo);
        ui.LabelledValue("MKDInfo.QuestComplete", info == null ? "(null)" : info->QuestComplete.ToString());

        var jobAgent = (AgentMKDSupportJob*)agents->GetAgentByInternalId(AgentId.MKDSupportJob);
        if (jobAgent == null)
        {
            ui.LabelledValue("MKDSupportJob", "(null)");
        }
        else
        {
            ui.LabelledValue(
                "MKDSupportJob",
                $"job={jobAgent->CurrentJob}  defaultAction={jobAgent->DefaultAction}  hiddenFlags={jobAgent->ActionHiddenFlags}");
        }

        MKDLoreModule* lore = MKDLoreModule.Instance();
        if (lore == null)
        {
            ui.LabelledValue("SeenLore", "(null)");
            return;
        }

        int seen = lore->SeenLore.Count;
        ui.LabelledValue("SeenLore count", seen);
        int preview = Math.Min(seen, 16);
        if (preview <= 0 || lore->SeenLore.First == null)
        {
            return;
        }

        var hex = new System.Text.StringBuilder(preview * 3);
        for (var i = 0; i < preview; i++)
        {
            if (i > 0)
            {
                hex.Append(' ');
            }

            hex.Append(lore->SeenLore.First[i].ToString("X2", CultureInfo.InvariantCulture));
        }

        ui.LabelledValue("SeenLore first bytes", hex.ToString());
    }

    private void RenderExcelSheets()
    {
        if (!ImGui.CollapsingHeader("EXD MKDData"))
        {
            return;
        }

        foreach (MKDData row in data.GetExcelSheet<MKDData>())
        {
            uint silver = row.CurrencyItem.Count > 0 ? row.CurrencyItem[0].RowId : 0;
            if (silver == 0)
            {
                continue;
            }

            uint gold = row.CurrencyItem.Count > 1 ? row.CurrencyItem[1].RowId : 0;
            ui.Text(
                $"row {row.RowId}  zoneAddon={row.ZoneName.RowId}  {ItemName(silver)}/{ItemName(gold)}  cipher={ItemName(row.CipherItem.RowId)}  quest={row.Quest.RowId}",
                branding.DalamudGrey);
        }

        if (ImGui.CollapsingHeader("EXD MKDChain"))
        {
            var n = 0;
            foreach (MKDChain row in data.GetExcelSheet<MKDChain>())
            {
                if (n >= 40)
                {
                    ui.Text("…truncated", branding.DalamudGrey);
                    break;
                }

                if (row.Unknown0 == 0 && row.Unknown1 == 0)
                {
                    continue;
                }

                ui.LabelledValue($"row {row.RowId}", $"{row.Unknown0} / {row.Unknown1}");
                n++;
            }

            if (n == 0)
            {
                ui.Text("All rows Unknown0/1 are 0.", branding.DalamudGrey);
            }
        }

        if (ImGui.CollapsingHeader("EXD MKDBNpcData (nearby)"))
        {
            RenderNearbyBNpcData();
        }

        if (ImGui.CollapsingHeader("EXD MKDTrait (current job)"))
        {
            byte jobId = supportJobs.TryGetCurrent(out SupportJob current) ? (byte)current.Id : (byte)255;
            var n = 0;
            foreach (MKDTrait trait in data.GetExcelSheet<MKDTrait>())
            {
                if (jobId != 255 && trait.MKDSupportJob.RowId != jobId)
                {
                    continue;
                }

                ui.LabelledValue($"{trait.RowId} lv{trait.LevelUnlock}", trait.Name.ToString());
                n++;
            }

            if (n == 0)
            {
                ui.Text("No traits for current job.", branding.DalamudGrey);
            }
        }
    }

    private void RenderNearbyBNpcData()
    {
        if (objects.LocalPlayer is not { } player)
        {
            return;
        }

        var sheet = data.GetExcelSheet<MKDBNpcData>();
        Vector3 origin = player.Position;
        var hits = 0;
        foreach (IGameObject obj in objects)
        {
            if (obj is not IBattleNpc battle || !battle.IsValid())
            {
                continue;
            }

            float dist = origin.Distance2D(battle.Position);
            if (dist > 40f)
            {
                continue;
            }

            bool byName = sheet.TryGetRow(battle.NameId, out MKDBNpcData nameRow);
            bool byBase = sheet.TryGetRow(battle.BaseId, out MKDBNpcData baseRow);
            if (!byName && !byBase)
            {
                continue;
            }

            hits++;
            string extra = byName
                ? $"NameId Unknown0={nameRow.Unknown0}"
                : $"BaseId Unknown0={baseRow.Unknown0}";
            ui.Text(
                $"{battle.Name}  NameId={battle.NameId}  BaseId={battle.BaseId}  {extra}  {dist.ToString("0.0", CultureInfo.InvariantCulture)}y");
        }

        if (hits == 0)
        {
            ui.Text("No nearby battle NPCs match MKDBNpcData by NameId or BaseId.", branding.DalamudGrey);
        }
    }

    private string PlaceName(uint id)
    {
        if (data.GetExcelSheet<PlaceName>().TryGetRow(id, out PlaceName row))
        {
            string name = row.Name.ToString();
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }
        }

        return "?";
    }

    private string ItemName(uint id)
    {
        if (id == 0)
        {
            return "0";
        }

        if (data.GetExcelSheet<Item>().TryGetRow(id, out Item row))
        {
            string name = row.Name.ToString();
            if (!string.IsNullOrWhiteSpace(name))
            {
                return $"{id} {name}";
            }
        }

        return id.ToString(CultureInfo.InvariantCulture);
    }
}

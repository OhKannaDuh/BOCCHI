using System.Numerics;
using System.Text.RegularExpressions;
using BOCCHI.Common.Config;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Services;
using BOCCHI.Treasure.Data;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Chat;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Ocelot.Extensions;
using Ocelot.Lifecycle;
using Ocelot.Services.PlayerState;

namespace BOCCHI.Treasure.Services;

public class TreasureTracker : ITreasureTracker, IOnUpdate, IDisposable
{
    private const uint ActiveChestLogMessageId = 10965;

    private const uint NoActiveChestsLogMessageId = 10966;

    private readonly IAddonLifecycle addonLifecycle;
    private readonly IChatGui chat;
    private readonly IDataManager data;
    private readonly IObjectTable objects;
    private readonly TimeSpan parseWideTextCooldown = TimeSpan.FromSeconds(5);
    private readonly IPlayer player;
    private readonly IZoneProvider zones;
    private readonly CofferLocationSyncService cofferLocations;
    private readonly TreasureConfig config;
    private readonly Func<ITreasureHunter> hunter;
    private readonly Func<ICarrotHunter> carrotHunter;

    private DateTime lastParseWideText = DateTime.MinValue;
    private List<TreasureCoffer> treasures = [];

    public UpdateLimit UpdateLimit =>
        new()
        {
            Mode = UpdateLimitMode.Milliseconds,
            Limit = 250
        };

    public TreasureTracker(
        IObjectTable objects,
        IAddonLifecycle addonLifecycle,
        IChatGui chat,
        IDataManager data,
        IZoneProvider zones,
        IPlayer player,
        CofferLocationSyncService cofferLocations,
        TreasureConfig config,
        Func<ITreasureHunter> hunter,
        Func<ICarrotHunter> carrotHunter
    )
    {
        this.objects = objects;
        this.addonLifecycle = addonLifecycle;
        this.chat = chat;
        this.data = data;
        this.zones = zones;
        this.player = player;
        this.cofferLocations = cofferLocations;
        this.config = config;
        this.hunter = hunter;
        this.carrotHunter = carrotHunter;
        addonLifecycle.RegisterListener(AddonEvent.PostDraw, "_WideText", OnWideTextPostDraw);
        chat.LogMessage += OnChatLogMessage;
    }

    public void Dispose()
    {
        addonLifecycle.UnregisterListener(AddonEvent.PostDraw, "_WideText", OnWideTextPostDraw);
        chat.LogMessage -= OnChatLogMessage;
    }

    public void Update()
    {
        if (!zones.GetZone().IsOccultCrescentZone())
        {
            if (treasures.Count > 0)
            {
                treasures.Clear();
            }

            ClearSightCounts();
            return;
        }

        if (!NeedsLiveCofferScan())
        {
            if (treasures.Count > 0)
            {
                treasures.Clear();
            }

            return;
        }

        // Key by GameObjectId — BaseId is shared by every bronze/silver of that type,
        // so a dictionary on BaseId kept only one coffer and dropped the rest (no radar line).
        Dictionary<ulong, IGameObject> worldTreasures = objects
            .Where(o => o is { ObjectKind: ObjectKind.Treasure, IsDead: false } && o.IsValid())
            .GroupBy(o => o.GameObjectId)
            .ToDictionary(g => g.Key, g => g.First());

        foreach (TreasureCoffer treasure in treasures)
        {
            if (!treasure.CheckOpened())
            {
                continue;
            }

            CofferType cofferType = treasure.GetCofferType();
            if (cofferType is CofferType.Bronze or CofferType.Silver)
            {
                Vector3 position = treasure.GetPosition();
                cofferLocations.Submit(
                    treasure.Id,
                    position.X,
                    position.Y,
                    position.Z,
                    cofferType.ToString());
            }

            if (CountInitialised)
            {
                if (cofferType == CofferType.Bronze)
                {
                    BronzeChests = Math.Max(0, BronzeChests - 1);
                }
                else if (cofferType == CofferType.Silver)
                {
                    SilverChests = Math.Max(0, SilverChests - 1);
                }
            }
        }

        HashSet<ulong> knownIds = treasures.Select(t => t.GameObjectId).ToHashSet();

        for (int i = treasures.Count - 1; i >= 0; i--)
        {
            TreasureCoffer treasure = treasures[i];
            if (!worldTreasures.ContainsKey(treasure.GameObjectId) || !treasure.IsValid())
            {
                treasures.RemoveAt(i);
            }
        }

        foreach ((ulong objectId, IGameObject obj) in worldTreasures)
        {
            if (knownIds.Contains(objectId))
            {
                continue;
            }

            TreasureCoffer treasure = new(obj, data);
            if (treasure.IsValid())
            {
                treasures.Add(treasure);
            }
        }

        treasures = treasures.OrderBy(t => player.Position.Distance(t.GetPosition())).ToList();
    }

    public IReadOnlyList<TreasureCoffer> Treasures => treasures;

    public bool CountInitialised { get; private set; }

    public DateTime LastCountUpdateUtc { get; private set; } = DateTime.MinValue;

    public int BronzeChests { get; private set; }

    public int SilverChests { get; private set; }

    public int SurveyRevision { get; private set; }

    private void OnChatLogMessage(ILogMessage message)
    {
        if (!zones.GetZone().IsOccultCrescentZone())
        {
            return;
        }

        if (message.LogMessageId == NoActiveChestsLogMessageId)
        {
            ApplySightCounts(0, 0);
            return;
        }

        if (message.LogMessageId != ActiveChestLogMessageId)
        {
            return;
        }

        if (!message.TryGetIntParameter(0, out int silver)
            || !message.TryGetIntParameter(1, out int bronze))
        {
            return;
        }

        ApplySightCounts(silver, bronze);
    }

    private unsafe void OnWideTextPostDraw(AddonEvent type, AddonArgs args)
    {
        if (!zones.GetZone().IsOccultCrescentZone())
        {
            return;
        }

        AtkUnitBase* addon = (AtkUnitBase*)args.Addon.Address;
        if (!addon->IsVisible)
        {
            return;
        }

        if (DateTime.Now - lastParseWideText < parseWideTextCooldown)
        {
            return;
        }

        AtkTextNode* textNode = addon->GetNodeById(3)->GetAsAtkTextNode();
        if (textNode == null)
        {
            return;
        }

        string text = textNode->NodeText.ToString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        string emptyPattern = LogMessageHelper.GetLogMessagePattern(data, NoActiveChestsLogMessageId);
        if (!string.IsNullOrEmpty(emptyPattern) && Regex.IsMatch(text, emptyPattern))
        {
            lastParseWideText = DateTime.Now;
            ApplySightCounts(0, 0);
            return;
        }

        string pattern = LogMessageHelper.GetLogMessagePattern(data, ActiveChestLogMessageId);
        Match match = Regex.Match(text, pattern);
        if (!match.Success)
        {
            return;
        }

        if (!int.TryParse(match.Groups[1].Value, out int silver)
            || !int.TryParse(match.Groups[2].Value, out int bronze))
        {
            return;
        }

        lastParseWideText = DateTime.Now;
        ApplySightCounts(silver, bronze);
    }

    private void ApplySightCounts(int silver, int bronze)
    {
        silver = Math.Clamp(silver, 0, 8);
        bronze = Math.Clamp(bronze, 0, 30);

        if (CountInitialised
            && SilverChests == silver
            && BronzeChests == bronze
            && DateTime.UtcNow - LastCountUpdateUtc < TimeSpan.FromSeconds(2))
        {
            return;
        }

        SilverChests = silver;
        BronzeChests = bronze;
        CountInitialised = true;
        LastCountUpdateUtc = DateTime.UtcNow;
        SurveyRevision++;
    }

    private void ClearSightCounts()
    {
        if (!CountInitialised && BronzeChests == 0 && SilverChests == 0)
        {
            return;
        }

        BronzeChests = 0;
        SilverChests = 0;
        CountInitialised = false;
        LastCountUpdateUtc = DateTime.MinValue;
    }

    private bool NeedsLiveCofferScan() =>
        config.DrawLineToBronzeChests
        || config.DrawLineToSilverChests
        || hunter().Running
        || carrotHunter().Running;
}

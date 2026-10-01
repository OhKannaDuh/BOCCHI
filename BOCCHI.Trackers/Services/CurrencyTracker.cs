using BOCCHI.Common.Data;
using BOCCHI.Common.Data.OccultCrescent;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Services;
using Dalamud.Game.Chat;
using Dalamud.Plugin.Services;
using Ocelot.Lifecycle;

namespace BOCCHI.Currency.Services;

public interface ICurrencyTracker
{
    double GoldPerHour { get; }

    double SilverPerHour { get; }

    float[] GetGoldHistory(TimeSpan sampleDuration);

    float[] GetSilverHistory(TimeSpan sampleDuration);
}

public class CurrencyTracker(IZoneProvider zones, IChatGui chat)
    : ICurrencyTracker, IOnUpdate, IOnTerritoryChanged, IOnStart, IOnStop
{
    private const uint ObtainedItemLogMessageId = 4592;

    private readonly DeltaRateTracker goldTracker = new(() => DeltaRateTracker.DefaultWindow);

    private readonly DeltaRateTracker silverTracker = new(() => DeltaRateTracker.DefaultWindow);

    private bool inOccultCrescent;

    public double GoldPerHour => goldTracker.PerHour;

    public double SilverPerHour => silverTracker.PerHour;

    public float[] GetGoldHistory(TimeSpan sampleDuration) => goldTracker.GetHistory(sampleDuration);

    public float[] GetSilverHistory(TimeSpan sampleDuration) => silverTracker.GetHistory(sampleDuration);

    public int Order => 0;

    public UpdateLimit UpdateLimit =>
        new()
        {
            Mode = UpdateLimitMode.Milliseconds,
            Limit = 250
        };

    public void OnStart() => chat.LogMessage += OnChatLogMessage;

    public void OnStop() => chat.LogMessage -= OnChatLogMessage;

    public void OnTerritoryChanged(uint territory) => ApplyZone(zones.GetZone().IsOccultCrescentZone());

    public void Update()
    {
        ApplyZone(zones.GetZone().IsOccultCrescentZone());
        RecordFromInventory();
    }

    private void OnChatLogMessage(ILogMessage message)
    {
        if (!inOccultCrescent
            || message.LogMessageId != ObtainedItemLogMessageId
            || !message.TryGetIntParameter(1, out int itemId)
            || !OccultCurrencies.IsTrackedCurrency((uint)itemId))
        {
            return;
        }

        RecordFromInventory();
    }

    private void RecordFromInventory()
    {
        if (!inOccultCrescent)
        {
            return;
        }

        if (!OccultCrescentHelper.IsStateAvailable())
        {
            goldTracker.SetCounting(false);
            silverTracker.SetCounting(false);
            return;
        }

        int gold = OccultCrescentHelper.GetGoldTotal();
        int silver = OccultCrescentHelper.GetSilverTotal();

        // Inventory can read 0 while bags are still loading — don't treat that as a spend.
        if ((gold == 0 && goldTracker.HasValue && goldTracker.LastValue > 0)
            || (silver == 0 && silverTracker.HasValue && silverTracker.LastValue > 0))
        {
            goldTracker.SetCounting(false);
            silverTracker.SetCounting(false);
            return;
        }

        goldTracker.SetCounting(true);
        silverTracker.SetCounting(true);

        if (AddonHelpers.IsShopExchangeOpen())
        {
            goldTracker.SyncBaseline(gold);
            silverTracker.SyncBaseline(silver);
            return;
        }

        goldTracker.RecordPositiveDelta(gold);
        silverTracker.RecordPositiveDelta(silver);
    }

    private void ApplyZone(bool inOc)
    {
        if (inOc == inOccultCrescent)
        {
            return;
        }

        inOccultCrescent = inOc;
        goldTracker.Reset();
        silverTracker.Reset();
    }
}

using BOCCHI.Common.Config;
using BOCCHI.Common.Ipc.Knightshopper;
using System.Text.RegularExpressions;

namespace BOCCHI.Services.Shopping.Backends;

/// <summary>Knightshopper's Occult Crescent list (<c>Knightshopper.Purchase.*</c>).</summary>
public sealed class KnightshopperShoppingBackend(IKnightshopperIpc knightshopper) : IShoppingBackend
{
    private Guid? operationId;

    public ShoppingBackendKind Kind => ShoppingBackendKind.Knightshopper;

    public string Name => "Knightshopper";

    public bool IsAvailable => knightshopper.IsAvailable;

    public bool HasRun => operationId is not null;

    public ShoppingReadiness CheckReady(out string reason)
    {
        if (!knightshopper.IsAvailable)
        {
            reason = "Knightshopper isn’t loaded or ready.";
            return ShoppingReadiness.NotReady;
        }

        if (knightshopper.IsBusy)
        {
            reason = "Knightshopper is already busy.";
            return ShoppingReadiness.Busy;
        }

        reason = string.Empty;
        return ShoppingReadiness.Ready;
    }

    public ShoppingStart TryStart()
    {
        if (knightshopper.IsBusy)
        {
            return new ShoppingStart(ShoppingStartKind.Retry, "Knightshopper busy");
        }

        StartResponse start = knightshopper.Start(CurrencyId.OccultCrescent);
        if (start.Started)
        {
            operationId = start.OperationId;
            return new ShoppingStart(ShoppingStartKind.Started, $"op={start.OperationId:N}");
        }

        string message = $"{start.Result} — {start.Message}";
        return start.Result is StartResult.EmptyList or StartResult.NotReady or StartResult.NotLoggedIn
            or StartResult.InvalidCurrency
            ? new ShoppingStart(ShoppingStartKind.Rejected, message)
            : new ShoppingStart(ShoppingStartKind.Retry, message);
    }

    public ShoppingPoll Poll()
    {
        if (operationId is not { } id)
        {
            return new ShoppingPoll(true, false, string.Empty);
        }

        PurchaseStatus status = knightshopper.GetStatus(id);
        bool stillActive = !status.IsFinished
            && (knightshopper.IsRunning(id)
                || status.State is PurchaseState.Running or PurchaseState.CancellationRequested);
        if (stillActive)
        {
            return new ShoppingPoll(false, false, status.Message);
        }

        operationId = null;
        return new ShoppingPoll(true, LooksLikeInsufficientFunds(status.Message), $"{status.State} — {status.Message}");
    }

    public void Cancel()
    {
        if (operationId is { } id)
        {
            knightshopper.Cancel(id);
        }

        operationId = null;
    }

    public void Forget() => operationId = null;

    public string DescribeStatus()
    {
        if (operationId is not { } id)
        {
            return $"Knightshopper available={knightshopper.IsAvailable} busy={knightshopper.IsBusy}";
        }

        PurchaseStatus status = knightshopper.GetStatus(id);
        bool running = knightshopper.IsRunning(id);
        return $"Knightshopper op={id:N} running={running} state={status.State} "
            + $"item={status.CurrentIndex + 1}/{status.TotalItems} id={status.CurrentItemId} — {status.Message}";
    }

    /// <summary>
    /// Knightshopper reports success with a message like
    /// "Insufficient OccultCrescent for … Need N, have M" when nothing was bought.
    /// Prefer the currency enum name + need/have counts (stable across UI languages);
    /// keep the English "Insufficient" wording as a fallback.
    /// </summary>
    private static bool LooksLikeInsufficientFunds(string? message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return false;
        }

        if (message.Contains("Insufficient", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // CurrencyId.ToString() stays "OccultCrescent"; item name may be localized.
        if (!message.Contains(nameof(CurrencyId.OccultCrescent), StringComparison.Ordinal))
        {
            return false;
        }

        Match counts = NeedHaveCounts.Match(message);
        return counts.Success
               && long.TryParse(counts.Groups[1].Value, out long need)
               && long.TryParse(counts.Groups[2].Value, out long have)
               && need > have;
    }

    /// <summary>Trailing need/have integers from Knightshopper's unaffordable finish line.</summary>
    private static readonly Regex NeedHaveCounts = new(
        @"(\d+)\D+(\d+)\.?\s*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
}

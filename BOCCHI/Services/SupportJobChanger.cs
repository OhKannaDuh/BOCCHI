using BOCCHI.Common.Data.SupportJobs;
using BOCCHI.Common.Services;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using Ocelot.Chain;
using Ocelot.Chain.Extensions;
using Ocelot.Extensions;
using Ocelot.Lifecycle;
using Ocelot.Services.Logger;

namespace BOCCHI.Services;

public class SupportJobChanger
(
    IChainManager chainManager,
    ISupportJobFactory supportJobs,
    IObjectTable objects,
    ICondition conditions,
    ILogger<SupportJobChanger> logger
) : ISupportJobChanger, IOnUpdate
{
    private Task<ChainResult>? task;

    private SupportJobId target;

    public void Update()
    {
        if (task?.IsCompleted != true)
        {
            return;
        }

        if (task.IsCompletedSuccessfully && task.Result is { IsSuccess: false, IsCanceled: false } result)
        {
            logger.Warning("Phantom job change to {Job} did not go through: {Reason}", target, result.ErrorMessage ?? "unknown");
        }

        task = null;
    }

    public void Change(SupportJobId id)
    {
        if (task != null)
        {
            return;
        }

        if (PhantomJobChangeGate.IsBlocked(conditions))
        {
            return;
        }

        if (objects.LocalPlayer is null)
        {
            return;
        }

        target = id;

        task = chainManager.ExecuteAsync(chains =>
        {
            return chains.Create("SupportJobChanger")
                .IfThen(
                    _ => supportJobs.TryGetCurrent(out SupportJob current) && current.Id == id,
                    _ => ValueTask.FromResult(StepResult.Break()),
                    "SupportJobChanger::CheckCurrentJob"
                )
                .Then(_ => PublicContentOccultCrescent.ChangeSupportJob((byte)id), "SupportJobChanger::Change")
                // The job status looked up by name never matched Phantom Dancer, so every switch to it "timed out".
                .WaitUntil(
                    _ => ValueTask.FromResult(supportJobs.TryGetCurrent(out SupportJob now) && now.Id == id),
                    TimeSpan.FromSeconds(5),
                    TimeSpan.FromMilliseconds(250),
                    "SupportJobChanger::WaitForChange"
                );
        });
    }

    public bool IsBusy() => task is { IsCompleted: false };
}

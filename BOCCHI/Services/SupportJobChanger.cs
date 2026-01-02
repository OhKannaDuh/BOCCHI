using BOCCHI.Common.Data.SupportJobs;
using BOCCHI.Common.Services;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using Ocelot.Chain;
using Ocelot.Chain.Extensions;
using Ocelot.Extensions;
using Ocelot.Lifecycle;

namespace BOCCHI.Services;

public class SupportJobChanger(
    IChainManager chainManager,
    ISupportJobFactory supportJobs,
    IObjectTable objects
) : ISupportJobChanger, IOnUpdate
{
    private Task<ChainResult>? task;

    public void Change(SupportJobId id)
    {
        if (task != null)
        {
            return;
        }

        if (objects.LocalPlayer is not { } player)
        {
            return;
        }

        var job = supportJobs.Create(id);

        task = chainManager.ExecuteAsync(chains =>
        {
            return chains.Create("SupportJobChanger")
                .IfThen(
                    // If we can't get our current job, or we are already that job
                    _ => !supportJobs.TryGetCurrent(out SupportJob current) || current.Id == id,
                    // Break from this chain
                    _ => ValueTask.FromResult(StepResult.Break()),
                    "SupportJobChanger::CheckCurrentJob"
                )
                .Then(_ => PublicContentOccultCrescent.ChangeSupportJob((byte)id), "SupportJobChanger::Change")
                .WaitUntil(
                    _ => ValueTask.FromResult(player.StatusList.Has(job.StatusId)),
                    TimeSpan.FromSeconds(5),
                    TimeSpan.FromMilliseconds(250),
                    "SupportJobChanger::WaitForChange"

                );
        });
    }

    public bool IsBusy()
    {
        return task is { IsCompleted: false };
    }

    public void Update()
    {
        if (task?.IsCompleted == true)
        {
            task.Dispose();
            task = null;
        }
    }
}

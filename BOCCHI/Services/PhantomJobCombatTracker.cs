using BOCCHI.Common.Data.SupportJobs;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using Ocelot.Lifecycle;

namespace BOCCHI.Services;

public sealed class PhantomJobCombatTracker(ICondition conditions) : IOnUpdate
{
    public void Update() => PhantomJobChangeGate.Track(conditions[ConditionFlag.InCombat]);
}

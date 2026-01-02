using BOCCHI.Buff.Data;
using BOCCHI.Buff.Services;
using BOCCHI.Common.Data.SupportJobs;
using BOCCHI.Common.Services;
using Dalamud.Plugin.Services;
using Ocelot.Services.PlayerState;

namespace BOCCHI.Buff.StateMachine.Handlers.ApplyingBuff;

public class ApplyingFleetfooted(
    IBuffProvider buffs,
    IObjectTable objects,
    ICondition conditions,
    ISupportJobFactory supportJobs,
    ISupportJobChanger changer
) : BaseHandler(BuffState.ApplyingFleetfooted, buffs, objects, conditions, supportJobs, changer);

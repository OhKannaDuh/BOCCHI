using FFXIVClientStructs.FFXIV.Client.Game;

namespace BOCCHI.ActionHelpers;

public static partial class Actions
{
    public static class Dancer
    {
        public static Action Dance { get; private set; } = new(ActionType.GeneralAction, 31);
        
        public static Action Quickstep { get; private set; } = new(ActionType.GeneralAction, 32);
        
        public static Action SteadfastStance { get; private set; } = new(ActionType.GeneralAction, 33);
        
        public static Action Mesmerize { get; private set; } = new(ActionType.GeneralAction, 34);
    }
}

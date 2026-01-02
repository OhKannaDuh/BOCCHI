using BOCCHI.Common.Data.Fates;
using Dalamud.Game.ClientState.Fates;

namespace BOCCHI.Fates.Data;

public interface IFateFactory
{
    Fate Create(IFate context);
}

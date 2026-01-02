using BOCCHI.Common.Data.Fates;
using Dalamud.Game.ClientState.Fates;
using Ocelot.Services.Data;

namespace BOCCHI.Fates.Data;

public class FateFactory(IDataRepository<FateData> fateDataRepository) : IFateFactory
{
    public Fate Create(IFate context)
    {
        var id = new FateId(context.FateId);

        return new Fate(id, context, fateDataRepository);
    }
}

using Ocelot.Chain;

namespace BOCCHI.Common.Services;

public interface IMateriaExtractionService
{
    bool ShouldExtract();

    IChain ExtractEquipped();
}

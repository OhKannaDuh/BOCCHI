using BOCCHI.Common.Config;

namespace BOCCHI.Services.Shopping.Backends;

/// <summary>Resolves the configured <see cref="IShoppingBackend"/>.</summary>
public sealed class ShoppingBackendSelector(ShoppingConfig config, IEnumerable<IShoppingBackend> backends)
{
    private readonly IShoppingBackend[] all = backends.ToArray();

    public IShoppingBackend Current =>
        all.FirstOrDefault(b => b.Kind == config.Backend)
        ?? all.First(b => b.Kind == ShoppingBackendKind.GatherBuddyReborn);
}

namespace BOCCHI.Common.Services;

public interface IAutomatorMemory
{
    bool TryRemember<T>(out T memory) where T : class;

    bool TryAdd<T>() where T : class, new();

    bool TryAdd<T>(T memory) where T : class;

    bool Forget<T>() where T : class;

    void Wipe();
}

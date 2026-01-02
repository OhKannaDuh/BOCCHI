using BOCCHI.Common.Services;

namespace BOCCHI.Automator.Services;

public class AutomatorMemory : IAutomatorMemory
{
    private readonly Dictionary<Type, object> memories = new();

    public bool TryRemember<T>(out T memory) where T : class
    {
        if(memories.TryGetValue(typeof(T), out var obj))
        {
            memory = (T)obj;
            return true;
        }

        memory = null!;
        return false;
    }

    public bool TryAdd<T>() where T : class, new()
    {
        return TryAdd(new T());
    }

    public bool TryAdd<T>(T memory) where T : class
    {
        if (memories.ContainsKey(typeof(T)))
        {
            return false;
        }

        memories.Add(typeof(T), memory);
        return true;
    }

    public bool Forget<T>() where T : class
    {
        return memories.Remove(typeof(T));
    }

    public void Wipe()
    {
        memories.Clear();
    }
}

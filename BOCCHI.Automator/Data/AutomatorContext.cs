namespace BOCCHI.Automator.Data;

public class AutomatorContext : IAutomatorContext
{
    public bool Enabled { get; private set; } = false;

    public void Toggle()
    {
        Enabled = !Enabled;
        OnToggle?.Invoke(Enabled);
    }

    public event Action<bool>? OnToggle;
}

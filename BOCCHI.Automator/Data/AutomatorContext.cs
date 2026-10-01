namespace BOCCHI.Automator.Data;

public interface IAutomatorContext
{
    AutomatorRunMode RunMode { get; }

    bool Enabled { get; }

    bool IsIllegalMode { get; }

    bool IsPotsAndTreasure { get; }

    bool IsCompletionist { get; }

    void Toggle();

    void TogglePotsAndTreasure();

    void ToggleCompletionist();

    void SetRunMode(AutomatorRunMode mode);

    event Action<bool>? OnToggle;
}

public class AutomatorContext : IAutomatorContext
{
    public AutomatorRunMode RunMode { get; private set; }

    public bool Enabled => RunMode != AutomatorRunMode.Off;

    public bool IsIllegalMode => RunMode == AutomatorRunMode.IllegalMode;

    public bool IsPotsAndTreasure => RunMode == AutomatorRunMode.PotsAndTreasure;

    public bool IsCompletionist => RunMode == AutomatorRunMode.Completionist;

    public void Toggle()
    {
        SetRunMode(IsIllegalMode ? AutomatorRunMode.Off : AutomatorRunMode.IllegalMode);
    }

    public void TogglePotsAndTreasure()
    {
        SetRunMode(IsPotsAndTreasure ? AutomatorRunMode.Off : AutomatorRunMode.PotsAndTreasure);
    }

    public void ToggleCompletionist()
    {
        SetRunMode(IsCompletionist ? AutomatorRunMode.Off : AutomatorRunMode.Completionist);
    }

    public void SetRunMode(AutomatorRunMode mode)
    {
        if (RunMode == mode)
        {
            return;
        }

        bool wasEnabled = Enabled;
        RunMode = mode;
        bool nowEnabled = Enabled;
        if (wasEnabled != nowEnabled)
        {
            OnToggle?.Invoke(nowEnabled);
        }
    }

    public event Action<bool>? OnToggle;
}

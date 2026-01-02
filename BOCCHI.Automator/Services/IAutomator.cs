using BOCCHI.Common.Data.CriticalEncounters;
using BOCCHI.Common.Data.Fates;

namespace BOCCHI.Automator.Services;

public interface IAutomator
{
    bool Enabled { get; }

    void Toggle();

    void Render();
}

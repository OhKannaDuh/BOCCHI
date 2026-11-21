namespace BOCCHI.Common;

public enum MainWindowSection
{
    Trackers = 0,
    Automation = 1,
    Completionist = 2,
    PotsTreasure = 3,
    MobFarmer = 4,
    World = 5,
    Treasure = 6
}

public interface IDynamicRenderer
{
    uint Order => 0;

    MainWindowSection Section => MainWindowSection.Automation;

    string? SubsectionTitle => null;

    string? CompactText => null;

    void Render();

    bool ShouldRender();
}

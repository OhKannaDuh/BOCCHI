namespace BOCCHI.MobFarmer.Services;

public interface IFarmerCombatController
{
    void Prepare();

    void EnableFighting();

    void Disable();

    bool IsActive { get; }

    void Tick();

    void Teardown();
}

namespace BOCCHI.Services.Shopping;

public interface IShoppingService
{
    bool IsActive { get; }

    void ForceStop();

    bool TryForceStart(out string detail);

    string DescribeStatus();
}

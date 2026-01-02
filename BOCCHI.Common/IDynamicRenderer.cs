namespace BOCCHI.Common;

public interface IDynamicRenderer
{
    uint Order
    {
        get => 0;
    }

    void Render();

    bool ShouldRender();
}

using BOCCHI.Common;
using Ocelot.Services.WindowManager;

namespace BOCCHI.Renderers;

public class MainRenderer(IEnumerable<IDynamicRenderer> renderers) : IMainRenderer
{
    private IEnumerable<IDynamicRenderer> orderedRenderers
    {
        get => renderers.Where(r => r.ShouldRender()).OrderBy(r => r.Order);
    }

    public void Render()
    {
        foreach (var renderer in orderedRenderers)
        {
            renderer.Render();
        }
    }
}

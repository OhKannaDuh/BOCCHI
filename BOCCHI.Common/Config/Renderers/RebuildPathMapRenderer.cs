using System.Reflection;
using BOCCHI.Common.Config.Fields;
using BOCCHI.Common.Services;
using Dalamud.Bindings.ImGui;
using Microsoft.Extensions.DependencyInjection;
using Ocelot.Config.Renderers;
using Ocelot.Extensions;
using Ocelot.Services.Translation;

namespace BOCCHI.Common.Config.Renderers;

public sealed class RebuildPathMapRenderer(IServiceProvider services) : IFieldRenderer<RebuildPathMapAttribute>
{
    public bool Render(object target, PropertyInfo prop, RebuildPathMapAttribute attr, Type owner, ITranslator translator)
    {
        if (ImGui.Button(prop.Label(owner, translator)))
        {
            services.GetRequiredService<IPathMapRebuilder>().RebuildPathMap();
        }

        prop.Tooltip(owner, translator);
        return false;
    }
}

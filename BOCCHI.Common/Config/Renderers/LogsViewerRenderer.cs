using System.Reflection;
using BOCCHI.Common.Config.Fields;
using BOCCHI.Common.Services.Logging;
using Ocelot.Config.Renderers;
using Ocelot.Services.Translation;

namespace BOCCHI.Common.Config.Renderers;

public sealed class LogsViewerRenderer(BocchiLogsPanel panel) : IFieldRenderer<LogsViewerAttribute>
{
    // A new scoped translator subscribes to two events and is never released; make it once.
    private ITranslator? scoped;

    public bool Render(object target, PropertyInfo prop, LogsViewerAttribute attr, Type owner, ITranslator translator)
    {
        scoped ??= translator.WithScope("windows.logs");
        panel.Draw(scoped, idSuffix: "_config");
        return false;
    }
}

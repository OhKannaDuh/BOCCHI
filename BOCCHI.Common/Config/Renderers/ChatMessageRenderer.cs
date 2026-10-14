using BOCCHI.Common.Config.Fields;
using BOCCHI.Common.UI;
using Dalamud.Bindings.ImGui;
using Ocelot.Config.Renderers;
using Ocelot.Extensions;
using Ocelot.Services.Translation;
using System.Reflection;

namespace BOCCHI.Common.Config.Renderers;

/// <summary>Plain single-line text box for a message BOCCHI sends to game chat.</summary>
public sealed class ChatMessageRenderer : IFieldRenderer<ChatMessageAttribute>
{
    // Game chat caps a line at 500 UTF-8 bytes; leave room for the channel command prefix.
    private const int MaxBytes = 400;

    public bool Render(object target, PropertyInfo prop, ChatMessageAttribute attr, Type owner, ITranslator translator)
    {
        if (prop.PropertyType != typeof(string))
        {
            throw new InvalidOperationException(
                $"[ChatMessageRenderer] must be used on string properties. "
                + $"{prop.DeclaringType?.Name}.{prop.Name} is {prop.PropertyType.Name}.");
        }

        string value = (string?)prop.GetValue(target) ?? string.Empty;

        BocchiUi.PushFieldStyle();
        try
        {
            bool changed = ImGui.InputText(prop.Label(owner, translator), ref value, MaxBytes);
            prop.Tooltip(owner, translator);

            if (changed)
            {
                prop.SetValue(target, value);
            }

            return changed;
        }
        finally
        {
            BocchiUi.PopFieldStyle();
        }
    }
}

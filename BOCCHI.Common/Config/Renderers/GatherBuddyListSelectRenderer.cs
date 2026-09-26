using BOCCHI.Common.Config.Fields;
using BOCCHI.Common.Ipc.GatherBuddy;
using BOCCHI.Common.UI;
using Dalamud.Bindings.ImGui;
using Ocelot.Config.Renderers;
using Ocelot.Extensions;
using Ocelot.Services.Translation;
using System.Reflection;

namespace BOCCHI.Common.Config.Renderers;

/// <summary>
///     Buy-list name as free text (works before GBR loads) plus a picker listing GBR's lists
///     when its IPC is reachable.
/// </summary>
public sealed class GatherBuddyListSelectRenderer(IGatherBuddyIpc gatherBuddy)
    : IFieldRenderer<GatherBuddyListSelectAttribute>
{
    private IReadOnlyList<string> names = [];

    public bool Render(
        object target,
        PropertyInfo prop,
        GatherBuddyListSelectAttribute attr,
        Type owner,
        ITranslator translator)
    {
        if (prop.PropertyType != typeof(string))
        {
            throw new InvalidOperationException(
                $"[GatherBuddyListSelect] can only be used on string properties. {prop.DeclaringType?.Name}.{prop.Name} is {prop.PropertyType.Name}.");
        }

        string value = (string?)prop.GetValue(target) ?? string.Empty;
        bool changed;

        BocchiUi.PushFieldStyle();
        try
        {
            float pickerWidth = ImGui.GetFrameHeight();
            ImGui.SetNextItemWidth(ImGui.CalcItemWidth() - pickerWidth - ImGui.GetStyle().ItemInnerSpacing.X);
            changed = ImGui.InputTextWithHint(
                $"##{prop.Name}_text",
                Resolve(translator, prop, owner, "hint", "GBR active list"),
                ref value,
                attr.MaxLength);
            prop.Tooltip(owner, translator);

            ImGui.SameLine(0, ImGui.GetStyle().ItemInnerSpacing.X);
            if (ImGui.BeginCombo($"##{prop.Name}_pick", string.Empty, ImGuiComboFlags.NoPreview | ImGuiComboFlags.PopupAlignLeft))
            {
                if (ImGui.IsWindowAppearing())
                {
                    names = gatherBuddy.IsAvailable ? gatherBuddy.ListNames() : [];
                }

                if (names.Count == 0)
                {
                    ImGui.TextDisabled(Resolve(translator, prop, owner, "no_lists", "No GatherBuddy Reborn lists found"));
                }

                foreach (string name in names)
                {
                    bool selected = string.Equals(name.Trim(), value.Trim(), StringComparison.OrdinalIgnoreCase);
                    if (ImGui.Selectable(name, selected))
                    {
                        value = name;
                        changed = true;
                    }
                }

                ImGui.EndCombo();
            }

            ImGui.SameLine(0, ImGui.GetStyle().ItemInnerSpacing.X);
            ImGui.TextUnformatted(prop.Label(owner, translator));
            prop.Tooltip(owner, translator);
        }
        finally
        {
            BocchiUi.PopFieldStyle();
        }

        if (changed)
        {
            prop.SetValue(target, value);
        }

        return changed;
    }

    private static string Resolve(ITranslator translator, PropertyInfo prop, Type owner, string field, string fallback)
    {
        string key = prop.GetFieldLabelKey(owner).Replace(".label", "." + field, StringComparison.Ordinal);
        return translator.Has(key) ? translator.T(key) : fallback;
    }
}

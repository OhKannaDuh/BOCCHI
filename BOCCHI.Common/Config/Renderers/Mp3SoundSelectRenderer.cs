using BOCCHI.Common.Config.Fields;
using BOCCHI.Common.Services;
using BOCCHI.Common.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Ocelot.Config.Renderers;
using Ocelot.Extensions;
using Ocelot.Services.Translation;
using System.Reflection;

namespace BOCCHI.Common.Config.Renderers;

public sealed class Mp3SoundSelectRenderer(IMp3SoundPlayer sounds) : IFieldRenderer<Mp3SoundSelectAttribute>
{
    public bool Render(object target, PropertyInfo prop, Mp3SoundSelectAttribute attr, Type owner, ITranslator translator)
    {
        if (prop.PropertyType != typeof(string))
        {
            throw new InvalidOperationException(
                $"[Mp3SoundSelectRenderer] must be used on string properties. " +
                $"{prop.DeclaringType?.Name}.{prop.Name} is {prop.PropertyType.Name}.");
        }

        string current = (string?)prop.GetValue(target) ?? "Moogle";
        IReadOnlyList<string> options = sounds.ListSounds();
        if (options.Count == 0)
        {
            options = ["Moogle"];
        }

        if (!options.Contains(current, StringComparer.OrdinalIgnoreCase))
        {
            current = options[0];
        }

        string label = prop.Label(owner, translator);
        bool changed = false;

        BocchiUi.PushFieldStyle();
        try
        {
            ImGui.SetNextItemWidth(ImGui.CalcTextSize("Game OverXXXX").X + ImGui.GetStyle().FramePadding.X * 4f);
            if (ImGui.BeginCombo(label, current))
            {
                foreach (string name in options)
                {
                    bool selected = string.Equals(name, current, StringComparison.OrdinalIgnoreCase);
                    if (ImGui.Selectable(name, selected))
                    {
                        current = name;
                        changed = true;
                        sounds.Play(name);
                    }

                    if (selected)
                    {
                        ImGui.SetItemDefaultFocus();
                    }
                }

                ImGui.EndCombo();
            }

            prop.Tooltip(owner, translator);

            ImGui.SameLine();
            if (FolderButton(translator, prop, owner))
            {
                sounds.OpenSoundsFolder();
            }
        }
        finally
        {
            BocchiUi.PopFieldStyle();
        }

        if (changed)
        {
            prop.SetValue(target, current);
        }

        return changed;
    }

    private static bool FolderButton(ITranslator translator, PropertyInfo prop, Type owner)
    {
        string tipKey = prop.GetFieldLabelKey(owner).Replace(".label", ".open_folder", StringComparison.Ordinal);
        string tip = translator.Has(tipKey)
            ? translator.T(tipKey)
            : "Open sound folder — drop MP3s here to add your own.";

        bool clicked;
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            clicked = ImGui.Button($"{FontAwesomeIcon.FolderOpen.ToIconString()}##hunt_sound_folder");
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(tip);
        }

        return clicked;
    }
}

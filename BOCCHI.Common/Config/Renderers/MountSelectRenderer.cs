using BOCCHI.Common.Config.Fields;
using BOCCHI.Common.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using Ocelot.Config.Renderers;
using Ocelot.Extensions;
using Ocelot.Services.Translation;
using System.Reflection;
using PlayerState = FFXIVClientStructs.FFXIV.Client.Game.UI.PlayerState;

namespace BOCCHI.Common.Config.Renderers;

public sealed class MountSelectRenderer(IDataManager data) : IFieldRenderer<MountSelectAttribute>
{
    private string search = string.Empty;

    private (uint[] Ids, string[] Names)? cache;

    public bool Render(object target, PropertyInfo prop, MountSelectAttribute attr, Type owner, ITranslator translator)
    {
        if (prop.PropertyType != typeof(uint) && prop.PropertyType != typeof(int))
        {
            throw new InvalidOperationException(
                $"[MountSelectRenderer] must be used on uint/int properties. " +
                $"{prop.DeclaringType?.Name}.{prop.Name} is {prop.PropertyType.Name}.");
        }

        cache ??= BuildMountList(translator, prop, owner);

        uint current = Convert.ToUInt32(prop.GetValue(target) ?? 0u);
        (uint[] ids, string[] names) = cache.Value;

        int index = Array.IndexOf(ids, current);
        if (index < 0)
        {
            index = 0;
            current = 0;
            if (prop.PropertyType == typeof(int))
            {
                prop.SetValue(target, 0);
            }
            else
            {
                prop.SetValue(target, 0u);
            }
        }

        string label = prop.Label(owner, translator);
        string preview = names[index];
        bool changed = false;

        BocchiUi.PushFieldStyle();
        try
        {
            if (ImGui.BeginCombo(label, preview, ImGuiComboFlags.HeightLarge))
            {
                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                if (ImGui.IsWindowAppearing())
                {
                    ImGui.SetKeyboardFocusHere();
                    cache = BuildMountList(translator, prop, owner);
                    (ids, names) = cache.Value;
                    index = Array.IndexOf(ids, current);
                    if (index < 0)
                    {
                        index = 0;
                    }
                }

                ImGui.InputTextWithHint("##mount_filter", ResolveSearchHint(translator, prop, owner), ref search, 256);

                int visibleRows = Math.Clamp(names.Length, 1, 12);
                var listSize = ImGui.GetContentRegionAvail() with { Y = ImGui.GetTextLineHeightWithSpacing() * visibleRows };
                using (ImRaii.Child("##mount_list", listSize))
                {
                    for (int i = 0; i < names.Length; i++)
                    {
                        if (!string.IsNullOrEmpty(search)
                            && !names[i].Contains(search, StringComparison.CurrentCultureIgnoreCase))
                        {
                            continue;
                        }

                        bool selected = i == index;
                        if (ImGui.Selectable(names[i], selected))
                        {
                            current = ids[i];
                            search = string.Empty;
                            changed = true;
                        }

                        if (selected)
                        {
                            ImGui.SetItemDefaultFocus();
                        }
                    }
                }

                ImGui.EndCombo();
            }

            prop.Tooltip(owner, translator);
        }
        finally
        {
            BocchiUi.PopFieldStyle();
        }

        if (changed)
        {
            if (prop.PropertyType == typeof(int))
            {
                prop.SetValue(target, (int)current);
            }
            else
            {
                prop.SetValue(target, current);
            }
        }

        return changed;
    }

    private (uint[] Ids, string[] Names) BuildMountList(ITranslator translator, PropertyInfo prop, Type owner)
    {
        string rouletteKey = prop.GetFieldLabelKey(owner).Replace(".label", ".roulette", StringComparison.Ordinal);
        string roulette = translator.Has(rouletteKey) ? translator.T(rouletteKey) : "Mount Roulette";

        List<(uint Id, string Name)> mounts = [];
        unsafe
        {
            PlayerState* playerState = PlayerState.Instance();
            foreach (Mount row in data.GetExcelSheet<Mount>())
            {
                if (row.RowId == 0 || row.Icon == 0)
                {
                    continue;
                }

                string name = row.Singular.ToString();
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                if (playerState != null && !playerState->IsMountUnlocked(row.RowId))
                {
                    continue;
                }

                mounts.Add((row.RowId, name));
            }
        }

        mounts.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));

        uint[] ids = new uint[mounts.Count + 1];
        string[] names = new string[mounts.Count + 1];
        ids[0] = 0;
        names[0] = roulette;
        for (int i = 0; i < mounts.Count; i++)
        {
            ids[i + 1] = mounts[i].Id;
            names[i + 1] = mounts[i].Name;
        }

        return (ids, names);
    }

    private static string ResolveSearchHint(ITranslator translator, PropertyInfo prop, Type owner)
    {
        string key = prop.GetFieldLabelKey(owner).Replace(".label", ".search", StringComparison.Ordinal);
        return translator.Has(key) ? translator.T(key) : "Search mounts...";
    }
}

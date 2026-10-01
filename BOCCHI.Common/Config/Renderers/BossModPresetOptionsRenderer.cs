using System.Reflection;
using BOCCHI.Common.Config.Fields;
using BOCCHI.Common.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Ocelot.Config.Renderers;
using Ocelot.Extensions;
using Ocelot.Rotation.Services;
using Ocelot.Services.PlayerState;
using Ocelot.Services.Translation;

namespace BOCCHI.Common.Config.Renderers;

public sealed class BossModPresetOptionsRenderer(ICombatRotationSession session, IPlayer player)
    : IFieldRenderer<BossModPresetOptionsAttribute>
{
    private const string MovementKey = "config.automator.fields.boss_mod_movement";

    public bool Render(object target, PropertyInfo prop, BossModPresetOptionsAttribute attr, Type owner, ITranslator translator)
    {
        if (prop.PropertyType != typeof(bool))
        {
            throw new InvalidOperationException(
                $"[BossModPresetOptions] can only be used on bool properties. "
                + $"{prop.DeclaringType?.Name}.{prop.Name} is {prop.PropertyType.Name}.");
        }

        if (target is not AutomatorConfig config || !config.CombatAutorotation.UsesCombatAutomation())
        {
            return false;
        }

        string fieldKey = $"config.automator.fields.{prop.Name.ToSnakeCase()}";
        bool changed = false;
        bool recreate = false;
        bool updateAuto = config.UpdateBossModPresetsAutomatically;

        BocchiUi.PushFieldStyle();
        try
        {
            using (ImRaii.Disabled(!session.BossModPresetsAvailable || updateAuto))
            {
                if (ImGui.Button(translator.T($"{fieldKey}.button")))
                {
                    recreate = true;
                }
            }

            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            {
                string buttonTip = updateAuto
                    ? translator.T($"{fieldKey}.button_tooltip_auto_on")
                    : translator.T($"{fieldKey}.button_tooltip");
                Ocelot.Extensions.PropertyInfoExtensions.DrawWrappedTooltip(buttonTip);
            }

            if (ImGui.Checkbox(prop.Label(owner, translator), ref updateAuto))
            {
                config.UpdateBossModPresetsAutomatically = updateAuto;
                changed = true;
            }

            prop.Tooltip(owner, translator);

            bool byRole = config.BossModMaxDistanceByRole;
            if (Checkbox(translator, "max_distance_by_role", ref byRole))
            {
                config.BossModMaxDistanceByRole = byRole;
                changed = true;
            }

            ImGui.PushItemWidth(ImGui.GetFontSize() * 12f);
            if (!byRole)
            {
                float distance = config.BossModMaxDistance;
                if (Slider(translator, "max_distance", ref distance))
                {
                    config.BossModMaxDistance = distance;
                    changed = true;
                }
            }
            else
            {
                bool onHitbox = config.BossModMeleeOnHitbox;
                if (Checkbox(translator, "melee_on_hitbox", ref onHitbox))
                {
                    config.BossModMeleeOnHitbox = onHitbox;
                    changed = true;
                }

                using (ImRaii.Disabled(onHitbox))
                {
                    float melee = config.BossModMaxDistanceMelee;
                    if (Slider(translator, "max_distance_melee", ref melee))
                    {
                        config.BossModMaxDistanceMelee = melee;
                        changed = true;
                    }
                }

                float ranged = config.BossModMaxDistanceRanged;
                if (Slider(translator, "max_distance_ranged", ref ranged))
                {
                    config.BossModMaxDistanceRanged = ranged;
                    changed = true;
                }
            }

            var overdodge = config.BossModOverdodge;
            if (Combo(translator, "overdodge", ref overdodge))
            {
                config.BossModOverdodge = overdodge;
                changed = true;
            }

            var delay = config.BossModMovementDelay;
            if (Combo(translator, "delay", ref delay))
            {
                config.BossModMovementDelay = delay;
                changed = true;
            }

            bool separateDodge = config.BossModSeparateDodgeDelay;
            if (Checkbox(translator, "separate_dodge_delay", ref separateDodge))
            {
                config.BossModSeparateDodgeDelay = separateDodge;
                changed = true;
            }

            if (separateDodge)
            {
                var dodgeDelay = config.BossModDodgeMovementDelay;
                if (Combo(translator, "dodge_delay", ref dodgeDelay))
                {
                    config.BossModDodgeMovementDelay = dodgeDelay;
                    changed = true;
                }
            }

            ImGui.PopItemWidth();
        }
        finally
        {
            BocchiUi.PopFieldStyle();
        }

        session.MovementSettings = BossModMovement.From(config, player.IsMelee(), player.GetClassJob()?.RowId);
        if (recreate)
        {
            session.TryForceRecreateBossModPresets(PresetKind(config.CombatAutorotation));
        }

        return changed;
    }

    private static bool Checkbox(ITranslator translator, string key, ref bool value)
    {
        bool changed = ImGui.Checkbox(translator.T($"{MovementKey}.{key}.label"), ref value);
        Tooltip(translator, key);
        return changed;
    }

    private static bool Slider(ITranslator translator, string key, ref float value)
    {
        bool changed = ImGui.SliderFloat(
            translator.T($"{MovementKey}.{key}.label"),
            ref value,
            BossModMovement.MinRange,
            BossModMovement.MaxRange,
            "%.1f");
        Tooltip(translator, key);
        if (changed)
        {
            value = Math.Clamp(MathF.Round(value, 1), BossModMovement.MinRange, BossModMovement.MaxRange);
        }

        return changed;
    }

    private static bool Combo<T>(ITranslator translator, string key, ref T value) where T : struct, Enum
    {
        T[] values = Enum.GetValues<T>();
        int index = Array.IndexOf(values, value);
        if (index < 0)
        {
            index = 0;
        }

        string[] labels = new string[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            labels[i] = translator.T($"{MovementKey}.{key}.{values[i].ToString().ToSnakeCase()}");
        }

        bool changed = ImGui.Combo(translator.T($"{MovementKey}.{key}.label"), ref index, labels, labels.Length);
        Tooltip(translator, key);
        if (changed)
        {
            value = values[index];
        }

        return changed;
    }

    private static void Tooltip(ITranslator translator, string key)
    {
        string tooltipKey = $"{MovementKey}.{key}.tooltip";
        if (translator.Has(tooltipKey) && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            Ocelot.Extensions.PropertyInfoExtensions.DrawWrappedTooltip(translator.T(tooltipKey));
        }
    }

    private static BossModPresetKind PresetKind(CombatAutorotation combat) =>
        combat is CombatAutorotation.BossMod or CombatAutorotation.BossModReborn
            ? BossModPresetKind.FullAr
            : BossModPresetKind.MiscAi;
}

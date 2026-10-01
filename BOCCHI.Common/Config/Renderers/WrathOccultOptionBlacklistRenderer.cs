using BOCCHI.Common.Config.Fields;
using BOCCHI.Common.Data.SupportJobs;
using BOCCHI.Common.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Game;
using Dalamud.Plugin.Services;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using Ocelot.Config.Renderers;
using Ocelot.Extensions;
using Ocelot.Rotation.Services.Wrath;
using Ocelot.Services.Translation;
using System.Reflection;
using System.Text.RegularExpressions;
using LuminaAction = Lumina.Excel.Sheets.Action;

namespace BOCCHI.Common.Config.Renderers;

public sealed partial class WrathOccultOptionBlacklistRenderer(IWrathOccultOptionCatalog catalog, IDataManager data)
    : IFieldRenderer<WrathOccultOptionBlacklistAttribute>
{
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(5);

    private IReadOnlyList<JobOptions> jobs = [];

    private DateTime nextLoadAttempt = DateTime.MinValue;

    public bool Render(object target, PropertyInfo prop, WrathOccultOptionBlacklistAttribute attr, Type owner, ITranslator translator)
    {
        if (prop.PropertyType != typeof(HashSet<string>))
        {
            throw new InvalidOperationException(
                $"[{nameof(WrathOccultOptionBlacklistRenderer)}] must be used on HashSet<string> properties. "
                + $"{prop.DeclaringType?.Name}.{prop.Name} is {prop.PropertyType.Name}.");
        }

        HashSet<string> disabled = (HashSet<string>?)prop.GetValue(target) ?? [];
        if (prop.GetValue(target) == null)
        {
            prop.SetValue(target, disabled);
        }

        string fieldKey = prop.GetFieldLabelKey(owner);
        BocchiUi.SectionTitle(prop.Label(owner, translator));
        prop.Tooltip(owner, translator);

        IReadOnlyList<JobOptions> loaded = GetJobs();
        if (loaded.Count == 0)
        {
            BocchiUi.MutedText(translator.T(fieldKey.Replace(".label", ".empty", StringComparison.Ordinal)));
            return false;
        }

        string builtInReason = translator.T(fieldKey.Replace(".label", ".built_in", StringComparison.Ordinal));
        bool changed = false;
        BocchiUi.PushFieldStyle();
        try
        {
            foreach (JobOptions job in loaded)
            {
                int blocked = job.Options.Count(o => disabled.Contains(o.Name));
                string header = blocked > 0 ? $"{job.JobName} ({blocked})" : job.JobName;
                if (!ImGui.TreeNode($"{header}###wrath_occult_{job.JobId}"))
                {
                    continue;
                }

                foreach (OptionEntry option in job.Options)
                {
                    changed |= DrawOption(option, disabled, builtInReason);
                }

                ImGui.TreePop();
            }
        }
        finally
        {
            BocchiUi.PopFieldStyle();
        }

        if (changed)
        {
            prop.SetValue(target, disabled);
        }

        return changed;
    }

    private bool DrawOption(OptionEntry option, HashSet<string> disabled, string builtInReason)
    {
        bool builtIn = catalog.BuiltInOptionsLeftOff.Contains(option.Name);
        bool allowed = !builtIn && !disabled.Contains(option.Name);
        bool changed = false;

        ImGui.PushID(option.Name);
        if (builtIn)
        {
            ImGui.BeginDisabled();
        }

        if (ImGui.Checkbox(option.Label, ref allowed) && !builtIn)
        {
            changed = allowed ? disabled.Remove(option.Name) : disabled.Add(option.Name);
        }

        if (builtIn)
        {
            ImGui.EndDisabled();
        }

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            Ocelot.Extensions.PropertyInfoExtensions.DrawWrappedTooltip(builtIn ? $"{option.Name}\n{builtInReason}" : option.Name);
        }

        ImGui.PopID();
        return changed;
    }

    private IReadOnlyList<JobOptions> GetJobs()
    {
        if (jobs.Count > 0 || DateTime.UtcNow < nextLoadAttempt)
        {
            return jobs;
        }

        nextLoadAttempt = DateTime.UtcNow + RetryInterval;
        jobs = LoadJobs();
        return jobs;
    }

    private List<JobOptions> LoadJobs()
    {
        ExcelSheet<MKDSupportJob> jobSheet = data.GetExcelSheet<MKDSupportJob>();
        ExcelSheet<LuminaAction> actions = data.GetExcelSheet<LuminaAction>();
        ExcelSheet<LuminaAction> actionsEn = data.GetExcelSheet<LuminaAction>(ClientLanguage.English);

        List<JobOptions> result = [];
        foreach (SupportJobId id in Enum.GetValues<SupportJobId>())
        {
            IReadOnlyList<string>? names = catalog.GetOptionNames(id.RowId());
            if (names is not { Count: > 0 } || !jobSheet.TryGetRow(id.RowId(), out MKDSupportJob row))
            {
                continue;
            }

            Dictionary<string, string> actionNames = LocalizedActionNames(row, actions, actionsEn);
            string jobName = row.Name.ToString();
            result.Add(new JobOptions(
                id.RowId(),
                string.IsNullOrWhiteSpace(jobName) ? id.ToString() : jobName,
                names.Select(name => new OptionEntry(name, OptionLabel(name, actionNames))).ToList()));
        }

        return result;
    }

    private static Dictionary<string, string> LocalizedActionNames(
        MKDSupportJob row,
        ExcelSheet<LuminaAction> actions,
        ExcelSheet<LuminaAction> actionsEn)
    {
        Dictionary<string, string> map = new(StringComparer.OrdinalIgnoreCase);
        foreach (MKDSupportJob.ActionsStruct slot in row.Actions)
        {
            uint actionId = slot.Action.RowId;
            if (actionId == 0
                || !actionsEn.TryGetRow(actionId, out LuminaAction en)
                || !actions.TryGetRow(actionId, out LuminaAction local))
            {
                continue;
            }

            map.TryAdd(NonAlphanumeric().Replace(en.Name.ToString(), string.Empty), local.Name.ToString());
        }

        return map;
    }

    private static string OptionLabel(string optionName, Dictionary<string, string> actionNames)
    {
        string[] parts = optionName.Split('_');
        if (parts.Length < 3)
        {
            return optionName;
        }

        string action = actionNames.TryGetValue(parts[2], out string? localized)
            ? localized
            : SplitCamelCase(parts[2]);

        return parts.Length > 3
            ? $"{action} ({string.Join(' ', parts.Skip(3).Select(SplitCamelCase))})"
            : action;
    }

    private static string SplitCamelCase(string value) => CamelCaseBoundary().Replace(value, " ");

    [GeneratedRegex("[^A-Za-z0-9]")]
    private static partial Regex NonAlphanumeric();

    [GeneratedRegex("(?<=[a-z])(?=[A-Z])")]
    private static partial Regex CamelCaseBoundary();

    private sealed record JobOptions(uint JobId, string JobName, IReadOnlyList<OptionEntry> Options);

    private sealed record OptionEntry(string Name, string Label);
}
